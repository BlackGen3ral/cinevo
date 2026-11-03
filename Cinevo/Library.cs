using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Cinevo;
public sealed class Movie
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Year { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Cover { get; set; } = "";
    public bool Watched { get; set; }
    public bool Favorite { get; set; }
    public int Rating { get; set; }
    public long Bytes { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore] public bool Available { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore] public string Subtitle => $"{(Year.Length > 0 ? Year + "  ·  " : "")}{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()}  ·  {Bytes / 1073741824d:0.0} GB";
    [System.Text.Json.Serialization.JsonIgnore] public string State => !Available ? "Arquivo indisponível" : Watched ? "✓ Assistido" : "Para assistir";
    [System.Text.Json.Serialization.JsonIgnore] public string Badge => Favorite ? "★ FAVORITO" : Rating > 0 ? new string('★', Rating) : "CINEVO";
    [System.Text.Json.Serialization.JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Title) ? "C" : Title[..1].ToUpperInvariant();
}
public sealed class Library
{
    public List<string> Folders { get; set; } = [];
    public List<Movie> Movies { get; set; } = [];
    public bool AutoUpdate { get; set; } = true;
    public static string DataDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cinevo");
    public static string DataPath => System.IO.Path.Combine(DataDirectory, "library.json");
    public static Library Load()
    {
        if (!File.Exists(DataPath)) return new();
        try { return JsonSerializer.Deserialize<Library>(File.ReadAllText(DataPath)) ?? throw new InvalidDataException("Biblioteca vazia."); }
        catch (JsonException ex) { throw new InvalidDataException("A biblioteca não pôde ser lida. O arquivo original foi preservado em " + DataPath, ex); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = DataPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(DataPath)) File.Replace(temp, DataPath, DataPath + ".bak"); else File.Move(temp, DataPath);
    }
}
public static class Scanner
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".mpg", ".mpeg", ".ts", ".m2ts", ".vob" };
    public static (string Title, string Year) Parse(string filename)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(filename);
        var years = Regex.Matches(name, @"(?<!\d)(19\d{2}|20\d{2})(?!\d)");
        var year = years.Count > 0 ? years[^1] : Match.Empty;
        var title = year.Success ? name[..year.Index] : Regex.Split(name, @"(?i)\b(2160p|1080p|720p|480p|bluray|brrip|webrip|web-dl|hdtv|x264|x265)\b")[0];
        title = Regex.Replace(title, @"[._]+", " ").Trim(' ', '-', '(', '[', ')', ']');
        return (title.Length > 0 ? title : name, year.Value);
    }
    public static ScanResult Scan(IEnumerable<string> roots, CancellationToken token)
    {
        var movies = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(folder)) continue;
            try
            {
                foreach (var path in Directory.GetFiles(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Extensions.Contains(System.IO.Path.GetExtension(path))) continue;
                    try { var info = new FileInfo(path); var (title, year) = Parse(path); movies[path] = new Movie { Path = path, Title = title, Year = year, Bytes = info.Length }; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(path); }
                }
                foreach (var dir in Directory.GetDirectories(folder))
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) pending.Push(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(folder); }
        }
        return new ScanResult(movies.Values.ToList(), errors);
    }
}
public record ScanResult(List<Movie> Movies, List<string> Errors);
