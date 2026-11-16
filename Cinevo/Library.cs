using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Cinevo;
public sealed class Movie : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public Dictionary<string,ViewingState> Viewers { get; set; } = [];
    public string SeasonPoster { get; set; } = "";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Year { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> Collections { get; set; } = [];
    public double UserScore { get; set; }
    public string Notes { get; set; } = "";
    public string CloudId { get; set; } = "";
    public string PortalId { get; set; } = "";
    public string PortalCoverUrl { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string ImportedFrom { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string StreamUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string TrailerUrl { get; set; } = "";
    public string TmdbUrl { get; set; } = "";
    public string CustomSynopsis { get; set; } = "";
    public bool ImportedOnly { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsOnline => PortalId.Length > 0 || IsTmdbImport || CloudId.Length>0 && (SourceUrl.Length>0 || StreamUrl.Length>0);
    public bool IsTmdbImport { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasLocalFile => Path.Length > 0 && File.Exists(Path);
    private string cover = "";
    public string Cover { get => cover; set { cover = value; Changed(); } }
    public string CoverSource { get; set; } = "";
    public DateTime CoverLastAttempt { get; set; }
    public long ResumeMilliseconds { get; set; }
    public int SubtitleDelayMilliseconds { get; set; }
    public string ExternalSubtitlePath { get; set; } = "";
    public int? PreferredAudioTrack { get; set; }
    public int? PreferredSubtitleTrack { get; set; }
    public long DurationMilliseconds { get; set; }
    public bool Watched { get; set; }
    public bool Favorite { get; set; }
    public int Rating { get; set; }
    public long Bytes { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore] public bool Available { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore] public string Subtitle => IsOnline && !HasLocalFile ? $"{(Year.Length > 0 ? Year + "  ·  " : "")}PORTAL ONLINE" : $"{(Year.Length > 0 ? Year + "  ·  " : "")}{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()}  ·  {Bytes / 1073741824d:0.0} GB";
    [System.Text.Json.Serialization.JsonIgnore] public string State => IsOnline && !HasLocalFile ? StreamUrl.Length > 0 ? "Vídeo online" : "Link da origem" : !Available ? "Arquivo indisponível" : Watched ? "✓ Assistido" : "Para assistir";
    [System.Text.Json.Serialization.JsonIgnore] public string Badge => Favorite ? "★ FAVORITO" : Rating > 0 ? new string('★', Rating) : "CINEVO";
    [System.Text.Json.Serialization.JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Title) ? "C" : Title[..1].ToUpperInvariant();
    [System.Text.Json.Serialization.JsonIgnore] public string PlayLabel => IsOnline && !HasLocalFile && StreamUrl.Length == 0 ? "↗  Abrir origem" : ResumeMilliseconds > 30000 && !Watched ? "▶  Continuar" : "▶  Assistir";
    [System.Text.Json.Serialization.JsonIgnore] public double Progress => DurationMilliseconds > 0 ? Math.Clamp(ResumeMilliseconds * 100d / DurationMilliseconds, 0, 100) : 0;
}
public sealed record ScheduledTorrent(string Hash, DateTime At);
public sealed class Library
{
    public List<ViewerProfile> Profiles { get; set; } = [new("principal","Principal")];
    public string ActiveViewer { get; set; } = "principal";
    public List<ScheduledTorrent> DownloadSchedule { get; set; } = [];
    public List<ImportCheckpoint> ImportCheckpoints { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public List<Movie> Movies { get; set; } = [];
    public List<CatalogSource> CatalogSources { get; set; } = [];
    public HashSet<string> RemovedPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoImportDownloads { get; set; } = true;
    public string MediaFolder { get; set; } = "";
    public bool EnableTv { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public bool AutoCovers { get; set; } = true;
    public static string DataDirectory => Environment.GetEnvironmentVariable("CINEVO_TEST_DATA") is { Length: > 0 } testDirectory ? System.IO.Path.GetFullPath(testDirectory) : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cinevo");
    public static string DataPath => System.IO.Path.Combine(DataDirectory, "library.json");
    public static Library Load()
    {
        if (!File.Exists(DataPath)) return new();
        try
        {
            var library = JsonSerializer.Deserialize<Library>(File.ReadAllText(DataPath)) ?? throw new InvalidDataException("Biblioteca vazia.");
            library.RemovedPaths = new(library.RemovedPaths, StringComparer.OrdinalIgnoreCase);
            return library;
        }
        catch (JsonException ex) { throw new InvalidDataException("A biblioteca não pôde ser lida. O arquivo original foi preservado em " + DataPath, ex); }
    }
    public void Save()
    {
        Viewers.Capture(this);
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
