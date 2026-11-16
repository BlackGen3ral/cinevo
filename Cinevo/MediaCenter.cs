using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private readonly System.Windows.Threading.DispatcherTimer mediaTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly Dictionary<string,long> mediaChecked=[];
    private bool mediaBusy;
    private string mediaStatus="Vídeos concluídos aparecem automaticamente na biblioteca e na TV.";
    private int mediaCursor;
    private void StartMediaCenter(){mediaTimer.Tick+=async(_,_)=>{if(closed){mediaTimer.Stop();return;}await IndexDownloadsAsync();};mediaTimer.Start();_ = IndexDownloadsAsync();}
    private async Task IndexDownloadsAsync()
    {
        if(mediaBusy||closed||!library.AutoImportDownloads||TorrentConnection.Load()==null)return;
        mediaBusy=true;
        try{
            var all=(await GetTorrentClient().TransfersAsync(lifetime.Token)).Where(t=>TorrentClient.IsHash(t.Hash)&&t.Completed>0&&(!mediaChecked.TryGetValue(t.Hash,out var size)||size!=t.Completed)).ToArray();
            if(all.Length==0)return;
            var added=new List<Movie>();
            foreach(var torrent in all.Skip(mediaCursor%all.Length).Concat(all.Take(mediaCursor%all.Length)).Take(8)){
                var files=await GetTorrentClient().FilesAsync(torrent.Hash,lifetime.Token);
                var known=library.Movies.Concat(added).Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                known.UnionWith(library.RemovedPaths);
                var discovered=await Task.Run(()=>{
                    var result=new List<Movie>();
                    foreach(var file in files.Where(f=>f.Progress>=1&&f.Priority>0)){
                        var path=VideoPath(torrent,file);if(path==null||!known.Add(path))continue;
                        var (title,year)=Scanner.Parse(path);result.Add(new Movie{Title=title,Year=year,Path=path,Bytes=file.Size});
                    }
                    return result;
                },lifetime.Token);
                added.AddRange(discovered);
                mediaChecked[torrent.Hash]=torrent.Completed;
            }
            mediaCursor+=8;
            if(added.Count>0){library.Movies.AddRange(added);try{library.Save();}catch{foreach(var item in added)library.Movies.Remove(item);mediaChecked.Clear();throw;}Refresh();if(library.AutoCovers)_=FetchCoversAsync();}
            mediaStatus=added.Count>0?$"{added.Count} vídeos concluídos adicionados à biblioteca e disponíveis na central.":"Vídeos concluídos verificados. A central está atualizada.";
        }catch(Exception ex) when(ex is IOException or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException){mediaStatus="Não foi possível consultar os downloads. Confira a conexão com o qBittorrent.";}
        finally{mediaBusy=false;}
    }
    private async Task<object?> MediaActionAsync(WebAction command)
    {
        if(command.Name=="media-auto"){var old=library.AutoImportDownloads;library.AutoImportDownloads=!old;try{library.Save();}catch{library.AutoImportDownloads=old;throw;}mediaChecked.Clear();}
        if(command.Name=="media-scan"){mediaChecked.Clear();await IndexDownloadsAsync();}
        if(command.Name=="media-folder"){
            string? folder=null;RunWebDialog(()=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="Pasta padrão dos downloads do Cinevo"};if(picker.ShowDialog()==true)folder=picker.FolderName;});
            if(folder!=null){var old=library.MediaFolder;library.MediaFolder=folder;try{library.Save();}catch{library.MediaFolder=old;throw;}}
        }
        var paths=library.Movies.Where(m=>!m.ImportedOnly).Select(m=>m.Path).ToArray();var ready=await Task.Run(()=>paths.Count(p=>p.Length>0&&File.Exists(p)),lifetime.Token);
        return new{ready,folder=library.MediaFolder,status=mediaStatus,automatic=library.AutoImportDownloads,tv=tv!=null};
    }
}
