using Cinevo;
internal static class ExperienceTests
{
 internal static void Run(string root){
  var now=DateTime.UtcNow;var timer=new EpisodeCountdown();timer.Begin(now);if(timer.Remaining(now)!=10||timer.Due(now.AddSeconds(9))||!timer.Due(now.AddSeconds(10)))throw new Exception("Contagem deve durar 10 segundos");timer.Cancel();if(timer.Due(now.AddMinutes(1)))throw new Exception("Cancelamento deve impedir reprodução automática");timer.Reset();timer.Begin(now);if(timer.Remaining(now.AddSeconds(4))!=6)throw new Exception("Contagem deve reiniciar para uma nova sessão");
  var path=Path.Combine(root,"home-fixture.mp4");File.WriteAllText(path,"fixture");var first=new Movie{Title="Aurora S01E01",Path=path,Watched=true};var next=new Movie{Title="Aurora S01E02",Path=path};
  Viewers.Set(first,"b","principal",new(false,false,5000));
  if(HomeFeed.Next([first,next],"principal","principal").Single()!=next||HomeFeed.Next([first,next],"b","principal").Single()!=first)throw new Exception("Próximo episódio deve respeitar o perfil");
  if(HomeFeed.Returning([first,next],"principal","principal").Any()||HomeFeed.Returning([first,next],"b","principal").Single()!=first)throw new Exception("Continue assistindo deve ser individual");
  if(!BackdropArtwork.Valid("/abc_123.jpg")||BackdropArtwork.Valid("https://private.test/image.jpg")||BackdropArtwork.Valid("/../../secret.png"))throw new Exception("Arte deve aceitar apenas identificadores de imagem TMDB");
  File.Delete(path);Console.WriteLine("PASS: início por perfil, próximo episódio, cancelamento e reinício da contagem e validação da arte panorâmica.");
 }
}
