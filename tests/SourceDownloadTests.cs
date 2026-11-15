using Cinevo;

internal static class SourceDownloadTests
{
    internal static void Run()
    {
        var unavailable = PortalService.ImportResponseError(503, System.Text.Encoding.UTF8.GetBytes("error code: 1102"));
        var rejected = PortalService.ImportResponseError(403, System.Text.Encoding.UTF8.GetBytes("<html>Denied</html>"));
        var specific = PortalService.ImportResponseError(422, System.Text.Encoding.UTF8.GetBytes("{\"error\":\"Origem incompatível\"}"));
        if (!unavailable.Message.Contains("indisponível") || !rejected.Message.Contains("recusou") || specific.Message != "Origem incompatível")
            throw new Exception("A resposta de importação perdeu a causa da falha.");
        var dynamicHtml = """
            <script>var LUMENDownloadConfig = {"ajaxUrl":"https://example.org/wp-admin/admin-ajax.php","action":"lumen_dl_reveal"};</script>
            <div data-dl-post="123"><div class="lumen-item odl-item"><span class="lumen-bloco-q q-fhd">1080P</span><span class="lumen-item-fmt">Episódio 01</span>
            <button class="odl-open" data-dl-group="dual_audio" data-dl-index="0" data-dl-kind="download">Baixar</button>
            <button class="odl-copy" data-dl-group="dual_audio" data-dl-index="0" data-dl-kind="download">Copiar</button></div></div>
            """;
        var buttons = SourceDownloads.Parse(dynamicHtml, new Uri("https://example.org/film"));
        if (buttons.Count != 1 || buttons[0].Kind != "resolve" || !buttons[0].Label.Contains("1080P") ||
            !buttons[0].Label.Contains("Episódio 01") || !buttons[0].Url.EndsWith("#cinevo-download=dual_audio/0"))
            throw new Exception("Os botões públicos carregados por clique não foram identificados corretamente.");
        var magnet = "magnet:?xt=urn:btih:" + new string('a', 40) + "&amp;dn=Demo.1080p&amp;tr=udp%3A%2F%2Ftracker.example.org";
        var html = $"""
            <script>const ignored='<a download href="/wrong.mp4">Baixar</a>';</script>
            <a href="{magnet}"><span>Torrent</span></a>
            <a href="{magnet}">Repetido</a>
            <a download href="/video.mp4"><b>720p</b></a>
            <a href="/categoria/1080p/">1080p</a>
            <a download href="https://127.0.0.1/private.mp4">Baixar</a>
            <a href="javascript:alert(1)">Download</a>
            """;
        var options = SourceDownloads.Parse(html, new Uri("https://example.org/film"));
        if (options.Count != 2 || options[0].Kind != "torrent" || options[0].Label != "1080p" ||
            options[0].Url.Contains("&amp;") || options[1].Kind != "file" || options[1].Url != "https://example.org/video.mp4")
            throw new Exception("A consulta local não preservou as opções públicas de download.");
        Console.WriteLine("PASS: consulta local com magnet, resolução, entidades, arquivo direto, duplicatas e links inválidos.");
    }
}
