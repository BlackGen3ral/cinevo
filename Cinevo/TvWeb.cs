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
internal sealed class TvWeb(Func<string,int,Task<object>> catalog,Func<string,Task<byte[]?>> cover,Func<string,Task<string?>> video,Func<string,long,long,bool?,Task> progress,Func<string,Task<object?>>? details=null)
{
    private WebApplication? app;
    private readonly ConcurrentDictionary<string,DateTime> sessions=new();
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
        var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>{if(Environment.GetEnvironmentVariable("CINEVO_TEST_DATA")!=null)o.Listen(IPAddress.Loopback,0);else o.ListenAnyIP(39876);o.AddServerHeader=false;o.Limits.MaxRequestBodySize=2048;});app=builder.Build();
        app.Use(async(context,next)=>{
            if(!Private(context.Connection.RemoteIpAddress)||!IPAddress.TryParse(context.Request.Host.Host,out var host)||!Private(host)){context.Response.StatusCode=403;return;}
            var origin=context.Request.Headers.Origin.ToString();if(origin.Length>0&&origin!=$"http://{context.Request.Host}"){context.Response.StatusCode=403;return;}
            context.Response.Headers.CacheControl="no-store";context.Response.Headers.XContentTypeOptions="nosniff";
            context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self' https:; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"]="no-referrer";
            if(context.Request.Path.StartsWithSegments("/api")&&context.Request.Path!="/api/pair"){
                var session=context.Request.Cookies["cinevo_tv"]??"";
                if(!sessions.TryGetValue(session,out var until)||until<DateTime.UtcNow){sessions.TryRemove(session,out _);context.Response.StatusCode=401;return;}
            }
            await next(context);
        });
        app.MapGet("/brand.png",()=>Results.Stream(typeof(TvWeb).Assembly.GetManifestResourceStream("Cinevo.Assets.cinevo.png")!,"image/png"));
        Asset("/tv","tv.html","text/html; charset=utf-8");Asset("/tv.js","tv.js","application/javascript");Asset("/tv.css","tv.css","text/css");Asset("/controller.js","controller.js","application/javascript");
        app.MapPost("/api/pair",async(HttpContext context)=>{
            var ip=context.Connection.RemoteIpAddress!.ToString();var now=DateTime.UtcNow;
            foreach(var old in attempts.Where(a=>a.Value.Start<now.AddMinutes(-2)).ToArray())attempts.TryRemove(old.Key,out _);
            if(attempts.Count>100)return Results.StatusCode(429);
            var attempt=attempts.AddOrUpdate(ip,(now,1),(_,old)=>old.Start<now.AddMinutes(-1)?(now,1):(old.Start,old.Count+1));
            if(attempt.Count>5)return Results.StatusCode(429);
            var data=await context.Request.ReadFromJsonAsync<TvPair>();
            if(now>=pinExpires||data?.Pin?.Length!=6||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(data.Pin),System.Text.Encoding.ASCII.GetBytes(pin)))return Results.Json(new{error="Código incorreto ou expirado."},statusCode:401);
            foreach(var old in sessions.Where(s=>s.Value<now).ToArray())sessions.TryRemove(old.Key,out _);
            if(sessions.Count>=8)return Results.StatusCode(429);
            var session=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));sessions[session]=now.AddHours(12);
            context.Response.Cookies.Append("cinevo_tv",session,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,MaxAge=TimeSpan.FromHours(12),Path="/"});Rotate();return Results.Json(new{ok=true});
        });
        app.MapGet("/api/list",async(string? search,int? page)=>Results.Json(await catalog((search??"")[..Math.Min(300,(search??"").Length)],Math.Clamp(page??1,1,100000))));
        app.MapGet("/api/title/{id}",async(string id)=>details!=null&&await details(id) is {} data?Results.Json(data):Results.NotFound());
        app.MapGet("/api/cover/{id}",async(string id)=>await cover(id) is {} bytes?Results.Bytes(bytes,bytes[0]==0xff?"image/jpeg":bytes[0]==0x89?"image/png":"image/webp"):Results.NotFound());
        app.MapGet("/api/video/{id}",async(string id)=>await video(id) is {} path?RemoteFiles.IsPublicHttps(path)?Results.Redirect(path):Results.File(path,ContentType(path),enableRangeProcessing:true):Results.NotFound());
        app.MapPost("/api/progress/{id}",async(string id,HttpContext context)=>{var input=await context.Request.ReadFromJsonAsync<TvProgress>();if(input==null||input.Position<0||input.Duration<0||input.Duration>1000000000||input.Position>input.Duration+1000)return Results.BadRequest();await progress(id,input.Position,input.Duration,input.Favorite);return Results.NoContent();});
        await app.StartAsync(token);Port=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().Replace("[::]","127.0.0.1")).Port;
    }
    private void Asset(string route,string file,string type)=>app!.MapGet(route,()=>Results.Stream(typeof(TvWeb).Assembly.GetManifestResourceStream("Cinevo.Web."+file)!,type));
    private static string ContentType(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{".mp4" or ".m4v"=>"video/mp4",".webm"=>"video/webm",".mkv"=>"video/x-matroska",".avi"=>"video/x-msvideo",_=>"application/octet-stream"};
    public void Stop(){sessions.Clear();if(app!=null)_=app.DisposeAsync();}
}
internal sealed record TvPair(string Pin);
internal sealed record TvProgress(long Position,long Duration,bool? Favorite);
