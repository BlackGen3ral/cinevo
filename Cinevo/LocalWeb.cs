using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cinevo;

internal sealed class LocalWeb(Func<string, string, int, int, string?, string?, Task<object>> catalog, Func<WebAction, Task<object?>> action,
    Func<string, Task<byte[]?>> cover, Func<Task<object>> status,Func<string,Task<byte[]?>>? seasonCover=null,Func<string,Task<byte[]?>>? backdrop=null,Func<string,Task<byte[]?>>? avatar=null)
{
    private readonly string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private WebApplication? app;
    private string origin = "";
    private volatile bool connected;
    public bool IsConnected => connected;
    public void PrepareWindow() => connected = false;
    public string Address => origin + "/#" + key;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = "Production"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 8192;
        });
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (origin.Length == 0 || context.Request.Host.Value != origin["http://".Length..] ||
                context.Request.Headers.Origin is { Count: > 0 } requestOrigin && requestOrigin != origin)
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' blob:; font-src 'self'; frame-src https://www.youtube-nocookie.com; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
            if (context.Request.Path.StartsWithSegments("/api") && !Authorized(context.Request.Headers.Authorization))
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next(context);
        });
        MapAsset("/avatar.svg", "avatar.svg", "image/svg+xml");
        MapAsset("/", "index.html", "text/html; charset=utf-8");
        MapAsset("/glass.css", "glass.css", "text/css; charset=utf-8");
        MapAsset("/history.css", "history.css", "text/css; charset=utf-8");
        MapAsset("/history.js", "history.js", "application/javascript; charset=utf-8");
        MapAsset("/glass.js", "glass.js", "application/javascript; charset=utf-8");
        MapAsset("/cinevo.css", "cinevo.css", "text/css; charset=utf-8");
        MapAsset("/cinevo.js", "cinevo.js", "application/javascript; charset=utf-8");
        MapAsset("/downloads.js", "downloads.js", "application/javascript; charset=utf-8");
        MapAsset("/interface.js", "interface.js", "application/javascript; charset=utf-8");
        MapAsset("/cinema.js", "cinema.js", "application/javascript; charset=utf-8");
        MapAsset("/features.js", "features.js", "application/javascript; charset=utf-8");
        MapAsset("/account.js", "account.js", "application/javascript; charset=utf-8");
        MapAsset("/controller.js", "controller.js", "application/javascript; charset=utf-8");
        MapAsset("/smart.js", "smart.js", "application/javascript; charset=utf-8");
        MapAsset("/settings.js", "settings.js", "application/javascript; charset=utf-8");
        MapAsset("/tmdb.svg", "tmdb.svg", "image/svg+xml");
        MapAsset("/cinema.jpg", "cinema.jpg", "image/jpeg");
        MapAsset("/instrument-serif.woff2", "instrument-serif.woff2", "font/woff2");
        app.MapGet("/favicon.ico", () => Results.Stream(typeof(LocalWeb).Assembly.GetManifestResourceStream("Cinevo.Assets.cinevo.ico")!, "image/x-icon"));
        app.MapGet("/cinevo.png", () => Results.Stream(typeof(LocalWeb).Assembly.GetManifestResourceStream("Cinevo.Assets.cinevo.png")!, "image/png"));
        app.MapGet("/api/avatar/{id}",async(string id)=>avatar!=null&&await avatar(id) is {} bytes?Results.Bytes(bytes,"image/jpeg"):Results.NotFound());
        app.MapGet("/api/status", async () =>
        {
            var data = await status();
            connected = true;
            return Results.Json(data);
        });
        app.MapGet("/api/catalog", async (string? section, string? search, int? page, int? limit, string? site, string? type) =>
            Results.Json(await catalog(section ?? "library", search ?? "", Math.Clamp(page ?? 1, 1, 100000), Math.Clamp(limit ?? 30, 1, 60), site, type)));
        app.MapGet("/api/backdrop/{id}",async(string id)=>backdrop!=null&&await backdrop(id) is {} bytes?Results.Bytes(bytes,ImageType(bytes)):Results.NotFound());
        app.MapGet("/api/season-cover/{id}",async(string id)=>seasonCover!=null&&await seasonCover(id) is {} bytes?Results.Bytes(bytes,ImageType(bytes)):Results.NotFound());
        app.MapGet("/api/cover/{id}", async (string id) =>
            await cover(id) is { } data ? Results.Bytes(data, ImageType(data)) : Results.NotFound());
        app.MapPost("/api/action", async (HttpContext context) =>
        {
            if (context.Request.Headers.Origin != origin || !context.Request.HasJsonContentType()) return Results.StatusCode(403);
            var command = await context.Request.ReadFromJsonAsync<WebAction>(JsonOptions, context.RequestAborted);
            if (string.IsNullOrWhiteSpace(command?.Name)) return Results.BadRequest();
            try { return await action(command) is { } result ? Results.Json(result) : Results.NoContent(); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (OperationCanceledException) { return Results.Json(new { error = "A operação demorou para responder ou foi cancelada. Tente novamente." }, statusCode: 503); }
            catch (System.Net.Http.HttpRequestException) { return Results.Json(new { error = "Não foi possível conectar ao serviço. Confira a internet e tente novamente." }, statusCode: 502); }
            catch (JsonException) { return Results.Json(new { error = "O serviço retornou dados inválidos. Tente novamente." }, statusCode: 502); }
            catch (IOException) { return Results.Json(new { error = "Não foi possível ler ou salvar os dados desta operação." }, statusCode: 500); }
        });
        await app.StartAsync(token).ConfigureAwait(false);
        origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    public void Stop()
    {
        if (app != null) _ = app.DisposeAsync();
    }

    private bool Authorized(string? value) => value?.Length == key.Length + 7 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes("Bearer " + key));

    private void MapAsset(string route, string name, string contentType) =>
        app!.MapGet(route, () => Results.Stream(typeof(LocalWeb).Assembly.GetManifestResourceStream("Cinevo.Web." + name)!, contentType));

    private static string ImageType(byte[] bytes) => bytes[0] switch
    {
        0xff => "image/jpeg",
        0x89 => "image/png",
        (byte)'G' => "image/gif",
        (byte)'B' => "image/bmp",
        _ => "image/webp"
    };
}

internal sealed record WebAction(string Name, string? Id, string? Address, bool Continuation, string? DownloadFolder = null, string? ImportRoot = null);
