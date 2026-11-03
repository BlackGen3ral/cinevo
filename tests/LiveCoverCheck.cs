using Cinevo;
public static class LiveCoverCheck
{
    public static async Task Run(string profile, string sampleVideo)
    {
        Environment.SetEnvironmentVariable("CINEVO_TEST_DATA", profile);
        Directory.CreateDirectory(profile);
        using var covers = new CoverService(); var library = new Library { AutoUpdate = false, AutoCovers = false };
        foreach (var (title,year) in new[] { ("Duna Parte Dois","2024"), ("Interestelar","2014"), ("Matrix","1999"), ("A Origem","2010"), ("Blade Runner 2049","2017"), ("O Senhor dos Anéis A Sociedade do Anel","2001") })
        {
            var path = Path.Combine(profile,title + ".mp4"); File.Copy(sampleVideo,path,true);
            var movie = new Movie { Path = path, Title = title, Year = year, Bytes = new FileInfo(path).Length };
            var results = await covers.SearchAsync(title, year, CancellationToken.None);
            var best = results.FirstOrDefault(c => c.Score >= 16);
            if (best != null) { movie.Cover = await covers.DownloadAsync(best, Path.Combine(profile,"covers"), CancellationToken.None); movie.CoverSource = best.PageUrl; }
            Console.WriteLine($"{title}: {best?.Title ?? "SEM CAPA"} (score {best?.Score})"); library.Movies.Add(movie);
        }
        library.Save();
    }
}
