using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Cinevo;
internal static class TimelinePreviews
{
 private static readonly PreviewCache cache=new();private static readonly SemaphoreSlim slot=new(1);
 internal static long CachedBytes{get{lock(cache)return cache.Bytes;}}
 public static async Task<byte[]?> GetAsync(string path,long position,CancellationToken token)
 {
  if(!File.Exists(path)||position<0)return null;
  var file=new FileInfo(path);var bucket=PreviewCache.Bucket(position);var key=$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{bucket}";
  lock(cache){if(cache.Get(key) is {} bytes)return bytes;}
  // No queue of obsolete hover requests or additional preview processes.
  await slot.WaitAsync(token);
  try{
   lock(cache){if(cache.Get(key) is {} bytes)return bytes;}
   var jpeg=await Task.Run(()=>{var frame=VlcEngine.ReadPreview(Path.Combine(AppContext.BaseDirectory,"vlc"),path,bucket,token);if(frame==null)return null;var image=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgr32,null,frame.Pixels,frame.Width*4);var encoder=new JpegBitmapEncoder{QualityLevel=65};encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();},token);
   if(jpeg!=null)lock(cache)cache.Put(key,jpeg);return jpeg;
  }catch(Exception ex)when(ex is IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException){return null;}
  finally{slot.Release();}
 }
}
