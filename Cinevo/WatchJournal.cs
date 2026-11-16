using System.Security.Cryptography;
using System.Text;
namespace Cinevo;
public sealed class WatchMember
{
 public string Id{get;set;}="";public string Title{get;set;}="";public int? Season{get;set;}public int? Episode{get;set;}public bool Watched{get;set;}public long Resume{get;set;}public long Duration{get;set;}public DateTime? Updated{get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public string EpisodeKey=>Episode.HasValue?$"{Season??1}:{Episode}":$"season:{Season??0}";
}
public sealed record WatchEvent(string Status,DateTime? At,string Episode="");
public sealed class WatchRecord
{
 public string Id{get;set;}="";public string Title{get;set;}="";public string Year{get;set;}="";public string Kind{get;set;}="movie";public string Series{get;set;}="";public string Status{get;set;}="watching";public bool Manual{get;set;}public DateTime? Started{get;set;}public DateTime? Updated{get;set;}public DateTime? Completed{get;set;}
 public Dictionary<string,WatchMember> Members{get;set;}=[];public List<WatchEvent> Events{get;set;}=[];
}
public static class WatchJournal
{
 public static string Key(Movie movie){var info=ImportedTitle.Describe(movie);var text=info.Type=="series"?"series|"+CoverService.Normalize(info.Series):"movie|"+CoverService.Normalize(Scanner.Parse(movie.Title+".mkv").Title)+"|"+movie.Year;return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();}
 public static Dictionary<string,WatchRecord> Book(Library library,string profile){if(!library.WatchHistory.TryGetValue(profile,out var book))library.WatchHistory[profile]=book=[];return book;}
 public static WatchRecord? Find(Library library,Movie movie,string profile)=>library.WatchHistory.TryGetValue(profile,out var book)&&book.Count>0?book.GetValueOrDefault(Key(movie)):null;
 public static bool Dropped(Library library,Movie movie,string profile)=>Find(library,movie,profile) is {Status:"dropped",Manual:true};
 public static WatchRecord Ensure(Library library,Movie movie,string profile){var book=Book(library,profile);var key=Key(movie);if(!book.TryGetValue(key,out var entry)){var info=ImportedTitle.Describe(movie);book[key]=entry=new(){Id=key,Title=info.Type=="series"?info.Series:movie.Title,Year=movie.Year,Kind=info.Type,Series=info.Series};}return entry;}
 public static void Capture(Library library)
 {
  var profiles=library.Profiles.Select(p=>p.Id).ToHashSet();
  foreach(var movie in library.Movies){
   if(!movie.Viewers.Values.Any(v=>v.Watched||v.Resume>1000)&&movie.HistoryId==null)continue;
   foreach(var (profile,state) in movie.Viewers){if(!profiles.Contains(profile))continue;var tracked=state.Watched||state.Resume>1000;var book=library.WatchHistory.GetValueOrDefault(profile);var key=Key(movie);var entry=book?.GetValueOrDefault(key);
    if(!tracked&&(entry==null||movie.HistoryId==null||!entry.Members.ContainsKey(movie.HistoryId)))continue;
    entry??=Ensure(library,movie,profile);movie.HistoryId??=Guid.NewGuid().ToString("N");var info=ImportedTitle.Describe(movie);var id=movie.HistoryId;entry.Members.TryGetValue(id,out var before);
    var changed=before==null||before.Watched!=state.Watched||before.Resume!=state.Resume;
    entry.Title=info.Type=="series"?info.Series:movie.Title;entry.Year=movie.Year;
    entry.Members[id]=new(){Id=id,Title=movie.Title,Season=info.Season,Episode=info.Episode,Watched=state.Watched,Resume=state.Resume,Duration=movie.DurationMilliseconds,Updated=changed?state.UpdatedAt:before?.Updated};
    if(!changed)continue;
    if(entry.Manual&&entry.Status=="dropped"&&(!state.UpdatedAt.HasValue||entry.Updated.HasValue&&state.UpdatedAt<entry.Updated))continue;
    entry.Manual=false;var status=state.Watched?"completed":"watching";entry.Status=info.Type=="series"?"watching":status;if(state.UpdatedAt.HasValue&&(entry.Updated==null||state.UpdatedAt>=entry.Updated))entry.Updated=state.UpdatedAt;
    if(entry.Started==null&&!state.Watched&&state.Resume>1000)entry.Started=state.UpdatedAt;
    if(state.Watched&&state.CompletedAt.HasValue)entry.Completed=state.CompletedAt;
    if(before==null||before.Watched!=state.Watched){entry.Events.Add(new(status,state.UpdatedAt,info.Type=="series"?EpisodeLabel(info.Season,info.Episode):""));}
   }
  }
 }
 public static string EpisodeLabel(int? season,int? episode)=>episode.HasValue?$"T{season??1:00} · E{episode:00}":season.HasValue?$"Temporada {season}":"Série";
 public static void Mark(WatchRecord entry,string status,DateTime now){if(status is not ("watching" or "completed" or "dropped"))throw new ArgumentException("Estado inválido.");entry.Status=status;entry.Manual=true;entry.Updated=now;if(status=="watching")entry.Started??=now;if(status=="completed")entry.Completed=now;if(entry.Events.LastOrDefault() is not {} last||last.Status!=status||last.Episode.Length>0)entry.Events.Add(new(status,now));}
}
