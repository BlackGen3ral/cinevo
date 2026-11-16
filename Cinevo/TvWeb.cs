using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Cinevo;
internal sealed class TvWeb(Func<string,int,string?,Task<object>> catalog,Func<string,Task<byte[]?>> cover,Func<string,Task<string?>> video,Func<string,long,long,bool?,string?,Task> progress,Func<string,string?,Task<object?>>? details=null,Func<string,string?,string?,Task<object?>>? extra=null,Func<string,Task<byte[]?>>? seasonCover=null,Func<string,int,string?,Task<object>>? imports=null,Func<TvMediaCommand,string,Task<object?>>? download=null,Func<string,long,CancellationToken,Task<byte[]?>>? preview=null)
{
    private WebApplication? app;
    private readonly ConcurrentDictionary<string,DateTime> sessions=new();
    private readonly ConcurrentDictionary<string,TvPresence> presence=new();
    private string Session(HttpContext context)=>context.Request.Cookies["cinevo_tv"]??"";
    public object Diagnostics()=>new{enabled=true,pcAvailable=true,devices=presence.Where(p=>sessions.TryGetValue(p.Key,out var expires)&&expires>DateTime.UtcNow).Select(p=>new{connected=MediaFailure.Online(p.Value.Seen,DateTime.UtcNow),lastSeen=p.Value.Seen,state=p.Value.State,error=p.Value.Error}).ToArray()};
    private readonly ConcurrentDictionary<string,(DateTime Start,int Count)> attempts=new();
    private string pin=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");
    private DateTime pinExpires=DateTime.UtcNow.AddMinutes(5);
    public int Port {get;private set;}
    public string Pin=>DateTime.UtcNow<pinExpires?pin:"Expirado: gere outro código";
    public void Rotate(){pin=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");pinExpires=DateTime.UtcNow.AddMinutes(5);}
    internal static bool Private(IPAddress? ip){if(ip==null)return false;if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();if(IPAddress.IsLoopback(ip))return true;var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31);}
    public string[] Addresses=>NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&Private(a)&&!IPAddress.IsLoopback(a)).Select(a=>$"http://{a}:{Port}/tv").Distinct().ToArray();
    public async Task StartAsync(CancellationToken token)
    {
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{Args=[],EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>{if(Environment.GetEnvironmentVariable("CINEVO_TEST_DATA")!=null)o.Listen(IPAddress.Loopback,0);else o.ListenAnyIP(39876);o.AddServerHeader=false;o.Limits.MaxRequestBodySize=2048;});app=builder.Build();
        app.Use(async(context,next)=>{
            if(!Private(context.Connection.RemoteIpAddress)||!IPAddress.TryParse(context.Request.Host.Host,out var host)||!Private(host)){context.Response.StatusCode=403;return;}
            var origin=context.Request.Headers.Origin.ToString();if(origin.Length>0&&origin!=$"http://{context.Request.Host}"){context.Response.StatusCode=403;return;}
            if(HttpMethods.IsPost(context.Request.Method)&&context.Request.Path.StartsWithSegments("/api")&&!context.Request.HasJsonContentType()){context.Response.StatusCode=415;return;}
            context.Response.Headers.CacheControl="no-store";context.Response.Headers.XContentTypeOptions="nosniff";
            context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self' https:; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"]="no-referrer";
            if(context.Request.Path.StartsWithSegments("/api")&&context.Request.Path!="/api/pair"){
                var session=context.Request.Cookies["cinevo_tv"]??"";
                if(!sessions.TryGetValue(session,out var until)||until<DateTime.UtcNow){sessions.TryRemove(session,out _);context.Response.StatusCode=401;return;}
                presence.AddOrUpdate(session,new TvPresence(DateTime.UtcNow,"Conectada",""),(_,old)=>old with{Seen=DateTime.UtcNow});
            }
            try{await next(context);}catch(InvalidDataException ex){if(!context.Response.HasStarted){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=ex.Message});}}catch(System.Net.Http.HttpRequestException){if(!context.Response.HasStarted){context.Response.StatusCode=502;await context.Response.WriteAsJsonAsync(new{error="O PC não conseguiu acessar a origem ou o qBittorrent. Confira a conexão no computador."});}}catch(OperationCanceledException){if(!context.RequestAborted.IsCancellationRequested&&!context.Response.HasStarted){context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new{error="A consulta demorou demais. Tente novamente."});}}catch(UnauthorizedAccessException){if(!context.Response.HasStarted){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="O PC não tem permissão de acesso ao arquivo ou à pasta dos downloads."});}}catch(IOException){if(!context.Response.HasStarted){context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(new{error="O PC não conseguiu ler ou salvar o arquivo. Confira o disco e a pasta dos downloads."});}}
        });
        app.MapGet("/brand.png",()=>Results.Stream(typeof(TvWeb).Assembly.GetManifestResourceStream("Cinevo.Assets.cinevo.png")!,"image/png"));
        Asset("/tv","tv.html","text/html; charset=utf-8");Asset("/tv.js","tv.js","application/javascript");Asset("/tv-central.js","tv-central.js","application/javascript");Asset("/tv.css","tv.css","text/css");Asset("/controller.js","controller.js","application/javascript");
        app.MapPost("/api/pair",async(HttpContext context)=>{
            var ip=context.Connection.RemoteIpAddress!.ToString();var now=DateTime.UtcNow;
            foreach(var old in attempts.Where(a=>a.Value.Start<now.AddMinutes(-2)).ToArray())attempts.TryRemove(old.Key,out _);
            if(attempts.Count>100)return Results.StatusCode(429);
            var attempt=attempts.AddOrUpdate(ip,(now,1),(_,old)=>old.Start<now.AddMinutes(-1)?(now,1):(old.Start,old.Count+1));
            if(attempt.Count>5)return Results.StatusCode(429);
            var data=await context.Request.ReadFromJsonAsync<TvPair>();
            if(now>=pinExpires||data?.Pin?.Length!=6||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(data.Pin),System.Text.Encoding.ASCII.GetBytes(pin)))return Results.Json(new{error="Código incorreto ou expirado."},statusCode:401);
            foreach(var old in sessions.Where(s=>s.Value<now).ToArray()){sessions.TryRemove(old.Key,out _);presence.TryRemove(old.Key,out _);}
            if(sessions.Count>=8)return Results.StatusCode(429);
            var session=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));sessions[session]=now.AddHours(12);
            context.Response.Cookies.Append("cinevo_tv",session,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,MaxAge=TimeSpan.FromHours(12),Path="/"});Rotate();return Results.Json(new{ok=true});
        });
        app.MapGet("/api/visual",async(string? profile)=>extra==null?Results.NotFound():Results.Json(await extra("visual",null,profile)));
        app.MapGet("/api/diagnostics",()=>Results.Json(Diagnostics()));
        app.MapGet("/api/playback/{id}",async(string id,string? profile)=>extra==null?Results.NotFound():Results.Json(await extra("playback",id,profile)));
        app.MapPost("/api/heartbeat",async(HttpContext context)=>{var data=await context.Request.ReadFromJsonAsync<TvHeartbeat>();if(data==null||data.ErrorCode is <0 or >4||data.State is not ("library" or "playing" or "paused" or "failed"))return Results.BadRequest();presence.TryGetValue(Session(context),out var previous);presence[Session(context)]=new(DateTime.UtcNow,data.State switch{"playing"=>"Reproduzindo","paused"=>"Em pausa","failed"=>"Falha de reprodução",_=>"Navegando"},data.State=="failed"?MediaFailure.Explain(data.ErrorCode):data.State=="playing"?"":previous?.Error??"");return Results.NoContent();});
        app.MapGet("/api/imports",async(string? search,int? page,string? profile)=>imports==null?Results.NotFound():Results.Json(await imports((search??"")[..Math.Min(300,(search??"").Length)],Math.Clamp(page??1,1,100000),profile)));
        app.MapGet("/api/downloads",async(HttpContext c)=>download==null?Results.NotFound():Results.Json(await download(new("tv-download-status",null,null),Session(c))));
        app.MapGet("/api/downloads/{id}",async(string id,HttpContext c)=>download==null?Results.NotFound():Results.Json(await download(new("tv-download-options",id,null),Session(c))));
        app.MapPost("/api/downloads/{id}",async(string id,HttpContext c)=>{if(download==null)return Results.NotFound();var input=await c.Request.ReadFromJsonAsync<TvDownloadChoice>();if(input?.Option==null||input.Option.Length!=32)return Results.BadRequest();return Results.Json(await download(new("tv-download-start",id,input.Option),Session(c)));});
        app.MapPost("/api/download-resume/{id}",async(string id,HttpContext c)=>download==null?Results.NotFound():Results.Json(await download(new("tv-download-resume",id,null),Session(c))));
        app.MapPost("/api/download-cancel/{id}",async(string id,HttpContext c)=>download==null?Results.NotFound():Results.Json(await download(new("tv-download-cancel",id,null),Session(c))));
        app.MapGet("/api/preview/{id}",async(string id,long? position,HttpContext c)=>preview==null||position is null or <0 or >1000000000?Results.BadRequest():await preview(id,position.Value,c.RequestAborted) is {} bytes?Results.Bytes(bytes,"image/jpeg"):Results.NotFound());
        app.MapGet("/api/profiles",async()=>extra==null?Results.NotFound():Results.Json(await extra("profiles",null,null)));
        app.MapGet("/api/next/{id}",async(string id,string? profile)=>extra==null?Results.NotFound():Results.Json(await extra("next",id,profile)));
        app.MapGet("/api/series/{id}",async(string id,string? profile)=>extra==null?Results.NotFound():Results.Json(await extra("series",id,profile)));
        app.MapPost("/api/season/{id}",async(string id,string? profile)=>extra==null?Results.NotFound():Results.Json(await extra("season",id,profile)));
        app.MapGet("/api/season-cover/{id}",async(string id)=>seasonCover!=null&&await seasonCover(id) is {} bytes?Results.Bytes(bytes,bytes[0]==0xff?"image/jpeg":"image/png"):Results.NotFound());
        app.MapGet("/api/list",async(string? search,int? page,string? profile)=>Results.Json(await catalog((search??"")[..Math.Min(300,(search??"").Length)],Math.Clamp(page??1,1,100000),profile)));
        app.MapGet("/api/title/{id}",async(string id,string? profile)=>details!=null&&await details(id,profile) is {} data?Results.Json(data):Results.NotFound());
        app.MapGet("/api/cover/{id}",async(string id)=>await cover(id) is {} bytes?Results.Bytes(bytes,bytes[0]==0xff?"image/jpeg":bytes[0]==0x89?"image/png":"image/webp"):Results.NotFound());
        app.MapGet("/api/video/{id}",async(string id)=>await video(id) is {} path?RemoteFiles.IsPublicHttps(path)?Results.Redirect(path):Results.File(path,ContentType(path),enableRangeProcessing:true):Results.NotFound());
        app.MapPost("/api/progress/{id}",async(string id,HttpContext context)=>{var input=await context.Request.ReadFromJsonAsync<TvProgress>();if(input==null||input.Position<0||input.Duration<0||input.Duration>1000000000||input.Position>input.Duration+1000)return Results.BadRequest();await progress(id,input.Position,input.Duration,input.Favorite,context.Request.Query["profile"].ToString());return Results.NoContent();});
        await app.StartAsync(token);Port=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().Replace("[::]","127.0.0.1")).Port;
    }
    private void Asset(string route,string file,string type)=>app!.MapGet(route,()=>Results.Stream(typeof(TvWeb).Assembly.GetManifestResourceStream("Cinevo.Web."+file)!,type));
    private static string ContentType(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{".mp4" or ".m4v"=>"video/mp4",".webm"=>"video/webm",".mkv"=>"video/x-matroska",".avi"=>"video/x-msvideo",_=>"application/octet-stream"};
    public void Stop(){sessions.Clear();presence.Clear();if(app!=null)_=app.DisposeAsync();}
}
internal sealed record TvPair(string Pin);
internal sealed record TvProgress(long Position,long Duration,bool? Favorite);

internal sealed record TvPresence(DateTime Seen,string State,string Error);
internal sealed record TvHeartbeat(string State,int ErrorCode);
internal sealed record TvDownloadChoice(string Option);
internal sealed record TvMediaCommand(string Name,string? Id,string? Address);
