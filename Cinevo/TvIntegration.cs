using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private TvWeb? tv;
    private async Task<object?> TvActionAsync(WebAction command)
    {
        if(command.Name=="tv-stop"){tv?.Stop();tv=null;library.EnableTv=false;library.Save();return null;}
        if(command.Name=="tv-start"&&tv==null){
            var server=new TvWeb((search,page,profile)=>Dispatcher.InvokeAsync(()=>WebCatalog("library",search,page,24,null,null,CheckViewer(string.IsNullOrEmpty(profile)?null:profile))).Task,ReadWebCoverAsync,
                id=>Dispatcher.InvokeAsync(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)&&!m.ImportedOnly?(m.HasLocalFile?m.Path:RemoteFiles.IsPublicHttps(m.StreamUrl)?m.StreamUrl:null):null).Task,
                (id,position,duration,favorite,profile)=>Dispatcher.InvokeAsync(()=>{
                    if(!webMovies.TryGetValue(id,out var m)||!library.Movies.Contains(m)||m.ImportedOnly)return;
                    var viewer=CheckViewer(string.IsNullOrEmpty(profile)?null:profile);var state=Viewers.Get(m,viewer,library.ActiveViewer);
                    if(favorite.HasValue)state=state with{Favorite=favorite.Value};
                    else{m.DurationMilliseconds=duration;var watched=duration>0&&position>=duration*.95;state=state with{Watched=watched,Resume=watched?0:position};}
                    Viewers.Set(m,viewer,library.ActiveViewer,state);
                    library.Save();Refresh();
                }).Task,
                (id,profile)=>Dispatcher.InvokeAsync<object?>(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)?WebMovieFor(m,CheckViewer(string.IsNullOrEmpty(profile)?null:profile)):null).Task,
                (operation,id,profile)=>Dispatcher.InvokeAsync(async()=>{
                    if(operation=="visual")return VisualInfo(string.IsNullOrEmpty(profile)?null:profile);
                    if(operation=="profiles")return (object?)ProfilesInfo();
                    if(operation=="playback")return TvPlaybackInfo(id);
                    if(id==null||!webMovies.TryGetValue(id,out var m))throw new InvalidDataException("Série indisponível.");
                    if(operation=="next"){var following=ImportedTitle.NextEpisode(m,library.Movies.Where(v=>!v.ImportedOnly));return following==null?null:WebMovieFor(following,CheckViewer(string.IsNullOrEmpty(profile)?null:profile));}
                    if(operation=="series")return SeriesHub(id,CheckViewer(string.IsNullOrEmpty(profile)?null:profile),!m.ImportedOnly);
                    return await SeriesActionAsync(new("series-season",id,null,false));
                }).Task.Unwrap(),ReadSeasonPosterAsync,(search,page,profile)=>Dispatcher.InvokeAsync(()=>WebCatalog("imports",search,page,24,null,null,CheckViewer(string.IsNullOrEmpty(profile)?null:profile))).Task,(command,session)=>Dispatcher.InvokeAsync(()=>TvDownloadAsync(command,session)).Task.Unwrap(),ReadTimelinePreview);
            try{await server.StartAsync(lifetime.Token);}catch{server.Stop();throw;}tv=server;library.EnableTv=true;library.Save();
        }
        if(command.Name=="tv-code")tv?.Rotate();
        if(command.Name=="tv-diagnostics")return tv?.Diagnostics()??new{enabled=false,pcAvailable=true,devices=Array.Empty<object>()};
        return new{enabled=tv!=null,addresses=tv?.Addresses??[],local=tv==null?"":$"http://127.0.0.1:{tv.Port}/tv",pin=tv?.Pin??""};
    }

    private object TvPlaybackInfo(string? id){if(id==null||!webMovies.TryGetValue(id,out var m)||!library.Movies.Contains(m))return new{available=false,reason="Este título não está mais na coleção do PC."};if(m.HasLocalFile)return new{available=true,reason="O arquivo está acessível no PC. Se a TV rejeitar a reprodução, o formato ou o codec pode ser incompatível com o navegador."};if(RemoteFiles.IsPublicHttps(m.StreamUrl))return new{available=true,reason="O vídeo vem de uma origem online. A disponibilidade também depende desse serviço."};return new{available=false,reason=m.Path.Length>0?"O arquivo não foi encontrado. Confira se o disco está conectado ao PC e se o vídeo foi movido.":"O conteúdo ainda não foi baixado no PC. Consulte as opções da origem e solicite o download."};}
}
