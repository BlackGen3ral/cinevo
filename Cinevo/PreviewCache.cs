namespace Cinevo;
public sealed record PreviewFrame(int Width,int Height,long Milliseconds,byte[] Pixels);
internal sealed class PreviewCache(long budget=8*1024*1024,int capacity=96)
{
 private readonly Dictionary<string,(byte[] Bytes,LinkedListNode<string> Node)> entries=[];
 private readonly LinkedList<string> order=new();
 public long Bytes{get;private set;}public int Count=>entries.Count;
 public byte[]? Get(string key){if(!entries.TryGetValue(key,out var value))return null;order.Remove(value.Node);order.AddLast(value.Node);return value.Bytes;}
 public void Put(string key,byte[] bytes){if(entries.Remove(key,out var previous)){Bytes-=previous.Bytes.Length;order.Remove(previous.Node);}if(bytes.Length>budget)return;while(entries.Count>=capacity||Bytes+bytes.Length>budget){var first=order.First!;Bytes-=entries[first.Value].Bytes.Length;entries.Remove(first.Value);order.RemoveFirst();}entries[key]=(bytes,order.AddLast(key));Bytes+=bytes.Length;}
 public static long Bucket(long milliseconds)=>Math.Max(0,milliseconds/10000*10000);
}
