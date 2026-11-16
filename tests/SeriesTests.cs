using Cinevo;

internal static class SeriesTests
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root,"episode.mp4");
        File.WriteAllText(path,"fixture");
        var current = new Movie { Title="Aurora.S01E02.1080p", Path=path };
        var next = new Movie { Title="Aurora S01E03", Path=path };
        var later = new Movie { Title="Aurora S02E01", Path=path };
        var unavailable = new Movie { Title="Aurora S01E04" };
        var other = new Movie { Title="Outra Série S01E03", Path=path };
        if (ImportedTitle.Describe(current) is not {Series:"Aurora",Season:1,Episode:2})
            throw new Exception("Temporada e episódio do arquivo não foram identificados.");
        if (ImportedTitle.NextEpisode(current,[later,other,next,unavailable]) != next ||
            ImportedTitle.NextEpisode(next,[later,unavailable]) != later || ImportedTitle.NextEpisode(later,[next]) != null)
            throw new Exception("A seleção do próximo episódio deve respeitar ordem e disponibilidade.");
        Console.WriteLine("PASS: séries locais, temporadas em ordem e próximo episódio disponível.");
        File.Delete(path);
    }
}
