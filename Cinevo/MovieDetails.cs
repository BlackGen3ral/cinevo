using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cinevo;

public partial class MainWindow
{
    private readonly SemaphoreSlim detailLookup = new(1);
    private readonly Dictionary<(string Title, string Year), (CoverCandidate? Match, MovieTrailer? Trailer)> detailCache = new();
    private readonly Dictionary<(string Title, string Year, string? Route), Task<TmdbDetails?>> tmdbCache = new();
    private int tmdbRevision;

    private async Task<object> MovieDetailsAsync(Movie movie)
    {
        var title = movie.IsOnline && !movie.IsTmdbImport
            ? Regex.Split(movie.Title, @"\s+(?:\d{3,4}p|WEB[ .-]?DL|Blu[ .-]?Ray|Dual [ÁA]udio|Dublado(?: e Legendado)?|Legendado|Torrent)\b",
                RegexOptions.IgnoreCase)[0].TrimEnd(' ', '.', '-', '–', '|')
            : movie.Title.Trim();
        var yearTag = Regex.Match(title, movie.IsOnline ? @"\s+\(?((?:19|20)\d{2})\)?\s*$" : @"\s*\(((?:19|20)\d{2})\)\s*$");
        if (yearTag.Success && yearTag.Index > 0) title = title[..yearTag.Index].TrimEnd(' ', '-', '–', '|');
        var year = movie.Year.Length == 4 ? movie.Year :
            yearTag.Groups[1].Value;
        var kind = movie.IsOnline ? ImportedTitle.Describe(movie) : null;
        if (kind is { Type: "series", Series.Length: > 0 })
        {
            title = kind.Series;
            if (kind.Season is int season) title += $" Temporada {season}";
        }
        var synopsis = movie.CustomSynopsis.Length > 0 ? movie.CustomSynopsis : movie.Description;
        var trailer = MovieTrailer.FromUrl(movie.TrailerUrl)?.VideoId;
        var tmdbState = "disconnected";
        try
        {
            var credential = TmdbClient.ReadCredential();
            if (credential.Length > 0)
            {
                tmdbState = "unmatched";
                var key = (title, year, TmdbClient.RouteFromUrl(movie.TmdbUrl) ?? TmdbClient.RouteFromUrl(movie.SourceUrl));
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
                    var cast=details.Cast.Select(p=>p.Name).Distinct().ToList();var castChanged=!movie.CastNames.SequenceEqual(cast);movie.CastNames=cast;
                    if(BackdropArtwork.Valid(details.Backdrop))movie.BackdropKey=details.Backdrop;
                    if (castChanged||movie.UserScore != details.Score || movie.Genre.Length == 0 || movie.DurationMilliseconds == 0) { movie.UserScore=details.Score; if(movie.Genre.Length==0)movie.Genre=string.Join(" · ",details.Genres); if(movie.DurationMilliseconds==0)movie.DurationMilliseconds=details.Minutes*60000L; library.Save(); }
                    var useTmdbSynopsis = movie.CustomSynopsis.Length == 0 && details.Overview.Length > 0;
                    return new { synopsis = useTmdbSynopsis ? details.Overview : synopsis,
                        trailerId = trailer ?? details.TrailerId, tmdb = details, tmdbSynopsis = useTmdbSynopsis };
                }
            }
        }
        catch (Exception ex) when (!lifetime.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            return new { synopsis, trailerId = trailer, retry = true, tmdbState = "error",
                tmdbError = ex is InvalidDataException ? ex.Message : "O TMDB está indisponível no momento. Tente novamente ao reabrir os detalhes." };
        }
        return await WikiMovieDetailsAsync(movie, title, synopsis, trailer, tmdbState);
    }

    private async Task<TmdbDetails?> LookupTmdbAsync(string credential, (string Title, string Year, string? Route) key)
    {
        using var client = new TmdbClient(credential);
        return key.Route == null ? await client.LookupAsync(key.Title, key.Year, lifetime.Token) :
            await client.LookupLinkedAsync(key.Route, lifetime.Token);
    }

    private async Task<object> WikiMovieDetailsAsync(Movie movie, string title, string savedSynopsis, string? savedVideo, string tmdbState)
    {
        var year = movie.Year;
        var savedTrailer = savedVideo == null ? null : new MovieTrailer(savedVideo);
        if (!library.AutoCovers || (savedSynopsis.Length > 0 && savedTrailer != null && File.Exists(movie.Cover)))
            return new { synopsis = savedSynopsis, trailerId = savedTrailer?.VideoId, tmdbState };

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
            return new { synopsis = savedSynopsis, trailerId = savedTrailer?.VideoId, retry = true, tmdbState };
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
            trailerId = (savedTrailer ?? relatedTrailer)?.VideoId,
            tmdbState
        };
    }

    private async Task<MovieTrailer?> TryFindTrailerAsync(string entity, CancellationToken token)
    {
        try { return await covers.FindTrailerAsync(entity, token); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or TaskCanceledException) { return null; }
    }
}
