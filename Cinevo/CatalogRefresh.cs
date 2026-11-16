namespace Cinevo;

public partial class MainWindow
{
    private int manualPortalImports;

    private async Task<PortalImportBatch> ImportTrackedBatchAsync(PortalConnection connection, string address, bool continuation)
    {
        manualPortalImports++;
        try
        {
            PortalImportBatch result;
            try
            {
                var useLocal = library.CatalogSources.Any(source => source.LocalImport &&
                    Uri.TryCreate(source.Address, UriKind.Absolute, out var saved) && new Uri(address).Host == saved.Host);
                result = useLocal ? await ImportLocalCatalogAsync(address, lifetime.Token) :
                    await PortalService.ImportBatchAsync(connection, address, continuation, lifetime.Token);
            }
            catch (Exception ex) when (!lifetime.IsCancellationRequested &&
                ex is System.IO.InvalidDataException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            { result = await ImportLocalCatalogAsync(address, lifetime.Token); }
            if (!continuation && result.Count > 0)
            {
                var source = CatalogWatch.Register(library, address);
                if (source != null) { source.LastCompletedUtc = DateTime.UtcNow; source.LocalImport = result.Local; }
                library.Save();
            }
            return result;
        }
        finally { manualPortalImports--; }
    }

    private async Task WatchCatalogsAsync()
    {
        while (!closed)
        {
            try
            {
                if (PortalService.LoadConnection() is { } connection && !webImporting && manualPortalImports == 0)
                {
                    CatalogWatch.Discover(library);
                    foreach (var source in library.CatalogSources.ToArray())
                    {
                        if (closed || webImporting || manualPortalImports > 0) break;
                        if (library.ImportCheckpoints.Any(item => item.Root == source.Address)) continue;
                        if (!CatalogWatch.IsDue(source, DateTime.UtcNow)) continue;
                        try
                        {
                            await CatalogWatch.AdvanceAsync(source,
                                (address, continuation, token) => source.LocalImport ? ImportLocalCatalogAsync(address, token) :
                                    PortalService.ImportBatchAsync(connection, address, continuation, token),
                                DateTime.UtcNow, lifetime.Token);
                        }
                        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                        catch (Exception)
                        {
                            source.RetryAfterUtc = DateTime.UtcNow.AddMinutes(5);
                        }
                        if (closed) return;
                        library.Save();
                        if (!source.LocalImport) await SyncPortalAsync(false);
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception) { /* A failed check is retried without interrupting playback. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token); }
            catch (OperationCanceledException) { return; }
        }
    }
}
