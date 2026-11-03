using System.Net;
using System.Text.Json;
using Cinevo;
public static class CoverTests
{
    public static async Task Run(string root)
    {
        var correct = CoverService.Score("Duna Parte Dois", "2024", "Duna: Parte Dois", "Filme de ficção científica de 2024", 1);
        var wrong = CoverService.Score("Duna Parte Dois", "2024", "Duna", "Filme de ficção científica de 1984", 1);
        if (correct <= wrong || correct < 20) throw new Exception("Correspondência de título/ano inadequada");
        if (CoverService.Score("Interestelar", "2014", "Guardiões da Galáxia", "Filme de 2014 sobre uma equipe no espaço interestelar", 1) >= 16) throw new Exception("Filme sem relação com título foi aceito");
        if (CoverService.Score("Interestelar", "2014", "Interstellar", "Interstellar (no Brasil, Interestelar) é um filme épico de ficção científica.", 1) < 16) throw new Exception("Título alternativo não reconhecido");
        if (CoverService.Normalize("Ação: O Filme") != "acao o filme") throw new Exception("Normalização de acentos");
        if (CoverService.IsImageUrl("https://upload.wikimedia.org.evil.test/a.png") || CoverService.IsImageUrl("http://upload.wikimedia.org/a.png")) throw new Exception("Host de capa inseguro aceito");
        using var handler = new FakeCoverHandler(); using var service = new CoverService(handler);
        var results = await service.SearchAsync("Duna Parte Dois", "2024", CancellationToken.None);
        if (results.Count != 1 || results[0].Title != "Duna: Parte Dois") throw new Exception("Seleção de pôster inválida");
        if (handler.Requests.Any(u => u.Contains(root, StringComparison.OrdinalIgnoreCase))) throw new Exception("Caminho local enviado à busca");
        var destination = Path.Combine(root,"covers"); var path = await service.DownloadAsync(results[0], destination, CancellationToken.None);
        var requests = handler.Requests.Count; await service.DownloadAsync(results[0], destination, CancellationToken.None);
        if (!File.Exists(path) || requests != handler.Requests.Count) throw new Exception("Cache de capas não respeitado");
        handler.BadImage = true;
        try { await service.DownloadAsync(results[0] with { ImageUrl = "https://upload.wikimedia.org/corrupt.png" }, destination, CancellationToken.None); throw new Exception("Arquivo não imagem aceito"); } catch (InvalidDataException) { }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await service.SearchAsync("Duna", "2024", cancel.Token); throw new Exception("Cancelamento de capa ignorado"); } catch (OperationCanceledException) { }
        Console.WriteLine("PASS: capas por título/ano, acentos, host seguro, filtro de pôster, cache, formato inválido e cancelamento.");
    }
    private sealed class FakeCoverHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool BadImage { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!.AbsoluteUri);
            HttpContent content = request.RequestUri.Host.EndsWith("wikipedia.org") ? new StringContent(JsonSerializer.Serialize(new { query = new { pages = new Dictionary<string, object> { ["1"] = new { title="Duna: Parte Dois", index=1, extract="Filme de ficção científica de 2024.", fullurl="https://pt.wikipedia.org/wiki/Duna:_Parte_Dois", thumbnail=new { source="https://upload.wikimedia.org/poster.png",width=320,height=500 } }, ["2"] = new { title="Elenco",index=2,extract="Elenco do filme",fullurl="https://pt.wikipedia.org/wiki/Elenco",thumbnail=new {source="https://upload.wikimedia.org/cast.png",width=500,height=300} } } } })) : new ByteArrayContent(BadImage ? System.Text.Encoding.UTF8.GetBytes("<html>error</html>") : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8UkAAAAASUVORK5CYII="));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
