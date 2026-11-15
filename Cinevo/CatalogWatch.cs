using System.Text.RegularExpressions;

namespace Cinevo;

public sealed class CatalogSource
{
    public bool LocalImport { get; set; }
    public string Address { get; set; } = "";
    public string Next { get; set; } = "";
    public DateTime LastCompletedUtc { get; set; }
    public DateTime LastHeadCheckUtc { get; set; }
    public DateTime RetryAfterUtc { get; set; }
    public HashSet<string> Visited { get; set; } = [];
    public int ImportRevision { get; set; }
}

public static class CatalogWatch
{
    public static CatalogSource? Register(Library library, string address)
    {
        if (!RemoteFiles.IsPublicHttps(address) || TmdbClient.IsSiteUrl(address)) return null;
        address = new UriBuilder(address) { Fragment = "" }.Uri.AbsoluteUri;
        var source = library.CatalogSources.FirstOrDefault(item => item.Address == address);
        if (source != null) return source;
        source = new CatalogSource { Address = address, ImportRevision = 1 };
        library.CatalogSources.Add(source);
        return source;
    }

    public static void Discover(Library library)
    {
        foreach (var movie in library.Movies.Where(movie => movie.PortalId.Length > 0 &&
            movie.ImportedFrom.Length > 0 && movie.ImportedFrom != movie.SourceUrl))
        {
            if (!RemoteFiles.IsPublicHttps(movie.ImportedFrom)) continue;
            var url = new UriBuilder(movie.ImportedFrom) { Fragment = "" };
            url.Path = Regex.Replace(url.Path, @"/page/\d+/?$", "/", RegexOptions.IgnoreCase);
            url.Query = string.Join("&", url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !Regex.IsMatch(part, @"^(?:page|paged)=\d+$", RegexOptions.IgnoreCase)));
            Register(library, url.Uri.AbsoluteUri);
        }
        foreach (var source in library.CatalogSources.Where(source => source.ImportRevision < 1))
        {
            source.Next = "";
            source.Visited.Clear();
            source.LastCompletedUtc = source.LastHeadCheckUtc = source.RetryAfterUtc = default;
            source.ImportRevision = 1;
        }
    }

    public static bool IsDue(CatalogSource source, DateTime now) => source.RetryAfterUtc <= now &&
        (source.Next.Length > 0 || now - source.LastCompletedUtc >= TimeSpan.FromMinutes(30));

    public static async Task AdvanceAsync(CatalogSource source,
        Func<string, bool, CancellationToken, Task<PortalImportBatch>> fetch, DateTime now, CancellationToken token)
    {
        var checkHead = source.Next.Length > 0 && now - source.LastHeadCheckUtc >= TimeSpan.FromMinutes(30);
        var continuation = source.Next.Length > 0 && !checkHead;
        var address = continuation ? source.Next : source.Address;
        var batch = await fetch(address, continuation, token);
        token.ThrowIfCancellationRequested();
        if (batch.Partial.Length > 0)
        {
            if (batch.Count > 0 && RemoteFiles.IsPublicHttps(batch.Next) && !source.Visited.Contains(batch.Next))
                source.Next = batch.Next;
            source.RetryAfterUtc = now.AddMinutes(5);
            return;
        }
        if (!continuation) source.LastHeadCheckUtc = now;
        source.RetryAfterUtc = default;
        // Recheck the newest pages while preserving a long catalog's backfill cursor.
        if (checkHead) return;
        source.Visited.Add(address);
        var next = batch.Next;
        if (next.Length > 0 && (!RemoteFiles.IsPublicHttps(next) || source.Visited.Contains(next))) next = "";
        source.Next = next;
        if (next.Length == 0)
        {
            source.LastCompletedUtc = now;
            source.Visited.Clear();
        }
    }
}
