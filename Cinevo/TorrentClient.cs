using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cinevo;

internal sealed record TorrentConnection(int Port, string Username, string Password)
{
    private static string FilePath => Path.Combine(Library.DataDirectory, "torrent.dat");
    public static TorrentConnection? Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<TorrentConnection>(LocalSecret.Unprotect(File.ReadAllBytes(FilePath))) : null;

    public void Save()
    {
        Directory.CreateDirectory(Library.DataDirectory);
        var temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, LocalSecret.Protect(JsonSerializer.SerializeToUtf8Bytes(this)));
        File.Move(temp, FilePath, true);
    }
}

internal sealed record TorrentTransfer(string Hash, string Name, double Progress, long Size, long Completed,
    [property: JsonPropertyName("dlspeed")] long Speed, long Eta, string State,
    [property: JsonPropertyName("save_path")] string SavePath);

internal sealed record TorrentFile(int Index, string Name, double Progress, int Priority, long Size);

internal sealed class TorrentClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TorrentConnection connection;
    private readonly HttpClient http;
    private readonly SemaphoreSlim login = new(1);
    private bool authenticated;
    private DateTime retryLoginAfter;

    public TorrentClient(TorrentConnection connection)
    {
        this.connection = connection;
        var origin = new Uri($"http://127.0.0.1:{connection.Port}/");
        http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = origin, Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 8 * 1024 * 1024
        };
        http.DefaultRequestHeaders.Referrer = origin;
        http.DefaultRequestHeaders.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
    }

    public static bool IsHash(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);
    public static bool IsMagnet(string? value) => value is { Length: > 0 and <= 4096 } &&
        !value.Any(char.IsControl) && System.Text.RegularExpressions.Regex.IsMatch(value,
            @"\Amagnet:\?(?:[^&]+&)*xt=urn:(?:btih:(?:[a-f0-9]{40}|[a-z2-7]{32})|btmh:1220[a-f0-9]{64})(?:&|\z)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private async Task AuthenticateAsync(bool expired, CancellationToken token)
    {
        await login.WaitAsync(token);
        try
        {
            if (authenticated && !expired) return;
            if (DateTime.UtcNow < retryLoginAfter)
                throw new InvalidDataException("O qBittorrent recusou o acesso. Confira usuário e senha em Configurar conexão.");
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = connection.Username, ["password"] = connection.Password
            });
            using var result = await http.PostAsync("api/v2/auth/login", body, token);
            if (result.StatusCode != HttpStatusCode.NoContent &&
                (!result.IsSuccessStatusCode || (await result.Content.ReadAsStringAsync(token)).Trim() != "Ok."))
            {
                authenticated = false;
                retryLoginAfter = DateTime.UtcNow.AddMinutes(5);
                throw new InvalidDataException("O qBittorrent recusou o acesso. Confira usuário e senha da Interface Web.");
            }
            authenticated = true;
        }
        finally { login.Release(); }
    }

    private async Task<string> SendAsync(string route, CancellationToken token, Func<HttpContent>? content = null,
        string? legacyRoute = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await AuthenticateAsync(false, timeout.Token);
            using var first = await Send(route);
            if (first.StatusCode == HttpStatusCode.NotFound && legacyRoute != null)
            {
                using var legacy = await Send(legacyRoute);
                return await Read(legacy);
            }
            if (first.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                await AuthenticateAsync(true, timeout.Token);
                using var retry = await Send(route);
                if (retry.StatusCode == HttpStatusCode.NotFound && legacyRoute != null)
                {
                    using var legacy = await Send(legacyRoute);
                    return await Read(legacy);
                }
                return await Read(retry);
            }
            return await Read(first);
        }
        catch (HttpRequestException)
        {
            throw new InvalidDataException("Não foi possível acessar o qBittorrent. Abra-o e confira a porta da Interface Web.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidDataException("O qBittorrent demorou para responder.");
        }

        async Task<HttpResponseMessage> Send(string path)
        {
            using var request = new HttpRequestMessage(content == null ? HttpMethod.Get : HttpMethod.Post, "api/v2/" + path);
            request.Content = content?.Invoke();
            return await http.SendAsync(request, timeout.Token);
        }

        async Task<string> Read(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException("O qBittorrent recusou a operação.");
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
    }

    public Task<string> VersionAsync(CancellationToken token) => SendAsync("app/version", token);
    public Task<TorrentTransfer[]> TransfersAsync(CancellationToken token, string? hash = null) =>
        ReadAsync<TorrentTransfer[]>("torrents/info?sort=added_on&reverse=true" + (hash == null ? "" : "&hashes=" + hash), token);
    public Task<TorrentFile[]> FilesAsync(string hash, CancellationToken token) =>
        ReadAsync<TorrentFile[]>("torrents/files?hash=" + hash, token);
    public Task SetRunningAsync(string hash, bool start, CancellationToken token) =>
        SendAsync(start ? "torrents/start" : "torrents/stop", token,
            () => new FormUrlEncodedContent(new Dictionary<string,string> { ["hashes"] = hash }), start ? "torrents/resume" : "torrents/pause");

    public async Task AddAsync(string address, CancellationToken token)
    {
        if (!IsMagnet(address)) throw new InvalidDataException("Cole um link magnet válido.");
        var result = await SendAsync("torrents/add", token, () =>
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(address), "urls");
            return form;
        });
        if (result.Trim() == "Fails.") throw new InvalidDataException("O qBittorrent não aceitou este link magnet.");
    }

    private async Task<T> ReadAsync<T>(string route, CancellationToken token)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(await SendAsync(route, token), Json)
                ?? throw new InvalidDataException("O qBittorrent não retornou dados.");
        }
        catch (JsonException) { throw new InvalidDataException("A resposta da Interface Web do qBittorrent é inválida."); }
    }

    public void Dispose() => http.Dispose();
}
