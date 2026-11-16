using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Cinevo;
internal sealed record SmartFilter(string Genre = "", double MinMinutes = 0, double MaxMinutes = 0, double Score = 0, string Resolution = "", string Language = "", string Collection = "", bool Duplicates = false)
{
    public static SmartFilter Parse(ref string search)
    {
        if (!search.StartsWith("@smart:")) return new();
        try { using var json = JsonDocument.Parse(search[7..]); search = json.RootElement.GetProperty("search").GetString() ?? "";
            return JsonSerializer.Deserialize<SmartFilter>(json.RootElement.GetProperty("filter"), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new(); }
        catch (JsonException) { search = ""; return new(); }
    }
    internal static string DuplicateKey(Movie m) { var kind = ImportedTitle.Describe(m); return CoverService.Normalize(kind.Type == "series" ? kind.Series : m.Title) + "|" + m.Year + "|" + kind.Season + "|" + kind.Episode; }
    internal static string Quality(Movie m) => m.Resolution.Length > 0 ? m.Resolution : Regex.Match(m.Title + " " + Path.GetFileName(m.Path), @"\b(2160|1080|720|480)p\b", RegexOptions.IgnoreCase).Value.ToLowerInvariant();
    public IEnumerable<Movie> Apply(IEnumerable<Movie> movies)
    {
        var list = movies.ToList();
        var duplicates = Duplicates ? list.GroupBy(DuplicateKey).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet() : [];
        return list.Where(m => (Genre.Length == 0 || m.Genre.Contains(Genre,StringComparison.OrdinalIgnoreCase)) &&
            (MinMinutes <= 0 || m.DurationMilliseconds >= MinMinutes * 60000) &&
            (MaxMinutes <= 0 || m.DurationMilliseconds > 0 && m.DurationMilliseconds <= MaxMinutes * 60000) &&
            (Score <= 0 || m.UserScore >= Score) && (Resolution.Length == 0 || Quality(m) == Resolution) &&
            (Language.Length == 0 || m.Language.Contains(Language,StringComparison.OrdinalIgnoreCase)) &&
            (Collection.Length == 0 || m.Collections.Contains(Collection,StringComparer.OrdinalIgnoreCase)) &&
            (!Duplicates || duplicates.Contains(DuplicateKey(m))));
    }
}
internal static class SmartStorage
{
    internal static IEnumerable<FileInfo> Files(string folder)
    {
        if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) yield break;
        foreach (var path in Directory.EnumerateFiles(folder)) { var f = new FileInfo(path); if ((f.Attributes & FileAttributes.ReparsePoint) == 0) yield return f; }
        foreach (var sub in Directory.EnumerateDirectories(folder)) foreach (var f in Files(sub)) yield return f;
    }
    internal static FileInfo[] Cleanable(string root, string kind, HashSet<string> protectedPaths)
    {
        var folder = Path.Combine(root, kind == "covers" ? "covers" : "updates");
        return Files(folder).Where(f => !protectedPaths.Contains(f.FullName) &&
            (kind == "covers" ? new[]{".jpg",".jpeg",".png",".img",".webp"}.Contains(f.Extension.ToLowerInvariant()) :
            f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-2) && new[]{"Cinevo-Setup.exe","Cinevo-Update.exe"}.Contains(f.Name))).ToArray();
    }
}
