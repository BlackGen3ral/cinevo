using System.IO;
namespace Cinevo;
public sealed record ViewerProfile(string Id,string Name);
public sealed record ViewingState(bool Watched=false,bool Favorite=false,long Resume=0,DateTime? UpdatedAt=null,DateTime? CompletedAt=null);
public static class Viewers
{
    public const string Default="principal";
    public static ViewingState Get(Movie movie,string profile,string active){var stored=movie.Viewers.GetValueOrDefault(profile)??new();return profile==active?stored with{Watched=movie.Watched,Favorite=movie.Favorite,Resume=movie.ResumeMilliseconds}:stored;}
    private static ViewingState Dated(ViewingState before,ViewingState after,bool stamp){if(before.Watched==after.Watched&&before.Resume==after.Resume)return after with{UpdatedAt=before.UpdatedAt,CompletedAt=before.CompletedAt};return after with{UpdatedAt=stamp?DateTime.UtcNow:null,CompletedAt=after.Watched&&!before.Watched?(stamp?DateTime.UtcNow:null):before.CompletedAt};}
    public static void Set(Movie movie,string profile,string active,ViewingState state,bool stamp=true){state=Dated(Get(movie,profile,active),state,stamp);movie.Viewers[profile]=state;if(profile==active){movie.Watched=state.Watched;movie.Favorite=state.Favorite;movie.ResumeMilliseconds=state.Resume;}}
    public static void Capture(Library library,bool stamp=true){foreach(var movie in library.Movies){var before=movie.Viewers.GetValueOrDefault(library.ActiveViewer)??new(movie.Watched,movie.Favorite,movie.ResumeMilliseconds);movie.Viewers[library.ActiveViewer]=Dated(before,before with{Watched=movie.Watched,Favorite=movie.Favorite,Resume=movie.ResumeMilliseconds},stamp);}}
    public static void Switch(Library library,string id){if(!library.Profiles.Any(p=>p.Id==id))throw new InvalidDataException("Perfil indisponível.");Capture(library);foreach(var m in library.Movies){var state=m.Viewers.GetValueOrDefault(id)??new();m.Watched=state.Watched;m.Favorite=state.Favorite;m.ResumeMilliseconds=state.Resume;}library.ActiveViewer=id;}
}
