using Cinevo;
using System.Net;
using System.Net.Sockets;
internal static class TorrentControlTests {
 internal static async Task Run() {
  var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
  using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
  var requests=new List<(string Path,string Body)>();
  var server=Task.Run(async()=>{for(int i=0;i<4;i++){var context=await listener.GetContextAsync();using var reader=new StreamReader(context.Request.InputStream);requests.Add((context.Request.Url!.AbsolutePath,await reader.ReadToEndAsync()));
    var bytes=System.Text.Encoding.UTF8.GetBytes(i==0?"{\"dl_limit\":256,\"max_active_downloads\":2}":"Ok.");context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();}});
  using var client=new TorrentClient(new(port,"","",true));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  var prefs=await client.PreferencesAsync(timeout.Token);if(prefs.GetProperty("dl_limit").GetInt32()!=256)throw new Exception("Preferências não lidas");
  await client.ConfigureAsync(new(){["json"]="{\"dl_limit\":256,\"queueing_enabled\":true}"},timeout.Token);
  await client.ControlAsync("filePrio",new(){["hash"]=new string('a',40),["id"]="0|2",["priority"]="1"},timeout.Token);
  await client.AddAsync("magnet:?xt=urn:btih:"+new string('a',40),timeout.Token,null,true);
  await server;
  if(requests[1].Path!="/api/v2/app/setPreferences" || !WebUtility.UrlDecode(requests[1].Body).Contains("\"dl_limit\":256") || requests[2].Path!="/api/v2/torrents/filePrio" || !WebUtility.UrlDecode(requests[2].Body).Contains("id=0|2") || !requests[3].Body.Contains("MetadataReceived"))throw new Exception("Comandos avançados enviados incorretamente");
  Console.WriteLine("PASS: transporte HTTP local para preferências, seleção de arquivos e pausa após metadados (servidor simulado).");
 }
}
