using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Cinevo;

public partial class MainWindow
{
    private TorrentClient? torrents;
    private TorrentClient GetTorrentClient()
    {
        if (torrents != null) return torrents;
        var connection = TorrentConnection.Load()
            ?? throw new InvalidDataException("Conecte o qBittorrent para acompanhar os downloads.");
        return torrents = new TorrentClient(connection);
    }

    private void ConfigureTorrents()
    {
        var current = TorrentConnection.Load();
        var panel = new StackPanel { Margin = new Thickness(26) };
        var window = new Window
        {
            Owner = this, Title = "Conectar qBittorrent — Cinevo", Width = 530,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel
        };
        panel.Children.Add(new TextBlock
        {
            Text = "No qBittorrent, ative a Interface Web em 127.0.0.1. Para conectar sem senha, marque “Bypass authentication for clients on localhost” e aplique.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        TextBox Field(Panel target, string label, string value)
        {
            target.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = value, MaxLength = 256 };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            target.Children.Add(box);
            return box;
        }
        var port = Field(panel, "Porta", (current?.Port ?? 8080).ToString());
        var local = new CheckBox
        {
            Content = "Conectar sem senha neste PC", IsChecked = current?.LocalWithoutPassword ?? true,
            Margin = new Thickness(0, 14, 0, 6), Foreground = window.Foreground
        };
        panel.Children.Add(local);
        var credentials = new StackPanel { Visibility = local.IsChecked == true ? Visibility.Collapsed : Visibility.Visible };
        panel.Children.Add(credentials);
        var username = Field(credentials, "Usuário", current?.Username is { Length: > 0 } user ? user : "admin");
        credentials.Children.Add(new TextBlock { Text = "Senha da Interface Web", Margin = new Thickness(0, 8, 0, 4) });
        var password = new PasswordBox { Password = current?.Password ?? "", MaxLength = 256 };
        credentials.Children.Add(password);
        local.Click += (_, _) => credentials.Visibility = local.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14) };
        panel.Children.Add(notice);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var connect = new Button { Content = "Testar e salvar", IsDefault = true };
        var close = new Button { Content = "Fechar", IsCancel = true };
        buttons.Children.Add(connect);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        using var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        window.Closed += (_, _) => dialogLifetime.Cancel();
        connect.Click += async (_, _) =>
        {
            var noPassword = local.IsChecked == true;
            if (!int.TryParse(port.Text, out var number) || number is < 1 or > 65535)
            {
                notice.Text = "Informe a porta da Interface Web do qBittorrent.";
                return;
            }
            if (!noPassword && (string.IsNullOrWhiteSpace(username.Text) || password.Password.Length == 0))
            {
                notice.Text = "Informe o usuário e a senha do qBittorrent.";
                return;
            }
            connect.IsEnabled = false;
            notice.Text = "Testando a conexão local…";
            var setup = new TorrentConnection(number, noPassword ? "" : username.Text.Trim(),
                noPassword ? "" : password.Password, noPassword);
            using var client = new TorrentClient(setup);
            var token = dialogLifetime.Token;
            try
            {
                var version = await client.VersionAsync(token);
                if (!version.TrimStart('v').StartsWith("5.") && !version.TrimStart('v').StartsWith("4."))
                    throw new InvalidDataException($"A porta {number} não retornou uma versão compatível do qBittorrent. Confira a porta em Interface Web e use outra se ela estiver ocupada.");
                token.ThrowIfCancellationRequested();
                setup.Save();
                torrents?.Dispose();
                torrents = null;
                window.DialogResult = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                notice.Text = ex.Message;
            }
            finally { connect.IsEnabled = true; }
        };
        window.ShowDialog();
    }

    private static void OpenTorrentApp()
    {
        var installed = Process.GetProcessesByName("qbittorrent");
        if (installed.Length > 0)
        {
            foreach (var process in installed) process.Dispose();
            return;
        }
        var exe = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "qBittorrent", "qbittorrent.exe"))
            .FirstOrDefault(File.Exists);
        if (exe == null) throw new InvalidDataException("O qBittorrent precisa estar instalado neste PC.");
        using var started = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    private async Task<TorrentTransfer> FinishedTransferAsync(string hash, CancellationToken token)
    {
        var torrent = (await GetTorrentClient().TransfersAsync(token, hash)).FirstOrDefault(item => item.Hash == hash);
        if (torrent is not { Progress: >= 1 } || !Path.IsPathFullyQualified(torrent.SavePath ?? ""))
            throw new InvalidDataException("Este download ainda não está disponível para assistir.");
        return torrent;
    }

    private async Task<TorrentFile[]> CompletedVideosAsync(string hash, CancellationToken token)
    {
        var torrent = await FinishedTransferAsync(hash, token);
        var files = await GetTorrentClient().FilesAsync(hash, token);
        return files.Where(file => file.Progress >= 1 && file.Priority > 0 && VideoPath(torrent, file) != null)
            .OrderByDescending(file => file.Size).ToArray();
    }

    private static string? VideoPath(TorrentTransfer transfer, TorrentFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Name) || Path.IsPathRooted(file.Name) ||
            !Scanner.Extensions.Contains(Path.GetExtension(file.Name))) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(transfer.SavePath));
            var path = Path.GetFullPath(file.Name, root);
            var relative = Path.GetRelativePath(root, path);
            return Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path) ? null : path;
        }
        catch (ArgumentException) { return null; }
    }

    private async Task PlayTorrentAsync(string hash, int index)
    {
        var torrent = await FinishedTransferAsync(hash, lifetime.Token);
        var files = await GetTorrentClient().FilesAsync(hash, lifetime.Token);
        var file = files.FirstOrDefault(item => item.Index == index && item.Progress >= 1 && item.Priority > 0);
        if (file == null || VideoPath(torrent, file) is not { } path)
            throw new InvalidDataException("O vídeo não foi encontrado na pasta de downloads.");
        var movie = library.Movies.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (movie == null)
        {
            var (title, year) = Scanner.Parse(path);
            movie = new Movie { Title = title, Year = year, Path = path, Bytes = file.Size };
        }
        await RunWebDialogAsync(() => Play(movie));
    }

    private async Task<object?> TorrentActionAsync(WebAction command)
    {
        if (command.Name == "torrent-configure") { RunWebDialog(ConfigureTorrents); return null; }
        if (command.Name == "torrent-open") { OpenTorrentApp(); return null; }
        if (command.Name == "torrent-list")
        {
            if (torrents == null && TorrentConnection.Load() == null) return new { configured = false };
            var transfers = await GetTorrentClient().TransfersAsync(lifetime.Token);
            return new
            {
                configured = true,
                transfers = transfers.Where(item => TorrentClient.IsHash(item.Hash)).Select(item =>
                    new { item.Hash, item.Name, item.Progress, item.Size, item.Completed, item.Speed, item.Eta, item.State })
            };
        }
        if (command.Name == "torrent-add")
        {
            await GetTorrentClient().AddAsync(command.Address ?? "", lifetime.Token);
            return null;
        }
        if (!TorrentClient.IsHash(command.Id)) throw new InvalidDataException("Download inválido.");
        var hash = command.Id!.ToLowerInvariant();
        switch (command.Name)
        {
            case "torrent-remove":
                await GetTorrentClient().RemoveAsync(hash, lifetime.Token);
                return null;
            case "torrent-pause":
            case "torrent-resume":
                await GetTorrentClient().SetRunningAsync(hash, command.Name == "torrent-resume", lifetime.Token);
                return null;
            case "torrent-files":
                var videos = await CompletedVideosAsync(hash, lifetime.Token);
                return videos.Select(file => new { file.Index, name = Path.GetFileName(file.Name), file.Size });
            case "torrent-play":
                if (int.TryParse(command.Address, out var index)) { await PlayTorrentAsync(hash, index); return null; }
                break;
        }
        throw new InvalidDataException("Ação de download inválida.");
    }
}
