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

    // Only offer links to download destinations; a film recommendation is another page.
    internal static bool IsOfferedDestination(string url, string label, Uri origin, bool explicitDownload = false)
    {
        if (TorrentClient.IsMagnet(url)) return true;
        if (!RemoteFiles.IsPublicHttps(url) || !Uri.TryCreate(url, UriKind.Absolute, out var target)) return false;
        if (Regex.IsMatch(target.AbsolutePath, @"^/(?:categoria|category|tag|genre|genero|qualidade|quality|page|pagina)/", RegexOptions.IgnoreCase, RegexTimeout)) return false;
        if (DynamicDownloads.IsLinkLiberado(url) || explicitDownload) return true;
        var offered = Regex.IsMatch(label, @"\b(?:download|baixar|\d{3,4}p)\b", RegexOptions.IgnoreCase, RegexTimeout);
        if (!offered) return false;
        if (Scanner.Extensions.Contains(Path.GetExtension(target.AbsolutePath)) || target.AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return true;
        var words = CoverService.Normalize(Quality.Replace(label, " "));
        words = Regex.Replace(words, @"\b(?:download|baixar|baixe|fazer|faca|o|do|de|da|a|em|no|na|para|por|aqui|clique|click|link|links|direto|direct|arquivo|file|torrent|dual|audio|dublado|legendado|legenda|portugues|ingles|pt|br|hd|sd|web|dl|bluray|x264|x265|hevc|h264|h265|hdr|hdr10|dv|dolby|atmos|truehd|ac3|eac3|aac|mp4|mkv|gb|mb|gib|mib|kb|b|mega|drive|google|gdrive|onedrive|mediafire|opcao|option|episodio|episode|ep|temporada|season|[0-9]+)\b", " ", RegexOptions.IgnoreCase, RegexTimeout);
        if (Regex.IsMatch(words, @"[a-z]", RegexOptions.IgnoreCase, RegexTimeout)) return false;
        return target.Host != origin.Host || target.AbsolutePath == origin.AbsolutePath ||
            Regex.IsMatch(target.AbsolutePath, @"(?:^|/)(?:download|downloads|baixar|out|go|redirect|get|dl)(?:/|\.|$)", RegexOptions.IgnoreCase, RegexTimeout);
    }

    internal static List<PortalDownload> FilterOptions(IEnumerable<PortalDownload> options, string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var origin)) return [];
        return options.Where(o => o.Kind == "torrent" ? TorrentClient.IsMagnet(o.Url) :
            o.Kind == "file" ? RemoteFiles.IsPublicHttps(o.Url) :
            o.Kind == "resolve" ? DynamicDownloads.IsLinkLiberado(o.Url) || o.Url.StartsWith(source.Split('#')[0]+"#cinevo-download=", StringComparison.Ordinal) :
            o.Kind == "link" && IsOfferedDestination(o.Url, o.Label, origin)).DistinctBy(o=>o.Url).Take(20).ToList();
    }

    private static string DownloadContent(string html)
    {
        html=Regex.Replace(html,@"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>","",RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexTimeout);
        var output=new StringBuilder(html.Length);var stack=new List<(string Tag,bool Skip)>();var ignored=0;var previous=0;
        foreach(Match token in Regex.Matches(html,@"<(?<close>/)?(?<tag>nav|aside|footer|div|section|article|ul|ol)\b(?<attrs>[^>]{0,12000})>",RegexOptions.IgnoreCase,RegexTimeout)){
            if(ignored==0)output.Append(html,previous,token.Index-previous);
            var tag=token.Groups["tag"].Value.ToLowerInvariant();
            if(token.Groups["close"].Success){var wasIgnored=ignored>0;var index=stack.FindLastIndex(f=>f.Tag==tag);if(index>=0){for(var i=index;i<stack.Count;i++)if(stack[i].Skip)ignored--;stack.RemoveRange(index,stack.Count-index);}if(!wasIgnored)output.Append(token.Value);}
            else{var markers=string.Join(" ",Attributes.Matches(token.Groups["attrs"].Value).Cast<Match>().Where(m=>m.Groups["key"].Value.ToLowerInvariant() is "class" or "id" or "role").Select(m=>m.Groups["value"].Value));
                var skip=tag is "nav" or "aside" or "footer" || markers.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Any(v=>v is "sidebar" or "site-sidebar" or "widget-area" or "complementary") || Regex.IsMatch(markers,@"(?:^|[\s_-])(?:related|relacionados?|recommend(?:ed|ations?)?|suggest(?:ed|ions?)?|recent-posts|latest-posts|recomendados?|mais-filmes|navigation)(?:$|[\s_-])",RegexOptions.IgnoreCase,RegexTimeout);
                if(stack.Count>=256)throw new InvalidDataException("A estrutura da página é complexa demais para consultar.");stack.Add((tag,skip));if(skip)ignored++;if(ignored==0)output.Append(token.Value);}
            previous=token.Index+token.Length;
        }
        if(ignored==0)output.Append(html,previous,html.Length-previous);return output.ToString();
    }

    internal static List<PortalDownload> Parse(string html, Uri origin)
    {
        var result = new Dictionary<string, PortalDownload>(StringComparer.Ordinal);
        var hasDynamic=html.Contains("LUMENDownloadConfig",StringComparison.Ordinal);html=DownloadContent(html);
        foreach (var option in DynamicDownloads.Parse((hasDynamic?"LUMENDownloadConfig":"")+html, origin)) result.TryAdd(option.Url, option);
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
            var liberated = DynamicDownloads.IsLinkLiberado(address.AbsoluteUri);
            var explicitDownload = liberated || Regex.IsMatch(anchor.Groups["attrs"].Value, @"(?:^|\s)download(?:\s|=|$)", RegexOptions.IgnoreCase, RegexTimeout);
            if (!IsOfferedDestination(torrent?href:address.AbsoluteUri,name,origin,explicitDownload)) continue;
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
            result.TryAdd(url, new PortalDownload(url, name[..Math.Min(name.Length, 160)], torrent ? "torrent" : liberated ? "resolve" : direct ? "file" : "link"));
            if (result.Count >= 20) break;
        }
        return result.Values.ToList();
    }

    internal static async Task<List<PortalDownload>> LoadAsync(string source, CancellationToken token)
    {
        var page = await ReadPageAsync(source, token);
        return Parse(page.Html, page.Url);
    }

    internal static async Task<(string Html, Uri Url)> ReadPageAsync(string source, CancellationToken token)
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
                if (type is not ("text/html" or "application/xhtml+xml")) return ("", new Uri(current));
                var bytes = await RemoteFiles.ReadBoundedAsync(response, 2 * 1024 * 1024, timeout.Token);
                var encoding = Encoding.UTF8;
                try { if (response.Content.Headers.ContentType?.CharSet is { } charset) encoding = Encoding.GetEncoding(charset.Trim('"')); }
                catch (ArgumentException) { }
                return (encoding.GetString(bytes), new Uri(current));
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
