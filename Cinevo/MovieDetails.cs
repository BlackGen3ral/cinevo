using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

public partial class MainWindow
{
    private readonly SemaphoreSlim detailLookup = new(1);
    private readonly Dictionary<(string Title, string Year), (CoverCandidate? Match, MovieTrailer? Trailer)> detailCache = new();

    private async Task<object> MovieDetailsAsync(Movie movie)
    {
        var title = Regex.Replace(movie.Title, @"\s*\((?:19|20)\d{2}\)\s*$", "").Trim();
        var year = movie.Year;
        var savedSynopsis = movie.CustomSynopsis.Length > 0 ? movie.CustomSynopsis : movie.Description;
        var savedTrailer = MovieTrailer.FromUrl(movie.TrailerUrl);
        if (!library.AutoCovers || (savedSynopsis.Length > 0 && savedTrailer != null && File.Exists(movie.Cover)))
            return new { synopsis = savedSynopsis, trailerId = savedTrailer?.VideoId };

        var token = lifetime.Token;
        var key = (title, year);
        CoverCandidate? match;
        MovieTrailer? relatedTrailer;
        await detailLookup.WaitAsync(token);
        try
        {
            if (!detailCache.TryGetValue(key, out var result))
            {
                var matches = await covers.SearchAsync(title, year, token, includeWithoutPoster: true);
                match = matches.FirstOrDefault(candidate => candidate.Score >= 16 &&
                    (!string.IsNullOrEmpty(year) || CoverService.Normalize(title) ==
                        CoverService.Normalize(Regex.Replace(candidate.Title, @"\([^)]*\)", ""))));
                relatedTrailer = match == null ? null : await TryFindTrailerAsync(match.Entity, token);
                if (detailCache.Count >= 150) detailCache.Clear();
                detailCache[key] = (match, relatedTrailer);
            }
            else (match, relatedTrailer) = result;
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            return new { synopsis = savedSynopsis, trailerId = savedTrailer?.VideoId, retry = true };
        }
        finally { detailLookup.Release(); }

        if (match is { ImageUrl.Length: > 0 } && !File.Exists(movie.Cover) && library.Movies.Contains(movie))
        {
            try
            {
                var path = await covers.DownloadAsync(match, Path.Combine(Library.DataDirectory, "covers"), token);
                if (library.Movies.Contains(movie) && !File.Exists(movie.Cover))
                {
                    movie.Cover = path;
                    movie.CoverSource = match.PageUrl;
                    library.Save();
                    webRevision++;
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested &&
                ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException) { }
        }
        return new
        {
            synopsis = savedSynopsis.Length > 0 ? savedSynopsis : match?.Description,
            synopsisSource = savedSynopsis.Length == 0 ? match?.PageUrl : null,
            trailerId = (savedTrailer ?? relatedTrailer)?.VideoId
        };
    }

    private async Task<MovieTrailer?> TryFindTrailerAsync(string entity, CancellationToken token)
    {
        try { return await covers.FindTrailerAsync(entity, token); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or TaskCanceledException) { return null; }
    }
}
