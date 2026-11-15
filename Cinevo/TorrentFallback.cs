using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

public partial class MainWindow
{
    private static readonly TimeSpan FallbackWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FallbackPoll = TimeSpan.FromSeconds(15);
    private readonly Dictionary<string, CancellationTokenSource> torrentFallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim torrentFallbackSwitch = new(1);
    private readonly Dictionary<string, string> torrentFallbackStatus = new(StringComparer.OrdinalIgnoreCase);

    private void TrackTorrentFallback(TorrentClient client, Movie movie, string address)
    {
        var hash = TorrentClient.MagnetHash(address);
        var identity = FilmIdentity(movie);
        if (hash == null || identity == null || !RemoteFiles.IsPublicHttps(movie.SourceUrl) || torrentFallbacks.ContainsKey(hash))
            return;

        var originalHost = DownloadHost(movie);
        var alternatives = library.Movies
            .Where(candidate => candidate.PortalId.Length > 0 && !candidate.IsTmdbImport &&
                RemoteFiles.IsPublicHttps(candidate.SourceUrl) && DownloadHost(candidate) != originalHost &&
                FilmIdentity(candidate) == identity)
            .DistinctBy(DownloadHost)
            .Take(4)
            .ToArray();
        if (alternatives.Length == 0) return;

        if (torrentFallbackStatus.Count >= 200) torrentFallbackStatus.Clear();
        torrentFallbackStatus.Remove(hash);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        torrentFallbacks.Add(hash, cancellation);
        _ = WatchTorrentFallbackAsync(client, hash, alternatives, cancellation);
    }

    private void CancelTorrentFallback(string hash)
    {
        if (torrentFallbacks.TryGetValue(hash, out var cancellation)) cancellation.Cancel();
        torrentFallbackStatus.Remove(hash);
    }

    private async Task WatchTorrentFallbackAsync(TorrentClient client, string hash, Movie[] alternatives, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var wait = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                await Task.Delay(FallbackPoll, token);
                var transfer = (await client.TransfersAsync(token, hash)).FirstOrDefault(item =>
                    item.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
                if (transfer == null || transfer.Completed > 0 || transfer.Progress > 0 ||
                    transfer.State.StartsWith("paused", StringComparison.OrdinalIgnoreCase) ||
                    transfer.State.StartsWith("stopped", StringComparison.OrdinalIgnoreCase) ||
                    transfer.State is "error" or "missingFiles")
                    return;
                if (!WaitingWithoutSeeds(transfer)) wait.Restart();
                if (wait.Elapsed >= FallbackWait) break;
            }

            torrentFallbackStatus[hash] = "Buscando o mesmo filme em outro site importado…";
            foreach (var candidate in alternatives)
            {
                if (!library.Movies.Contains(candidate)) continue;
                List<PortalDownload> downloads;
                using var sourceTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                sourceTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                try { downloads = await PortalService.GetDownloadsAsync(candidate.PortalId, candidate.SourceUrl, sourceTimeout.Token); }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or OperationCanceledException or InvalidDataException)
                { continue; }

                await torrentFallbackSwitch.WaitAsync(token);
                try
                {
                    var queue = await client.TransfersAsync(token);
                    if (!WaitingWithoutSeeds(queue.FirstOrDefault(item => item.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase))))
                    {
                        torrentFallbackStatus.Remove(hash);
                        return;
                    }
                    var used = queue.Select(item => item.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var alternative = downloads.FirstOrDefault(option => option.Kind == "torrent" &&
                        TorrentClient.MagnetHash(option.Url) is { } alternativeHash && !used.Contains(alternativeHash));
                    if (alternative == null) continue;

                    await client.SetRunningAsync(hash, false, token);
                    try { await client.AddAsync(alternative.Url, token); }
                    catch (Exception ex) when (!token.IsCancellationRequested && ex is InvalidDataException or IOException)
                    {
                        var alternativeHash = TorrentClient.MagnetHash(alternative.Url);
                        if (!(await client.TransfersAsync(token, alternativeHash)).Any(item =>
                            item.Hash.Equals(alternativeHash, StringComparison.OrdinalIgnoreCase)))
                        {
                            await client.SetRunningAsync(hash, true, token);
                            throw;
                        }
                    }
                    torrentFallbackStatus[hash] = "Fonte original pausada. Tentando " + DownloadHost(candidate) + ".";
                    return;
                }
                finally { torrentFallbackSwitch.Release(); }
            }

            torrentFallbackStatus[hash] = "Nenhum torrent diferente encontrado para este filme nos outros sites.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ObjectDisposedException)
        {
            torrentFallbackStatus[hash] = "A tentativa em outra fonte foi interrompida.";
        }
        finally
        {
            torrentFallbacks.Remove(hash);
            cancellation.Dispose();
        }
    }

    private static bool WaitingWithoutSeeds(TorrentTransfer? transfer) =>
        transfer is { Completed: 0, Progress: 0, Speed: 0, Seeds: 0 } &&
        (transfer.State.EndsWith("metaDL", StringComparison.OrdinalIgnoreCase) ||
         transfer.State.Equals("stalledDL", StringComparison.OrdinalIgnoreCase));

    private static string DownloadHost(Movie movie)
    {
        var host = new Uri(movie.SourceUrl).IdnHost;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static (string Title, string Year)? FilmIdentity(Movie movie)
    {
        if (ImportedTitle.Describe(movie).Type != "movie") return null;
        var title = Regex.Split(movie.Title, @"\s+(?:\d{3,4}p|WEB[ .-]?DL|Blu[ .-]?Ray|Dual [ÁA]udio|Dublado(?: e Legendado)?|Legendado|Torrent)\b",
            RegexOptions.IgnoreCase)[0].TrimEnd(' ', '.', '-', '–', '|');
        var yearTag = Regex.Match(title, @"\s*\(?\b((?:19|20)\d{2})\)?$");
        var year = Regex.IsMatch(movie.Year, @"\A(?:19|20)\d{2}\z") ? movie.Year : yearTag.Groups[1].Value;
        if (yearTag.Success) title = title[..yearTag.Index];
        var normalized = CoverService.Normalize(title);
        return year.Length > 0 && normalized.Length > 0 ? (normalized, year) : null;
    }
}
