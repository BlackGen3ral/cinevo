using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

public partial class MainWindow
{
    private static readonly HttpClient CatalogHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "Cinevo/" + Updater.CurrentVersion } }
    };
    private readonly Dictionary<string, (DateTime Saved, Dictionary<string, (string Cover, string Year)> Art)> catalogLayouts = new();

    private async Task<PortalImportBatch> ImportLocalCatalogAsync(string address, CancellationToken token)
    {
        address = CatalogWatch.NormalizeImportAddress(address);
        var origin = new Uri(address);
        var api = new UriBuilder(origin) { Fragment = "" };
        var isApi = origin.AbsolutePath.TrimEnd('/').EndsWith("/wp-json/wp/v2/posts", StringComparison.Ordinal);
        if (!isApi)
        {
            api.Path = "/wp-json/wp/v2/posts";
            var page = Regex.Match(origin.AbsolutePath, @"/page/(\d+)/?$").Groups[1].Value;
            var slug = origin.AbsolutePath.Trim('/');
            api.Query = page.Length > 0 ? "page=" + page : slug.Length > 0 ? "slug=" + Uri.EscapeDataString(slug) : "page=1";
        }
        var queryParts = api.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var requestedSize = queryParts.FirstOrDefault(part => part.StartsWith("per_page=", StringComparison.Ordinal))?[9..];
        var pageSize = int.TryParse(requestedSize, out var savedSize) && savedSize is >= 1 and <= 100 ? savedSize : 100;
        api.Query = string.Join("&", queryParts.Where(part => !part.StartsWith("per_page=") &&
            !part.StartsWith("_embed=") && !part.StartsWith("_fields="))) +
            $"&per_page={pageSize}&_embed=wp:featuredmedia&_fields=id,link,title,excerpt,_links,_embedded";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var http = CatalogHttp;
        var current = api.Uri;
        for (var redirect = 0; redirect < 5; redirect++)
        {
            await RemoteFiles.ValidateAsync(current.AbsoluteUri, timeout.Token);
            using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location == null) break;
                current = new Uri(current, response.Headers.Location);
                continue;
            }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new InvalidDataException("O portal está indisponível e a origem não ofereceu uma listagem pública compatível. Tente novamente mais tarde ou importe a página de um filme.");
            var bytes = await RemoteFiles.ReadBoundedAsync(response, 2 * 1024 * 1024, timeout.Token);
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("A origem retornou uma listagem inválida.");
            var pageQuery = current.Query.TrimStart('?').Split('&');
            var pageParameter = pageQuery.FirstOrDefault(part => part.StartsWith("page="))?[5..];
            var catalogPage = int.TryParse(pageParameter, out var catalogNumber) ? catalogNumber : 1;
            var posts = json.RootElement.EnumerateArray().Where(post => post.ValueKind == JsonValueKind.Object).ToArray();
            var existingSources = library.Movies.Select(movie => movie.SourceUrl).ToHashSet(StringComparer.Ordinal);
            var needsArt = posts.Any(post => post.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String &&
                !existingSources.Contains(link.GetString()!) && (!post.TryGetProperty("_embedded", out var embedded) ||
                !embedded.TryGetProperty("wp:featuredmedia", out var images) || images.ValueKind != JsonValueKind.Array || images.GetArrayLength() == 0));
            var art = needsArt ? await ReadBatchArtAsync(http, origin, catalogPage, pageSize, timeout.Token) : [];
            var added = new List<Movie>();
            var count = 0;
            foreach (var post in posts)
            {
                if (!post.TryGetProperty("link", out var link) || link.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(link.GetString(), UriKind.Absolute, out var source) || source.Host != origin.Host ||
                    !RemoteFiles.IsPublicHttps(source.AbsoluteUri) || !post.TryGetProperty("title", out var titleNode) ||
                    !titleNode.TryGetProperty("rendered", out var titleValue) || titleValue.ValueKind != JsonValueKind.String) continue;
                var title = CatalogText(titleValue.GetString()!, 240);
                if (title.Length == 0) continue;
                count++;
                if (!existingSources.Add(source.AbsoluteUri)) continue;
                var movie = new Movie
                {
                    Title = title, SourceUrl = source.AbsoluteUri, ImportedFrom = origin.GetLeftPart(UriPartial.Authority) + "/",
                    ImportedOnly = true, PortalId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri))),
                    Year = Regex.Match(title, @"\b(?:19|20)\d{2}\b").Value
                };
                if (post.TryGetProperty("excerpt", out var excerpt) && excerpt.TryGetProperty("rendered", out var synopsis) && synopsis.ValueKind == JsonValueKind.String)
                    movie.Description = CatalogText(synopsis.GetString()!, 2000);
                if (post.TryGetProperty("_embedded", out var embedded) && embedded.TryGetProperty("wp:featuredmedia", out var images) && images.ValueKind == JsonValueKind.Array &&
                    images.GetArrayLength() > 0 && images[0].TryGetProperty("source_url", out var cover) && cover.ValueKind == JsonValueKind.String && RemoteFiles.IsPublicHttps(cover.GetString()!))
                    movie.PortalCoverUrl = cover.GetString()!;
                if (art.TryGetValue(source.AbsoluteUri, out var poster))
                {
                    if (movie.PortalCoverUrl.Length == 0) movie.PortalCoverUrl = poster.Cover;
                    if (movie.Year.Length == 0) movie.Year = poster.Year;
                }
                library.Movies.Add(movie);
                added.Add(movie);
            }
            try { library.Save(); }
            catch { foreach (var movie in added) library.Movies.Remove(movie); throw; }
            Refresh();
            var query = current.Query.TrimStart('?').Split('&');
            var pageValue = query.FirstOrDefault(part => part.StartsWith("page="))?[5..];
            var pageNumber = int.TryParse(pageValue, out var pageIndex) ? pageIndex : 1;
            var pages = response.Headers.TryGetValues("X-WP-TotalPages", out var pageHeaders) && int.TryParse(pageHeaders.FirstOrDefault(), out var pageCount) ? pageCount : 1;
            var total = response.Headers.TryGetValues("X-WP-Total", out var totals) && int.TryParse(totals.FirstOrDefault(), out var totalCount) ? totalCount : count;
            var next = new UriBuilder(current) { Query = string.Join("&", query.Where(part => !part.StartsWith("page="))) + "&page=" + (pageNumber + 1) };
            return new PortalImportBatch(count, pageNumber < pages ? next.Uri.AbsoluteUri : "", "Importação recuperada diretamente da origem.", "", total, true);
        }
        throw new InvalidDataException("A origem redirecionou a importação muitas vezes.");
    }

    private static string CatalogText(string text, int limit)
    {
        var clean = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)));
        clean = Regex.Replace(clean, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim();
        return clean[..Math.Min(limit, clean.Length)];
    }

    private static async Task<Dictionary<string, (string Cover, string Year)>> ReadCatalogArtAsync(HttpClient http, Uri address, CancellationToken token)
    {
        var result = new Dictionary<string, (string Cover, string Year)>();
        try
        {
            await RemoteFiles.ValidateAsync(address.AbsoluteUri, token);
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/html") return result;
            var bytes = await RemoteFiles.ReadBoundedAsync(response, 2 * 1024 * 1024, token);
            var html = Encoding.UTF8.GetString(bytes);
            const string pattern = "<a\\b[^>]*href=[\"'](?<url>[^\"']+)[\"'][^>]*>(?<body>.*?)</a\\s*>";
            foreach (Match card in Regex.Matches(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
            {
                if (!Regex.IsMatch(card.Groups["body"].Value, @"<h[1-6]\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200))) continue;
                if (!Uri.TryCreate(address, WebUtility.HtmlDecode(card.Groups["url"].Value), out var source) || source.Host != address.Host) continue;
                var image = Regex.Match(card.Groups["body"].Value, "<img\\b[^>]*src=[\"'](?<src>[^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
                if (!image.Success || !Uri.TryCreate(address, WebUtility.HtmlDecode(image.Groups["src"].Value), out var cover) || !RemoteFiles.IsPublicHttps(cover.AbsoluteUri)) continue;
                result.TryAdd(source.AbsoluteUri, (cover.AbsoluteUri, Regex.Match(CatalogText(card.Groups["body"].Value, 500), @"\b(?:19|20)\d{2}\b").Value));
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or RegexMatchTimeoutException) { }
        return result;
    }

    private async Task<Dictionary<string, (string Cover, string Year)>> ReadBatchArtAsync(
        HttpClient http, Uri origin, int apiPage, int pageSize, CancellationToken token)
    {
        var home = new Uri(origin, "/");
        if (!catalogLayouts.TryGetValue(home.AbsoluteUri, out var layout) || apiPage == 1 || DateTime.UtcNow - layout.Saved > TimeSpan.FromMinutes(2))
        {
            var art = await ReadCatalogArtAsync(http, home, token);
            if (catalogLayouts.Count >= 8) catalogLayouts.Clear();
            layout = (DateTime.UtcNow, art);
            catalogLayouts[home.AbsoluteUri] = layout;
        }
        var first = new Dictionary<string, (string Cover, string Year)>(layout.Art);
        var cardsPerPage = first.Count > 0 ? first.Count : 16;
        var start = (apiPage - 1) * pageSize / cardsPerPage + 1;
        var end = (apiPage * pageSize + cardsPerPage - 1) / cardsPerPage;
        using var slots = new SemaphoreSlim(3);
        var pages = Enumerable.Range(start, Math.Min(12, end - start + 1)).Where(page => page > 1);
        var results = await Task.WhenAll(pages.Select(async page =>
        {
            await slots.WaitAsync(token);
            try { return await ReadCatalogArtAsync(http, new Uri(home, "page/" + page + "/"), token); }
            finally { slots.Release(); }
        }));
        foreach (var page in results)
            foreach (var (source, art) in page) first.TryAdd(source, art);
        return first;
    }
}
