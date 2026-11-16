using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private readonly SemaphoreSlim seasonLookup=new(1);
    private readonly HashSet<string> seasonAttempts=[];
    private object SeriesHub(string id,string? viewer=null,bool savedOnly=false)
    {
        var profile=CheckViewer(viewer??library.ActiveViewer);
        if(!webMovies.TryGetValue(id,out var selected)||!library.Movies.Contains(selected)||ImportedTitle.Describe(selected).Type!="series")throw new InvalidDataException("Série indisponível.");
        var entries=SeriesCenter.Episodes(selected,library.Movies.Where(m=>!savedOnly||!m.ImportedOnly)).ToArray();
        var next=SeriesCenter.Continue(entries,profile,library.ActiveViewer);
        return new{title=ImportedTitle.Describe(selected).Series,description=selected.CustomSynopsis.Length>0?selected.CustomSynopsis:selected.Description,
            next=next==null?null:WebMovieFor(next,profile),
            seasons=entries.GroupBy(m=>ImportedTitle.Describe(m).Season??1).Select(group=>new{number=group.Key,coverId=WebId(group.First()),episodes=group.Select(m=>WebMovieFor(m,profile)).ToArray()}).ToArray()};
    }
    private async Task<object?> SeriesActionAsync(WebAction command)
    {
        if(command.Id==null)throw new InvalidDataException("Série indisponível.");
        if(command.Name=="series-hub")return SeriesHub(command.Id);
        if(command.Name!="series-season"||!webMovies.TryGetValue(command.Id,out var selected)||!library.Movies.Contains(selected))throw new InvalidDataException("Temporada indisponível.");
        var info=ImportedTitle.Describe(selected);var season=info.Season??1;
        if(selected.SeasonPoster.Length>0&&File.Exists(selected.SeasonPoster))return new{available=true};
        var credential=TmdbClient.ReadCredential();if(credential.Length==0)return new{available=false};
        await seasonLookup.WaitAsync(lifetime.Token);
        try {
            var key=SeriesCenter.Key(selected)+"|"+season;
            if(!seasonAttempts.Add(key))return new{available=selected.SeasonPoster.Length>0};
            using var client=new TmdbClient(credential);
            var route=TmdbClient.RouteFromUrl(selected.TmdbUrl);
            var details=route?.StartsWith("tv/")==true?await client.LookupLinkedAsync(string.Join('/',route.Split('/').Take(2))+"/season/"+season,lifetime.Token):await client.LookupAsync(info.Series+" Temporada "+season,selected.Year,lifetime.Token);
            if(details==null)return new{available=false};
            var path=await client.DownloadPosterAsync(details.Poster,TmdbClient.RouteFromUrl(details.Url)!,lifetime.Token);
            foreach(var movie in SeriesCenter.Episodes(selected,library.Movies).Where(m=>(ImportedTitle.Describe(m).Season??1)==season))movie.SeasonPoster=path;
            library.Save();return new{available=path.Length>0};
        }catch(Exception ex) when(ex is IOException or System.Net.Http.HttpRequestException or InvalidDataException or OperationCanceledException){return new{available=false};}
        finally{seasonLookup.Release();}
    }
    private async Task<byte[]?> ReadSeasonPosterAsync(string id)
    {
        var path=await Dispatcher.InvokeAsync(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)?m.SeasonPoster:"");
        return await ReadCoverFileAsync(path)??await ReadWebCoverAsync(id);
    }
}
