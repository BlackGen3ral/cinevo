using Cinevo;
internal static class ViewerTests
{
 internal static void Run(string root){
  var movie=new Movie{Title="Aurora S01E01",Favorite=true,Watched=true,ResumeMilliseconds=20000};var library=new Library{Movies=[movie]};
  library.Profiles.Add(new("pessoa-b","Pessoa B"));Viewers.Switch(library,"pessoa-b");
  if(movie.Favorite||movie.Watched||movie.ResumeMilliseconds!=0)throw new Exception("Novo perfil deve começar sem progresso");
  movie.ResumeMilliseconds=9000;Viewers.Switch(library,"principal");if(!movie.Favorite||!movie.Watched||movie.ResumeMilliseconds!=20000)throw new Exception("Perfil principal deve preservar dados anteriores");
  Viewers.Set(movie,"pessoa-b",library.ActiveViewer,new(false,true,5000));if(movie.ResumeMilliseconds!=20000||Viewers.Get(movie,"pessoa-b",library.ActiveViewer).Resume!=5000)throw new Exception("A TV não pode trocar o perfil ativo do PC");
  var saved=System.Text.Json.JsonSerializer.Deserialize<Library>(System.Text.Json.JsonSerializer.Serialize(library))!;if(saved.Movies[0].Viewers["pessoa-b"].Resume!=5000)throw new Exception("Estado dos perfis não persistiu");
  var file=Path.Combine(root,"viewer-fixture.mp4");File.WriteAllText(file,"fixture");movie.Path=file;var next=new Movie{Title="Aurora S02E01",Path=file};
  var episodes=SeriesCenter.Episodes(movie,[next,movie]).ToArray();if(episodes[0]!=movie||SeriesCenter.Continue(episodes,"principal","principal")!=next||SeriesCenter.Continue(episodes,"pessoa-b","principal")!=movie)throw new Exception("Continuação da série deve respeitar perfil e ordem das temporadas");
  File.Delete(file);Console.WriteLine("PASS: perfis preservam dados antigos, estado da TV independente do PC e séries continuam no episódio do perfil.");
 }
}
