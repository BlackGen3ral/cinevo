using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cinevo;
public static class PortalTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run(string root)
    {
        foreach(var url in new[]{"http://example.org/a.mp4","https://127.0.0.1/a.mp4","https://localhost/a.mp4","https://a.internal/a.mp4","https://user:pass@media.w3.org/a.mp4"})Check(!RemoteFiles.IsPublicHttps(url),"Origem inválida aceita");
        Check(!RemoteFiles.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")),"IP mapeado privado aceito");
        Check(RemoteFiles.IsPublicAddress(IPAddress.Parse("1.1.1.1")),"IP público rejeitado");
        var connection=new PortalConnection(PortalService.DefaultEndpoint,new string('x',64));
        try{PortalService.ValidateConnection(new("https://evil.org",connection.Key));throw new Exception("Portal externo aceito");}catch(InvalidDataException){}
        var id=new string('a',64);var item=new PortalItem{Id=id,Title="Online demo",SourceUrl="https://media.w3.org/demo",StreamUrl="https://media.w3.org/demo.mp4",DownloadUrl="https://media.w3.org/demo.mp4",Year="2020"};
        var library=new Library();
        await PortalService.SyncAsync(library,connection,CancellationToken.None,new FeedHandler(item));
        Check(library.Movies.Count==1&&library.Movies[0].Path==""&&library.Movies[0].IsOnline&&library.Movies[0].ImportedOnly,"Título remoto deve chegar à aba de importados");
        var movie=library.Movies[0];movie.Favorite=true;movie.Notes="Minha anotação";movie.Watched=true;movie.ImportedOnly=false;
        await PortalService.SyncAsync(library,connection,CancellationToken.None,new FeedHandler(item));
        Check(library.Movies.Count==1&&movie.Favorite&&movie.Watched&&movie.Notes=="Minha anotação"&&!movie.ImportedOnly,"Sincronização duplicou ou perdeu preferências");
        var newItem = new PortalItem { Id = new string('b', 64), Title = "Lançamento da origem",
            SourceUrl = "https://media.w3.org/novo", ImportedFrom = "https://media.w3.org/catalogo" };
        await PortalService.SyncAsync(library, connection, CancellationToken.None, new FeedHandler(item, newItem));
        Check(library.Movies.Count == 2 && library.Movies[1].ImportedOnly && movie.Favorite && !movie.ImportedOnly,
            "Novidades devem entrar nos importados sem duplicar os títulos existentes ou mudar sua biblioteca");
        var legacy = JsonSerializer.Deserialize<Movie>("""{"Title":"Filme de versão anterior","PortalId":"legado"}""");
        Check(legacy is { ImportedOnly: false }, "A biblioteca existente deve continuar visível após atualizar");
        var target=Path.Combine(root,"download.mp4");var payload=new byte[350000];Random.Shared.NextBytes(payload);
        var progress=new ImmediateProgress();
        await RemoteFiles.DownloadAsync(item.DownloadUrl,target,progress,CancellationToken.None,new DownloadHandler(payload),(_,_)=>Task.CompletedTask);
        Check(File.ReadAllBytes(target).SequenceEqual(payload),"Download diverge do arquivo da origem");
        Check(progress.Received==payload.Length,"Progresso não reportado");
        try{await RemoteFiles.DownloadAsync(item.DownloadUrl,target,progress,CancellationToken.None,new DownloadHandler(payload,"text/html"),(_,_)=>Task.CompletedTask);throw new Exception("HTML baixado como vídeo");}catch(InvalidDataException){}
        Check(File.ReadAllBytes(target).SequenceEqual(payload),"Arquivo anterior alterado após falha");
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        try{await RemoteFiles.DownloadAsync(item.DownloadUrl,target,progress,cancel.Token,new DownloadHandler(payload),(_,t)=>{t.ThrowIfCancellationRequested();return Task.CompletedTask;});throw new Exception("Cancelamento ignorado");}catch(OperationCanceledException){}
        Check(Directory.GetFiles(root,"*.part").Length==0,"Arquivo parcial ficou no disco");
        if(OperatingSystem.IsWindows())
        {
            var original=Environment.GetEnvironmentVariable("CINEVO_TEST_DATA");Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",Path.Combine(root,"secret-profile"));
            try{PortalService.SaveConnection(connection);Check(PortalService.LoadConnection()==connection,"Credencial DPAPI não restaurada");Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(PortalService.ConnectionFile)).Contains(connection.Key),"Credencial salva em texto aberto");}
            finally{Environment.SetEnvironmentVariable("CINEVO_TEST_DATA",original);}
        }
        Console.WriteLine("PASS: portal privado, sincronização sem duplicatas, preferências preservadas, download em disco, progresso, cancelamento e credencial DPAPI.");
    }
    private sealed class ImmediateProgress:IProgress<(long Received,long? Total)>{public long Received;public void Report((long Received,long? Total) p)=>Received=p.Received;}
    private sealed class FeedHandler(params PortalItem[] items):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Check(request.Headers.Authorization?.Scheme=="Bearer","Token de autenticação ausente");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{items}))});}
    }
    private sealed class DownloadHandler(byte[] payload,string type="video/mp4"):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Check(request.Headers.Authorization==null,"Credencial do portal enviada à origem do vídeo");token.ThrowIfCancellationRequested();var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(type);return Task.FromResult(response);}
    }
}
