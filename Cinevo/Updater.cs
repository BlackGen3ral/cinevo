using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace Cinevo;
public static class Updater
{
    public const string Repository = "BlackGen3ral/cinevo";
    private const string InstallerName = "Cinevo-Setup.exe";
    private static string StagingDirectory => Path.Combine(Library.DataDirectory, "updates");
    public static bool IsInstalled => AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar).Equals(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Cinevo"), StringComparison.OrdinalIgnoreCase);
    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public static async Task<string?> DownloadAsync(CancellationToken token = default, IProgress<UpdateProgress>? progress = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cinevo/" + CurrentVersion);
        return await DownloadAsync(http, Version.Parse(CurrentVersion), StagingDirectory, token, progress).ConfigureAwait(false);
    }
    public sealed record UpdateProgress(string Stage, long Received = 0, long? Total = null);

    internal static async Task CleanInstalledUpdatesAsync()
    {
        var installed = Version.Parse(CurrentVersion);
        // On automatic restart, Inno Setup can still be releasing its executable.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CleanCache(StagingDirectory, installed) || attempt == 2) return;
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    internal static bool CleanCache(string stagingDirectory, Version installed, Version? pending = null)
    {
        var complete = true;
        try
        {
            if (!Directory.Exists(stagingDirectory) || File.GetAttributes(stagingDirectory).HasFlag(FileAttributes.ReparsePoint)) return true;
            foreach (var directory in new DirectoryInfo(stagingDirectory).EnumerateDirectories())
            {
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    !Version.TryParse(directory.Name, out var version) || version.Revision != -1 || version.Build < 0) continue;
                if (version > installed && (pending == null || version == pending)) continue;
                try
                {
                    var installer = Path.Combine(directory.FullName, InstallerName);
                    File.Delete(installer);
                    File.Delete(installer + ".download");
                    File.Delete(Path.Combine(directory.FullName, "Cinevo-Update.exe"));
                    File.Delete(Path.Combine(directory.FullName, "Cinevo-Update.exe.download"));
                    if (!directory.EnumerateFileSystemInfos().Any()) directory.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { complete = false; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        return complete;
    }

    public static async Task<string?> DownloadAsync(HttpClient http, Version current, string stagingDirectory, CancellationToken token = default, IProgress<UpdateProgress>? progress = null, string? installedRuntimeId = null)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=10", token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var release = json.RootElement.EnumerateArray().FirstOrDefault(r => !r.TryGetProperty("draft", out var draft) || !draft.GetBoolean());
        if (release.ValueKind == JsonValueKind.Undefined) return null;
        if (!Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v').Split('-')[0], out var latest) || latest <= current) return null;
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == InstallerName);
        var checksums = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
        if (installer.ValueKind == JsonValueKind.Undefined || checksums.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Publicação sem instalador ou verificação de integridade.");
        string Url(JsonElement asset) { var url = asset.GetProperty("browser_download_url").GetString()!; if (!url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Origem da atualização inválida."); return url; }
        var sums = await http.GetStringAsync(Url(checksums), token).ConfigureAwait(false);
        var packageName = InstallerName;
        var runtimeFile = Path.Combine(AppContext.BaseDirectory, "runtime-id.txt");
        installedRuntimeId ??= IsInstalled && File.Exists(runtimeFile) ? File.ReadAllText(runtimeFile).Trim() : null;
        var compact = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "Cinevo-Update.exe");
        var runtime = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "runtime-id.txt");
        string? Checksum(string name) => sums.Split('\n').Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*') == name)?[0];
        if (installedRuntimeId is { Length: 64 } && compact.ValueKind != JsonValueKind.Undefined && runtime.ValueKind != JsonValueKind.Undefined)
        {
            var runtimeBytes = await http.GetByteArrayAsync(Url(runtime), token).ConfigureAwait(false);
            if (runtimeBytes.Length <= 128 && Convert.ToHexString(SHA256.HashData(runtimeBytes)).Equals(Checksum("runtime-id.txt"), StringComparison.OrdinalIgnoreCase) &&
                System.Text.Encoding.UTF8.GetString(runtimeBytes).Trim().Equals(installedRuntimeId, StringComparison.OrdinalIgnoreCase))
            {
                installer = compact;
                packageName = "Cinevo-Update.exe";
            }
        }
        var hash = Checksum(packageName);
        if (hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Verificação de integridade ausente.");
        var directory = System.IO.Path.Combine(stagingDirectory, latest.ToString()); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, packageName);
        if (File.Exists(target))
        {
            progress?.Report(new("verify"));
            if ((await HashAsync(target, token).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                CleanCache(stagingDirectory, current, latest);
                return target;
            }
        }
        using var download = await http.GetAsync(Url(installer), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); download.EnsureSuccessStatusCode();
        var temp = target + ".download";
        try
        {
            var total = download.Content.Headers.ContentLength;
            long received = 0;
            var lastReport = Environment.TickCount64;
            progress?.Report(new("download", received, total));
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await using (var source = await download.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    received += count;
                    if (Environment.TickCount64 - lastReport >= 200)
                    {
                        progress?.Report(new("download", received, total));
                        lastReport = Environment.TickCount64;
                    }
                }
            }
            progress?.Report(new("verify", received, total));
            var actual = await HashAsync(temp, token).ConfigureAwait(false);
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O instalador baixado falhou na verificação de integridade.");
        }
        catch { if (File.Exists(temp)) File.Delete(temp); throw; }
        File.Move(temp, target, true);
        CleanCache(stagingDirectory, current, latest);
        return target;
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
    }
}
