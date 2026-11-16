using System.IO;
namespace Cinevo;
public partial class MainWindow
{
 private readonly TvDownloadTickets tvTickets=new();
 private readonly Dictionary<string,TvJob> tvJobs=[];
 private readonly SemaphoreSlim tvDirectSlots=new(2);private readonly HashSet<Task> tvDirectTasks=[];
 private async Task ObserveTvTask(Task task){try{await task;}finally{tvDirectTasks.Remove(task);}}
 private readonly System.Windows.Threading.DispatcherTimer tvJobTimer=new(){Interval=TimeSpan.FromSeconds(10)};private bool tvJobTimerStarted,tvJobPolling;
 private void StartTvJobPolling(){if(tvJobTimerStarted)return;tvJobTimerStarted=true;tvJobTimer.Tick+=async(_,_)=>{if(closed){tvJobTimer.Stop();return;}if(tvJobPolling)return;tvJobPolling=true;try{foreach(var session in tvJobs.Values.Where(j=>j.Kind=="torrent"&&j.State is "queued" or "downloading" or "paused").Select(j=>j.Session).Distinct().ToArray())await TvDownloadAsync(new("tv-download-status",null,null),session);}catch(Exception ex)when(ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException){}finally{tvJobPolling=false;}};tvJobTimer.Start();}

