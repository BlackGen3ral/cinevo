using System.IO;
using System.Text.Json;
namespace Cinevo;

public partial class MainWindow
{
    private readonly System.Windows.Threading.DispatcherTimer scheduleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool scheduleBusy;
    private void StartDownloadSchedule()
    {
        scheduleTimer.Tick += async (_,_) => {
            if (closed) { scheduleTimer.Stop(); return; }
            if (scheduleBusy || !library.DownloadSchedule.Any(s => s.At <= DateTime.UtcNow)) return;
            scheduleBusy = true;
            try {
                foreach (var entry in library.DownloadSchedule.Where(s => s.At <= DateTime.UtcNow).ToArray()) {
                    await GetTorrentClient().SetRunningAsync(entry.Hash,true,lifetime.Token);
                    library.DownloadSchedule.Remove(entry); library.Save();
                }
            } catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or System.Net.Http.HttpRequestException) { }
            finally { scheduleBusy = false; }
        };
        scheduleTimer.Start();
    }
    private async Task<object?> SmartActionAsync(WebAction command)
    {
        using var json = JsonDocument.Parse(command.Address ?? "{}");
        var data = json.RootElement;
        string Text(string key) => data.TryGetProperty(key,out var value) ? value.GetString()?.Trim() ?? "" : "";
        int Number(string key) => data.TryGetProperty(key,out var value) ? value.GetInt32() : 0;
        var token = lifetime.Token;
        if (command.Name == "smart-storage" || command.Name == "smart-clean") {
            var protectedPaths = library.Movies.Where(m => m.Path.Length > 0 || m.Cover.Length > 0)
                .SelectMany(m => new[]{m.Path,m.Cover,m.ExternalSubtitlePath,m.SeasonPoster}).Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(updateInstaller != null) protectedPaths.Add(Path.GetFullPath(updateInstaller));
            var videos = library.Movies.Where(m => m.HasLocalFile).Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var kind = Text("kind");
            if (command.Name == "smart-clean" && kind is not ("covers" or "updates")) throw new InvalidDataException("Escolha o tipo de cache.");
            return await Task.Run<object>(() => {
                long removed = 0; int skipped = 0;
                if (command.Name == "smart-clean") foreach(var file in SmartStorage.Cleanable(Library.DataDirectory,kind,protectedPaths)) {
                    try { var size=file.Length; file.Delete(); removed+=size; } catch(IOException) { skipped++; } catch(UnauthorizedAccessException) { skipped++; }
                }
                return new { videos = videos.Sum(p => {try{return new FileInfo(p).Length;}catch(IOException){return 0L;}}),
                    covers = SmartStorage.Files(Path.Combine(Library.DataDirectory,"covers")).Sum(f=>f.Length),
                    temporary = SmartStorage.Files(Path.Combine(Library.DataDirectory,"updates")).Sum(f=>f.Length),
                    cleanCovers = SmartStorage.Cleanable(Library.DataDirectory,"covers",protectedPaths).Sum(f=>f.Length),
                    cleanTemporary = SmartStorage.Cleanable(Library.DataDirectory,"updates",protectedPaths).Sum(f=>f.Length), removed, skipped };
            },token);
        }
        if (command.Name == "smart-options") return new { collections = library.Movies.SelectMany(m=>m.Collections).Distinct().Order().ToArray() };
        if (command.Name == "smart-limits-read") { var prefs=await GetTorrentClient().PreferencesAsync(token); return new { limit=prefs.GetProperty("dl_limit").GetInt32(), active=prefs.GetProperty("max_active_downloads").GetInt32() }; }
        if (command.Name == "smart-limits") {
            var limit = Number("limit"); var active=Number("active");
            if(limit < 0 || limit > 1000000 || active < 1 || active > 100) throw new InvalidDataException("Confira o limite e o tamanho da fila.");
            await GetTorrentClient().ConfigureAsync(new() { ["json"] = JsonSerializer.Serialize(new { dl_limit=limit==0?-1:limit, queueing_enabled=true, max_active_downloads=active, max_active_torrents=active+5 }) },token);
            return null;
        }
        if(command.Name == "smart-metadata") { var version=(await GetTorrentClient().VersionAsync(token)).Trim().TrimStart('v'); if(!Version.TryParse(version,out var parsed) || parsed.Major<5) throw new InvalidDataException("A seleção antes de baixar requer qBittorrent 5 ou superior.");
            if(TorrentClient.MagnetHash(Text("url")) is {} existing && (await GetTorrentClient().TransfersAsync(token,existing)).Any()) throw new InvalidDataException("Esse torrent já está na fila. Use Organizar / episódios na transferência existente.");
            await GetTorrentClient().AddAsync(Text("url"),token,ValidateDownloadFolder(Text("folder").Length>0?Text("folder"):library.MediaFolder.Length>0?library.MediaFolder:null),true); return null; }
        if (command.Name.StartsWith("smart-torrent-")) {
            if(!TorrentClient.IsHash(command.Id)) throw new InvalidDataException("Torrent inválido.");
            var hash=command.Id!;
            if(command.Name == "smart-torrent-files") return await GetTorrentClient().FilesAsync(hash,token);
            if(command.Name == "smart-torrent-priority") {
                var direction=Text("direction"); if(direction is not ("topPrio" or "bottomPrio")) throw new InvalidDataException("Prioridade inválida.");
                await GetTorrentClient().ConfigureAsync(new(){["json"]="{\"queueing_enabled\":true}"},token);
                await GetTorrentClient().ControlAsync(direction,new(){["hashes"]=hash},token); return null;
            }
            if(command.Name == "smart-torrent-schedule") {
                if(!DateTime.TryParse(Text("at"),null,System.Globalization.DateTimeStyles.RoundtripKind,out var at) || at.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidDataException("Escolha um horário futuro.");
                CancelTorrentFallback(hash);
                await GetTorrentClient().SetRunningAsync(hash,false,token);
                library.DownloadSchedule.RemoveAll(s=>s.Hash==hash); library.DownloadSchedule.Add(new(hash,at.ToUniversalTime())); library.Save(); return null;
            }
            if(command.Name == "smart-torrent-select") {
                var files=await GetTorrentClient().FilesAsync(hash,token);
                var ids=data.GetProperty("ids").EnumerateArray().Select(i=>i.GetInt32()).ToHashSet();
                if(ids.Count==0 || ids.Any(i=>!files.Any(f=>f.Index==i))) throw new InvalidDataException("Escolha ao menos um arquivo disponível.");
                CancelTorrentFallback(hash);
                await GetTorrentClient().SetRunningAsync(hash,false,token);
                foreach(var group in files.GroupBy(f=>ids.Contains(f.Index))) await GetTorrentClient().ControlAsync("filePrio",new(){["hash"]=hash,["id"]=string.Join('|',group.Select(f=>f.Index)),["priority"]=group.Key?"1":"0"},token);
                library.DownloadSchedule.RemoveAll(s=>s.Hash==hash); library.Save();
                await GetTorrentClient().SetRunningAsync(hash,true,token); return null;
            }
        }
        if (command.Id == null || !webMovies.TryGetValue(command.Id,out var movie) || !library.Movies.Contains(movie)) throw new InvalidDataException("Título indisponível.");
        if (command.Name == "smart-identify") {
            var title=Text("title"); if(title.Length is <1 or >500) throw new InvalidDataException("Informe o título.");
            var minutes=Number("minutes"); var score=data.GetProperty("score").GetDouble();
            if(minutes<0 || minutes>100000 || !double.IsFinite(score) || score<0 || score>10 || Number("season")<0 || Number("episode")<0) throw new InvalidDataException("Confira duração, nota e episódio.");
            var url=Text("tmdb"); var cover=movie.Cover; TmdbDetails? details=null;
            if(url.Length>0 && url != movie.TmdbUrl) {
                var route=TmdbClient.RouteFromUrl(url) ?? throw new InvalidDataException("Cole o endereço da ficha ou temporada no TMDB.");
                var credential=TmdbClient.ReadCredential(); if(credential.Length==0) throw new InvalidDataException("Conecte o TMDB nas configurações.");
                using var client=new TmdbClient(credential); details=await client.LookupLinkedAsync(route,token);
                cover=await client.DownloadPosterAsync(details.Poster,route,token);
            }
            var snapshot=JsonSerializer.Serialize(movie);
            try {
                movie.Title=details?.Title ?? title; movie.Year=details?.Year ?? Text("year"); movie.Genre=details==null?Text("genre"):string.Join(" · ",details.Genres);
                movie.DurationMilliseconds=(details?.Minutes ?? minutes)*60000L; movie.UserScore=details?.Score ?? score;
                movie.Resolution=Text("resolution"); movie.Language=Text("language"); movie.Collections=Text("collections").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
                movie.SeriesTitle=Text("series"); if(movie.SeriesTitle.Length==0 && TmdbClient.RouteFromUrl(url)?.StartsWith("tv/")==true)movie.SeriesTitle=details?.Title ?? title; movie.MediaType=movie.SeriesTitle.Length>0?"series":"movie";
                movie.Season=Number("season")>0?Number("season"):null; movie.Episode=Number("episode")>0?Number("episode"):null;
                movie.TmdbUrl=details?.Url ?? url; if(details!=null){movie.Description=details.Overview; movie.CustomSynopsis="";movie.Cover=cover;movie.CoverSource=details.Url; movie.TrailerUrl=details.TrailerId==null?"":"https://www.youtube.com/watch?v="+details.TrailerId;}
                library.Save();
            } catch { var restored=JsonSerializer.Deserialize<Movie>(snapshot)!; foreach(var prop in typeof(Movie).GetProperties().Where(p=>p.CanWrite)) prop.SetValue(movie,prop.GetValue(restored)); throw; }
            tmdbCache.Clear(); tmdbRevision++; Refresh(); return WebMovie(movie);
        }
        throw new InvalidDataException("Opção desconhecida.");
    }
}
