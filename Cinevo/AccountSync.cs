using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
namespace Cinevo;
public partial class MainWindow
{
    private const string AccountEndpoint="https://cinevo-portal.arthurgabrielfera.workers.dev";
    private AccountState? account;
    private string accountStatus="Entre para sincronizar seus dados.";
    private bool accountBusy;
    private readonly System.Windows.Threading.DispatcherTimer accountTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private static readonly JsonSerializerOptions AccountJson=new(JsonSerializerDefaults.Web);
    private void StartAccountSync()
    {
        try {account=AccountState.Load();}catch(Exception ex) when(ex is IOException or System.Security.Cryptography.CryptographicException or JsonException){accountStatus="Não foi possível ler sua conexão. Entre novamente; o catálogo foi preservado.";}
        accountTimer.Tick+=async(_,_)=>{if(closed){accountTimer.Stop();return;}await SyncAccountAsync();};accountTimer.Start();_ = SyncAccountAsync();
    }
    private async Task<JsonDocument> AccountRequestAsync(string route,object? body=null,string? token=null)
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
        using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,AccountEndpoint+route);
        if(token!=null)request.Headers.Authorization=new("Bearer",token);
        if(body!=null)request.Content=JsonContent.Create(body,options:AccountJson);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,lifetime.Token);
        var bytes=await RemoteFiles.ReadBoundedAsync(response,1024*1024,lifetime.Token);
        var json=JsonDocument.Parse(bytes);
        if(!response.IsSuccessStatusCode){var message=json.RootElement.TryGetProperty("error",out var error)?error.GetString():"A sincronização falhou.";json.Dispose();throw new InvalidDataException(message);}
        return json;
    }
    private async Task SyncAccountAsync()
    {
        if(account==null||accountBusy||closed)return;
        accountBusy=true;var active=account;
        try {
            var remote=new Dictionary<string,CloudRow>();string after="";
            do {using var page=await AccountRequestAsync("/api/account/titles?after="+after,token:active.Token);
                foreach(var item in page.RootElement.GetProperty("items").EnumerateArray()){var row=item.Deserialize<CloudRow>(AccountJson)!;remote[row.Id]=row;}
                after=page.RootElement.GetProperty("next").GetString()??"";
            }while(after.Length>0&&remote.Count<50000);
            if(account!=active)return;
            var local=library.Movies.Where(m=>!m.ImportedOnly||m.CloudId.Length>0).ToList();
            foreach(var m in local)if(m.CloudId.Length==0)m.CloudId=SyncTitle.Identity(m);
            var map=local.GroupBy(m=>m.CloudId).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var row in remote.Values)if(!map.ContainsKey(row.Id)){
                var placeholder=new Movie{CloudId=row.Id,Title=row.Data.Title,ImportedOnly=true,Available=false};
                SyncTitle.Apply(placeholder,row.Data,SyncTitle.From(placeholder));library.Movies.Add(placeholder);map[row.Id]=placeholder;
            }
            foreach(var id in active.Baseline.Keys.ToArray())if(!map.ContainsKey(id)){
                var missing=active.Baseline[id] with{Member=false};var placeholder=new Movie{CloudId=id,Title=missing.Title,ImportedOnly=true,Available=false};
                SyncTitle.Apply(placeholder,missing,SyncTitle.From(placeholder));library.Movies.Add(placeholder);map[id]=placeholder;
            }
            var changes=new List<(Movie Movie,SyncTitle Original,SyncTitle Merged,int Version)>();
            foreach(var (id,movie) in map){var original=SyncTitle.From(movie);active.Baseline.TryGetValue(id,out var baseline);remote.TryGetValue(id,out var row);
                var merged=SyncTitle.Merge(original,baseline,row?.Data);
                if(row!=null&&merged==row.Data){SyncTitle.Apply(movie,merged,original);active.Baseline[id]=merged;}
                else changes.Add((movie,original,merged,row?.Version??0));
            }
            library.Save();
            foreach(var batch in changes.Chunk(20)){
                using var reply=await AccountRequestAsync("/api/account/titles",new{items=batch.Select(c=>new{id=c.Movie.CloudId,data=c.Merged,version=c.Version})},active.Token);
                if(account!=active)return;
                foreach(var result in reply.RootElement.GetProperty("items").EnumerateArray()){
                    if(result.GetProperty("conflict").GetBoolean())continue;
                    var id=result.GetProperty("id").GetString()!;var change=batch.First(c=>c.Movie.CloudId==id);
                    SyncTitle.Apply(change.Movie,change.Merged,change.Original);active.Baseline[id]=change.Merged;
                }
            }
            library.Save();active.Save();accountStatus="Sincronizado às "+DateTime.Now.ToString("HH:mm");Refresh();
        }catch(Exception ex) when(ex is IOException or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException or JsonException){accountStatus=ex is InvalidDataException?ex.Message:"Sem conexão. Suas alterações locais aguardam sincronização.";}
        finally{accountBusy=false;}
    }
    private async Task<object?> AccountActionAsync(WebAction command)
    {
        if(command.Name=="account-read")return new{username=account?.Username,status=accountStatus,busy=accountBusy};
        if(command.Name=="account-sync"){await SyncAccountAsync();return new{status=accountStatus};}
        if(accountBusy)throw new InvalidDataException("Aguarde a sincronização atual terminar.");
        if(command.Name=="account-logout"){
            if(account!=null)using(await AccountRequestAsync("/api/account/logout",new{},account.Token)){}
            account=null;AccountState.Remove();accountStatus="Conta desconectada. A biblioteca local foi preservada.";return null;
        }
        if(command.Name is not ("account-login" or "account-register"))throw new InvalidDataException("Operação inválida.");
        using var data=JsonDocument.Parse(command.Address??"{}");
        var username=data.RootElement.GetProperty("username").GetString();var password=data.RootElement.GetProperty("password").GetString();
        using var reply=await AccountRequestAsync(command.Name=="account-login"?"/api/account/login":"/api/account/register",new{username,password});
        account=new(){Username=reply.RootElement.GetProperty("username").GetString()!,Token=reply.RootElement.GetProperty("token").GetString()!};account.Save();
        await SyncAccountAsync();return new{username=account.Username,status=accountStatus};
    }
}
