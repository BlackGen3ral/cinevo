using Cinevo;
using System.Net;
using System.Net.Http.Json;
internal static class TvWebTests
{
 internal static async Task Run(string root)
 {
  var previous=Environment.GetEnvironmentVariable("CINEVO_TEST_DATA");Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",root);
  var path=Path.Combine(root,"range-fixture.mp4");File.WriteAllBytes(path,Enumerable.Range(0,100).Select(i=>(byte)i).ToArray());
  var tickets=new TvDownloadTickets();var added=0;string? ticket=null;
  Task<object?> Download(TvMediaCommand command,string session){if(command.Name=="tv-download-options"){ticket=tickets.Add(session,command.Id!,"https://example.org/video.mp4","file","1080p");return Task.FromResult<object?>(new[]{new{id=ticket,label="1080p",kind="file"}});}if(command.Name=="tv-download-start"){if(tickets.Get(command.Address!,session,command.Id!)==null)throw new InvalidDataException("Opção inválida ou expirada.");added++;return Task.FromResult<object?>(new{state="queued"});}return Task.FromResult<object?>(Array.Empty<object>());}
  long position=-1;var server=new TvWeb((q,p,viewer)=>Task.FromResult<object>(new{movies=Array.Empty<object>()}),id=>Task.FromResult<byte[]?>(null),id=>Task.FromResult<string?>(id=="known"?path:null),(id,time,duration,favorite,viewer)=>{position=time;return Task.CompletedTask;},imports:(q,p,viewer)=>Task.FromResult<object>(new{movies=new[]{new{title="Importado"}}}),download:Download,preview:(id,time,token)=>Task.FromResult<byte[]?>(new byte[]{255,216,255,217}));
  try {
   await server.StartAsync(CancellationToken.None);var origin=$"http://127.0.0.1:{server.Port}";
   using var http=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=true});
   if((await http.GetAsync(origin+"/api/list")).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Catálogo TV deve exigir pareamento");
   foreach(var endpoint in new[]{"/api/imports","/api/diagnostics","/api/downloads","/api/preview/known?position=1000"})if((await http.GetAsync(origin+endpoint)).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Central não pode expor conteúdo sem pareamento");
   using var hostile=new HttpRequestMessage(HttpMethod.Get,origin+"/tv");hostile.Headers.Host="attacker.example";if((await http.SendAsync(hostile)).StatusCode!=HttpStatusCode.Forbidden)throw new Exception("Host de fora da rede deve ser rejeitado");
   using var pair=await http.PostAsJsonAsync(origin+"/api/pair",new{pin=server.Pin});pair.EnsureSuccessStatusCode();
   if(!pair.Headers.GetValues("Set-Cookie").Any(c=>c.Contains("httponly",StringComparison.OrdinalIgnoreCase)))throw new Exception("Sessão TV deve ser HttpOnly");
   (await http.GetAsync(origin+"/api/list")).EnsureSuccessStatusCode();
   var html=await http.GetStringAsync(origin+"/tv");if(!html.Contains("Seu cinema"))throw new Exception("Interface TV ausente");
   using var range=new HttpRequestMessage(HttpMethod.Get,origin+"/api/video/known");range.Headers.Range=new(10,19);using var video=await http.SendAsync(range);
   if(video.StatusCode!=HttpStatusCode.PartialContent||!(await video.Content.ReadAsByteArrayAsync()).SequenceEqual(Enumerable.Range(10,10).Select(i=>(byte)i)))throw new Exception("Streaming deve respeitar HTTP Range");
   if((await http.GetAsync(origin+"/api/video/unknown")).StatusCode!=HttpStatusCode.NotFound)throw new Exception("Vídeos desconhecidos não podem ser acessados");
   (await http.PostAsJsonAsync(origin+"/api/progress/known",new{position=12000,duration=30000})).EnsureSuccessStatusCode();if(position!=12000)throw new Exception("Progresso não recebido");
   using var cross=new HttpRequestMessage(HttpMethod.Post,origin+"/api/progress/known"){Content=JsonContent.Create(new{position=0,duration=30000})};cross.Headers.Add("Origin","https://attacker.example");if((await http.SendAsync(cross)).StatusCode!=HttpStatusCode.Forbidden)throw new Exception("Origem externa não pode alterar progresso");
   var options=await http.GetStringAsync(origin+"/api/downloads/known");if(!options.Contains("1080p")||options.Contains("example.org"))throw new Exception("Opção deve usar identificador de sessão sem URL arbitrária");
   using(var hostileDownload=new HttpRequestMessage(HttpMethod.Post,origin+"/api/downloads/known")){hostileDownload.Headers.Add("Origin","https://example.org");hostileDownload.Content=JsonContent.Create(new{option=ticket});if((await http.SendAsync(hostileDownload)).StatusCode!=HttpStatusCode.Forbidden||added!=0)throw new Exception("Outra origem não pode solicitar downloads");}
   using var second=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=true});if(!(await second.PostAsJsonAsync(origin+"/api/pair",new{pin=server.Pin})).IsSuccessStatusCode)throw new Exception("Pareamento do segundo teste falhou");
   if((await second.PostAsJsonAsync(origin+"/api/downloads/known",new{option=ticket})).StatusCode!=HttpStatusCode.BadRequest||added!=0)throw new Exception("Opção de uma TV não pode ser usada por outra sessão");
   if(!(await http.PostAsJsonAsync(origin+"/api/downloads/known",new{option=ticket})).IsSuccessStatusCode||added!=1)throw new Exception("Solicitação pareada não chegou à fila do PC simulado");
   if(!(await http.PostAsJsonAsync(origin+"/api/heartbeat",new{state="failed",errorCode=3})).IsSuccessStatusCode||!(await http.GetStringAsync(origin+"/api/diagnostics")).Contains("decodificar"))throw new Exception("Falha da TV deve aparecer no diagnóstico do PC");
   if((await http.GetAsync(origin+"/api/preview/known?position=-1")).StatusCode!=HttpStatusCode.BadRequest||(await http.GetAsync(origin+"/api/preview/known?position=1000")).Content.Headers.ContentType?.MediaType!="image/jpeg")throw new Exception("Prévia deve validar o tempo e retornar imagem para sessão pareada");
   Console.WriteLine("PASS: central TV pareada, HTTP Range, opções por sessão, solicitação à fila simulada, prévias autenticadas e falhas no diagnóstico.");
  } finally {server.Stop();Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",previous);File.Delete(path);}
 }
}
