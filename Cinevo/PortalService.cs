using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace Cinevo;
public sealed record PortalConnection(string Endpoint, string Key);
public sealed class PortalItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Year { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string CoverUrl { get; set; } = "";
}
public sealed class PortalFeed
{
    public List<PortalItem> Items { get; set; } = [];
    public int? NextOffset { get; set; }
}
public sealed record PortalSyncResult(int Added, int Total);
public sealed record PortalDownload(string Url, string Label, string Kind);
public sealed record PortalDownloads(List<PortalDownload> Options);
public static class PortalService
{
    public const string DefaultEndpoint = "https://cinevo-portal.arthurgabrielfera.workers.dev";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static string ConnectionFile => System.IO.Path.Combine(Library.DataDirectory, "portal.dat");
    public static PortalConnection? LoadConnection()
    {
        if (!File.Exists(ConnectionFile)) return null;
        var clear = LocalSecret.Unprotect(File.ReadAllBytes(ConnectionFile));
        return JsonSerializer.Deserialize<PortalConnection>(clear, JsonOptions);
    }
    public static void SaveConnection(PortalConnection connection)
    {
        ValidateConnection(connection); Directory.CreateDirectory(Library.DataDirectory);
        var encrypted = LocalSecret.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(connection)));
        var temp = ConnectionFile + ".tmp"; File.WriteAllBytes(temp, encrypted); File.Move(temp, ConnectionFile, true);
    }
    public static void ValidateConnection(PortalConnection c)
    {
        if (!Uri.TryCreate(c.Endpoint, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != DefaultEndpoint || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || c.Key.Length is < 30 or > 200) throw new InvalidDataException("O arquivo não contém uma conexão válida com o portal oficial do Cinevo.");
    }
    public static PortalConnection ReadPairing(string path)
    {
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Arquivo de conexão inválido.");
        var c = JsonSerializer.Deserialize<PortalConnection>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Arquivo de conexão vazio."); ValidateConnection(c); return c;
    }
    private static HttpClient Client(PortalConnection c,HttpMessageHandler? handler=null)
    {
        ValidateConnection(c); var http = new HttpClient(handler??new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45), BaseAddress = new Uri(c.Endpoint) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", c.Key); http.DefaultRequestHeaders.UserAgent.ParseAdd("Cinevo/" + Updater.CurrentVersion); return http;
    }
    public static async Task<List<PortalDownload>> GetDownloadsAsync(string itemId, CancellationToken token)
    {
        if (itemId.Length != 64 || !itemId.All(Uri.IsHexDigit)) return [];
        var connection = LoadConnection();
        if (connection == null) throw new InvalidDataException("Conecte o portal para consultar os downloads.");
        using var http = Client(connection);
        using var response = await http.GetAsync($"/api/items/{itemId}/downloads", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await RemoteFiles.ReadBoundedAsync(response, 128 * 1024, token);
        return JsonSerializer.Deserialize<PortalDownloads>(bytes, JsonOptions)?.Options ?? [];
    }
    public static async Task<PortalSyncResult> SyncAsync(Library library, PortalConnection connection, CancellationToken token,HttpMessageHandler? handler=null)
    {
        using var http = Client(connection,handler);
        var items = new Dictionary<string, PortalItem>(StringComparer.Ordinal);
        int? offset = 0;
        while (offset is int page)
        {
            using var response = await http.GetAsync($"/api/items?offset={page}", HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidDataException("A chave do portal não foi aceita. Importe novamente o arquivo de conexão.");
            response.EnsureSuccessStatusCode();
            var bytes = await RemoteFiles.ReadBoundedAsync(response, 8 * 1024 * 1024, token);
            var feed = JsonSerializer.Deserialize<PortalFeed>(bytes, JsonOptions) ?? throw new InvalidDataException("O portal retornou um catálogo inválido.");
            foreach (var item in feed.Items) items[item.Id] = item;
            offset = feed.NextOffset;
            if (offset <= page) throw new InvalidDataException("O portal retornou uma página inválida.");
        }
        var imported = 0;
        var total = 0;
        var existing = library.Movies.Where(movie => movie.IsOnline).ToLookup(movie => movie.PortalId, StringComparer.Ordinal);
        foreach (var item in items.Values)
        {
            if (item.Id.Length != 64 || !item.Id.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(item.Title)) continue;
            if (!RemoteFiles.IsPublicHttps(item.SourceUrl)) continue;
            total++;
            var movie = existing[item.Id].FirstOrDefault();
            if (movie == null) { movie = new Movie { PortalId = item.Id, Title = item.Title, Year = item.Year }; library.Movies.Add(movie); imported++; }
            movie.SourceUrl = item.SourceUrl; movie.StreamUrl = RemoteFiles.IsPublicHttps(item.StreamUrl) ? item.StreamUrl : ""; movie.DownloadUrl = RemoteFiles.IsPublicHttps(item.DownloadUrl) ? item.DownloadUrl : ""; movie.Description = item.Description;
            if (item.CoverUrl.Length > 0 && (movie.Cover.Length == 0 || !File.Exists(movie.Cover)))
            {
                try
                {
                    using var cover = await http.GetAsync("/api/items/" + item.Id + "/cover", HttpCompletionOption.ResponseHeadersRead, token);
                    if (!cover.IsSuccessStatusCode) continue;
                    var image = await RemoteFiles.ReadBoundedAsync(cover, 5 * 1024 * 1024, token);
                    if (!RemoteFiles.IsImage(image)) continue;
                    var dir = System.IO.Path.Combine(Library.DataDirectory, "covers"); Directory.CreateDirectory(dir); var path = System.IO.Path.Combine(dir, "portal-" + item.Id + ".img"); await File.WriteAllBytesAsync(path, image, token); movie.Cover = path; movie.CoverSource = item.SourceUrl;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested) { }
            }
        }
        return new PortalSyncResult(imported, total);
    }
    public static string AccessLink(PortalConnection connection) => connection.Endpoint.TrimEnd('/') + "/#connect=" + Uri.EscapeDataString(connection.Key);
}
public static class RemoteFiles
{
    public static bool IsPublicHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" || u.UserInfo.Length > 0 || !u.IsDefaultPort || !u.Host.Contains('.') || IPAddress.TryParse(u.Host, out _) || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        var p = address.GetAddressBytes(); if (p.Length == 16) return (p[0] & 0xe0) == 0x20 && !(p[0]==0x20 && p[1]==1 && p[2]==0x0d && p[3]==0xb8);
        return !(p[0] is 0 or 10 or 127 || p[0]>=224 || p[0]==169&&p[1]==254 || p[0]==172&&p[1]>=16&&p[1]<=31 || p[0]==192&&(p[1] is 168 or 0 or 2) || p[0]==100&&p[1]>=64&&p[1]<=127 || p[0]==198&&(p[1] is 18 or 19 or 51) || p[0]==203&&p[1]==0);
    }
    public static async Task ValidateAsync(string url, CancellationToken token)
    {
        if (!IsPublicHttps(url)) throw new InvalidDataException("O link precisa ser HTTPS e de uma origem pública.");
        var addresses = await Dns.GetHostAddressesAsync(new Uri(url).Host, token); if (addresses.Length==0 || addresses.Any(a=>!IsPublicAddress(a))) throw new InvalidDataException("A origem não é um servidor público válido.");
    }
    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int max, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Resposta maior que o limite permitido.");
        await using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var chunk=new byte[8192]; int count;
        while((count=await input.ReadAsync(chunk,token))>0){if(output.Length+count>max)throw new InvalidDataException("Resposta maior que o limite permitido.");output.Write(chunk,0,count);}return output.ToArray();
    }
    public static bool IsImage(byte[] bytes) => bytes.Length>8 && (bytes[0]==0xff&&bytes[1]==0xd8 || bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) || Encoding.ASCII.GetString(bytes,0,4)=="RIFF" || Encoding.ASCII.GetString(bytes,0,3)=="GIF");
    public static async Task DownloadAsync(string url, string destination, IProgress<(long Received,long? Total)> progress, CancellationToken token,HttpMessageHandler? handler=null,Func<string,CancellationToken,Task>? validate=null)
    {
        using var http = new HttpClient(handler??new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) { Timeout=Timeout.InfiniteTimeSpan };
        var current=url; HttpResponseMessage? response=null;
        try
        {
            for(var redirects=0;redirects<5;redirects++)
            {
                await (validate??ValidateAsync)(current,token); using var request = new HttpRequestMessage(HttpMethod.Get,current); response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
                if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308) { current=new Uri(new Uri(current),response.Headers.Location??throw new InvalidDataException("Redirecionamento inválido.")).AbsoluteUri;response.Dispose();response=null;continue; } break;
            }
            if(response==null)throw new InvalidDataException("A origem redirecionou muitas vezes.");response.EnsureSuccessStatusCode();
            var type=response.Content.Headers.ContentType?.MediaType??"";
            if(!(type.StartsWith("video/")||type is "application/octet-stream" or "binary/octet-stream" or "application/mp4"))throw new InvalidDataException("A origem não forneceu um arquivo de vídeo para baixar.");
            var temp=destination+".cinevo-"+Guid.NewGuid().ToString("N")+".part";
            try
            {
                await using(var input=await response.Content.ReadAsStreamAsync(token))
                await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,true))
                {
                    var chunk=new byte[128*1024];long received=0;int count;var last=Environment.TickCount64;
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                    while(true){timeout.CancelAfter(TimeSpan.FromSeconds(90));count=await input.ReadAsync(chunk,timeout.Token);if(count==0)break;await output.WriteAsync(chunk.AsMemory(0,count),token);received+=count;if(Environment.TickCount64-last>200){progress.Report((received,response.Content.Headers.ContentLength));last=Environment.TickCount64;}}
                    if(received==0 || response.Content.Headers.ContentLength is long expected && received!=expected)throw new InvalidDataException("O download ficou incompleto. Tente novamente.");
                    progress.Report((received,response.Content.Headers.ContentLength));
                }
                token.ThrowIfCancellationRequested(); File.Move(temp,destination,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        finally{response?.Dispose();}
    }
}
// Windows DPAPI binds the portal credential to the signed-in Windows user.
public static class LocalSecret
{
    [StructLayout(LayoutKind.Sequential)]private struct Blob { public int Length; public IntPtr Data; }
    public static byte[] Protect(byte[] bytes)=>Convert(bytes,true);
    public static byte[] Unprotect(byte[] bytes)=>Convert(bytes,false);
    private static byte[] Convert(byte[] bytes,bool encrypt)
    {
        var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)}; Marshal.Copy(bytes,0,input.Data,bytes.Length);Blob output=default;
        try{var ok=encrypt?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new System.ComponentModel.Win32Exception();var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;}
        finally{Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
    [DllImport("crypt32",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32")]private static extern IntPtr LocalFree(IntPtr memory);
}
