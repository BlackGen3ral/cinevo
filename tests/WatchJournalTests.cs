using Cinevo;
internal static class WatchJournalTests
{
 internal static void Run(string root){
  var first=new Movie{Title="Horizonte",Year="2020",Watched=true};var e1=new Movie{Title="Aurora S01E01",SeriesTitle="Aurora",Season=1,Episode=1,ResumeMilliseconds=10000,DurationMilliseconds=20000};var e2=new Movie{Title="Aurora S01E02",SeriesTitle="Aurora",Season=1,Episode=2};var library=new Library{Movies=[first,e1,e2]};library.Profiles.Add(new("b","Pessoa B"));
  Viewers.Capture(library,false);WatchJournal.Capture(library);var film=WatchJournal.Find(library,first,"principal")!;var series=WatchJournal.Find(library,e1,"principal")!;
  if(film.Updated!=null||film.Events.Single().At!=null||series.Members.Count!=1||WatchJournal.Book(library,"b").Count!=0)throw new Exception("Migração deve preservar estados sem inventar datas ou misturar perfis");
  WatchJournal.Mark(series,"dropped",DateTime.UtcNow);var resume=e1.ResumeMilliseconds;Viewers.Capture(library);WatchJournal.Capture(library);if(!WatchJournal.Dropped(library,e1,"principal")||e1.ResumeMilliseconds!=resume)throw new Exception("Abandonar deve preservar o ponto e resistir a salvamentos comuns");
  e1.Favorite=true;Viewers.Capture(library);WatchJournal.Capture(library);if(!WatchJournal.Dropped(library,e1,"principal"))throw new Exception("Favoritar não pode retomar um título abandonado");
  e1.ResumeMilliseconds=12000;Viewers.Capture(library);WatchJournal.Capture(library);if(WatchJournal.Dropped(library,e1,"principal")||series.Updated==null)throw new Exception("Novo progresso real deve retomar o acompanhamento e registrar a data");var count=series.Events.Count;
  for(var i=0;i<15;i++){e1.ResumeMilliseconds++;Viewers.Capture(library);WatchJournal.Capture(library);}if(series.Events.Count!=count)throw new Exception("Cada atualização de progresso não pode gerar um evento redundante");
  Viewers.Set(e1,"b",library.ActiveViewer,new(true,false,0));WatchJournal.Capture(library);if(WatchJournal.Find(library,e1,"b")?.Completed==null||e1.Watched)throw new Exception("Conclusão da TV deve ficar no perfil correto sem alterar o PC");
  library.Movies.Remove(first);if(!WatchJournal.Book(library,"principal").ContainsKey(film.Id))throw new Exception("Retirar um título da biblioteca não pode apagar seu histórico");
  var another=new Movie{Title="Horizonte",Year="2021",Watched=true};library.Movies.Add(another);Viewers.Capture(library);WatchJournal.Capture(library);if(WatchJournal.Key(another)==film.Id)throw new Exception("Filmes de anos diferentes não devem se misturar");
  WatchJournal.Mark(series,"dropped",DateTime.UtcNow);var eventsBefore=series.Events.Count;e1.Favorite=true;Viewers.Capture(library);WatchJournal.Capture(library);if(!WatchJournal.Dropped(library,e1,"principal")||series.Events.Count!=eventsBefore)throw new Exception("Favoritar não pode desfazer abandono ou criar eventos");
  Viewers.Set(e1,"principal","principal",Viewers.Get(e1,"principal","principal") with{Resume=11000});WatchJournal.Capture(library);if(WatchJournal.Dropped(library,e1,"principal"))throw new Exception("Progresso real deve retomar uma série abandonada");
  var old=Environment.GetEnvironmentVariable("CINEVO_TEST_DATA");Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",Path.Combine(root,"journal"));try{library.Save();var restored=Library.Load();if(restored.WatchHistory["principal"].Count!=library.WatchHistory["principal"].Count||restored.WatchHistory["b"].Count!=1||!File.Exists(Library.DataPath))throw new Exception("Histórico precisa persistir entre sessões");}finally{Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",old);}
  Console.WriteLine("PASS: histórico por perfil, legado sem datas inventadas, abandono, retomada, eventos sem duplicação, séries, remoção e persistência.");
 }
}
