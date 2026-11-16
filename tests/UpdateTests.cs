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
        string Json(string version, string origin = "https://github.com/BlackGen3ral/cinevo/releases/download/v1.0.1/") => JsonSerializer.Serialize(new[] { new { tag_name = version, draft = false, assets = new[] { new { name = "Cinevo-Setup.exe", browser_download_url = origin + "Cinevo-Setup.exe" }, new { name = "SHA256SUMS.txt", browser_download_url = origin + "SHA256SUMS.txt" } } } });
        using var handler = new FakeHandler(Json("v1.0.1"), hash, bytes);
        using var http = new HttpClient(handler);
        var progress = new ProgressLog();
        var cache = Path.Combine(root, "update-valid");
        var installedCache = Path.Combine(cache, "1.0.0");
        Directory.CreateDirectory(installedCache);
        File.WriteAllBytes(Path.Combine(installedCache, "Cinevo-Setup.exe"), bytes);
        File.WriteAllText(Path.Combine(installedCache, "preservar.txt"), "fora do cache de instaladores");
        var supersededCache = Path.Combine(cache, "1.0.2");
        Directory.CreateDirectory(supersededCache);
        File.WriteAllBytes(Path.Combine(supersededCache, "Cinevo-Setup.exe.download"), bytes);
        var target = await Updater.DownloadAsync(http, new Version(1, 0, 0), cache, progress: progress);
        if (target == null || !File.ReadAllBytes(target).SequenceEqual(bytes)) throw new Exception("Atualização válida não foi preparada");
        if (progress.Values.Count < 2 || progress.Values[0] != new Updater.UpdateProgress("download", 0, bytes.Length) ||
            progress.Values[^1] != new Updater.UpdateProgress("verify", bytes.Length, bytes.Length))
            throw new Exception("O progresso deve informar os bytes reais antes da verificação.");
        if (File.Exists(Path.Combine(installedCache, "Cinevo-Setup.exe")) || Directory.Exists(supersededCache) ||
            !File.Exists(Path.Combine(installedCache, "preservar.txt")))
            throw new Exception("A limpeza deve remover apenas os instaladores fora de uso.");
        Updater.CleanCache(cache, new Version(1, 0, 0));
        if (!File.Exists(target)) throw new Exception("A atualização pendente foi removida antes da instalação.");
        Updater.CleanCache(cache, new Version(1, 0, 1));
        if (File.Exists(target)) throw new Exception("O instalador da versão instalada ficou no cache.");
        handler.ReleaseJson = Json("v0.1.1-beta.2");
        if (await Updater.DownloadAsync(http, new Version(0, 1, 0), Path.Combine(root, "update-beta")) == null) throw new Exception("Atualização beta não foi preparada");
        handler.ReleaseJson = Json("v1.0.0");
        if (await Updater.DownloadAsync(http, new Version(1, 0, 0), root) != null) throw new Exception("Versão igual deve ser ignorada");
        handler.ReleaseJson = Json("v1.0.1", "https://example.com/");
        try { await Updater.DownloadAsync(http, new Version(1, 0, 0), root); throw new Exception("Origem externa aceita"); } catch (InvalidDataException) { }
        handler.ReleaseJson = Json("v1.0.1"); handler.Hash = new string('0', 64);
        var badPath = Path.Combine(root, "update-corrupt");
        try { await Updater.DownloadAsync(http, new Version(1, 0, 0), badPath); throw new Exception("Download corrompido aceito"); } catch (InvalidDataException) { }
        if (Directory.GetFiles(badPath, "*", SearchOption.AllDirectories).Length > 0) throw new Exception("Download inválido não foi removido");
        using var components = new ComponentHandler();
        using var compactHttp = new HttpClient(components);
        var small = await Updater.DownloadAsync(compactHttp, new Version(0, 1, 0), Path.Combine(root,"small-update"), installedRuntimeId: components.RuntimeId);
        if (Path.GetFileName(small) != "Cinevo-Update.exe") throw new Exception("Runtime compatível deve selecionar o pacote menor.");
        var full = await Updater.DownloadAsync(compactHttp, new Version(0, 1, 0), Path.Combine(root,"full-update"), installedRuntimeId: new string('b',64));
        if (Path.GetFileName(full) != "Cinevo-Setup.exe") throw new Exception("Runtime diferente exige pacote completo.");
        components.CorruptRuntime = true;
        full = await Updater.DownloadAsync(compactHttp, new Version(0, 1, 0), Path.Combine(root,"bad-runtime"), installedRuntimeId: components.RuntimeId);
        if (Path.GetFileName(full) != "Cinevo-Setup.exe") throw new Exception("Manifesto sem integridade deve usar instalador completo.");
        Updater.CleanCache(Path.Combine(root,"small-update"), new Version(0,1,1));
        if (File.Exists(small)) throw new Exception("Pacote menor deve ser removido após instalar.");
        Console.WriteLine("PASS: atualização válida, versão igual ignorada, origem externa rejeitada e download corrompido rejeitado/removido.");
    }
    private sealed class ProgressLog : IProgress<Updater.UpdateProgress>
    {
        public List<Updater.UpdateProgress> Values { get; } = [];
        public void Report(Updater.UpdateProgress value) => Values.Add(value);
    }
    private sealed class ComponentHandler : HttpMessageHandler
    {
        public string RuntimeId => new string('a',64);
        public bool CorruptRuntime;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var origin = "https://github.com/BlackGen3ral/cinevo/releases/download/v0.1.1/";
            var runtime = Encoding.UTF8.GetBytes(RuntimeId);
            var installer = Encoding.UTF8.GetBytes("verified package");
            var names = new[]{"Cinevo-Setup.exe","Cinevo-Update.exe","runtime-id.txt","SHA256SUMS.txt"};
            var sums = string.Join("\n", names.Take(3).Select(name => Convert.ToHexString(SHA256.HashData(name=="runtime-id.txt" ? runtime : installer))+"  "+name));
            var content = request.RequestUri!.Host == "api.github.com"
                ? new StringContent(JsonSerializer.Serialize(new[]{new{tag_name="v0.1.1",draft=false,assets=names.Select(name=>new{name,browser_download_url=origin+name})}}))
                : request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt") ? new StringContent(sums)
                : new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("runtime-id.txt") ? CorruptRuntime ? Encoding.UTF8.GetBytes("corrupted") : runtime : installer) as HttpContent;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
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
