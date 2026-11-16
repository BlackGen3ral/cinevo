using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Cinevo;
public sealed record SyncTitle(string Title,string Year,string Genre,string SeriesTitle,string SourceUrl,string StreamUrl,string CoverUrl,string TmdbUrl,bool Member,bool Favorite,bool Watched,long Resume,long Duration,int Season,int Episode)
{
    public static SyncTitle From(Movie m) => new(m.Title,m.Year,m.Genre,m.SeriesTitle,Public(m.SourceUrl),Public(m.StreamUrl),Public(m.PortalCoverUrl),Public(m.TmdbUrl),!m.ImportedOnly,m.Favorite,m.Watched,m.ResumeMilliseconds,m.DurationMilliseconds,m.Season??0,m.Episode??0);
    private static string Public(string value) => RemoteFiles.IsPublicHttps(value)?value:"";
    public static string Identity(Movie m) { var kind=ImportedTitle.Describe(m); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CoverService.Normalize(m.Title)+"|"+m.Year+"|"+kind.Season+"|"+kind.Episode))).ToLowerInvariant(); }
    public static SyncTitle Merge(SyncTitle local, SyncTitle? baseline, SyncTitle? remote)
    {
        if(remote==null)return local;
        if(baseline==null)return remote with {Member=local.Member||remote.Member};
        var result=remote with {};
        foreach(var property in typeof(SyncTitle).GetProperties())if(!Equals(property.GetValue(local),property.GetValue(baseline)))property.SetValue(result,property.GetValue(local));
        return result;
    }
    public static void Apply(Movie movie,SyncTitle merged,SyncTitle original)
    {
        // Preserve edits made while an HTTP request was in flight.
        var current=From(movie);var resolved=Merge(current,original,merged);
        movie.Title=resolved.Title;movie.Year=resolved.Year;movie.Genre=resolved.Genre;movie.SeriesTitle=resolved.SeriesTitle;
        movie.SourceUrl=resolved.SourceUrl;movie.StreamUrl=resolved.StreamUrl;movie.PortalCoverUrl=resolved.CoverUrl;movie.TmdbUrl=resolved.TmdbUrl;
        movie.ImportedOnly=!resolved.Member;movie.Favorite=resolved.Favorite;movie.Watched=resolved.Watched;movie.ResumeMilliseconds=resolved.Resume;
        movie.DurationMilliseconds=resolved.Duration;movie.Season=resolved.Season>0?resolved.Season:null;movie.Episode=resolved.Episode>0?resolved.Episode:null;
        if(movie.SeriesTitle.Length>0)movie.MediaType="series";
    }
}
public sealed record CloudRow(string Id,SyncTitle Data,int Version);
public sealed class AccountState
{
    public string Username {get;set;}="";
    public string Token {get;set;}="";
    public Dictionary<string,SyncTitle> Baseline {get;set;}=[];
    private static string PathName=>Path.Combine(Library.DataDirectory,"account.dat");
    public static AccountState? Load()=>File.Exists(PathName)?JsonSerializer.Deserialize<AccountState>(LocalSecret.Unprotect(File.ReadAllBytes(PathName))):null;
    public void Save(){Directory.CreateDirectory(Library.DataDirectory);var temp=PathName+".tmp";File.WriteAllBytes(temp,LocalSecret.Protect(JsonSerializer.SerializeToUtf8Bytes(this)));File.Move(temp,PathName,true);}
    public static void Remove()=>File.Delete(PathName);
}
