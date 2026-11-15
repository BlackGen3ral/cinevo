using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

internal static class DynamicDownloads
{
    internal static bool IsLinkLiberado(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        RemoteFiles.IsPublicHttps(address) && uri.Host.TrimEnd('.').Equals("linkliberado.xyz", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.TrimEnd('/').Equals("/liberar.php", StringComparison.OrdinalIgnoreCase);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static string Attribute(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        Regex.Escape(name) + "=[\"'](?<v>[^\"']*)[\"']", RegexOptions.IgnoreCase, Timeout).Groups["v"].Value);

    internal static List<PortalDownload> Parse(string html, Uri source)
    {
        var options = new List<PortalDownload>();
        if (!html.Contains("LUMENDownloadConfig", StringComparison.Ordinal)) return options;
        foreach (Match button in Regex.Matches(html, @"<button\b[^>]*>", RegexOptions.IgnoreCase, Timeout))
        {
            var tag = button.Value;
            var group = Attribute(tag, "data-dl-group");
            var index = Attribute(tag, "data-dl-index");
            if (!Attribute(tag, "class").Split(' ').Contains("odl-open") || Attribute(tag, "data-dl-kind") != "download" ||
                !Regex.IsMatch(group, @"\A[a-zA-Z0-9_-]{1,60}\z") || !int.TryParse(index, out var number) || number is < 0 or > 10000) continue;
            var blockStart = html.LastIndexOf("lumen-item odl-item", button.Index, StringComparison.Ordinal);
            var prefix = blockStart >= 0 ? html[blockStart..button.Index] : "";
            string Text(string className)
            {
                var match = Regex.Match(prefix, "<span[^>]*class=[\"'][^\"']*" + className + "[^\"']*[\"'][^>]*>(?<v>.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase, Timeout);
                return WebUtility.HtmlDecode(Regex.Replace(match.Groups["v"].Value, "<[^>]*>", "", RegexOptions.None, Timeout)).Trim();
            }
            var label = string.Join(" · ", new[] { Text("lumen-bloco-q"), Text("lumen-item-fmt"), group.Replace('_', ' ') }.Where(value => value.Length > 0));
            options.Add(new PortalDownload(new UriBuilder(source) { Fragment = "cinevo-download=" + group + "/" + number }.Uri.AbsoluteUri,
                label.Length > 0 ? label[..Math.Min(label.Length, 160)] : "Consultar download", "resolve"));
            if (options.Count >= 20) break;
        }
        return options;
    }

    internal static async Task<PortalDownload> ResolveAsync(string source, PortalDownload choice, CancellationToken token)
    {
        if (IsLinkLiberado(choice.Url)) return await ResolveLinkAsync(choice, token);
        var page = await SourceDownloads.ReadPageAsync(source, token);
        if (!Parse(page.Html, page.Url).Any(option => option.Url == choice.Url))
            throw new InvalidDataException("Essa opção mudou na origem. Reabra a ficha do filme e tente novamente.");
        var configText = Regex.Match(page.Html, @"LUMENDownloadConfig\s*=\s*(?<json>\{[^;]{1,3000}\})", RegexOptions.None, Timeout).Groups["json"].Value;
        using var config = JsonDocument.Parse(configText);
        var endpoint = config.RootElement.GetProperty("ajaxUrl").GetString() ?? "";
        var action = config.RootElement.GetProperty("action").GetString() ?? "";
        var post = Regex.Match(page.Html, "data-dl-post=[\"'](?<v>\\d+)[\"']", RegexOptions.None, Timeout).Groups["v"].Value;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var api) || api.GetLeftPart(UriPartial.Authority) != page.Url.GetLeftPart(UriPartial.Authority) ||
            action != "lumen_dl_reveal" || post.Length == 0) throw new InvalidDataException("A origem alterou a consulta de downloads. Abra a página original.");
        var key = new Uri(choice.Url).Fragment["#cinevo-download=".Length..].Split('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        await RemoteFiles.ValidateAsync(endpoint, timeout.Token);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = action, ["post_id"] = post, ["group"] = key[0], ["index"] = key[1], ["kind"] = "download"
        });
        using var response = await client.PostAsync(api, body, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await RemoteFiles.ReadBoundedAsync(response, 16 * 1024, timeout.Token));
        if (!data.RootElement.GetProperty("success").GetBoolean() || !data.RootElement.GetProperty("data").TryGetProperty("url", out var value))
            throw new InvalidDataException("A origem não disponibilizou esse link agora.");
        var url = value.GetString() ?? "";
        return await ResolveLinkAsync(new PortalDownload(url, choice.Label, "link"), token);
    }

    internal static async Task<PortalDownload> ResolveLinkAsync(PortalDownload choice, CancellationToken token)
    {
        var url = choice.Url;
        if (IsLinkLiberado(url))
        {
            var destination = new Uri(url);
            var query = destination.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("ll_magnet=", StringComparison.OrdinalIgnoreCase));
            var publicEndpoint = new UriBuilder(destination) { Query = string.Join("&", query) + "&ll_magnet=1" }.Uri.AbsoluteUri;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            await RemoteFiles.ValidateAsync(publicEndpoint, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, publicEndpoint);
            request.Headers.Accept.ParseAdd("application/json");
            using var revealed = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!revealed.IsSuccessStatusCode)
                throw new InvalidDataException("O Link Liberado não disponibilizou o download agora. Tente novamente ou abra a origem.");
            try
            {
                using var magnet = JsonDocument.Parse(await RemoteFiles.ReadBoundedAsync(revealed, 16 * 1024, timeout.Token));
                if (!magnet.RootElement.TryGetProperty("magnet", out var magnetValue) || magnetValue.ValueKind != JsonValueKind.String ||
                    !TorrentClient.IsMagnet(magnetValue.GetString()))
                    throw new InvalidDataException("O Link Liberado ainda não retornou um magnet válido. Tente novamente ou abra a origem.");
                url = magnetValue.GetString()!;
            }
            catch (JsonException) { throw new InvalidDataException("O Link Liberado retornou uma página em vez do magnet. Abra a origem e confira a liberação."); }
        }
        if (TorrentClient.IsMagnet(url)) return new PortalDownload(url, choice.Label, "torrent");
        if (RemoteFiles.IsPublicHttps(url)) return new PortalDownload(url, choice.Label,
            Scanner.Extensions.Contains(Path.GetExtension(new Uri(url).AbsolutePath)) ? "file" : "link");
        throw new InvalidDataException("O endereço retornado pela origem não é compatível.");
    }
}
