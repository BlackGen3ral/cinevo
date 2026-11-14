using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Cinevo;
public partial class MainWindow
{
    private readonly SemaphoreSlim portalSync = new(1);
    private void ImportAddress_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(26) };
        var window = new Window
        {
            Title = "Importar endereço — Cinevo", Owner = this, Width = 590, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Brush("#101218"), Foreground = Brush("#ECEEF4"), Content = panel
        };
        panel.Children.Add(new TextBlock { Text = "Importar endereço", FontSize = 26, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock
        {
            Text = "Cole o link HTTPS de um catálogo ou de um filme. O Cinevo segue as próximas páginas automaticamente.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#ADB0BE"), Margin = new Thickness(0, 0, 0, 14)
        });
        var input = new TextBox { MaxLength = 4000 };
        System.Windows.Automation.AutomationProperties.SetName(input, "Endereço do catálogo ou filme");
        panel.Children.Add(input);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16), MinHeight = 32 };
        panel.Children.Add(message);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var start = new Button { Content = "Importar", IsDefault = true, Background = Brush("#B49AF8"), Foreground = Brush("#171121") };
        var pause = new Button { Content = "Pausar", Visibility = Visibility.Collapsed };
        var resume = new Button { Content = "Continuar importação", Visibility = Visibility.Collapsed };
        var close = new Button { Content = "Fechar", IsCancel = true };
        foreach (var button in new[] { start, pause, resume, close }) controls.Children.Add(button);
        panel.Children.Add(controls);
        window.Loaded += (_, _) => input.Focus();

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        var running = false;
        var paused = false;
        var continuation = false;
        pause.Click += (_, _) =>
        {
            paused = true;
            pause.IsEnabled = false;
            message.Text = "Pausando após as páginas em andamento…";
        };
        window.Closing += (_, args) =>
        {
            if (running) args.Cancel = true;
        };
        start.Click += async (_, _) =>
        {
            var value = input.Text.Trim();
            if (!RemoteFiles.IsPublicHttps(value))
            {
                message.Text = "Cole um endereço HTTPS público completo.";
                return;
            }
            visited.Clear();
            next = new UriBuilder(value) { Fragment = "" }.Uri.AbsoluteUri;
            continuation = false;
            await ImportAsync();
        };
        resume.Click += async (_, _) => await ImportAsync();
        window.ShowDialog();

        async Task ImportAsync()
        {
            if (running || string.IsNullOrEmpty(next)) return;
            running = true;
            paused = false;
            start.IsEnabled = input.IsEnabled = close.IsEnabled = false;
            pause.Visibility = Visibility.Visible;
            pause.IsEnabled = true;
            resume.Visibility = Visibility.Collapsed;
            var count = 0;
            var outcome = "";
            try
            {
                message.Text = "Buscando filmes no endereço…";
                var connection = await PortalService.GetOrCreateConnectionAsync(lifetime.Token);
                while (!paused && !string.IsNullOrEmpty(next))
                {
                    var current = next;
                    var batch = await PortalService.ImportBatchAsync(connection, current, continuation, lifetime.Token);
                    count += batch.Count;
                    next = batch.Next;
                    outcome = batch.Message;
                    continuation = true;
                    if (!string.IsNullOrEmpty(batch.Partial))
                    {
                        outcome = batch.Partial;
                        break;
                    }
                    visited.Add(current);
                    if (!string.IsNullOrEmpty(next) && (!RemoteFiles.IsPublicHttps(next) || visited.Contains(next)))
                    {
                        outcome = "O site devolveu uma página repetida ou inválida.";
                        next = null;
                        break;
                    }
                    if (!paused && !string.IsNullOrEmpty(next))
                        message.Text = $"{count} títulos processados. Buscando próximas páginas…";
                }
                if (paused && !string.IsNullOrEmpty(next)) outcome = "Importação pausada.";
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                outcome = "A resposta demorou demais. Você pode tentar continuar.";
            }
            catch (Exception ex)
            {
                outcome = ex.Message;
            }
            finally
            {
                pause.Visibility = Visibility.Collapsed;
                if (!closed && count > 0)
                {
                    message.Text = "Atualizando sua biblioteca…";
                    var titlesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = SyncPortalAsync(true, titlesReady: titlesReady);
                    if (!await titlesReady.Task) outcome += " Confira a sincronização na biblioteca.";
                }
                message.Text = count > 1 && outcome.EndsWith("processados.", StringComparison.Ordinal)
                    ? $"{count} títulos processados."
                    : count > 1 ? $"{count} títulos processados. {outcome}" : outcome;
                start.IsEnabled = input.IsEnabled = close.IsEnabled = true;
                resume.Visibility = string.IsNullOrEmpty(next) ? Visibility.Collapsed : Visibility.Visible;
                running = false;
            }
        }
    }
    private async Task SyncPortalAsync(bool manual, bool showResult = false, TaskCompletionSource<bool>? titlesReady = null)
    {
        var acquired = false;
        try
        {
            if (closed) return;
            acquired = await portalSync.WaitAsync(titlesReady == null ? 0 : Timeout.Infinite, lifetime.Token);
            if (!acquired) return;
            var connection=PortalService.LoadConnection();
            if(connection==null){if(manual)Status.Text="Abra Portal online e importe o arquivo de conexão uma vez.";return;}
            if(manual)Status.Text="Sincronizando sua coleção online…";
            var result = await PortalService.SyncAsync(library, connection, lifetime.Token, onTitlesReady: () =>
            {
                library.Save();
                Refresh();
                titlesReady?.TrySetResult(true);
            });
            if (closed) return;
            library.Save();
            Refresh();
            if (manual || showResult || result.Added > 0)
                Status.Text = result.Total == 0
                    ? "A coleção conectada está vazia. Use Abrir meu portal no Cinevo e importe os filmes nessa coleção."
                    : $"Portal sincronizado · {result.Total} na coleção online · {result.Added} novos.";
        }
        catch(Exception ex){if((manual||showResult)&&!closed)Status.Text="Não foi possível sincronizar: "+ex.Message;}
        finally
        {
            if (acquired) portalSync.Release();
            titlesReady?.TrySetResult(false);
        }
    }
    private void Portal_Click(object sender,RoutedEventArgs e)
    {
        var window=new Window{Title="Portal online — Cinevo",Width=610,Height=480,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(16,18,24)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(new TextBlock{Text="Sua coleção conectada.",FontSize=26,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock{Text="Cole endereços no portal e sincronize aqui para ver os títulos.\nVídeos diretos podem ser reproduzidos no Cinevo.\nDownloads aparecem nos detalhes quando a origem oferece o arquivo.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,22)});
        var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,12,0,0)};
        var open=new Button{Content="Abrir meu portal",Margin=new Thickness(0,0,0,12)};open.Click+=(_,_)=>{try{var c=PortalService.LoadConnection();Process.Start(new ProcessStartInfo(c==null?PortalService.DefaultEndpoint:PortalService.AccessLink(c)){UseShellExecute=true});}catch(Exception ex){message.Text=ex.Message;}};panel.Children.Add(open);
        var sync=new Button{Content="Sincronizar coleção agora",Margin=new Thickness(0,0,0,12)};sync.Click+=async(_,_)=>{sync.IsEnabled=false;await SyncPortalAsync(true);message.Text=Status.Text;sync.IsEnabled=true;};panel.Children.Add(sync);
        var connect=new Button{Content="Conectar portal · escolher arquivo",Margin=new Thickness(0,0,0,12)};connect.Click+=async(_,_)=>{var dialog=new OpenFileDialog{Title="Escolha o arquivo baixado em Conectar ao app no portal",Filter="Conexão Cinevo|*.cinevo.json;*.json"};if(dialog.ShowDialog(window)!=true)return;try{var c=PortalService.ReadPairing(dialog.FileName);PortalService.SaveConnection(c);await SyncPortalAsync(true);message.Text=Status.Text;}catch(Exception ex){message.Text="Não foi possível conectar: "+ex.Message;}};panel.Children.Add(connect);
        panel.Children.Add(new TextBlock{Text="A conexão é salva protegida pelo seu usuário do Windows.\nA sincronização acontece ao abrir o Cinevo ou pelo botão acima.\nSeus vídeos locais não são enviados ao portal.",Foreground=Brushes.Gray,FontSize=11,Margin=new Thickness(0,10,0,0)});panel.Children.Add(message);window.Content=panel;window.ShowDialog();
    }
    private void DownloadMovie(Movie movie,Window owner,string downloadUrl)
    {
        if(!RemoteFiles.IsPublicHttps(downloadUrl)){MessageBox.Show(owner,"A origem não oferece um link direto de download para este título.","Cinevo");return;}
        var extension=System.IO.Path.GetExtension(new Uri(downloadUrl).AbsolutePath).ToLowerInvariant();if(!new[]{".mp4",".mkv",".webm",".mov",".m4v",".avi"}.Contains(extension)){MessageBox.Show(owner,"Este link não representa um arquivo de vídeo compatível para download.","Cinevo");return;}
        var title=string.Concat(movie.Title.Select(ch=>System.IO.Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
        var dialog=new SaveFileDialog{Title="Salvar filme no PC",FileName=title+extension,Filter=$"Vídeo {extension}|*{extension}",DefaultExt=extension,AddExtension=true,OverwritePrompt=true};if(dialog.ShowDialog(owner)!=true)return;
        var download=new DownloadWindow(downloadUrl,dialog.FileName){Owner=owner};download.ShowDialog();
        if(download.Completed){movie.Path=dialog.FileName;movie.Bytes=new FileInfo(dialog.FileName).Length;movie.Available=true;library.Save();Refresh();Status.Text="Download concluído. O filme pode ser assistido offline no Cinevo.";}
    }
}
