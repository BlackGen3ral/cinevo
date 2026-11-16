using System.Text.RegularExpressions;

namespace Cinevo;

internal sealed record ImportedTitle(string Type, string Series, int? Season, int? Episode)
{
    public static ImportedTitle Describe(Movie movie)
    {
        if (movie.MediaType == "movie") return new("movie", "", null, null);
        var title = Regex.Replace(movie.Title, @"\s*\(?\b(?:19|20)\d{2}\)?\s*$", "").Trim().Replace('.', ' ').Replace('_', ' ');
        var season = Regex.Match(title, @"\b(?:temporada\s*(?<n>\d{1,3})|(?<n>\d{1,3})\s*[ªºoa]?\s*temporada|(?:season\s*|s)(?<n>\d{1,3})(?=\b|e\d))", RegexOptions.IgnoreCase);
        var episode = Regex.Match(title, @"(?:\bepis[oó]dio\s*|\bepisode\s*|\bs\d{1,3}e)(?<n>\d{1,4})\b", RegexOptions.IgnoreCase);
        var seriesPath = Uri.TryCreate(movie.SourceUrl, UriKind.Absolute, out var uri) &&
            Regex.IsMatch(Uri.UnescapeDataString(uri.AbsolutePath), @"/(?:tv|series|séries|epis[oó]dios?|episodes?)/", RegexOptions.IgnoreCase);
        if (movie.MediaType != "series" && !season.Success && !episode.Success && !seriesPath && movie.Season == null && movie.Episode == null)
            return new("movie", "", null, null);
        var end = Math.Min(season.Success ? season.Index : title.Length, episode.Success ? episode.Index : title.Length);
        var name = movie.SeriesTitle.Length > 0 ? movie.SeriesTitle : title[..end].TrimEnd(' ', '-', ':', '–', '—');
        return new("series", name.Length > 0 ? name : movie.Title,
            movie.Season ?? Parse(season), movie.Episode ?? Parse(episode));
    }

    private static int? Parse(Match match) => int.TryParse(match.Groups["n"].Value, out var number) ? number : null;

    public static Movie? NextEpisode(Movie current, IEnumerable<Movie> movies)
    {
        var episode = Describe(current);
        if (episode.Type != "series" || episode.Episode == null) return null;
        var name = CoverService.Normalize(episode.Series);
        return movies.Where(movie => movie != current && (movie.HasLocalFile || movie.StreamUrl.Length > 0))
            .Select(movie => (Movie: movie, Kind: Describe(movie)))
            .Where(item => item.Kind.Type == "series" && item.Kind.Episode != null &&
                CoverService.Normalize(item.Kind.Series) == name &&
                ((item.Kind.Season ?? 1) > (episode.Season ?? 1) ||
                 (item.Kind.Season ?? 1) == (episode.Season ?? 1) && item.Kind.Episode > episode.Episode))
            .OrderBy(item => item.Kind.Season ?? 1).ThenBy(item => item.Kind.Episode).Select(item => item.Movie).FirstOrDefault();
    }
}
