using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Cinevo;
public record CoverCandidate(string Title, string ImageUrl, string PageUrl, string Description, double Score, string Entity = "");
public sealed class CoverService : IDisposable
{
    private readonly HttpClient http;
    public CoverService(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(18);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cinevo/0.1.1 (https://github.com/BlackGen3ral/cinevo)");
    }
    public static string Normalize(string text) => Regex.Replace(new string(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    public static double Score(string requested, string year, string title, string description, int index)
    {
        var desired = Normalize(requested); var actual = Normalize(Regex.Replace(title, @"\([^)]*\)", ""));
        var words = desired.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lead = Normalize(description[..Math.Min(350, description.Length)]);
        var introduction = Regex.Match(lead, @"^(.*?)\b(?:e um|e uma|is a|is an)\b");
        var alias = introduction.Success ? introduction.Groups[1].Value : "";
        var score = actual == desired || (desired.Length > 2 && alias.Contains(desired, StringComparison.Ordinal)) ? 12d : words.Length > 0 ? words.Count(w => actual.Split(' ').Contains(w)) * 8d / words.Length : 0;
        if (score < 5) return -100;
        score += Math.Max(0, 4 - index);
        if (Regex.IsMatch(lead, @"\b(filme|film|movie)\b")) score += 5;
        else return -100;
        if (year.Length == 4)
        {
            var explicitYear = Regex.Match(title, @"\((19\d{2}|20\d{2})\)");
            if (explicitYear.Success && explicitYear.Groups[1].Value != year) score -= 14;
            else if (Regex.IsMatch(Normalize(description), @"\b" + Regex.Escape(year) + @"\b")) score += 5;
        }
        return score;
    }
    public async Task<List<CoverCandidate>> SearchAsync(string title, string year, CancellationToken token, bool includeWithoutPoster = false)
    {
        var candidates = new List<CoverCandidate>();
        var searches = new List<(string Language, string Term)> { ("pt", title) };
        var titleWords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (titleWords.Length > 6) searches.Add(("pt", string.Join(' ', titleWords.TakeLast(4))));
        searches.Add(("en", title));
        foreach (var (language, searchTitle) in searches)
        {
            var term = Uri.EscapeDataString(searchTitle);
            var uri = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=0&gsrlimit=5&gsrsearch={term}&prop=pageimages%7Cinfo%7Cextracts%7Cpageprops&ppprop=wikibase_item&piprop=thumbnail&pithumbsize=960&pilicense=any&inprop=url&exintro=1&explaintext=1&exchars=1400&exlimit=5";
            using var response = await http.GetAsync(uri, token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) continue;
            foreach (var entry in pages.EnumerateObject())
            {
                var page = entry.Value;
                var imageUrl = "";
                if (page.TryGetProperty("thumbnail", out var image))
                {
                    var url = image.GetProperty("source").GetString() ?? "";
                    if (IsImageUrl(url) && image.GetProperty("width").GetInt32() < image.GetProperty("height").GetInt32() &&
                        !Regex.IsMatch(new Uri(url).AbsolutePath, @"\.(?:webp|svg)$", RegexOptions.IgnoreCase)) imageUrl = url;
                }
                if (imageUrl.Length == 0 && !includeWithoutPoster) continue;
                var name = page.GetProperty("title").GetString()!;
                var text = page.TryGetProperty("extract", out var extract) ? extract.GetString() ?? "" : "";
                var source = page.GetProperty("fullurl").GetString()!;
                if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme != "https" || !sourceUri.Host.EndsWith(".wikipedia.org", StringComparison.OrdinalIgnoreCase)) continue;
                var entity = page.TryGetProperty("pageprops", out var props) && props.TryGetProperty("wikibase_item", out var item) ? item.GetString() ?? "" : "";
                candidates.Add(new(name, imageUrl, source, text,
                    Score(includeWithoutPoster ? title : searchTitle, year, name, text, page.GetProperty("index").GetInt32()), entity));
            }
            if (candidates.Any(c => c.Score >= 16)) break;
        }
        return candidates.Where(c => c.Score > -100).DistinctBy(c => includeWithoutPoster ? c.PageUrl : c.ImageUrl).OrderByDescending(c => c.Score).ToList();
    }

    internal async Task<MovieTrailer?> FindTrailerAsync(string entity, CancellationToken token)
    {
        if (!Regex.IsMatch(entity, @"\AQ[1-9][0-9]{0,15}\z")) return null;
        var url = $"https://www.wikidata.org/w/api.php?action=wbgetclaims&entity={entity}&property=P1651&format=json";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await RemoteFiles.ReadBoundedAsync(response, 1024 * 1024, token);
        using var json = JsonDocument.Parse(bytes);
        return ReadTrailer(json.RootElement);
    }

    internal static MovieTrailer? ReadTrailer(JsonElement data)
    {
        if (!data.TryGetProperty("claims", out var claims) || !claims.TryGetProperty("P1651", out var videos)) return null;
        foreach (var claim in videos.EnumerateArray())
        {
            if (claim.TryGetProperty("rank", out var rank) && rank.GetString() == "deprecated") continue;
            if (!claim.TryGetProperty("qualifiers", out var qualifiers) || !qualifiers.TryGetProperty("P3831", out var roles)) continue;
            if (!roles.EnumerateArray().Any(role => role.TryGetProperty("datavalue", out var dv) &&
                dv.GetProperty("value").TryGetProperty("id", out var id) && id.GetString() == "Q622550")) continue;
            if (claim.GetProperty("mainsnak").TryGetProperty("datavalue", out var value) &&
                value.GetProperty("value").GetString() is { } video && MovieTrailer.IsVideoId(video)) return new MovieTrailer(video);
        }
        return null;
    }
    public static bool IsImageUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("thumb.wikimedia.org", StringComparison.OrdinalIgnoreCase));
    public async Task<string> DownloadAsync(CoverCandidate candidate, string directory, CancellationToken token)
    {
        if (!IsImageUrl(candidate.ImageUrl)) throw new InvalidDataException("Origem de imagem inválida.");
        Directory.CreateDirectory(directory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.ImageUrl))) + ".img";
        var path = System.IO.Path.Combine(directory, name);
        if (File.Exists(path)) return path;
        using var response = await http.GetAsync(candidate.ImageUrl, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new InvalidDataException("Imagem grande demais.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
        while ((count = await input.ReadAsync(chunk, token)) > 0) { if (buffer.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("Imagem grande demais."); buffer.Write(chunk, 0, count); }
        var bytes = buffer.ToArray();
        if (!(bytes.Length > 8 && ((bytes[0] == 0xff && bytes[1] == 0xd8) || bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})))) throw new InvalidDataException("Formato de imagem não suportado.");
        var temporary = path + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    public void Dispose() => http.Dispose();
}

internal sealed record MovieTrailer(string VideoId)
{
    public static bool IsVideoId(string id) => Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z");

    public static MovieTrailer? FromUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !url.IsDefaultPort || url.UserInfo.Length > 0) return null;
        var id = url.IdnHost switch
        {
            "youtu.be" => url.AbsolutePath.Trim('/'),
            "youtube.com" or "www.youtube.com" or "m.youtube.com" when url.AbsolutePath == "/watch" =>
                url.Query.TrimStart('?').Split('&').FirstOrDefault(part => part.StartsWith("v=", StringComparison.Ordinal))?[2..],
            _ => null
        };
        return id != null && IsVideoId(id) ? new MovieTrailer(id) : null;
    }
}
