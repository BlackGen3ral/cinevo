namespace Cinevo;
internal static class SeriesCenter
{
    public static string Key(Movie movie)=>CoverService.Normalize(ImportedTitle.Describe(movie).Series);
    public static IEnumerable<Movie> Episodes(Movie selected,IEnumerable<Movie> movies)=>movies.Where(m=>ImportedTitle.Describe(m).Type=="series"&&Key(m)==Key(selected))
        .OrderBy(m=>ImportedTitle.Describe(m).Season??1).ThenBy(m=>ImportedTitle.Describe(m).Episode??0).ThenBy(m=>m.Title);
    public static Movie? Continue(IEnumerable<Movie> movies,string viewer,string active)
    {
        var playable=movies.Where(m=>m.HasLocalFile||RemoteFiles.IsPublicHttps(m.StreamUrl)).ToArray();
        return playable.Where(m=>!Viewers.Get(m,viewer,active).Watched&&Viewers.Get(m,viewer,active).Resume>1000).OrderByDescending(m=>m.Added).FirstOrDefault()
            ??playable.FirstOrDefault(m=>!Viewers.Get(m,viewer,active).Watched)??playable.FirstOrDefault();
    }
}
