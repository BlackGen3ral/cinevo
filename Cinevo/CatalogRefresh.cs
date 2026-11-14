namespace Cinevo;

public partial class MainWindow
{
    private int manualPortalImports;

    private async Task<PortalImportBatch> ImportTrackedBatchAsync(PortalConnection connection, string address, bool continuation)
    {
        manualPortalImports++;
        try
        {
            var result = await PortalService.ImportBatchAsync(connection, address, continuation, lifetime.Token);
            if (!continuation && result.Count > 0)
            {
                var source = CatalogWatch.Register(library, address);
                if (source != null) source.LastCompletedUtc = DateTime.UtcNow;
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
                        if (!CatalogWatch.IsDue(source, DateTime.UtcNow)) continue;
                        try
                        {
                            await CatalogWatch.AdvanceAsync(source,
                                (address, continuation, token) => PortalService.ImportBatchAsync(connection, address, continuation, token),
                                DateTime.UtcNow, lifetime.Token);
                        }
                        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                        catch (Exception)
                        {
                            source.RetryAfterUtc = DateTime.UtcNow.AddMinutes(5);
                        }
                        if (closed) return;
                        library.Save();
                        await SyncPortalAsync(false);
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
