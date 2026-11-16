using Cinevo;
internal static class InterfaceTests
{
 internal static void Run(){
  if(!new VisualStyle().Valid()||!new VisualStyle("small",0,100,"compact").Valid()||new VisualStyle("huge").Valid()||new VisualStyle(Glow:101).Valid()||new VisualStyle(ClickVolume:-1).Valid()||new VisualStyle(Density:"other").Valid())throw new Exception("Preferências fora dos limites devem ser recusadas");
  var old=new Movie{Title="Duna",Year="1984"};var recent=new Movie{Title="Duna",Year="2021",TorrentHashes=[new string('a',40)]};var index=new TransferArtworkIndex([old,recent]);
  if(index.Find(new string('a',40),"Nome alternativo")!=recent||index.Find("","Duna.2021.1080p.WEB-DL")!=recent||index.Find("","Duna.1984.720p")!=old||index.Find("","Duna.2000.1080p")!=null||index.Find("","Outro.2021")!=null)throw new Exception("Capas devem respeitar hash, título e ano sem associar outro filme");
  var film=new Movie{Title="Aurora",Year="2024"};var e1=new Movie{Title="Aurora S01E01",SeriesTitle="Aurora",Season=1,Episode=1};var e2=new Movie{Title="Aurora S01E02",SeriesTitle="Aurora",Season=1,Episode=2};Movie[] all=[film,e1,e2];
  if(LibrarySearch.Films(all,"Aurora").Single()!=film||LibrarySearch.Series(all,"Aurora").Count()!=1||LibrarySearch.Episodes(all,"Aurora").Count()!=2)throw new Exception("Busca precisa separar filmes, séries agrupadas e episódios");
  var activity=new PlayerActivity();activity.Touch(100);if(activity.Hide(3099,true,false)||!activity.Hide(3100,true,false)||activity.Hide(10000,false,false)||activity.Hide(10000,true,true))throw new Exception("Controles devem permanecer acessíveis em pausa e durante interação");activity.Touch(10000);if(activity.Hide(10001,true,false))throw new Exception("Entrada deve revelar os controles imediatamente");
  Console.WriteLine("PASS: limites visuais, capa por hash/título/ano, categorias da busca e inatividade do player.");
 }
}
