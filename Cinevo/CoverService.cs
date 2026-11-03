using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Cinevo;
public record CoverCandidate(string Title, string ImageUrl, string PageUrl, string Description, double Score);
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
    public async Task<List<CoverCandidate>> SearchAsync(string title, string year, CancellationToken token)
    {
        var candidates = new List<CoverCandidate>();
        var titleWords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var searches = new List<(string Language, string Term)> { ("pt", title) };
        if (titleWords.Length > 6) searches.Add(("pt", string.Join(' ', titleWords.TakeLast(4))));
        searches.Add(("en", title));
        foreach (var (language, searchTitle) in searches)
        {
            var term = Uri.EscapeDataString(searchTitle);
            var uri = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=0&gsrlimit=5&gsrsearch={term}&prop=pageimages%7Cinfo%7Cextracts&piprop=thumbnail&pithumbsize=500&pilicense=any&inprop=url&exintro=1&explaintext=1&exchars=800&exlimit=5";
            using var response = await http.GetAsync(uri, token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) continue;
            foreach (var entry in pages.EnumerateObject())
            {
                var page = entry.Value;
                if (!page.TryGetProperty("thumbnail", out var image)) continue;
                var imageUrl = image.GetProperty("source").GetString()!;
                if (!IsImageUrl(imageUrl) || image.GetProperty("width").GetInt32() >= image.GetProperty("height").GetInt32()) continue;
                // WPF decodes JPEG and PNG on every supported Windows installation.
                if (new Uri(imageUrl).AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || new Uri(imageUrl).AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                var name = page.GetProperty("title").GetString()!;
                var text = page.TryGetProperty("extract", out var extract) ? extract.GetString() ?? "" : "";
                var source = page.GetProperty("fullurl").GetString()!;
                if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme != "https" || !sourceUri.Host.EndsWith(".wikipedia.org", StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add(new(name, imageUrl, source, text, Score(searchTitle, year, name, text, page.GetProperty("index").GetInt32())));
            }
            if (candidates.Any(c => c.Score >= 16)) break;
        }
        return candidates.Where(c => c.Score > -100).DistinctBy(c => c.ImageUrl).OrderByDescending(c => c.Score).ToList();
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
