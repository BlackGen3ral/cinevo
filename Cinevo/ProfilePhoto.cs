using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Cinevo;
internal static class ProfilePhoto
{
    private static string FileName(string id)=>Path.Combine(Library.DataDirectory,"avatars",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))+".jpg");
    public static long Version(string id){var path=FileName(id);return File.Exists(path)?File.GetLastWriteTimeUtc(path).Ticks:0;}
    public static async Task<byte[]?> Read(string id){var path=FileName(id);return File.Exists(path)?await File.ReadAllBytesAsync(path):null;}
    public static Task SaveAsync(string id,string source){var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var worker=new Thread(()=>{try{Save(id,source);done.SetResult();}catch(Exception ex){done.SetException(ex);}}){IsBackground=true};worker.SetApartmentState(ApartmentState.STA);worker.Start();return done.Task;}
    public static void Save(string id,string source)
    {
        if(new FileInfo(source).Length>20*1024*1024)throw new InvalidDataException("Escolha uma foto de até 20 MB.");
        using var stream=File.OpenRead(source);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelWidth<1||image.PixelHeight<1||image.PixelHeight>8192)throw new InvalidDataException("Escolha uma imagem com proporções normais.");
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(38,32,53)),null,new Rect(0,0,256,256));var scale=Math.Max(256d/image.PixelWidth,256d/image.PixelHeight);var w=image.PixelWidth*scale;var h=image.PixelHeight*scale;drawing.DrawImage(image,new Rect((256-w)/2,(256-h)/2,w,h));}
        var resized=new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32);resized.Render(visual);resized.Freeze();
        var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(resized));var path=FileName(id);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+".tmp";
        try{using(var file=File.Create(temporary))encoder.Save(file);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Remove(string id){var path=FileName(id);if(File.Exists(path))File.Delete(path);}
}
