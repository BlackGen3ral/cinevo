using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cinevo;
public static class UpdateTests
{
    public static async Task Run(string root)
    {
        var bytes = Encoding.UTF8.GetBytes("fake installer for integrity testing only");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        string Json(string version, string origin = "https://github.com/BlackGen3ral/cinevo/releases/download/v1.0.1/") => JsonSerializer.Serialize(new { tag_name = version, assets = new[] { new { name = "Cinevo-Setup.exe", browser_download_url = origin + "Cinevo-Setup.exe" }, new { name = "SHA256SUMS.txt", browser_download_url = origin + "SHA256SUMS.txt" } } });
        using var handler = new FakeHandler(Json("v1.0.1"), hash, bytes);
        using var http = new HttpClient(handler);
        var target = await Updater.DownloadAsync(http, new Version(1, 0, 0), Path.Combine(root, "update-valid"));
        if (target == null || !File.ReadAllBytes(target).SequenceEqual(bytes)) throw new Exception("Atualização válida não foi preparada");
        handler.ReleaseJson = Json("v1.0.0");
        if (await Updater.DownloadAsync(http, new Version(1, 0, 0), root) != null) throw new Exception("Versão igual deve ser ignorada");
        handler.ReleaseJson = Json("v1.0.1", "https://example.com/");
        try { await Updater.DownloadAsync(http, new Version(1, 0, 0), root); throw new Exception("Origem externa aceita"); } catch (InvalidDataException) { }
        handler.ReleaseJson = Json("v1.0.1"); handler.Hash = new string('0', 64);
        var badPath = Path.Combine(root, "update-corrupt");
        try { await Updater.DownloadAsync(http, new Version(1, 0, 0), badPath); throw new Exception("Download corrompido aceito"); } catch (InvalidDataException) { }
        if (Directory.GetFiles(badPath, "*", SearchOption.AllDirectories).Length > 0) throw new Exception("Download inválido não foi removido");
        Console.WriteLine("PASS: atualização válida, versão igual ignorada, origem externa rejeitada e download corrompido rejeitado/removido.");
    }
    private sealed class FakeHandler(string releaseJson, string hash, byte[] bytes) : HttpMessageHandler
    {
        public string ReleaseJson { get; set; } = releaseJson;
        public string Hash { get; set; } = hash;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = request.RequestUri!.Host == "api.github.com" ? new StringContent(ReleaseJson) : request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt") ? new StringContent(Hash + "  Cinevo-Setup.exe\n") : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
