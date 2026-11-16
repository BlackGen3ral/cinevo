using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cinevo;

internal sealed record TorrentConnection(int Port, string Username, string Password, bool LocalWithoutPassword = false)
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
    [property: JsonPropertyName("save_path")] string SavePath,
    [property: JsonPropertyName("num_seeds")] int Seeds = -1,
    [property: JsonPropertyName("num_leechs")] int Leechers = -1, int Priority = 0);

internal sealed record TorrentFile(int Index, string Name, double Progress, int Priority, long Size);
internal sealed record TorrentTracker(string Url, int Status,
    [property: JsonPropertyName("num_peers")] int Peers,
    [property: JsonPropertyName("num_seeds")] int Seeds, string Msg);
internal sealed record TorrentNetwork(
    [property: JsonPropertyName("connection_status")] string Connection,
    [property: JsonPropertyName("dht_nodes")] int Dht);

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
        http = new HttpClient(new HttpClientHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = !connection.LocalWithoutPassword
        })
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

    internal static string? MagnetHash(string address)
    {
        if (!IsMagnet(address)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(address,
            @"(?:\?|&)xt=urn:btih:([a-f0-9]{40}|[a-z2-7]{32})(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(address,
                @"(?:\?|&)xt=urn:btmh:1220([a-f0-9]{64})(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
        var encoded = match.Groups[1].Value.ToUpperInvariant();
        if (encoded.Length == 40) return encoded.ToLowerInvariant();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new byte[20];
        int buffer = 0, bits = 0, index = 0;
        foreach (char symbol in encoded)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(symbol);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes[index++] = (byte)(buffer >> bits);
        }
        return Convert.ToHexStringLower(bytes);
    }

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
            if (result.StatusCode == HttpStatusCode.NotFound) throw WrongPort();
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
            if (!connection.LocalWithoutPassword) await AuthenticateAsync(false, timeout.Token);
            using var first = await Send(route);
            if (first.StatusCode == HttpStatusCode.NotFound && legacyRoute != null)
            {
                using var legacy = await Send(legacyRoute);
                return await Read(legacy);
            }
            if (first.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                if (connection.LocalWithoutPassword)
                    throw new InvalidDataException("O qBittorrent recusou a conexão sem senha. Em Interface Web, marque “Bypass authentication for clients on localhost” e aplique.");
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
            var processes = System.Diagnostics.Process.GetProcessesByName("qbittorrent");
            foreach (var process in processes) process.Dispose();
            throw new InvalidDataException(processes.Length == 0
                ? "O qBittorrent está fechado. Clique em Abrir qBittorrent e tente novamente."
                : $"O qBittorrent está aberto, mas a Interface Web não respondeu na porta {connection.Port}. Confira a porta em Configurar conexão.");
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
            if (response.StatusCode == HttpStatusCode.NotFound) throw WrongPort();
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException("O qBittorrent recusou a operação.");
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
    }

    private InvalidDataException WrongPort() => new(
        $"O qBittorrent não respondeu na porta {connection.Port}. Outro programa pode estar usando a porta. Em Interface Web do qBittorrent, escolha outra porta e informe a mesma no Cinevo.");

    public Task<string> VersionAsync(CancellationToken token) => SendAsync("app/version", token);
    public Task<TorrentTracker[]> TrackersAsync(string hash, CancellationToken token) =>
        ReadAsync<TorrentTracker[]>("torrents/trackers?hash=" + hash, token);
    public Task<TorrentNetwork> NetworkAsync(CancellationToken token) => ReadAsync<TorrentNetwork>("transfer/info", token);
    public Task<TorrentTransfer[]> TransfersAsync(CancellationToken token, string? hash = null) =>
        ReadAsync<TorrentTransfer[]>("torrents/info?sort=added_on&reverse=true" + (hash == null ? "" : "&hashes=" + hash), token);
    public Task<TorrentFile[]> FilesAsync(string hash, CancellationToken token) =>
        ReadAsync<TorrentFile[]>("torrents/files?hash=" + hash, token);
    public Task SetRunningAsync(string hash, bool start, CancellationToken token) =>
        SendAsync(start ? "torrents/start" : "torrents/stop", token,
            () => new FormUrlEncodedContent(new Dictionary<string,string> { ["hashes"] = hash }), start ? "torrents/resume" : "torrents/pause");

    public Task ReannounceAsync(string hash, CancellationToken token) =>
        SendAsync("torrents/reannounce", token,
            () => new FormUrlEncodedContent(new Dictionary<string, string> { ["hashes"] = hash }));

    public Task RemoveAsync(string hash, CancellationToken token) =>
        SendAsync("torrents/delete", token, () => new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["hashes"] = hash, ["deleteFiles"] = "false"
        }));

    public Task<JsonElement> PreferencesAsync(CancellationToken token) => ReadAsync<JsonElement>("app/preferences",token);
    public Task ConfigureAsync(Dictionary<string,string> values, CancellationToken token) =>
        SendAsync("app/setPreferences", token, () => new FormUrlEncodedContent(values));
    public Task ControlAsync(string method, Dictionary<string,string> values, CancellationToken token) =>
        SendAsync("torrents/" + method, token, () => new FormUrlEncodedContent(values));

    public async Task AddAsync(string address, CancellationToken token, string? folder = null, bool metadataOnly = false)
    {
        if (!IsMagnet(address)) throw new InvalidDataException("Cole um link magnet válido.");
        var result = await SendAsync("torrents/add", token, () =>
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(address), "urls");
            if (metadataOnly) { form.Add(new StringContent("MetadataReceived"), "stopCondition"); form.Add(new StringContent("false"), "stopped"); form.Add(new StringContent("false"), "paused"); }
            if (!string.IsNullOrWhiteSpace(folder)) form.Add(new StringContent(folder), "savepath");
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
