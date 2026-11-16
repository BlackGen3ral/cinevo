using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace Cinevo;

public partial class MainWindow
{
    private static string? ValidateDownloadFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
            throw new InvalidDataException("Escolha uma pasta existente para o download.");
        return Path.GetFullPath(folder);
    }

    private async Task<object?> WebSettingsAsync(WebAction command)
    {
        try
        {
            switch (command.Name)
            {
                case "settings-read":
                    var current = TorrentConnection.Load();
                    return new { library.AutoUpdate, library.AutoCovers, library.SidebarCollapsed, version = Updater.CurrentVersion.ToString(),
                        canUpdate = Updater.IsInstalled, tmdb = File.Exists(TmdbClient.CredentialPath),
                        port = current?.Port ?? 8080, username = current?.Username ?? "admin",
                        noPassword = current?.LocalWithoutPassword ?? true,
                        showNews = Updater.IsInstalled && (!File.Exists(Path.Combine(Library.DataDirectory, "news-version")) ||
                            File.ReadAllText(Path.Combine(Library.DataDirectory, "news-version")) != Updater.CurrentVersion) };
                case "settings-news-seen":
                    Directory.CreateDirectory(Library.DataDirectory);
                    var marker = Path.Combine(Library.DataDirectory, "news-version");
                    await File.WriteAllTextAsync(marker + ".tmp", Updater.CurrentVersion, lifetime.Token);
                    File.Move(marker + ".tmp", marker, true);
                    return null;
                case "settings-history":
                    if (!int.TryParse(command.Address ?? "1", out var page) || page < 1 || page > 100)
                        throw new InvalidDataException("Página inválida.");
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cinevo/" + Updater.CurrentVersion);
                        using var response = await http.GetAsync($"https://api.github.com/repos/{Updater.Repository}/releases?per_page=30&page={page}",
                            HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
                        response.EnsureSuccessStatusCode();
                        var bytes = await RemoteFiles.ReadBoundedAsync(response, 2 * 1024 * 1024, lifetime.Token);
                        using var json = JsonDocument.Parse(bytes);
                        return json.RootElement.EnumerateArray().Where(item => !item.GetProperty("draft").GetBoolean()).Select(item => new
                        {
                            title = item.GetProperty("name").GetString(), tag = item.GetProperty("tag_name").GetString(),
                            date = item.GetProperty("published_at").GetString(), notes = item.GetProperty("body").GetString(),
                            url = item.GetProperty("html_url").GetString()
                        }).ToArray();
                    }
                case "settings-sidebar":
                    using(var sidebar=JsonDocument.Parse(command.Address??"{}")){var previous=library.SidebarCollapsed;library.SidebarCollapsed=sidebar.RootElement.GetProperty("collapsed").GetBoolean();try{library.Save();}catch{library.SidebarCollapsed=previous;throw;}return null;}
                case "settings-save":
                    using (var json = JsonDocument.Parse(command.Address ?? "{}"))
                    {
                        var oldUpdate = library.AutoUpdate;
                        var oldCovers = library.AutoCovers;
                        library.AutoUpdate = json.RootElement.GetProperty("autoUpdate").GetBoolean();
                        library.AutoCovers = json.RootElement.GetProperty("autoCovers").GetBoolean();
                        try { library.Save(); }
                        catch { library.AutoUpdate = oldUpdate; library.AutoCovers = oldCovers; throw; }
                        if (!library.AutoCovers) coverSearch?.Cancel();
                        return null;
                    }
                case "settings-folder":
                    string? folder = null;
                    RunWebDialog(() =>
                    {
                        var picker = new OpenFolderDialog { Title = "Escolha a pasta do download" };
                        if (picker.ShowDialog() == true) folder = picker.FolderName;
                    });
                    return new { folder };
                case "settings-torrent":
                    var setup = JsonSerializer.Deserialize<TorrentConnection>(command.Address ?? "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new InvalidDataException("Confira a conexão do qBittorrent.");
                    if (setup.Port is < 1 or > 65535 || setup.Username.Length > 256 || setup.Password.Length > 256)
                        throw new InvalidDataException("Confira a porta e as credenciais.");
                    if (setup.LocalWithoutPassword) setup = setup with { Username = "", Password = "" };
                    else if (setup.Password.Length == 0 && TorrentConnection.Load() is { } saved && saved.Username == setup.Username)
                        setup = setup with { Password = saved.Password };
                    if (!setup.LocalWithoutPassword && (setup.Username.Length == 0 || setup.Password.Length == 0))
                        throw new InvalidDataException("Informe o usuário e a senha da Interface Web.");
                    using (var client = new TorrentClient(setup))
                    {
                        var version = (await client.VersionAsync(lifetime.Token)).Trim().TrimStart('v');
                        if (!version.StartsWith("4.") && !version.StartsWith("5."))
                            throw new InvalidDataException("Essa porta não retornou uma versão compatível do qBittorrent.");
                    }
                    setup.Save();
                    torrents?.Dispose();
                    torrents = null;
                    return null;
                case "settings-tmdb":
                    var credential = command.Address?.Trim() ?? "";
                    if (!TmdbClient.ValidCredential(credential)) throw new InvalidDataException("Use o token de leitura ou a chave API v3 do TMDB.");
                    using (var client = new TmdbClient(credential)) await client.VerifyAsync(lifetime.Token);
                    TmdbClient.StoreCredential(credential);
                    TmdbChanged();
                    return null;
                case "settings-tmdb-remove":
                    File.Delete(TmdbClient.CredentialPath);
                    TmdbChanged();
                    return null;
                case "settings-backup":
                    RunWebDialog(() =>
                    {
                        var dialog = new SaveFileDialog { FileName = "Cinevo-biblioteca.json", Filter = "Biblioteca JSON|*.json" };
                        if (dialog.ShowDialog() == true) { library.Save(); File.Copy(Library.DataPath, dialog.FileName, true); }
                    });
                    return null;
                case "settings-data":
                    Directory.CreateDirectory(Library.DataDirectory);
                    using (Process.Start(new ProcessStartInfo(Library.DataDirectory) { UseShellExecute = true })) { }
                    return null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { throw new InvalidDataException(ex is InvalidDataException ? ex.Message : "Não foi possível salvar ou verificar as configurações. Tente novamente."); }
        throw new InvalidDataException("Configuração desconhecida.");
    }
}
