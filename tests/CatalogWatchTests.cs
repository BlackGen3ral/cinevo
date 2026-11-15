using System.Text.Json;
using Cinevo;

internal static class CatalogWatchTests
{
    public static async Task Run()
    {
        var legacy = "https://example.org/wp-json/wp/v2/posts?page=49" +
            string.Concat(Enumerable.Repeat("&per_page=16&_embed=wp:featuredmedia&_fields=id,link,title", 100));
        var normalized = CatalogWatch.NormalizeImportAddress(legacy);
        if (normalized.Length > 300 || !normalized.Contains("page=49&") ||
            normalized.Split("per_page=").Length != 2 || !normalized.Contains("per_page=16"))
            throw new Exception("A continuação antiga deve manter a página e o tamanho do lote sem acumular parâmetros.");
        if (CatalogWatch.NormalizeImportAddress("https://example.org/?tag=a&tag=b") != "https://example.org/?tag=a&tag=b")
            throw new Exception("Parâmetros de endereços comuns devem ser preservados.");
        var now = DateTime.UtcNow;
        var library = new Library
        {
            Movies = [new Movie { PortalId = "catalog", SourceUrl = "https://example.org/filme",
                ImportedFrom = "https://example.org/page/4/" }]
        };
        CatalogWatch.Discover(library);
        CatalogWatch.Discover(library);
        var source = library.CatalogSources.Single();
        if (source.Address != "https://example.org/" || !CatalogWatch.IsDue(source, now))
            throw new Exception("O catálogo antigo deve ser acompanhado desde a primeira página sem fontes duplicadas.");
        if (CatalogWatch.Register(library, "https://www.themoviedb.org/movie/41") != null ||
            CatalogWatch.Register(library, "https://127.0.0.1/catalog") != null)
            throw new Exception("Endereço inválido ou ficha TMDB incluída na varredura de catálogo.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
        {
            if (address != source.Address || continuation) throw new Exception("A atualização deve começar pela origem.");
            return Task.FromResult(new PortalImportBatch(20, "https://example.org/page/5/", "", ""));
        }, now, CancellationToken.None);
        library = JsonSerializer.Deserialize<Library>(JsonSerializer.Serialize(library))!;
        source = library.CatalogSources.Single();
        if (source.Next != "https://example.org/page/5/" || !CatalogWatch.IsDue(source, now))
            throw new Exception("O cursor de paginação precisa sobreviver ao reinício do app.");
        var longCatalog = JsonSerializer.Deserialize<CatalogSource>(JsonSerializer.Serialize(source))!;
        await CatalogWatch.AdvanceAsync(longCatalog, (address, continuation, token) =>
        {
            if (address != source.Address || continuation) throw new Exception("As páginas novas devem ser revisitadas durante catálogos longos.");
            return Task.FromResult(new PortalImportBatch(21, "https://example.org/page/5/", "", ""));
        }, now.AddMinutes(30), CancellationToken.None);
        if (longCatalog.Next != "https://example.org/page/5/") throw new Exception("A busca de novidades perdeu a paginação anterior.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
            Task.FromResult(new PortalImportBatch(3, "https://example.org/page/7/", "", "Site indisponível")), now, CancellationToken.None);
        if (source.Next != "https://example.org/page/7/" || CatalogWatch.IsDue(source, now.AddMinutes(4)))
            throw new Exception("Uma página bloqueada precisa ser repetida depois, sem perder o cursor.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
        {
            if (address != "https://example.org/page/7/" || !continuation) throw new Exception("A continuação foi perdida.");
            return Task.FromResult(new PortalImportBatch(10, "", "", ""));
        }, now.AddMinutes(5), CancellationToken.None);
        if (CatalogWatch.IsDue(source, now.AddMinutes(34)) || !CatalogWatch.IsDue(source, now.AddMinutes(35)) || source.Next.Length > 0)
            throw new Exception("O catálogo deve voltar a ser consultado após 30 minutos da conclusão.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
            Task.FromResult(new PortalImportBatch(2, source.Address, "", "")), now.AddMinutes(35), CancellationToken.None);
        if (source.Next.Length > 0) throw new Exception("Paginação circular não foi encerrada.");
        source.ImportRevision = 0;
        source.Next = "https://example.org/page/53/";
        CatalogWatch.Discover(library);
        if (source.Next.Length > 0 || source.ImportRevision != 1 || !CatalogWatch.IsDue(source, now))
            throw new Exception("Fontes antigas devem recomeçar com a leitura completa do catálogo.");
        Console.WriteLine("PASS: catálogos antigos, atualização periódica, paginação persistente e repetição após falha.");
    }
}
