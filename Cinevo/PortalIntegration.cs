using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Cinevo;
public partial class MainWindow
{
    private bool syncingPortal;
    private async Task SyncPortalAsync(bool manual, bool showResult = false)
    {
        if(syncingPortal||closed)return;syncingPortal=true;
        try
        {
            var connection=PortalService.LoadConnection();
            if(connection==null){if(manual)Status.Text="Abra Portal online e importe o arquivo de conexão uma vez.";return;}
            if(manual)Status.Text="Sincronizando sua coleção online…";
            var result = await PortalService.SyncAsync(library, connection, lifetime.Token);
            if (closed) return;
            library.Save();
            Refresh();
            if (manual || showResult || result.Added > 0)
                Status.Text = result.Total == 0
                    ? "A coleção conectada está vazia. Use Abrir meu portal no Cinevo e importe os filmes nessa coleção."
                    : $"Portal sincronizado · {result.Total} na coleção online · {result.Added} novos.";
        }
        catch(Exception ex){if((manual||showResult)&&!closed)Status.Text="Não foi possível sincronizar: "+ex.Message;}
        finally{syncingPortal=false;}
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
    private void DownloadMovie(Movie movie,Window owner)
    {
        if(!RemoteFiles.IsPublicHttps(movie.DownloadUrl)){MessageBox.Show(owner,"A origem não oferece um link direto de download para este título.","Cinevo");return;}
        var extension=System.IO.Path.GetExtension(new Uri(movie.DownloadUrl).AbsolutePath).ToLowerInvariant();if(!new[]{".mp4",".mkv",".webm",".mov",".m4v",".avi"}.Contains(extension)){MessageBox.Show(owner,"Este link não representa um arquivo de vídeo compatível para download.","Cinevo");return;}
        var title=string.Concat(movie.Title.Select(ch=>System.IO.Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
        var dialog=new SaveFileDialog{Title="Salvar filme no PC",FileName=title+extension,Filter=$"Vídeo {extension}|*{extension}",DefaultExt=extension,AddExtension=true,OverwritePrompt=true};if(dialog.ShowDialog(owner)!=true)return;
        var download=new DownloadWindow(movie.DownloadUrl,dialog.FileName){Owner=owner};download.ShowDialog();
        if(download.Completed){movie.Path=dialog.FileName;movie.Bytes=new FileInfo(dialog.FileName).Length;movie.Available=true;library.Save();Refresh();Status.Text="Download concluído. O filme pode ser assistido offline no Cinevo.";}
    }
}
