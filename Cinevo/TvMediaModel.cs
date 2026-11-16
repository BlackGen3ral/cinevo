namespace Cinevo;
internal sealed class TvDownloadTickets
{
 internal sealed record Ticket(string Session,string Movie,string Url,string Kind,string Label,DateTime Expires);
 private readonly Dictionary<string,Ticket> entries=[];
 internal string Add(string session,string movie,string url,string kind,string label)=>Add(session,movie,url,kind,label,DateTime.UtcNow);
 internal Ticket? Get(string id,string session,string movie)=>Get(id,session,movie,DateTime.UtcNow);
 internal string Add(string session,string movie,string url,string kind,string label,DateTime now){foreach(var old in entries.Where(p=>p.Value.Expires<=now).Select(p=>p.Key).ToArray())entries.Remove(old);if(entries.Count>=256)entries.Remove(entries.Keys.First());var id=Guid.NewGuid().ToString("N");entries[id]=new(session,movie,url,kind,label,now.AddMinutes(10));return id;}
 internal Ticket? Get(string id,string session,string movie,DateTime now)=>entries.TryGetValue(id,out var value)&&value.Session==session&&value.Movie==movie&&value.Expires>now?value:null;
}
internal static class MediaFailure
{
 internal static string Explain(int code)=>code switch{1=>"A reprodução foi interrompida na TV.",2=>"A TV perdeu acesso ao vídeo. Confira a rede, o PC e a conexão da origem.",3=>"O navegador da TV não conseguiu decodificar o vídeo ou o áudio deste arquivo.",4=>"O formato ou a origem não foi aceito pelo navegador da TV.",_=>"A reprodução ainda não informou um erro. Se travar, confira a rede e tente novamente."};
 internal static bool Online(DateTime last,DateTime now)=>(now-last).TotalSeconds<=45;
}
