using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

internal sealed record TmdbCredit(string Name, string Role);
internal sealed record TmdbDetails(string Url, string OriginalTitle, string Overview, double Score, int Votes,
    int Minutes, string[] Genres, string[] Directors, string[] Writers, string[] Creators, TmdbCredit[] Cast, string? TrailerId,
    string Title, string Year, string Poster);

internal sealed class TmdbClient : IDisposable
{
    internal static string CredentialPath => Path.Combine(Library.DataDirectory, "tmdb.dat");
    private readonly HttpClient http;
    private readonly string credential;
    private static readonly Uri Api = new("https://api.themoviedb.org/3/");
    private static readonly Regex Season = new(@"\s*[-–:]?\s*(?:(?<number>\d{1,2})\s*[ªºoa]?\s*temporada|temporada\s*(?<number>\d{1,2})|season\s*(?<number>\d{1,2}))\s*$", RegexOptions.IgnoreCase);

    public TmdbClient(string credential, HttpMessageHandler? handler = null)
    {
        this.credential = credential;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = Api, Timeout = TimeSpan.FromSeconds(8)
        };
        if (credential.Length != 32) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    }

    public static bool ValidCredential(string value) => Regex.IsMatch(value,
        @"\A(?:[0-9a-fA-F]{32}|[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,})\z") && value.Length <= 2048;

    public static string ReadCredential() => File.Exists(CredentialPath)
        ? System.Text.Encoding.UTF8.GetString(LocalSecret.Unprotect(File.ReadAllBytes(CredentialPath))) : "";

    public static void StoreCredential(string value)
    {
        Directory.CreateDirectory(Library.DataDirectory);
        var temp = CredentialPath + ".tmp";
        try
        {
            File.WriteAllBytes(temp, LocalSecret.Protect(System.Text.Encoding.UTF8.GetBytes(value)));
            File.Move(temp, CredentialPath, true);
        }
        finally { File.Delete(temp); }
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        using var config = await GetAsync("configuration", token);
    }

    private async Task<JsonDocument> GetAsync(string route, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(8));
        if (credential.Length == 32) route += (route.Contains('?') ? "&" : "?") + "api_key=" + credential;
        using var response = await http.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, limit.Token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidDataException("O TMDB recusou a credencial. Confira o token ou a chave em Configurar TMDB.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"O TMDB está indisponível no momento (HTTP {(int)response.StatusCode}).");
        return JsonDocument.Parse(await RemoteFiles.ReadBoundedAsync(response, 5 * 1024 * 1024, limit.Token));
    }

    public async Task<TmdbDetails?> LookupAsync(string title, string year, CancellationToken token)
    {
        var season = Season.Match(title);
        if (season.Success) title = title[..season.Index].Trim();
        if (CoverService.Normalize(title).Length == 0) return null;
        using var search = await GetAsync("search/multi?language=pt-BR&include_adult=false&query=" + Uri.EscapeDataString(title), token);
        var candidates = Items(search.RootElement, "results")
            .Select(item => (Item: item, Kind: Text(item, "media_type")))
            .Where(row => row.Kind is "movie" or "tv" && (!season.Success || row.Kind == "tv"))
            .Select(row => (row.Item, row.Kind, Score: MatchScore(row.Item, row.Kind, title, year, season.Success)))
            .Where(row => row.Score > 0 && Number(row.Item, "id") > 0)
            .OrderByDescending(row => row.Score).ThenByDescending(row => Number(row.Item, "vote_count")).ToArray();
        if (candidates.Length == 0 || candidates.Length > 1 && candidates[0].Score == candidates[1].Score && year.Length == 0)
            return null;

        var (_, kind, _) = candidates[0];
        var id = Number(candidates[0].Item, "id");
        var route = $"{kind}/{id}";
        var seasonRoute = season.Success && int.TryParse(season.Groups["number"].Value, out var number) && number > 0
            ? $"{route}/season/{number}" : null;
        return await FetchDetailsAsync(route, seasonRoute, token);
    }

    internal static string? RouteFromUrl(string value)
    {
        if (value.Length > 512 || !TrySiteUrl(value, out var url)) return null;
        var match = Regex.Match(url.AbsolutePath, @"\A/(movie|tv)/([1-9][0-9]{0,9})(?:-[^/]+)?(?:/season/([0-9]{1,3}))?/?\z");
        if (!match.Success || (match.Groups[1].Value == "movie" && match.Groups[3].Success)) return null;
        var route = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
        return match.Groups[3].Success ? $"{route}/season/{int.Parse(match.Groups[3].Value)}" : route;
    }

    internal static bool IsSiteUrl(string value) => TrySiteUrl(value, out _);

    private static bool TrySiteUrl(string value, out Uri url)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) && parsed.Scheme == "https" &&
            parsed.IsDefaultPort && parsed.UserInfo.Length == 0 && parsed.IdnHost is "www.themoviedb.org" or "themoviedb.org")
        {
            url = parsed;
            return true;
        }
        url = null!;
        return false;
    }

    public Task<TmdbDetails> LookupLinkedAsync(string route, CancellationToken token)
    {
        var parts = route.Split('/');
        return FetchDetailsAsync($"{parts[0]}/{parts[1]}", parts.Length == 4 ? route : null, token);
    }

    private async Task<TmdbDetails> FetchDetailsAsync(string route, string? seasonRoute, CancellationToken token)
    {
        using var details = await GetAsync(route + "?language=pt-BR&append_to_response=credits,videos", token);
        using var seasonDetails = seasonRoute == null ? null :
            await GetAsync(seasonRoute + "?language=pt-BR&append_to_response=videos", token);
        var root = details.RootElement;
        var selected = seasonDetails?.RootElement ?? root;
        var trailer = SelectTrailer(selected);
        if (trailer == null)
        {
            try
            {
                using var fallback = await GetAsync((seasonRoute ?? route) + "/videos?language=en-US", token);
                trailer = PickVideo(Items(fallback.RootElement, "results"));
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is InvalidDataException or HttpRequestException or OperationCanceledException) { }
        }
        var credits = Property(root, "credits");
        var cast = Items(credits, "cast").Take(10)
            .Select(person => new TmdbCredit(Text(person, "name"), Text(person, "character"))).Where(person => person.Name.Length > 0).ToArray();
        var crew = Items(credits, "crew").ToArray();
        string[] Crew(params string[] jobs) => crew.Where(person => jobs.Contains(Text(person, "job")))
            .Select(person => Text(person, "name")).Where(name => name.Length > 0).Distinct().Take(8).ToArray();
        var duration = Number(root, "runtime");
        if (duration == 0 && Items(root, "episode_run_time").FirstOrDefault() is { ValueKind: JsonValueKind.Number } runtime)
            duration = runtime.GetInt32();
        var overview = Text(selected, "overview");
        if (overview.Length == 0) overview = Text(root, "overview");
        var movie = route.StartsWith("movie/", StringComparison.Ordinal);
        var title = Text(root, movie ? "title" : "name");
        if (title.Length == 0) title = Text(root, movie ? "original_title" : "original_name");
        if (seasonRoute != null) title += " Temporada " + seasonRoute.Split('/')[^1];
        var date = Text(selected, movie ? "release_date" : seasonRoute == null ? "first_air_date" : "air_date");
        var poster = Text(selected, "poster_path");
        if (poster.Length == 0) poster = Text(root, "poster_path");
        return new TmdbDetails($"https://www.themoviedb.org/{seasonRoute ?? route}?language=pt-BR",
            Text(root, route.StartsWith("movie/") ? "original_title" : "original_name"), overview,
            Property(selected, "vote_average") is { ValueKind: JsonValueKind.Number } score ? score.GetDouble() : 0,
            Number(selected, "vote_count"), duration, Items(root, "genres").Select(genre => Text(genre, "name")).ToArray(),
            Crew("Director"), Crew("Writer", "Screenplay", "Story"),
            Items(root, "created_by").Select(person => Text(person, "name")).Take(8).ToArray(), cast, trailer,
            title, date.Length >= 4 ? date[..4] : "", poster);
    }

    public async Task<string> DownloadPosterAsync(string poster, string route, CancellationToken token)
    {
        if (!Regex.IsMatch(poster, @"\A/[A-Za-z0-9_-]+\.(?:jpg|png)\z")) return "";
        var directory = Path.Combine(Library.DataDirectory, "covers");
        var path = Path.Combine(directory, "tmdb-" + route.Replace('/', '-') + "-" + poster[1..]);
        if (File.Exists(path)) return path;
        // Use a separate client so the TMDB API credential never goes to the image host.
        using var images = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await images.GetAsync("https://image.tmdb.org/t/p/w780" + poster,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) return "";
        var bytes = await RemoteFiles.ReadBoundedAsync(response, 5 * 1024 * 1024, timeout.Token);
        if (!RemoteFiles.IsImage(bytes)) return "";
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, token);
            File.Move(temp, path, true);
            return path;
        }
        finally { File.Delete(temp); }
    }

    private static int MatchScore(JsonElement item, string kind, string title, string year, bool season)
    {
        var field = kind == "movie" ? "title" : "name";
        var expected = CoverService.Normalize(title);
        if (expected != CoverService.Normalize(Text(item, field)) && expected != CoverService.Normalize(Text(item, "original_" + field))) return 0;
        var date = Text(item, kind == "movie" ? "release_date" : "first_air_date");
        if (year.Length != 4) return 2;
        if (date.StartsWith(year + "-", StringComparison.Ordinal)) return 3;
        return season || date.Length == 0 ? 1 : 0;
    }

    internal static string? SelectTrailer(JsonElement details) => PickVideo(Items(Property(details, "videos"), "results"));

    private static string? PickVideo(IEnumerable<JsonElement> videos) => videos
        .Where(video => Text(video, "site") == "YouTube" && Text(video, "type") == "Trailer" && MovieTrailer.IsVideoId(Text(video, "key")))
        .OrderByDescending(video => Text(video, "iso_639_1") == "pt" && Text(video, "iso_3166_1") == "BR")
        .ThenByDescending(video => Property(video, "official").ValueKind == JsonValueKind.True)
        .Select(video => Text(video, "key")).FirstOrDefault();

    private static JsonElement Property(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) ? value : default;
    private static IEnumerable<JsonElement> Items(JsonElement data, string name) =>
        Property(data, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];
    private static string Text(JsonElement data, string name) =>
        Property(data, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
    private static int Number(JsonElement data, string name) =>
        Property(data, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var n) ? n : 0;
    public void Dispose() => http.Dispose();
}
