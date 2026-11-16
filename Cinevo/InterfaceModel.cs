namespace Cinevo;
public sealed record VisualStyle(string Covers="medium",int Glow=65,int ClickVolume=30,string Density="comfortable")
{
 public bool Valid()=>Covers is "small" or "medium" or "large"&&Glow is >=0 and <=100&&ClickVolume is >=0 and <=100&&Density is "compact" or "comfortable" or "spacious";
}
internal sealed class TransferArtworkIndex
{
 internal sealed record Entry(Movie Source,string Title,string Year,string[] Hashes,bool Covered);
 private readonly Dictionary<string,Movie> hashes=new(StringComparer.OrdinalIgnoreCase);
 private readonly Dictionary<string,List<Entry>> titles=[];
 private static string Key(string title)=>CoverService.Normalize(Scanner.Parse(title+".mkv").Title);
 internal static Entry[] Capture(IEnumerable<Movie> movies)=>movies.Select(m=>new Entry(m,m.Title,m.Year,[..m.TorrentHashes],m.Cover.Length>0||m.PortalCoverUrl.Length>0)).ToArray();
 internal TransferArtworkIndex(IEnumerable<Movie> movies):this(Capture(movies)){}
 internal TransferArtworkIndex(IEnumerable<Entry> entries){foreach(var entry in entries){foreach(var hash in entry.Hashes)hashes.TryAdd(hash,entry.Source);var key=Key(entry.Title);if(key.Length==0)continue;if(!titles.TryGetValue(key,out var list))titles[key]=list=[];list.Add(entry);}}
 internal Movie? Find(string hash,string name){if(hashes.TryGetValue(hash,out var known))return known;var parsed=Scanner.Parse(name+".mkv");return titles.GetValueOrDefault(Key(name))?.Where(m=>parsed.Year.Length==0||m.Year==parsed.Year).OrderByDescending(m=>m.Covered).FirstOrDefault()?.Source;}
}
internal static class LibrarySearch
{
 internal static bool Matches(Movie m,string query)=>$"{m.Title} {m.SeriesTitle} {m.Year} {m.Genre}".Contains(query,StringComparison.CurrentCultureIgnoreCase);
 internal static IEnumerable<Movie> Films(IEnumerable<Movie> movies,string query)=>movies.Where(m=>ImportedTitle.Describe(m).Type!="series"&&Matches(m,query));
 internal static IEnumerable<Movie> Episodes(IEnumerable<Movie> movies,string query)=>movies.Where(m=>ImportedTitle.Describe(m).Type=="series"&&ImportedTitle.Describe(m).Episode!=null&&Matches(m,query));
 internal static IEnumerable<Movie> Series(IEnumerable<Movie> movies,string query)=>movies.Where(m=>ImportedTitle.Describe(m).Type=="series"&&Matches(m,query)).GroupBy(SeriesCenter.Key).Select(g=>g.First());
}
