using Cinevo;
using System.Net;
using System.Net.Http.Json;
internal static class TvWebTests
{
 internal static async Task Run(string root)
 {
  var previous=Environment.GetEnvironmentVariable("CINEVO_TEST_DATA");Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",root);
  var path=Path.Combine(root,"range-fixture.mp4");File.WriteAllBytes(path,Enumerable.Range(0,100).Select(i=>(byte)i).ToArray());
  long position=-1;var server=new TvWeb((q,p)=>Task.FromResult<object>(new{movies=Array.Empty<object>()}),id=>Task.FromResult<byte[]?>(null),id=>Task.FromResult<string?>(id=="known"?path:null),(id,time,duration,favorite)=>{position=time;return Task.CompletedTask;});
  try {
   await server.StartAsync(CancellationToken.None);var origin=$"http://127.0.0.1:{server.Port}";
   using var http=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=true});
   if((await http.GetAsync(origin+"/api/list")).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Catálogo TV deve exigir pareamento");
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
   Console.WriteLine("PASS: central TV exige pareamento, protege origens e envia vídeo com HTTP Range e progresso.");
  } finally {server.Stop();Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",previous);File.Delete(path);}
 }
}
