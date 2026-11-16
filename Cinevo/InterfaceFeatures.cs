using System.IO;
using System.Text.Json;
namespace Cinevo;
public partial class MainWindow
{
 private object VisualInfo(string? viewer=null){var id=CheckViewer(viewer??library.ActiveViewer);return library.VisualStyles.GetValueOrDefault(id)??new VisualStyle();}
 private Task<object?> VisualActionAsync(WebAction command){
  if(command.Name=="visual-read")return Task.FromResult<object?>(VisualInfo());
  if(command.Name=="visual-import-covers"){
   if(!int.TryParse(command.Address,out var width)||width is <124 or >280)throw new InvalidDataException("Escolha um tamanho de capa válido.");
   var profile=CheckViewer(command.Id??library.ActiveViewer);var current=(VisualStyle)VisualInfo(profile);library.VisualStyles.TryGetValue(profile,out var old);var next=current with{ImportCoverWidth=width};library.VisualStyles[profile]=next;try{library.Save();}catch{if(old==null)library.VisualStyles.Remove(profile);else library.VisualStyles[profile]=old;throw;}return Task.FromResult<object?>(next);
  }
  if(command.Name!="visual-save")throw new InvalidDataException("Preferência inválida.");
  var value=JsonSerializer.Deserialize<VisualStyle>(command.Address??"{}",new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();if(!value.Valid())throw new InvalidDataException("Confira tamanho das capas, brilho, volume e densidade.");
  var id=library.ActiveViewer;library.VisualStyles.TryGetValue(id,out var previous);library.VisualStyles[id]=value;
  try{library.Save();}catch{if(previous==null)library.VisualStyles.Remove(id);else library.VisualStyles[id]=previous;throw;}
  return Task.FromResult<object?>(value);
 }
 private async Task<object> OrganizedSearch(string query){query=query.Trim();if(query.Length is <1 or >200)throw new InvalidDataException("Digite até 200 caracteres.");
  // Snapshot search fields on the dispatcher; classify large catalogs away from it.
  var source=library.Movies.ToArray();var movies=source.Select(m=>new Movie{Title=m.Title,SeriesTitle=m.SeriesTitle,Year=m.Year,Genre=m.Genre,Season=m.Season,Episode=m.Episode,CastNames=[..m.CastNames]}).ToArray();
  var found=await Task.Run(()=>{
   var indices=movies.Select((m,i)=>(m,i)).ToDictionary(x=>x.m,x=>x.i);
   return(films:LibrarySearch.Films(movies,query).Take(60).Select(m=>indices[m]).ToArray(),series:LibrarySearch.Series(movies,query).Take(60).Select(m=>indices[m]).ToArray(),episodes:LibrarySearch.Episodes(movies,query).Take(60).Select(m=>indices[m]).ToArray(),people:movies.SelectMany(m=>m.CastNames).Where(n=>n.Contains(query,StringComparison.CurrentCultureIgnoreCase)).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(20).ToArray());
  },lifetime.Token);
  object[] Map(int[] ids)=>ids.Select(i=>source[i]).Where(library.Movies.Contains).Select(WebMovie).ToArray();
  return new{query,films=Map(found.films),series=Map(found.series),episodes=Map(found.episodes),found.people};
 }
 private async Task<object?> SearchActionAsync(WebAction command){
  var query=command.Address?.Trim()??"";
  if(command.Name=="search-organized")return await OrganizedSearch(query);
  if(command.Name=="search-person-local")return library.Movies.Where(m=>m.CastNames.Contains(query,StringComparer.CurrentCultureIgnoreCase)).Take(80).Select(WebMovie).ToArray();
  var credential=TmdbClient.ReadCredential();if(credential.Length==0)return new{configured=false};
  using var client=new TmdbClient(credential);
  if(command.Name=="search-people"){if(query.Length is <2 or >200)throw new InvalidDataException("Digite um nome válido.");return new{configured=true,people=await client.PeopleAsync(query,lifetime.Token)};}
  if(command.Name=="search-person-library"){
   if(!int.TryParse(command.Id,out var id)||id<1)throw new InvalidDataException("Pessoa inválida.");
   var credits=await client.PersonCreditsAsync(id,lifetime.Token);
   return library.Movies.Where(m=>credits.Any(c=>TmdbClient.RouteFromUrl(m.TmdbUrl)?.Split('/').Take(2).SequenceEqual(c.Route.Split('/'))==true||CoverService.Normalize(m.Title)==CoverService.Normalize(c.Title)&&(m.Year.Length==0||c.Year==m.Year))).Take(80).Select(WebMovie).ToArray();
  }
  throw new InvalidDataException("Busca inválida.");
 }
 private TransferArtworkIndex? transferArtwork;private int transferArtworkRevision=-1;
 private async Task PrepareTransferArtwork(){if(transferArtwork!=null&&transferArtworkRevision==webRevision)return;var revision=webRevision;var entries=TransferArtworkIndex.Capture(library.Movies);var index=await Task.Run(()=>new TransferArtworkIndex(entries),lifetime.Token);if(revision==webRevision){transferArtwork=index;transferArtworkRevision=revision;}}
 private Movie? TransferMovie(string hash,string name){var movie=transferArtwork?.Find(hash,name);return movie!=null&&library.Movies.Contains(movie)?movie:null;}
 private void RememberTransfer(string? id,string url){if(id!=null&&webMovies.TryGetValue(id,out var movie)&&TorrentClient.MagnetHash(url) is {} hash&&movie.TorrentHashes.Add(hash)){library.Save();transferArtwork=null;}}
}
