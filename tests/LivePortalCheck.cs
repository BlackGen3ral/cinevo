using Cinevo;
public static class LivePortalCheck
{
    public static async Task Run(string connectionPath,string profile)
    {
        Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",profile);
        var connection=PortalService.ReadPairing(connectionPath);PortalService.SaveConnection(connection);
        var library=new Library{AutoUpdate=false,AutoCovers=false};
        var added=await PortalService.SyncAsync(library,connection,CancellationToken.None);library.Save();
        Console.WriteLine($"Portal real: {added.Added} títulos sincronizados; {library.Movies.Count(m=>m.Cover.Length>0)} capas salvas.");
        var movie=library.Movies.FirstOrDefault(m=>m.DownloadUrl.EndsWith("flower.mp4"));
        if(movie!=null){var path=Path.Combine(profile,"download-flower.mp4");await RemoteFiles.DownloadAsync(movie.DownloadUrl,path,new Progress<(long,long?)>(),CancellationToken.None);movie.Path=path;movie.Bytes=new FileInfo(path).Length;library.Save();Console.WriteLine($"Download real concluído: {movie.Bytes} bytes, arquivo disponível offline.");}
    }
}
