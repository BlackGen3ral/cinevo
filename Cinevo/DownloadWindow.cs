using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Cinevo;
public sealed class DownloadWindow : Window
{
    public bool Completed { get; private set; }
    public DownloadWindow(string url,string destination)
    {
        Title="Baixar no PC — Cinevo";Width=560;Height=255;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(16,18,24));Foreground=Brushes.White;
        var cancel=new CancellationTokenSource();var running=true;var panel=new StackPanel{Margin=new Thickness(25)};var label=new TextBlock{Text="Conectando à origem…",Margin=new Thickness(0,0,0,12)};panel.Children.Add(label);var bar=new ProgressBar{Height=9,IsIndeterminate=true,Maximum=100};panel.Children.Add(bar);panel.Children.Add(new TextBlock{Text=System.IO.Path.GetFileName(destination),Foreground=Brushes.Gray,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,12,0,14)});var stop=new Button{Content="Cancelar download",HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(stop);Content=panel;
        stop.Click+=(_,_)=>{if(running){cancel.Cancel();stop.IsEnabled=false;}else Close();};
        Closing+=(_,e)=>{if(running){e.Cancel=true;cancel.Cancel();label.Text="Cancelando download…";}};
        Loaded+=async(_,_)=>
        {
            try{await RemoteFiles.DownloadAsync(url,destination,new Progress<(long Received,long? Total)>(p=>{bar.IsIndeterminate=p.Total==null;if(p.Total>0)bar.Value=p.Received*100d/p.Total.Value;label.Text=p.Total>0?$"Baixando · {p.Received/1048576d:0.0} de {p.Total/1048576d:0.0} MB":$"Baixando · {p.Received/1048576d:0.0} MB";}),cancel.Token);Completed=true;running=false;DialogResult=true;}
            catch(OperationCanceledException){running=false;label.Text="Download cancelado ou conexão interrompida. O arquivo parcial foi removido.";stop.Content="Fechar";stop.IsEnabled=true;}
            catch(Exception ex){running=false;label.Text="Não foi possível baixar: "+ex.Message;label.TextWrapping=TextWrapping.Wrap;stop.Content="Fechar";stop.IsEnabled=true;}
            finally{running=false;cancel.Dispose();}
        };
    }
}
