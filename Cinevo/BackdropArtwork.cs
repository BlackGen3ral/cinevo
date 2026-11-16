using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
namespace Cinevo;
internal static class BackdropArtwork
{
    private static readonly HttpClient Images=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)};
    private static readonly SemaphoreSlim Gate=new(1);
    private static readonly Dictionary<string,byte[]> Cache=[];
    internal static bool Valid(string key)=>Regex.IsMatch(key,@"\A/[A-Za-z0-9_-]+\.(?:jpg|png)\z");
    public static async Task<byte[]?> FetchAsync(string key,CancellationToken token)
    {
        if(!Valid(key))return null;
        await Gate.WaitAsync(token);
        try{
            if(Cache.TryGetValue(key,out var saved))return saved;
            using var response=await Images.GetAsync("https://image.tmdb.org/t/p/w1280"+key,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();var bytes=await RemoteFiles.ReadBoundedAsync(response,6*1024*1024,token);
            if(!RemoteFiles.IsImage(bytes))return null;
            if(Cache.Count>=4)Cache.Remove(Cache.Keys.First());Cache[key]=bytes;return bytes;
        }catch(Exception ex) when(ex is IOException or HttpRequestException or OperationCanceledException){return null;}
        finally{Gate.Release();}
    }
}
