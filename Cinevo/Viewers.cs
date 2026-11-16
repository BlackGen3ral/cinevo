using System.IO;
namespace Cinevo;
public sealed record ViewerProfile(string Id,string Name);
public sealed record ViewingState(bool Watched=false,bool Favorite=false,long Resume=0);
public static class Viewers
{
    public const string Default="principal";
    public static ViewingState Get(Movie movie,string profile,string active)=>profile==active?new(movie.Watched,movie.Favorite,movie.ResumeMilliseconds):movie.Viewers.GetValueOrDefault(profile)??new();
    public static void Set(Movie movie,string profile,string active,ViewingState state){movie.Viewers[profile]=state;if(profile==active){movie.Watched=state.Watched;movie.Favorite=state.Favorite;movie.ResumeMilliseconds=state.Resume;}}
    public static void Capture(Library library){foreach(var movie in library.Movies)movie.Viewers[library.ActiveViewer]=new(movie.Watched,movie.Favorite,movie.ResumeMilliseconds);}
    public static void Switch(Library library,string id){if(!library.Profiles.Any(p=>p.Id==id))throw new InvalidDataException("Perfil indisponível.");Capture(library);foreach(var m in library.Movies){var state=m.Viewers.GetValueOrDefault(id)??new();m.Watched=state.Watched;m.Favorite=state.Favorite;m.ResumeMilliseconds=state.Resume;}library.ActiveViewer=id;}
}
