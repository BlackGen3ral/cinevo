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
                    (section, search, page, limit) => Dispatcher.InvokeAsync(() => WebCatalog(section, search, page, limit)).Task,
                    command => Dispatcher.InvokeAsync(() => DispatchWebActionAsync(command)).Task.Unwrap(),
                    ReadWebCoverAsync,
                    () => Dispatcher.InvokeAsync<object>(() => new
                    {
                        revision = webRevision, status = Status.Text, scanning = scan != null,
                        canUpdate = Updater.IsInstalled, update = updateProgress, updateRequested = installRequested
                    }).Task);
                try { await server.StartAsync(lifetime.Token); }
                catch { server.Stop(); throw; }
                if (closed) { server.Stop(); return; }
                web = server;
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

    private object WebCatalog(string section, string search, int page, int limit)
    {
        var saved = library.Movies.Where(movie => !movie.ImportedOnly).ToList();
        var imports = library.Movies.Where(movie => movie.IsOnline).ToList();
        var collection = section switch
        {
            "imports" => imports.OrderBy(SourceSite, StringComparer.OrdinalIgnoreCase).ThenBy(movie => !movie.ImportedOnly).ThenByDescending(movie => movie.Added),
            "favorites" => saved.Where(movie => movie.Favorite).OrderBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase),
            "continue" => saved.Where(movie => !movie.Watched && movie.ResumeMilliseconds > 30000).OrderByDescending(movie => movie.Added),
            _ => saved.OrderByDescending(movie => movie.Added)
        };
        var offset = (page - 1) * limit;
        var list = collection.Where(movie => search.Length == 0 ||
            $"{movie.Title} {movie.Year} {movie.Genre}".Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .Skip(offset).Take(limit + 1).ToList();
        var returning = saved.Where(movie => !movie.Watched && movie.ResumeMilliseconds > 30000).OrderByDescending(movie => movie.Added).Take(12);
        var favorites = saved.Where(movie => movie.Favorite).OrderByDescending(movie => movie.Added).Take(12);
        var spotlight = page == 1 && section == "library" && search.Length == 0
            ? returning.FirstOrDefault() ?? saved.OrderByDescending(movie => movie.Added).FirstOrDefault() : null;
        return new
        {
            movies = list.Take(limit).Select(WebMovie).ToArray(),
            more = list.Count > limit,
            spotlight = spotlight == null ? null : WebMovie(spotlight),
            returning = spotlight == null ? null : returning.Select(WebMovie).ToArray(),
            favorites = spotlight == null ? null : favorites.Select(WebMovie).ToArray(),
            total = saved.Count,
            folderCount = library.Folders.Count,
            imported = imports.Count(movie => movie.ImportedOnly),
            version = Updater.CurrentVersion
        };
    }

    private static string SourceSite(Movie movie) => Uri.TryCreate(movie.ImportedFrom.Length > 0 ? movie.ImportedFrom : movie.SourceUrl, UriKind.Absolute, out var uri)
        ? uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost : "Origem desconhecida";

    private object WebMovie(Movie movie)
    {
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
            id, movie.Title, movie.Year, movie.Genre, description = movie.CustomSynopsis.Length > 0 ? movie.CustomSynopsis : movie.Description, movie.Favorite, movie.Watched,
            site = movie.IsOnline ? SourceSite(movie) : "",
            customTrailer = MovieTrailer.FromUrl(movie.TrailerUrl)?.VideoId,
            tmdbLink = movie.TmdbUrl,
            tmdbConfigured = File.Exists(TmdbClient.CredentialPath),
            tmdbRevision,
            movie.IsOnline, movie.ImportedOnly, movie.Progress, movie.Rating, movie.PlayLabel, movie.Available,
            hasCover = movie.Cover.Length > 0,
            coverVersion = movie.Cover.GetHashCode(StringComparison.Ordinal)
        };
    }

    private async Task<byte[]?> ReadWebCoverAsync(string id)
    {
        var path = await Dispatcher.InvokeAsync(() => webMovies.TryGetValue(id, out var movie) && library.Movies.Contains(movie) ? movie.Cover : null);
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
        if (command.Name.StartsWith("torrent-", StringComparison.Ordinal)) return await TorrentActionAsync(command);
        if (command.Name == "movie-details")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var movie) || !library.Movies.Contains(movie))
                throw new InvalidDataException("O título não está mais disponível.");
            return await MovieDetailsAsync(movie);
        }
        if (command.Name == "hero-slide")
        {
            if (!int.TryParse(command.Address, out var offset) || offset < 0) throw new InvalidDataException("Destaque inválido.");
            var movie = library.Movies.Where(movie => !movie.ImportedOnly)
                .OrderByDescending(movie => !movie.Watched && movie.ResumeMilliseconds > 30000)
                .ThenByDescending(movie => movie.Added).Skip(offset).FirstOrDefault();
            return movie == null ? null : WebMovie(movie);
        }
        if (command.Name is "download-options" or "download-source")
        {
            if (command.Id == null || !webMovies.TryGetValue(command.Id, out var movie) || !library.Movies.Contains(movie) || !movie.IsOnline)
                throw new InvalidDataException("Título indisponível.");
            List<PortalDownload> options;
            try { options = await PortalService.GetDownloadsAsync(movie.PortalId, lifetime.Token); }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            {
                if (!RemoteFiles.IsPublicHttps(movie.DownloadUrl)) throw new InvalidDataException("Não foi possível consultar as opções da origem.");
                options = [];
            }
            if (RemoteFiles.IsPublicHttps(movie.DownloadUrl) && !options.Any(option => option.Url == movie.DownloadUrl))
                options.Insert(0, new PortalDownload(movie.DownloadUrl, "Baixar no PC", "file"));
            options = options.Where(option => option.Kind == "torrent" ? TorrentClient.IsMagnet(option.Url) :
                option.Kind is "file" or "link" && RemoteFiles.IsPublicHttps(option.Url)).ToList();
            if (command.Name == "download-options") return options;
            var selected = options.Find(option => option.Url == command.Address && option.Kind != "torrent")
                ?? throw new InvalidDataException("A opção escolhida não está mais disponível.");
            if (selected.Kind == "file") RunWebDialog(() => DownloadMovie(movie, this, selected.Url));
            else using (Process.Start(new ProcessStartInfo(selected.Url) { UseShellExecute = true })) { }
            return null;
        }
        if (command.Name == "import-batch")
        {
            var address = command.Address ?? "";
            if (address.Length > 4000 || !RemoteFiles.IsPublicHttps(address))
                throw new InvalidDataException("Cole um endereço HTTPS público completo.");
            if (webImporting) throw new InvalidDataException("Já existe uma importação em andamento.");
            webImporting = true;
            try
            {
                var connection = await PortalService.GetOrCreateConnectionAsync(lifetime.Token);
                return await PortalService.ImportBatchAsync(connection, address, command.Continuation, lifetime.Token);
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
