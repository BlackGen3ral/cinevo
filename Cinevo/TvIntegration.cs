using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private TvWeb? tv;
    private async Task<object?> TvActionAsync(WebAction command)
    {
        if(command.Name=="tv-stop"){tv?.Stop();tv=null;library.EnableTv=false;library.Save();return null;}
        if(command.Name=="tv-start"&&tv==null){
            var server=new TvWeb((search,page)=>Dispatcher.InvokeAsync(()=>WebCatalog("library",search,page,24,null,null)).Task,ReadWebCoverAsync,
                id=>Dispatcher.InvokeAsync(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)&&!m.ImportedOnly?(m.HasLocalFile?m.Path:RemoteFiles.IsPublicHttps(m.StreamUrl)?m.StreamUrl:null):null).Task,
                (id,position,duration,favorite)=>Dispatcher.InvokeAsync(()=>{
                    if(!webMovies.TryGetValue(id,out var m)||!library.Movies.Contains(m)||m.ImportedOnly)return;
                    if(favorite.HasValue)m.Favorite=favorite.Value;
                    else{m.DurationMilliseconds=duration;m.ResumeMilliseconds=position;m.Watched=duration>0&&position>=duration*.95;if(m.Watched)m.ResumeMilliseconds=0;}
                    library.Save();Refresh();
                }).Task,
                id=>Dispatcher.InvokeAsync<object?>(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)&&!m.ImportedOnly?WebMovie(m):null).Task);
            try{await server.StartAsync(lifetime.Token);}catch{server.Stop();throw;}tv=server;library.EnableTv=true;library.Save();
        }
        if(command.Name=="tv-code")tv?.Rotate();
        return new{enabled=tv!=null,addresses=tv?.Addresses??[],local=tv==null?"":$"http://127.0.0.1:{tv.Port}/tv",pin=tv?.Pin??""};
    }
}
