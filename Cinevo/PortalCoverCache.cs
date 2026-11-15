using System.IO;
using System.Text.RegularExpressions;

namespace Cinevo;

internal static class PortalCoverCache
{
    internal const long LimitBytes = 64L * 1024 * 1024;
    private static readonly Regex FileName = new(@"\Aportal-[a-f0-9]{64}-[a-f0-9]{64}\.img\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim Gate = new(1);

    internal static async Task TrimAsync(string directory)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { MakeRoom(directory, 0, LimitBytes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { Gate.Release(); }
    }

    internal static void Touch(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - File.GetLastWriteTimeUtc(path) > TimeSpan.FromHours(1) && File.Exists(path))
                File.SetLastWriteTimeUtc(path, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static async Task StoreAsync(string path, byte[] bytes, CancellationToken token, long limit = LimitBytes)
    {
        if (bytes.Length > limit || !FileName.IsMatch(Path.GetFileName(path))) return;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            if (!MakeRoom(directory, bytes.Length, limit)) return;
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { Gate.Release(); }
    }

    private static bool MakeRoom(string directory, int incoming, long limit)
    {
        var folder = new DirectoryInfo(directory);
        if (!folder.Exists) return true;
        if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        var files = folder.EnumerateFiles("portal-*")
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                FileName.IsMatch(file.Name.EndsWith(".tmp", StringComparison.Ordinal) ? file.Name[..^4] : file.Name))
            .ToArray();
        long used = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            var temporary = file.Name.EndsWith(".tmp", StringComparison.Ordinal);
            if (used + incoming <= limit && !temporary) continue;
            try
            {
                var length = file.Length;
                file.Delete();
                used -= length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return used + incoming <= limit;
    }
}
