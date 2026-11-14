using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

public partial class MainWindow
{
    private readonly SemaphoreSlim detailLookup = new(1);
    private readonly Dictionary<(string Title, string Year), (CoverCandidate? Match, MovieTrailer? Trailer)> detailCache = new();
    private readonly Dictionary<(string Title, string Year), Task<TmdbDetails?>> tmdbCache = new();
    private int tmdbRevision;

    private async Task<object> MovieDetailsAsync(Movie movie)
    {
        var title = Regex.Replace(movie.Title, @"\s*\((?:19|20)\d{2}\)\s*$", "").Trim();
        var year = movie.Year.Length == 4 ? movie.Year :
            Regex.Match(movie.Title, @"\(((?:19|20)\d{2})\)\s*$").Groups[1].Value;
        var synopsis = movie.CustomSynopsis.Length > 0 ? movie.CustomSynopsis : movie.Description;
        var trailer = MovieTrailer.FromUrl(movie.TrailerUrl)?.VideoId;
        try
        {
            var credential = TmdbClient.ReadCredential();
            if (credential.Length > 0)
            {
                var key = (title, year);
                if (!tmdbCache.TryGetValue(key, out var lookup))
                {
                    if (tmdbCache.Count >= 150) tmdbCache.Clear();
                    tmdbCache[key] = lookup = LookupTmdbAsync(credential, key);
                }
                TmdbDetails? details;
                try { details = await lookup; }
                catch
                {
                    if (tmdbCache.TryGetValue(key, out var active) && active == lookup) tmdbCache.Remove(key);
                    throw;
                }
                if (details != null)
                {
                    var useTmdbSynopsis = movie.CustomSynopsis.Length == 0 && details.Overview.Length > 0;
                    return new { synopsis = useTmdbSynopsis ? details.Overview : synopsis,
                        trailerId = trailer ?? details.TrailerId, tmdb = details, tmdbSynopsis = useTmdbSynopsis };
                }
            }
        }
        catch (Exception ex) when (!lifetime.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            return new { synopsis, trailerId = trailer, retry = true,
                tmdbError = ex is InvalidDataException ? ex.Message : "O TMDB está indisponível no momento. Tente novamente ao reabrir os detalhes." };
        }
        return await WikiMovieDetailsAsync(movie, title, synopsis, trailer);
    }

    private async Task<TmdbDetails?> LookupTmdbAsync(string credential, (string Title, string Year) key)
    {
        using var client = new TmdbClient(credential);
        return await client.LookupAsync(key.Title, key.Year, lifetime.Token);
    }

    private async Task<object> WikiMovieDetailsAsync(Movie movie, string title, string savedSynopsis, string? savedVideo)
    {
        var year = movie.Year;
        var savedTrailer = savedVideo == null ? null : new MovieTrailer(savedVideo);
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
