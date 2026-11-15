using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Cinevo;

internal static class SourceDownloads
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly Regex Anchors = new(@"<a\b(?<attrs>[^>]{0,12000})>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
    private static readonly Regex Attributes = new("(?<key>[\\w-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex Tags = new(@"<[^>]*>", RegexOptions.None, RegexTimeout);
    private static readonly Regex Quality = new(@"\b(?:4320|2160|1440|1080|720|576|480|360)[pi]\b|\b[48]k\b|\bfull[ ._-]?hd\b", RegexOptions.IgnoreCase, RegexTimeout);

    internal static List<PortalDownload> Parse(string html, Uri origin)
    {
        var result = new Dictionary<string, PortalDownload>(StringComparer.Ordinal);
        html = Regex.Replace(html, @"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        foreach (Match anchor in Anchors.Matches(html))
        {
            var attributes = Attributes.Matches(anchor.Groups["attrs"].Value)
                .Cast<Match>().GroupBy(match => match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => WebUtility.HtmlDecode(group.First().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
            if (!attributes.TryGetValue("href", out var href) || href.Length > 4096) continue;
            var torrent = TorrentClient.IsMagnet(href);
            if (!Uri.TryCreate(origin, href, out var address) || !torrent && !RemoteFiles.IsPublicHttps(address.AbsoluteUri)) continue;
            var name = WebUtility.HtmlDecode(Tags.Replace(anchor.Groups["text"].Value, " "));
            name = Regex.Replace(name, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
            var label = attributes.GetValueOrDefault("aria-label") ?? attributes.GetValueOrDefault("title") ?? "";
            if (name.Length == 0) name = label;
            var explicitDownload = Regex.IsMatch(anchor.Groups["attrs"].Value, @"(?:^|\s)download(?:\s|=|$)", RegexOptions.IgnoreCase, RegexTimeout);
            if (!torrent && !explicitDownload)
            {
                var offered = Regex.IsMatch(name, @"\b(?:download|baixar|\d{3,4}p)\b", RegexOptions.IgnoreCase, RegexTimeout);
                var category = Regex.IsMatch(address.AbsolutePath, @"^/(?:categoria|category|tag|genre|genero|qualidade|quality)/", RegexOptions.IgnoreCase, RegexTimeout);
                if (!offered || category) continue;
            }
            if (torrent && !Quality.IsMatch(name))
            {
                var displayName = href.Split('&').FirstOrDefault(part => part.StartsWith("dn=", StringComparison.OrdinalIgnoreCase));
                var quality = Quality.Match(label);
                if (!quality.Success && displayName != null)
                    quality = Quality.Match(Uri.UnescapeDataString(displayName[3..]).Replace('.', ' ').Replace('_', ' '));
                if (quality.Success) name = quality.Value;
            }
            if (name.Length == 0) name = torrent ? "Qualidade não informada" : "Baixar arquivo";
            var direct = Scanner.Extensions.Contains(Path.GetExtension(address.AbsolutePath));
            var url = torrent ? href : address.AbsoluteUri;
            result.TryAdd(url, new PortalDownload(url, name[..Math.Min(name.Length, 160)], torrent ? "torrent" : direct ? "file" : "link"));
            if (result.Count >= 20) break;
        }
        return result.Values.ToList();
    }

    internal static async Task<List<PortalDownload>> LoadAsync(string source, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cinevo/" + Updater.CurrentVersion);
        var current = source;
        try
        {
            for (var redirect = 0; redirect < 5; redirect++)
            {
                await RemoteFiles.ValidateAsync(current, timeout.Token);
                using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location == null) break;
                    current = new Uri(new Uri(current), response.Headers.Location).AbsoluteUri;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new InvalidDataException($"A página de origem recusou a consulta (HTTP {(int)response.StatusCode}). Abra a origem para conferir o acesso.");
                var type = response.Content.Headers.ContentType?.MediaType;
                if (type is not ("text/html" or "application/xhtml+xml")) return [];
                var bytes = await RemoteFiles.ReadBoundedAsync(response, 2 * 1024 * 1024, timeout.Token);
                var encoding = Encoding.UTF8;
                try { if (response.Content.Headers.ContentType?.CharSet is { } charset) encoding = Encoding.GetEncoding(charset.Trim('"')); }
                catch (ArgumentException) { }
                return Parse(encoding.GetString(bytes), new Uri(current));
            }
            throw new InvalidDataException("A origem redirecionou a consulta muitas vezes.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidDataException("A página de origem demorou para responder. Tente novamente."); }
        catch (HttpRequestException)
        { throw new InvalidDataException("Não foi possível conectar à página de origem. Confira a internet e tente novamente."); }
        catch (RegexMatchTimeoutException)
        { throw new InvalidDataException("O formato da página não pôde ser lido. Abra a origem para conferir os downloads."); }
    }
}
