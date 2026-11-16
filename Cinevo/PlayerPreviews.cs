using System.IO;
using System.Windows.Media.Imaging;
namespace Cinevo;
public partial class PlayerWindow
{
 private CancellationTokenSource? previewRequest;private long previewBucket=-1;
 private readonly System.Windows.Threading.DispatcherTimer previewHide=new(){Interval=TimeSpan.FromSeconds(2)};
 private void SetupPreviews(){Timeline.MouseMove+=(_,e)=>{if(engine==null||engine.Length<=0)return;ShowTimelinePreview((long)(Math.Clamp(e.GetPosition(Timeline).X/Math.Max(1,Timeline.ActualWidth),0,1)*engine.Length),false);};Timeline.MouseLeave+=(_,_)=>HideTimelinePreview();previewHide.Tick+=(_,_)=>HideTimelinePreview();}
 private async void ShowTimelinePreview(long position,bool temporary=true)
 {
  if(!movie.HasLocalFile||closed||teardownStarted)return;var bucket=PreviewCache.Bucket(position);PreviewTime.Text=TimeText(bucket);TimelinePreview.HorizontalOffset=Math.Clamp(Timeline.ActualWidth*position/Math.Max(1,engine?.Length??1)-100,0,Math.Max(0,Timeline.ActualWidth-208));TimelinePreview.IsOpen=true;
  previewHide.Stop();if(temporary)previewHide.Start();if(previewBucket==bucket)return;previewBucket=bucket;previewRequest?.Cancel();var cancel=previewRequest=new();PreviewImage.Source=null;
  try{await Task.Delay(180,cancel.Token);var bytes=await TimelinePreviews.GetAsync(movie.Path,position,cancel.Token);if(cancel.IsCancellationRequested||closed)return;if(bytes==null){PreviewTime.Text=TimeText(bucket)+" · Sem prévia";return;}using var stream=new MemoryStream(bytes);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();PreviewImage.Source=image;}
  catch(OperationCanceledException){}catch(Exception ex)when(ex is IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException){PreviewTime.Text="Prévia indisponível";}
 }
 private void HideTimelinePreview(){TimelinePreview.IsOpen=false;previewHide.Stop();previewRequest?.Cancel();previewRequest=null;previewBucket=-1;}
}
