using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace Cinevo;

public partial class MainWindow
{
    private void ConfigureTmdb()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        var window = new Window { Owner = this, Title = "Configurar TMDB — Cinevo", Width = 565,
            SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = "Na sua conta do TMDB, abra Configurações → API. Cole abaixo o API Read Access Token ou a chave API v3. O Cinevo guarda a credencial protegida neste PC." });
        var open = new Button { Content = "Abrir configurações da API no TMDB ↗", Margin = new Thickness(0, 12, 0, 12) };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("https://www.themoviedb.org/settings/api") { UseShellExecute = true });
        panel.Children.Add(open);
        var input = new PasswordBox { MaxLength = 2048, Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(input, "Token ou chave do TMDB");
        panel.Children.Add(input);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
            Text = File.Exists(TmdbClient.CredentialPath) ? "Há uma credencial salva neste PC." : "Aguardando credencial." };
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "Testar e salvar", IsDefault = true };
        var disconnect = new Button { Content = "Desconectar", IsEnabled = File.Exists(TmdbClient.CredentialPath) };
        var close = new Button { Content = "Fechar", IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(disconnect); buttons.Children.Add(close);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A8A0AF"), Margin = new Thickness(0, 14, 0, 0),
            Text = "This product uses the TMDB API but is not endorsed or certified by TMDB." });
        using var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        window.Closed += (_, _) => dialogLifetime.Cancel();
        var token = dialogLifetime.Token;
        save.Click += async (_, _) =>
        {
            var value = input.Password.Trim();
            if (!TmdbClient.ValidCredential(value)) { status.Text = "Use o token de leitura ou a chave API v3 da sua conta."; return; }
            save.IsEnabled = disconnect.IsEnabled = false;
            status.Text = "Testando acesso ao TMDB…";
            try
            {
                using var client = new TmdbClient(value);
                await client.VerifyAsync(token);
                token.ThrowIfCancellationRequested();
                TmdbClient.StoreCredential(value);
                TmdbChanged();
                window.Close();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (InvalidDataException ex) { status.Text = ex.Message; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException or System.Text.Json.JsonException)
            { status.Text = "Não foi possível verificar ou salvar a conexão do TMDB."; }
            finally { save.IsEnabled = true; disconnect.IsEnabled = File.Exists(TmdbClient.CredentialPath); }
        };
        disconnect.Click += (_, _) =>
        {
            try { File.Delete(TmdbClient.CredentialPath); TmdbChanged(); window.Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Não foi possível remover a credencial salva."; }
        };
        window.ShowDialog();
    }

    private void TmdbChanged()
    {
        tmdbCache.Clear();
        tmdbRevision++;
        webRevision++;
    }
}
