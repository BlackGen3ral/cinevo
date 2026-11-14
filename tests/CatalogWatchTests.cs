using System.Text.Json;
using Cinevo;

internal static class CatalogWatchTests
{
    public static async Task Run()
    {
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
        if (source.Next != "https://example.org/page/5/" || CatalogWatch.IsDue(source, now.AddMinutes(4)))
            throw new Exception("Uma página bloqueada precisa ser repetida depois, sem perder o cursor.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
        {
            if (address != "https://example.org/page/5/" || !continuation) throw new Exception("A continuação foi perdida.");
            return Task.FromResult(new PortalImportBatch(10, "", "", ""));
        }, now.AddMinutes(5), CancellationToken.None);
        if (CatalogWatch.IsDue(source, now.AddMinutes(34)) || !CatalogWatch.IsDue(source, now.AddMinutes(35)) || source.Next.Length > 0)
            throw new Exception("O catálogo deve voltar a ser consultado após 30 minutos da conclusão.");
        await CatalogWatch.AdvanceAsync(source, (address, continuation, token) =>
            Task.FromResult(new PortalImportBatch(2, source.Address, "", "")), now.AddMinutes(35), CancellationToken.None);
        if (source.Next.Length > 0) throw new Exception("Paginação circular não foi encerrada.");
        Console.WriteLine("PASS: catálogos antigos, atualização periódica, paginação persistente e repetição após falha.");
    }
}
