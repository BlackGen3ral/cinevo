using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;

namespace Cinevo;

public partial class MainWindow
{
    private LocalWeb? web;
    private Process? webWindow;
    private bool openingWeb;
    private bool webShown;
    private int webRevision;
    private readonly Dictionary<Movie, string> webIds = new();
    private readonly Dictionary<string, Movie> webMovies = new(StringComparer.Ordinal);

    private async Task OpenWebAsync()
    {
        if (closed || openingWeb) return;
        if (webShown && webWindow is { HasExited: false }) { if (IsVisible) Hide(); return; }
        openingWeb = true;
        try
        {
            if (web == null)
            {
                var server = new LocalWeb(
                    (section, search, page, limit, site, type) => Dispatcher.InvokeAsync(() => WebCatalog(section, search, page, limit, site, type)).Task,
                    command => Dispatcher.InvokeAsync(() => DispatchWebActionAsync(command)).Task.Unwrap(),
                    ReadWebCoverAsync,
                    () => Dispatcher.InvokeAsync<object>(() => new
                    {
                        revision = webRevision, status = Status.Text, scanning = scan != null,
                        canUpdate = Updater.IsInstalled, update = updateProgress, updateRequested = installRequested
                    }).Task,ReadSeasonPosterAsync,ReadBackdropAsync,ReadProfilePhotoAsync);
                try { await server.StartAsync(lifetime.Token); }
                catch { server.Stop(); throw; }
                if (closed) { server.Stop(); return; }
                web = server;
            }
            if (Environment.GetEnvironmentVariable("CINEVO_TEST_DATA") is { Length: > 0 } &&
                Environment.GetEnvironmentVariable("CINEVO_TEST_HEADLESS") == "1")
            {
                await File.WriteAllTextAsync(Path.Combine(Library.DataDirectory, "web-test-url"), web.Address, lifetime.Token);
                webShown = true;
                return;
            }
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            var browser = new[] { Path.Combine("Google", "Chrome", "Application", "chrome.exe"), Path.Combine("Microsoft", "Edge", "Application", "msedge.exe") }
                .SelectMany(relative => roots.Select(root => Path.Combine(root, relative)))
                .FirstOrDefault(File.Exists);
            if (browser == null)
            {
                Process.Start(new ProcessStartInfo(web.Address) { UseShellExecute = true });
                return;
            }
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var response = await http.GetAsync(new Uri(web.Address), lifetime.Token);
            response.EnsureSuccessStatusCode();
            if (closed) return;

            // Each launch owns its browser process. Sharing a profile lets Edge hand
            // the window to an existing process and exit before Cinevo can track it.
            var sessions = Path.GetFullPath(Path.Combine(Library.DataDirectory, "web-sessions"));
            var profile = Path.Combine(sessions, Guid.NewGuid().ToString("N"));
            var preferences = Directory.CreateDirectory(Path.Combine(profile, "Default"));
            await File.WriteAllTextAsync(Path.Combine(preferences.FullName, "Preferences"),
                """{"translate":{"enabled":false}}""", lifetime.Token);
            if (closed) return;
            web.PrepareWindow();
            var launch = new ProcessStartInfo(browser) { UseShellExecute = false };
            launch.ArgumentList.Add("--app=" + web.Address);
            launch.ArgumentList.Add("--user-data-dir=" + profile);
            launch.ArgumentList.Add("--no-first-run");
            launch.ArgumentList.Add("--no-default-browser-check");
            launch.ArgumentList.Add("--no-proxy-server");
            launch.ArgumentList.Add("--disable-background-mode");
            launch.ArgumentList.Add("--disable-extensions");
            launch.ArgumentList.Add("--disable-sync");
            launch.ArgumentList.Add("--disable-features=msEdgeStartupBoost,msEdgeFirstRunExperience");
            var process = Process.Start(launch) ?? throw new IOException("A janela do Cinevo não abriu.");
            webWindow = process;
            var opened = false;
            process.Exited += (_, _) =>
            {
                _ = RemoveWebProfileAsync(sessions, profile);
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (webWindow == process) webWindow = null;
                    if (opened) webShown = false;
                    process.Dispose();
                    if (!closed && opened && !IsVisible) Close();
                });
            };
            process.EnableRaisingEvents = true;
            var timeout = Stopwatch.StartNew();
            while (!closed && webWindow == process && !process.HasExited && timeout.Elapsed < TimeSpan.FromSeconds(12))
            {
                if (web.IsConnected)
                {
                    opened = true;
                    webShown = true;
                    if (IsVisible) Hide();
                    return;
                }
                await Task.Delay(150, lifetime.Token);
            }
            if (!closed)
            {
                Status.Text = "A interface web não confirmou a abertura. Você pode usar a interface clássica ou tentar abrir a web novamente.";
                if (webWindow == process && !process.HasExited) process.CloseMainWindow();
            }
        }
        catch (Exception ex)
        {
            if (!closed) Status.Text = "Interface web indisponível. " + ex.Message;
        }
        finally { openingWeb = false; }
    }

    private static async Task RemoveWebProfileAsync(string parent, string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent, StringComparison.OrdinalIgnoreCase))
            return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); return; }
            catch (IOException) { await Task.Delay(350).ConfigureAwait(false); }
            catch (UnauthorizedAccessException) { await Task.Delay(350).ConfigureAwait(false); }
        }
    }

    private object WebCatalog(string section, string search, int page, int limit, string? site, string? type, string? viewer=null)
    {
        var profile=CheckViewer(viewer??library.ActiveViewer);
        var saved = library.Movies.Where(movie => !movie.ImportedOnly).ToList();
        var imports = library.Movies.Where(movie => movie.IsOnline).ToList();
        var importGroups = section == "imports" ? imports.Select(movie => (Movie: movie, Site: SourceSite(movie), Info: ImportedTitle.Describe(movie))).ToArray() : [];
        var collection = section switch
        {
            "imports" => importGroups.Where(item => string.IsNullOrEmpty(site) || item.Site == site)
                .Where(item => string.IsNullOrEmpty(type) || item.Info.Type == type)
                .OrderBy(item => item.Site, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Info.Type)
                .ThenBy(item => item.Info.Series, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Info.Season ?? -1).ThenBy(item => item.Info.Episode ?? -1)
                .ThenByDescending(item => item.Movie.Added).Select(item => item.Movie),
            "favorites" => saved.Where(movie => movie.Favorite).OrderBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase),
            "continue" => saved.Where(movie => !movie.Watched && movie.ResumeMilliseconds > 30000).OrderByDescending(movie => movie.Added),
            _ => saved.OrderByDescending(movie => movie.Added)
        };
        if(section=="library"&&type is "movie" or "series")collection=collection.Where(m=>ImportedTitle.Describe(m).Type==type);
        var smart = SmartFilter.Parse(ref search);
        collection = smart.Apply(collection);
        if(section!="imports")collection=collection.GroupBy(m=>ImportedTitle.Describe(m).Type=="series"?"series:"+SeriesCenter.Key(m):(object)m).Select(g=>g.First());
        var offset = (page - 1) * limit;
        var matches = collection.Where(movie => search.Length == 0 ||
            $"{movie.Title} {movie.Year} {movie.Genre}".Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var feed=saved.Where(m=>!WatchJournal.Dropped(library,m,profile)).ToList();
        var returning = HomeFeed.Returning(feed,profile,library.ActiveViewer);
        var favorites = saved.Where(movie => Viewers.Get(movie,profile,library.ActiveViewer).Favorite).OrderByDescending(movie => movie.Added).Take(12);
        var spotlight = page == 1 && section == "library" && search.Length == 0 && smart == new SmartFilter()
            ? returning.FirstOrDefault() ?? feed.OrderByDescending(movie => movie.Added).FirstOrDefault() : null;
        return new
        {
            movies = matches.Skip(offset).Take(limit).Select(m=>WebMovieFor(m,profile)).ToArray(),
            more = matches.Count > offset + limit,
            resultCount = matches.Count,
            spotlight = spotlight == null ? null : WebMovieFor(spotlight,profile),
            returning = spotlight == null ? null : returning.Select(m=>WebMovieFor(m,profile)).ToArray(),
            favorites = spotlight == null ? null : favorites.Select(m=>WebMovieFor(m,profile)).ToArray(),
            nextEpisodes=spotlight==null?null:HomeFeed.Next(feed,profile,library.ActiveViewer).Select(m=>WebMovieFor(m,profile)).ToArray(),
            recentDownloads=spotlight==null?null:saved.Where(m=>m.HasLocalFile&&m.DownloadedAt!=null).OrderByDescending(m=>m.DownloadedAt).Take(12).Select(m=>WebMovieFor(m,profile)).ToArray(),
            librarySummary=section=="library"?new{films=saved.Count(m=>ImportedTitle.Describe(m).Type!="series"),series=saved.Where(m=>ImportedTitle.Describe(m).Type=="series").Select(SeriesCenter.Key).Distinct().Count(),watching=feed.Where(m=>!Viewers.Get(m,profile,library.ActiveViewer).Watched&&Viewers.Get(m,profile,library.ActiveViewer).Resume>1000).GroupBy(m=>ImportedTitle.Describe(m).Type=="series"?"series:"+SeriesCenter.Key(m):(object)m).Count()}:null,
            viewerName=library.Profiles.First(p=>p.Id==profile).Name,
            total = saved.Count,
            folderCount = library.Folders.Count,
            imported = imports.Count(movie => movie.ImportedOnly),
            sites = importGroups.GroupBy(item => item.Site).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { name = group.Key, count = group.Count() }).ToArray(),
            version = Updater.CurrentVersion
        };
    }

    private static string SourceSite(Movie movie) => Uri.TryCreate(movie.ImportedFrom.Length > 0 ? movie.ImportedFrom : movie.SourceUrl, UriKind.Absolute, out var uri)
        ? uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost : "Origem desconhecida";

    private string WebId(Movie movie){WebMovie(movie);return webIds[movie];}
    private object WebMovie(Movie movie)=>WebMovieFor(movie,library.ActiveViewer);
    private object WebMovieFor(Movie movie,string profile)
    {
        var viewing=Viewers.Get(movie,profile,library.ActiveViewer);
        var kind = ImportedTitle.Describe(movie);
        if (!webIds.TryGetValue(movie, out var id))
        {
            if (webIds.Count > library.Movies.Count + 100)
            {
                var present = library.Movies.ToHashSet();
                foreach (var removed in webIds.Keys.Where(key => !present.Contains(key)).ToArray())
                {
                    webMovies.Remove(webIds[removed]);
                    webIds.Remove(removed);
                }
            }
            id = Guid.NewGuid().ToString("N");
            webIds.Add(movie, id);
            webMovies.Add(id, movie);
        }
        return new
        {
            id, movie.Title, movie.Year, movie.Genre, resolution=SmartFilter.Quality(movie), movie.Language, movie.Collections, movie.DurationMilliseconds, resumeMilliseconds=viewing.Resume, movie.UserScore, description = movie.CustomSynopsis.Length > 0 ? movie.CustomSynopsis : movie.Description, favorite=viewing.Favorite, watched=viewing.Watched,
            site = movie.IsOnline ? SourceSite(movie) : "",
            mediaType = kind.Type,
            seriesTitle = kind.Series,
            kind.Season,
            kind.Episode,
            customTrailer = MovieTrailer.FromUrl(movie.TrailerUrl)?.VideoId,
            tmdbLink = movie.TmdbUrl,
            tmdbConfigured = File.Exists(TmdbClient.CredentialPath),
            tmdbRevision,
            trackingStatus=WatchJournal.Find(library,movie,profile)?.Status??(viewing.Watched?"completed":viewing.Resume>1000?"watching":""),historyId=WatchJournal.Find(library,movie,profile)?.Id, movie.IsOnline, movie.ImportedOnly, progress=movie.DurationMilliseconds>0?Math.Clamp(viewing.Resume*100d/movie.DurationMilliseconds,0,100):0, movie.Rating, playLabel=viewing.Resume>1000&&!viewing.Watched?"▶ Continuar":movie.PlayLabel, movie.Available, hasLocalFile=movie.HasLocalFile, canPlay=movie.HasLocalFile||RemoteFiles.IsPublicHttps(movie.StreamUrl),
            hasDownloadOptions = movie.PortalId.Length > 0,
            hasCover = movie.Cover.Length > 0 || movie.PortalCoverUrl.Length > 0,
            coverVersion = HashCode.Combine(movie.Cover, movie.PortalCoverUrl)
        };
    }

    private readonly SemaphoreSlim portalCoverSlots = new(6);

    private async Task<byte[]?> ReadWebCoverAsync(string id)
    {
        var cover = await Dispatcher.InvokeAsync(() => webMovies.TryGetValue(id, out var movie) && library.Movies.Contains(movie)
            ? (movie.Cover, movie.PortalId, movie.PortalCoverUrl) : ("", "", ""));
        var local = await ReadCoverFileAsync(cover.Item1);
        if (local != null || cover.Item3.Length == 0 || cover.Item2.Length != 64 || !cover.Item2.All(Uri.IsHexDigit)) return local;

        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cover.Item3)));
        var cache = Path.Combine(Library.DataDirectory, "covers", $"portal-{cover.Item2}-{fingerprint}.img");
        await portalCoverSlots.WaitAsync(lifetime.Token);
        try
        {
            var cached = await ReadCoverFileAsync(cache);
            if (cached != null)
            {
                PortalCoverCache.Touch(cache);
                return cached;
            }
            var image = await PortalService.FetchSourceCoverAsync(cover.Item3, lifetime.Token);
            if (image == null && PortalService.LoadConnection() is { } connection)
                image = await PortalService.FetchCoverAsync(connection, cover.Item2, lifetime.Token);
            if (image != null) await PortalCoverCache.StoreAsync(cache, image, lifetime.Token);
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally { portalCoverSlots.Release(); }
    }

    private async Task<byte[]?> ReadBackdropAsync(string id){var key=await Dispatcher.InvokeAsync(()=>webMovies.TryGetValue(id,out var m)&&library.Movies.Contains(m)?m.BackdropKey:"");return await BackdropArtwork.FetchAsync(key,lifetime.Token);}
    private async Task<byte[]?> ReadCoverFileAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            await using var input = File.OpenRead(path);
            if (input.Length is < 9 or > 12 * 1024 * 1024) return null;
            var bytes = new byte[(int)input.Length];
            await input.ReadExactlyAsync(bytes, lifetime.Token);
            return RemoteFiles.IsImage(bytes) || bytes[0] == 'B' && bytes[1] == 'M' ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private bool webDialogOpen;
    private bool webImporting;
    private async Task<object?> DispatchWebActionAsync(WebAction command)
    {
        if (command.Name == "import-checkpoints")
            return library.ImportCheckpoints.OrderByDescending(item => item.Updated).ToArray();
        if (command.Name == "movie-sources")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var selected) || !library.Movies.Contains(selected))
                throw new InvalidDataException("Filme indisponível.");
            var identity = FilmIdentity(selected);
            return library.Movies.Where(movie => movie.IsOnline && RemoteFiles.IsPublicHttps(movie.SourceUrl) &&
                (movie == selected || identity != null && FilmIdentity(movie) == identity))
                .OrderByDescending(movie => movie == selected).DistinctBy(movie => movie.SourceUrl).Select(WebMovie).ToArray();
        }
        if (command.Name == "series-episodes")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var selected)) throw new InvalidDataException("Série indisponível.");
            var series = ImportedTitle.Describe(selected);
            if (series.Type != "series") return Array.Empty<object>();
            return library.Movies.Select(movie => (Movie: movie, Kind: ImportedTitle.Describe(movie)))
                .Where(item => item.Kind.Type == "series" && CoverService.Normalize(item.Kind.Series) == CoverService.Normalize(series.Series))
                .OrderBy(item => item.Kind.Season ?? 1).ThenBy(item => item.Kind.Episode ?? 0)
                .Select(item => WebMovie(item.Movie)).ToArray();
        }
        if (command.Name == "catalog-sources")
        {
            CatalogWatch.Discover(library);
            return library.CatalogSources.Select(source => source.Address)
                .Concat(library.Movies.Where(movie => movie.IsOnline &&
                    (movie.IsTmdbImport || movie.ImportedFrom.Length == 0 || movie.ImportedFrom == movie.SourceUrl))
                    .Select(movie => movie.ImportedFrom.Length > 0 ? movie.ImportedFrom : movie.SourceUrl))
                .Where(RemoteFiles.IsPublicHttps).Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        if (command.Name.StartsWith("visual-", StringComparison.Ordinal)) return await VisualActionAsync(command);
        if (command.Name.StartsWith("search-", StringComparison.Ordinal)) return await SearchActionAsync(command);
        if (command.Name.StartsWith("history-", StringComparison.Ordinal)) return await HistoryActionAsync(command);
        if (command.Name.StartsWith("profile-", StringComparison.Ordinal)) return await ProfileActionAsync(command);
        if (command.Name.StartsWith("media-", StringComparison.Ordinal)) return await MediaActionAsync(command);
        if (command.Name.StartsWith("series-", StringComparison.Ordinal) && command.Name!="series-episodes") return await SeriesActionAsync(command);
        if (command.Name.StartsWith("account-", StringComparison.Ordinal)) return await AccountActionAsync(command);
        if (command.Name.StartsWith("tv-", StringComparison.Ordinal)) return await TvActionAsync(command);
        if (command.Name.StartsWith("smart-", StringComparison.Ordinal)) return await SmartActionAsync(command);
        if (command.Name.StartsWith("settings-", StringComparison.Ordinal)) return await WebSettingsAsync(command);
        if (command.Name.StartsWith("torrent-", StringComparison.Ordinal)) return await TorrentActionAsync(command);
        if (command.Name is "movie-details" or "movie-details-refresh")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var movie) || !library.Movies.Contains(movie))
                throw new InvalidDataException("O título não está mais disponível.");
            return await MovieDetailsAsync(movie,command.Name=="movie-details-refresh");
        }
        if (command.Name == "hero-slide")
        {
            if (!int.TryParse(command.Address, out var offset) || offset < 0) throw new InvalidDataException("Destaque inválido.");
            var movie = library.Movies.Where(movie => !movie.ImportedOnly)
                .OrderByDescending(movie => !movie.Watched && movie.ResumeMilliseconds > 30000)
                .ThenByDescending(movie => movie.Added).Skip(offset).FirstOrDefault();
            return movie == null ? null : WebMovie(movie);
        }
        if (command.Name is "download-options" or "download-source" or "download-resolve")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var movie) || !library.Movies.Contains(movie) || !movie.IsOnline)
                throw new InvalidDataException("Título indisponível.");
            List<PortalDownload> options;
            try { options = await PortalService.GetDownloadsAsync(movie.PortalId, movie.SourceUrl, lifetime.Token); }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            {
                if (!RemoteFiles.IsPublicHttps(movie.DownloadUrl))
                    throw new InvalidDataException(ex is System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
                        ? "O título não está mais na coleção do portal. Importe o endereço novamente ou abra a página original."
                        : ex is InvalidDataException ? ex.Message : "A consulta de downloads falhou. Tente novamente ou abra a página original.");
                options = [];
            }
            if (RemoteFiles.IsPublicHttps(movie.DownloadUrl) && !options.Any(option => option.Url == movie.DownloadUrl))
                options.Insert(0, new PortalDownload(movie.DownloadUrl, "Baixar no PC", "file"));
            options = options.Where(option => option.Kind == "torrent" ? TorrentClient.IsMagnet(option.Url) :
                option.Kind is "file" or "link" or "resolve" && RemoteFiles.IsPublicHttps(option.Url)).ToList();
            options = options.Select(option => DynamicDownloads.IsLinkLiberado(option.Url) ? option with { Kind = "resolve" } : option).ToList();
            if (command.Name == "download-options") return options;
            var selected = options.Find(option => option.Url == command.Address && option.Kind != "torrent")
                ?? throw new InvalidDataException("A opção escolhida não está mais disponível.");
            if (selected.Kind == "resolve") selected = await DynamicDownloads.ResolveAsync(movie.SourceUrl, selected, lifetime.Token);
            if (command.Name == "download-resolve") return selected;
            if (selected.Kind == "file") RunWebDialog(() => DownloadMovie(movie, this, selected.Url));
            else using (Process.Start(new ProcessStartInfo(selected.Url) { UseShellExecute = true })) { }
            return null;
        }
        if (command.Name == "import-batch")
        {
            var address = CatalogWatch.NormalizeImportAddress(command.Address ?? "");
            if (address.Length > 4000 || !RemoteFiles.IsPublicHttps(address))
                throw new InvalidDataException("Cole um endereço HTTPS público completo.");
            if (webImporting) throw new InvalidDataException("Já existe uma importação em andamento.");
            webImporting = true;
            try
            {
                if (TmdbClient.IsSiteUrl(address)) return await ImportTmdbAsync(address);
                var connection = await PortalService.GetOrCreateConnectionAsync(lifetime.Token);
                var root = command.ImportRoot ?? address;
                if (!RemoteFiles.IsPublicHttps(root) || root.Length > 4000) throw new InvalidDataException("Origem da importação inválida.");
                var checkpoint = library.ImportCheckpoints.FirstOrDefault(item => item.Root == root);
                if (!command.Continuation || checkpoint == null)
                {
                    checkpoint = new ImportCheckpoint { Root = root, Next = address };
                    library.ImportCheckpoints.RemoveAll(item => item.Root == root);
                    library.ImportCheckpoints.Add(checkpoint);
                    library.Save();
                }
                var batch = await ImportTrackedBatchAsync(connection, address, command.Continuation);
                checkpoint.Next = batch.Next.Length > 0 ? batch.Next : batch.Partial.Length > 0 ? address : "";
                checkpoint.Count += batch.Count;
                checkpoint.Total = Math.Max(checkpoint.Total, batch.Total);
                checkpoint.SyncNeeded |= !batch.Local && batch.Count > 0;
                checkpoint.Updated = DateTime.UtcNow;
                library.Save();
                return batch;
            }
            catch (System.Net.Http.HttpRequestException)
            {
                throw new InvalidDataException("O portal está indisponível. Confira a conexão e tente novamente.");
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                throw new InvalidDataException("A resposta demorou demais. Você pode tentar continuar.");
            }
            finally { webImporting = false; }
        }
        if (command.Name == "import-finish")
        {
            library.ImportCheckpoints.RemoveAll(item => item.Root == command.Address && item.Next.Length == 0);
            library.Save();
            return null;
        }
        if (command.Name == "sync-imports")
        {
            var titlesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = SyncPortalAsync(true, titlesReady: titlesReady);
            if (!await titlesReady.Task) throw new InvalidDataException("A importação foi salva no portal. Sincronize novamente para ver os títulos.");
            return null;
        }
        await RunWebActionAsync(command);
        return null;
    }

    private async Task RunWebActionAsync(WebAction command)
    {
        if (closed) return;
        var args = new RoutedEventArgs();
        switch (command.Name)
        {
            case "add-folder": RunWebDialog(() => AddFolder_Click(this, args)); return;
            case "portal": RunWebDialog(() => Portal_Click(this, args)); return;
            case "folders": RunWebDialog(() => Folders_Click(this, args)); return;
            case "settings": RunWebDialog(() => Settings_Click(this, args)); return;
            case "tmdb-settings": RunWebDialog(ConfigureTmdb); return;
            case "classic": Show(); Activate(); return;
            case "quit": Close(); return;
            case "scan": await ScanAsync(); return;
            case "cancel-scan": scan?.Cancel(); return;
            case "sync": await SyncPortalAsync(true); return;
            case "covers": Covers_Click(this, args); return;
            case "update": _ = CheckUpdate(true); return;
            case "clear-library": ClearLibrary(); return;
        }
        if (command.Id == null || !webMovies.TryGetValue(command.Id, out var movie) || !library.Movies.Contains(movie))
            throw new InvalidDataException("O título não está mais disponível.");
        switch (command.Name)
        {
            case "play": await RunWebDialogAsync(() => Play(movie)); return;
            case "details": RunWebDialog(() => ShowMovieDetails(movie)); return;
            case "add-to-library": AddImportedMovie(movie); return;
            case "remove-movie": RemoveFromLibrary(movie); return;
            case "favorite": movie.Favorite = !movie.Favorite; break;
            case "watched": movie.Watched = !movie.Watched; break;
            default: throw new InvalidDataException("Ação desconhecida.");
        }
        library.Save();
        Refresh();
    }

    private void RunWebDialog(Action show)
    {
        if (webDialogOpen) return;
        webDialogOpen = true;
        try { show(); }
        finally { webDialogOpen = false; }
    }

    private async Task RunWebDialogAsync(Func<Task> show)
    {
        if (webDialogOpen) return;
        webDialogOpen = true;
        try { await show(); }
        finally { webDialogOpen = false; }
    }

    private void AddImportedMovie(Movie movie)
    {
        if (!movie.ImportedOnly) return;
        movie.ImportedOnly = false;
        library.Save();
        Status.Text = $"Adicionado à biblioteca · {movie.Title}";
        Refresh();
    }

    private void RemoveFromLibrary(Movie movie)
    {
        if (movie.ImportedOnly) return;
        var index = library.Movies.IndexOf(movie);
        var ignored = movie.Path.Length > 0 && library.RemovedPaths.Add(movie.Path);
        if (movie.IsOnline) movie.ImportedOnly = true;
        else library.Movies.RemoveAt(index);
        try { library.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ignored) library.RemovedPaths.Remove(movie.Path);
            if (movie.IsOnline) movie.ImportedOnly = false;
            else library.Movies.Insert(index, movie);
            throw new InvalidDataException("Não foi possível salvar a remoção.", ex);
        }
        Status.Text = $"Removido da biblioteca · {movie.Title}";
        Refresh();
    }

    private void ClearLibrary()
    {
        var originalMovies = library.Movies;
        var originalFolders = library.Folders;
        var promoted = originalMovies.Where(movie => movie.IsOnline && !movie.ImportedOnly).ToArray();
        library.Movies = originalMovies.Where(movie => movie.IsOnline).ToList();
        library.Folders = [];
        foreach (var movie in promoted) movie.ImportedOnly = true;
        try { library.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            library.Movies = originalMovies;
            library.Folders = originalFolders;
            foreach (var movie in promoted) movie.ImportedOnly = false;
            throw new InvalidDataException("Não foi possível gravar a biblioteca. Os registros foram preservados.", ex);
        }
        scan?.Cancel();
        coverSearch?.Cancel();
        Status.Text = "Biblioteca limpa. Seus arquivos continuam no PC.";
        Refresh();
    }
}
