using Cinevo;
internal static class SmartLibraryTests {
 internal static void Run(string root) {
  var a=new Movie{Title="Aurora",Year="2020",Genre="Drama",DurationMilliseconds=5400000,UserScore=8,Resolution="1080p",Language="Português",Collections=["Família"]};
  var b=new Movie{Title="Aurora",Year="2020"}; var c=new Movie{Title="Aurora",Year="2021"};
  if(new SmartFilter("Drama",80,100,7,"1080p","Português","Família").Apply([a,b,c]).Single()!=a)throw new Exception("Filtro combinado incorreto");
  if(new SmartFilter(Duplicates:true).Apply([a,b,c]).Count()!=2)throw new Exception("Duplicados devem respeitar ano");
  if(new SmartFilter(MaxMinutes:90).Apply([b]).Any())throw new Exception("Duração desconhecida não atende filtro");
  var s="@smart:{\"search\":\"Aurora\",\"filter\":{\"score\":8}}";if(SmartFilter.Parse(ref s).Score!=8||s!="Aurora")throw new Exception("Filtro e busca não combinados");
  var cache=Path.Combine(root,"storage");Directory.CreateDirectory(Path.Combine(cache,"covers"));Directory.CreateDirectory(Path.Combine(cache,"updates"));
  var cover=Path.Combine(cache,"covers","kept.jpg");File.WriteAllText(cover,"cover");File.WriteAllText(Path.Combine(cache,"covers","unused.img"),"cache");File.WriteAllText(Path.Combine(cache,"covers","film.mp4"),"video");
  var old=Path.Combine(cache,"updates","Cinevo-Setup.exe");File.WriteAllText(old,"old");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-3));
  var partial=old+".download";File.WriteAllText(partial,"partial");File.SetLastWriteTimeUtc(partial,DateTime.UtcNow.AddDays(-3));
  var protectedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase){cover};
  if(SmartStorage.Cleanable(cache,"covers",protectedPaths).Single().Name!="unused.img")throw new Exception("Capas em uso e vídeos devem ser protegidos");
  if(SmartStorage.Cleanable(cache,"updates",protectedPaths).Single().FullName!=old)throw new Exception("Parciais e pacotes recentes devem ser protegidos");
  File.Delete(Path.Combine(cache,"covers","film.mp4"));
  Console.WriteLine("PASS: filtros combinados, duplicados, metadados ausentes e limpeza restrita aos caches permitidos.");
 }
}
