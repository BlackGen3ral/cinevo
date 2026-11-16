using Cinevo;
using System.Text.Json;
internal static class AccountModelTests
{
 internal static void Run()
 {
  var movie=new Movie{Title="Aurora",Year="2020",Path=@"C:\Privado\filme.mp4",Cover=@"C:\Privado\capa.jpg",ExternalSubtitlePath=@"C:\Privado\legenda.srt",Favorite=true};
  var state=SyncTitle.From(movie);var json=JsonSerializer.Serialize(state);if(json.Contains("Privado")||json.Contains("Path"))throw new Exception("Caminhos locais não podem ser sincronizados");
  var remote=state with{Favorite=false,Resume=40000};var baseline=state with{Favorite=false};var local=baseline with{Favorite=true};
  var merged=SyncTitle.Merge(local,baseline,remote);
  if(!merged.Favorite||merged.Resume!=40000||remote.Favorite)throw new Exception("A combinação deve preservar progresso remoto e favorito local sem alterar a resposta remota");
  var same=state with{};SyncTitle.Apply(movie,merged,same);if(!movie.Favorite||movie.ResumeMilliseconds!=40000||movie.Path!=@"C:\Privado\filme.mp4")throw new Exception("Aplicação dos dados deve preservar arquivos locais");
  Console.WriteLine("PASS: sincronização combina alterações por campo e exclui caminhos e arquivos privados.");
 }
}
