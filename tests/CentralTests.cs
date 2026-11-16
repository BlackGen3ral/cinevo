using Cinevo;
internal static class CentralTests
{
 internal static void Run(){
  var cache=new PreviewCache(10,3);cache.Put("a",new byte[4]);cache.Put("b",new byte[4]);cache.Get("a");cache.Put("c",new byte[4]);if(cache.Get("b")!=null||cache.Get("a")==null||cache.Bytes!=8||cache.Count!=2)throw new Exception("Prévias precisam descartar a menos recente ao atingir o orçamento");cache.Put("a",new byte[11]);if(cache.Get("a")!=null||cache.Bytes!=4)throw new Exception("Prévia acima do limite não pode entrar no cache");
  var tickets=new TvDownloadTickets();var now=DateTime.UtcNow;var option=tickets.Add("tv-a","movie-a","https://example.org/video.mp4","file","1080p",now);
  if(tickets.Get(option,"tv-a","movie-a",now)==null||tickets.Get(option,"tv-b","movie-a",now)!=null||tickets.Get(option,"tv-a","movie-b",now)!=null||tickets.Get(option,"tv-a","movie-a",now.AddMinutes(11))!=null)throw new Exception("Opções precisam pertencer à sessão, ao título e expirar");
  for(var i=0;i<300;i++)tickets.Add("tv-a","movie-a","https://example.org/"+i,"file","teste",now);if(tickets.Get(option,"tv-a","movie-a",now)!=null)throw new Exception("Lista de opções deve ser limitada");
  if(!MediaFailure.Online(now.AddSeconds(-45),now)||MediaFailure.Online(now.AddSeconds(-46),now)||!MediaFailure.Explain(3).Contains("decodificar"))throw new Exception("Diagnóstico deve distinguir perda de contato e falha de decodificação");
  Console.WriteLine("PASS: prévias limitadas por bytes e LRU, opções por sessão/título com expiração e presença da central.");
 }
}
