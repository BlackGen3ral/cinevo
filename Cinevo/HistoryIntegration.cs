using System.IO;
using System.Text.Json;
namespace Cinevo;
public partial class MainWindow
{
 private sealed record HistoryQuery(string State="",string Type="",string Search="",int Page=1);
 private sealed record HistoryKnown(Movie Source,Movie Metadata,ViewingState State);
 private sealed record HistoryRow(WatchRecord Record,string Status,int Done,int Total,string Unit,double? Progress,Movie? Source);
 private static HistoryRow HistoryRowFor(WatchRecord record,List<HistoryKnown> known)
 {
  var archived=record.Members.Values.Select(m=>new{m.Title,m.Season,m.Episode,m.Watched,m.Resume,m.Duration,m.Updated});
  var current=known.Select(m=>{var info=ImportedTitle.Describe(m.Metadata);return new{Title=m.Metadata.Title,Season=info.Season,Episode=info.Episode,m.State.Watched,m.State.Resume,Duration=m.Metadata.DurationMilliseconds,Updated=m.State.UpdatedAt};});
  var members=archived.Concat(current).GroupBy(m=>record.Kind=="movie"?"film":m.Episode.HasValue?$"{m.Season??1}:{m.Episode}":$"season:{m.Season??0}").Select(g=>g.OrderByDescending(m=>m.Updated).ThenByDescending(m=>m.Watched).First()).ToArray();
  var episodes=members.Any(m=>m.Episode.HasValue)?members.Where(m=>m.Episode.HasValue).ToArray():members;
  var done=episodes.Count(m=>m.Watched);var total=episodes.Length;
  var latest=members.OrderByDescending(m=>m.Updated).ThenByDescending(m=>m.Watched).FirstOrDefault();
  var status=record.Manual?record.Status:record.Kind=="movie"?(latest?.Watched==true?"completed":"watching"):total>0&&done==total?"completed":"watching";
  double? progress=status=="completed"?100:record.Kind=="series"&&total>0?episodes.Sum(m=>m.Watched?1:m.Duration>0?Math.Clamp(m.Resume/(double)m.Duration,0,1):0)*100/total:latest?.Duration>0?Math.Clamp(latest.Resume*100d/latest.Duration,0,100):null;
  var source=known.OrderByDescending(m=>!m.State.Watched&&m.State.Resume>1000).ThenBy(m=>m.State.Watched).ThenBy(m=>ImportedTitle.Describe(m.Metadata).Season??1).ThenBy(m=>ImportedTitle.Describe(m.Metadata).Episode??0).Select(m=>m.Source).FirstOrDefault();
  return new(record,status,done,total,record.Kind=="series"&&episodes.Any(m=>m.Episode.HasValue)?"episódios":"registros",progress,source);
 }
 private async Task<object?> HistoryActionAsync(WebAction command,string? viewer=null)
 {
  var profile=CheckViewer(viewer??library.ActiveViewer);Viewers.Capture(library);WatchJournal.Capture(library);
  if(command.Name=="history-mark"){
   if(profile==library.ActiveViewer&&OwnedWindows.OfType<PlayerWindow>().Any())throw new InvalidDataException("Feche o player antes de alterar o acompanhamento deste perfil.");
   if(command.Id==null||!WatchJournal.Book(library,profile).TryGetValue(command.Id,out var entry))throw new InvalidDataException("Título indisponível no histórico deste perfil.");
   var status=command.Address??"";if(status is not ("watching" or "completed" or "dropped"))throw new InvalidDataException("Escolha um estado válido.");
   var bookCopy=JsonSerializer.Serialize(WatchJournal.Book(library,profile));var movies=library.Movies.Where(m=>(!m.ImportedOnly||m.HistoryId!=null)&&WatchJournal.Key(m)==entry.Id).ToArray();var previous=movies.ToDictionary(m=>m,m=>Viewers.Get(m,profile,library.ActiveViewer));
   try{
    if(status=="completed")foreach(var m in movies)Viewers.Set(m,profile,library.ActiveViewer,previous[m] with{Watched=true,Resume=0});
    if(status=="watching"&&movies.Length>0&&movies.All(m=>Viewers.Get(m,profile,library.ActiveViewer).Watched)){var first=movies.OrderBy(m=>ImportedTitle.Describe(m).Season??1).ThenBy(m=>ImportedTitle.Describe(m).Episode??0).First();Viewers.Set(first,profile,library.ActiveViewer,previous[first] with{Watched=false,Resume=0});}
    Viewers.Capture(library);WatchJournal.Capture(library);entry=WatchJournal.Book(library,profile)[entry.Id];if(status=="completed")foreach(var member in entry.Members.Values){member.Watched=true;member.Resume=0;member.Updated=DateTime.UtcNow;}WatchJournal.Mark(entry,status,DateTime.UtcNow);library.Save();Refresh();return new{ok=true};
   }catch{library.WatchHistory[profile]=JsonSerializer.Deserialize<Dictionary<string,WatchRecord>>(bookCopy)!;foreach(var (m,state) in previous){m.Viewers[profile]=state;if(profile==library.ActiveViewer){m.Watched=state.Watched;m.Favorite=state.Favorite;m.ResumeMilliseconds=state.Resume;}}throw;}
  }
  if(command.Name=="history-begin"){
   if(command.Id==null||!webMovies.TryGetValue(command.Id,out var movie)||!library.Movies.Contains(movie))throw new InvalidDataException("Título indisponível.");
   if(command.Address is not ("watching" or "completed" or "dropped"))throw new InvalidDataException("Escolha um estado válido.");var oldId=movie.HistoryId;var existed=WatchJournal.Find(library,movie,profile)!=null;var entry=WatchJournal.Ensure(library,movie,profile);movie.HistoryId??=Guid.NewGuid().ToString("N");try{return await HistoryActionAsync(new("history-mark",entry.Id,command.Address,false),profile);}catch{if(!existed)WatchJournal.Book(library,profile).Remove(entry.Id);movie.HistoryId=oldId;throw;}
  }
  if(command.Name=="history-detail"){
   if(command.Id==null||!WatchJournal.Book(library,profile).TryGetValue(command.Id,out var entry))throw new InvalidDataException("Histórico indisponível.");
   var page=int.TryParse(command.Address,out var parsed)?Math.Clamp(parsed,1,100000):1;var events=entry.Events.AsEnumerable().Reverse().Skip((page-1)*30).Take(30).ToArray();
   return new{entry.Title,entry.Kind,entry.Series,entry.Started,entry.Updated,entry.Completed,episodes=entry.Members.Values.Where(m=>m.Episode.HasValue).GroupBy(m=>m.EpisodeKey).Select(g=>g.OrderByDescending(m=>m.Updated).ThenByDescending(m=>m.Watched).First()).OrderBy(m=>m.Season??1).ThenBy(m=>m.Episode).Select(m=>new{m.Title,m.Season,m.Episode,m.Watched,m.Resume,m.Duration,m.Updated}).ToArray(),events,more=entry.Events.Count>page*30};
  }
  if(command.Name!="history-list")throw new InvalidDataException("Histórico inválido.");
  var query=JsonSerializer.Deserialize<HistoryQuery>(command.Address??"{}",new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();if(query.Search.Length>200||query.State is not ("" or "watching" or "completed" or "dropped")||query.Type is not ("" or "movie" or "series"))throw new InvalidDataException("Filtro inválido.");
  var records=JsonSerializer.Deserialize<Dictionary<string,WatchRecord>>(JsonSerializer.Serialize(WatchJournal.Book(library,profile)))!.Values.ToArray();
  var catalog=library.Movies.Where(m=>!m.ImportedOnly||m.HistoryId!=null).Select(m=>new HistoryKnown(m,new Movie{Title=m.Title,Year=m.Year,SeriesTitle=m.SeriesTitle,MediaType=m.MediaType,Season=m.Season,Episode=m.Episode,DurationMilliseconds=m.DurationMilliseconds},Viewers.Get(m,profile,library.ActiveViewer))).ToArray();
  var rows=await Task.Run(()=>{var groups=catalog.GroupBy(m=>WatchJournal.Key(m.Metadata)).ToDictionary(g=>g.Key,g=>g.ToList());return records.Select(r=>HistoryRowFor(r,groups.GetValueOrDefault(r.Id)??[])).ToArray();},lifetime.Token);
  var matches=rows.Where(r=>(query.State.Length==0||r.Status==query.State)&&(query.Type.Length==0||r.Record.Kind==query.Type)&&(query.Search.Length==0||$"{r.Record.Title} {r.Record.Year}".Contains(query.Search,StringComparison.CurrentCultureIgnoreCase))).OrderByDescending(r=>r.Record.Updated).ThenBy(r=>r.Record.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();var offset=(Math.Clamp(query.Page,1,100000)-1)*24;
  return new{viewer=library.Profiles.First(p=>p.Id==profile),total=rows.Length,counts=new{watching=rows.Count(r=>r.Status=="watching"),completed=rows.Count(r=>r.Status=="completed"),dropped=rows.Count(r=>r.Status=="dropped"),films=rows.Count(r=>r.Record.Kind=="movie"),series=rows.Count(r=>r.Record.Kind=="series")},resultCount=matches.Length,more=matches.Length>offset+24,
   items=matches.Skip(offset).Take(24).Select(r=>new{r.Record.Id,r.Record.Title,r.Record.Year,r.Record.Kind,r.Status,r.Done,r.Total,r.Unit,r.Progress,r.Record.Started,r.Record.Updated,r.Record.Completed,eventCount=r.Record.Events.Count,retained=r.Source==null,movie=r.Source!=null&&library.Movies.Contains(r.Source)?WebMovieFor(r.Source,profile):null}).ToArray()};
 }
}