 private sealed class TvJob
 {
  public string Id=Guid.NewGuid().ToString("N"),Session="",Title="",Label="",Kind="",Hash="",State="queued",Error="",Detail="",Source="";public bool SingleVideo;public Movie OriginMovie=null!,Movie=null!;public long Received,Speed;public long? Total;public double Progress;public long Eta=-1;public DateTime Created=DateTime.UtcNow;public CancellationTokenSource? Cancel;
 }
 private object TvJobInfo(TvJob j)=>new{j.Id,j.Title,j.Label,j.Kind,j.State,j.Progress,j.Received,j.Total,j.Speed,j.Eta,j.Error,j.Detail,canPlay=j.State=="ready"&&j.SingleVideo&&library.Movies.Contains(j.Movie)&&j.Movie.HasLocalFile,movie=WebMovie(j.Movie)};
 private async Task<object?> TvDownloadAsync(TvMediaCommand command,string session)
 {
  if(command.Name=="tv-download-status"){
   var active=tvJobs.Values.Where(j=>j.Session==session&&j.Kind=="torrent"&&j.State is "queued" or "downloading" or "paused").ToArray();
   if(active.Length>0){try{var transfers=await GetTorrentClient().TransfersAsync(lifetime.Token);foreach(var job in active){var transfer=transfers.FirstOrDefault(t=>t.Hash.Equals(job.Hash,StringComparison.OrdinalIgnoreCase));if(transfer==null){job.State="failed";job.Error="A transferência saiu da fila do PC. Confira Downloads no Cinevo.";continue;}job.Error="";job.Detail=transfer.State.Contains("meta",StringComparison.OrdinalIgnoreCase)?"Buscando dados do torrent":transfer.State.Contains("stalled",StringComparison.OrdinalIgnoreCase)?"Aguardando pares":transfer.State.Contains("queued",StringComparison.OrdinalIgnoreCase)?"Na fila do qBittorrent":"";job.Progress=transfer.Progress;job.Received=transfer.Completed;job.Total=transfer.Size>0?transfer.Size:null;job.Speed=transfer.Speed;job.Eta=transfer.Eta;job.State=transfer.Progress>=1?"ready":transfer.State.StartsWith("paused")||transfer.State.StartsWith("stopped")?"paused":"downloading";if(job.State=="ready"){job.Speed=0;job.Detail="Pronto no PC";var files=await GetTorrentClient().FilesAsync(job.Hash,lifetime.Token);var videos=files.Where(f=>f.Progress>=1&&f.Priority>0).Select(f=>VideoPath(transfer,f)).Where(p=>p!=null).ToArray();CompleteTvTorrent(job,videos!);}}}catch(Exception ex)when(ex is IOException or System.Net.Http.HttpRequestException){foreach(var j in active)if(j.Error.Length==0)j.Error="O PC não conseguiu consultar o qBittorrent. Abra-o e confira a Interface Web.";}}
   return tvJobs.Values.Where(j=>j.Session==session).OrderByDescending(j=>j.Created).Take(32).Select(TvJobInfo).ToArray();
  }
  if(command.Name=="tv-download-cancel"||command.Name=="tv-download-resume"){
   if(command.Id==null||!tvJobs.TryGetValue(command.Id,out var job)||job.Session!=session)throw new InvalidDataException("Solicitação indisponível.");
   if(command.Name=="tv-download-resume"){if(job.Kind!="torrent"||job.State!="paused")throw new InvalidDataException("Esta solicitação não está pausada.");await GetTorrentClient().SetRunningAsync(job.Hash,true,lifetime.Token);job.State="downloading";job.Detail="";return TvJobInfo(job);}
   if(job.State=="ready")return TvJobInfo(job);if(job.Kind=="torrent")await GetTorrentClient().SetRunningAsync(job.Hash,false,lifetime.Token);else job.Cancel?.Cancel();job.State=job.Kind=="torrent"?"paused":"cancelled";return TvJobInfo(job);
  }
  if(command.Id==null||!webMovies.TryGetValue(command.Id,out var movie)||!library.Movies.Contains(movie)||!movie.IsOnline)throw new InvalidDataException("Título indisponível na origem.");
  if(command.Name=="tv-download-options"){
   var options=(await DispatchWebActionAsync(new("download-options",command.Id,null,false)) as IEnumerable<PortalDownload>??[]).Where(o=>o.Kind is "file" or "torrent" or "resolve");
   return options.Take(30).Select(o=>new{id=tvTickets.Add(session,command.Id,o.Url,o.Kind,o.Label),o.Label,o.Kind}).ToArray();
  }
  if(waitingTvDownloads||closed)throw new InvalidDataException("O Cinevo está encerrando no PC. Aguarde e conecte novamente.");
  var ticket=tvTickets.Get(command.Address??"",session,command.Id)??throw new InvalidDataException("Esta opção expirou. Consulte as opções da origem novamente.");
  var current=tvJobs.Values.FirstOrDefault(j=>j.OriginMovie==movie&&j.Source==ticket.Url&&j.State is "queued" or "downloading" or "ready");
  if(current!=null){if(current.Session!=session)throw new InvalidDataException("Este conteúdo já foi solicitado no PC. Atualize a biblioteca quando terminar.");return TvJobInfo(current);}
  if(tvJobs.Values.Count(j=>j.State is "queued" or "downloading")>=8)throw new InvalidDataException("Já há oito solicitações em andamento. Aguarde um download terminar.");
  var optionsNow=(await DispatchWebActionAsync(new("download-options",command.Id,null,false)) as IEnumerable<PortalDownload>??[]).ToArray();
  var selected=optionsNow.FirstOrDefault(o=>o.Url==ticket.Url&&o.Kind==ticket.Kind)??throw new InvalidDataException("A origem alterou esta opção. Consulte novamente.");
  if(selected.Kind=="resolve")selected=await DynamicDownloads.ResolveAsync(movie.SourceUrl,selected,lifetime.Token);
  if(selected.Kind is not ("torrent" or "file"))throw new InvalidDataException("A origem forneceu uma página externa, sem arquivo direto para o PC baixar.");
  current=tvJobs.Values.FirstOrDefault(j=>j.OriginMovie==movie&&j.Source==ticket.Url&&j.State is "queued" or "downloading" or "ready");if(current!=null){if(current.Session!=session)throw new InvalidDataException("Este conteúdo já foi solicitado no PC.");return TvJobInfo(current);}
  var entry=new TvJob{OriginMovie=movie,Movie=movie,Session=session,Title=movie.Title,Label=ticket.Label,Kind=selected.Kind,Source=ticket.Url};
  foreach(var old in tvJobs.Values.Where(j=>j.State is not ("queued" or "downloading")).OrderBy(j=>j.Created).Take(Math.Max(0,tvJobs.Count-31)).ToArray())tvJobs.Remove(old.Id);
  tvJobs.Add(entry.Id,entry);
  try{
   if(selected.Kind=="torrent"){
    entry.Hash=TorrentClient.MagnetHash(selected.Url)??throw new InvalidDataException("O magnet da origem é inválido.");
    await TorrentActionAsync(new("torrent-add",command.Id,selected.Url,false));
    entry.State="downloading";StartTvJobPolling();
   }else{
    if(!RemoteFiles.IsPublicHttps(selected.Url))throw new InvalidDataException("A origem não oferece um arquivo direto público.");
    var extension=Path.GetExtension(new Uri(selected.Url).AbsolutePath).ToLowerInvariant();if(!new[]{".mp4",".m4v",".mkv",".webm",".avi",".mov"}.Contains(extension))throw new InvalidDataException("Este endereço não é um arquivo de vídeo direto compatível.");
    var folder=ValidateDownloadFolder(library.MediaFolder)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Cinevo");Directory.CreateDirectory(folder);
    var stem=string.Concat(movie.Title.Take(80).Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');if(stem.Length==0)stem="Video";
    var destination=Path.Combine(folder,stem+"-"+entry.Id+extension);entry.Cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
    var work=DownloadTvFileAsync(entry,selected.Url,destination);tvDirectTasks.Add(work);_=ObserveTvTask(work);
   }
  }catch{tvJobs.Remove(entry.Id);entry.Cancel?.Dispose();throw;}
  return TvJobInfo(entry);
 }
 private async Task DownloadTvFileAsync(TvJob job,string url,string destination)
 {
  var acquired=false;try{await tvDirectSlots.WaitAsync(job.Cancel!.Token);acquired=true;job.State="downloading";var started=Environment.TickCount64;
   await RemoteFiles.DownloadAsync(url,destination,new Progress<(long Received,long? Total)>(p=>{if(job.State!="downloading")return;job.Received=p.Received;job.Total=p.Total;job.Progress=p.Total>0?p.Received/(double)p.Total:0;job.Speed=(long)(p.Received/Math.Max(1,(Environment.TickCount64-started)/1000d));job.Eta=job.Total>0&&job.Speed>0?(job.Total.Value-job.Received)/job.Speed:-1;}),job.Cancel.Token);
   CompleteTvMovie(job,destination);job.State="ready";job.Progress=1;job.Speed=0;job.Eta=0;
  }catch(OperationCanceledException){job.State="cancelled";job.Error="Solicitação cancelada. O arquivo parcial foi removido.";}catch(Exception ex)when(ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException){job.State="failed";if(job.Error.Length==0)job.Error=ex is UnauthorizedAccessException?"O PC não tem permissão para gravar na pasta escolhida.":"O download falhou. Confira a origem, a conexão e o espaço no disco do PC.";}finally{if(acquired)tvDirectSlots.Release();job.Cancel?.Dispose();job.Cancel=null;}
 }
 private void CompleteTvTorrent(TvJob job,string?[] paths){if(paths.Length==0){job.State="failed";job.Error="Nenhum vídeo compatível e concluído foi encontrado nesta transferência.";return;}if(paths.Length==1){CompleteTvMovie(job,paths[0]!);return;}var known=library.Movies.Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);var added=new List<Movie>();foreach(var path in paths){if(path==null||!known.Add(path)||library.RemovedPaths.Contains(path))continue;var (title,year)=Scanner.Parse(path);added.Add(new Movie{Title=title,Year=year,Path=path,Bytes=new FileInfo(path).Length,DownloadedAt=DateTime.UtcNow,Cover=job.Movie.Cover});}if(added.Count==0)return;library.Movies.AddRange(added);try{library.Save();}catch{foreach(var item in added)library.Movies.Remove(item);throw;}Refresh();}
 private void CompleteTvMovie(TvJob job,string path){
  if(!library.Movies.Contains(job.Movie)||!File.Exists(path))return;var existing=library.Movies.FirstOrDefault(m=>m!=job.Movie&&m.Path.Equals(path,StringComparison.OrdinalIgnoreCase));if(existing!=null){job.Movie=existing;job.SingleVideo=true;return;}var movie=job.Movie;var previous=(movie.Path,movie.Bytes,movie.Available,movie.DownloadedAt,movie.ImportedOnly);
  movie.Path=path;movie.Bytes=new FileInfo(path).Length;movie.Available=true;movie.DownloadedAt=DateTime.UtcNow;movie.ImportedOnly=false;
  try{library.Save();}catch{(movie.Path,movie.Bytes,movie.Available,movie.DownloadedAt,movie.ImportedOnly)=previous;job.State="failed";job.Error="O vídeo foi baixado, mas a biblioteca não pôde ser salva. Confira o disco do PC.";throw;}
  job.SingleVideo=true;Refresh();
 }

 private async Task<byte[]?> ReadTimelinePreview(string id,long position,CancellationToken token){var path=await Dispatcher.InvokeAsync(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)&&m.HasLocalFile?m.Path:null);return path==null?null:await TimelinePreviews.GetAsync(path,position,token);}
}
