using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private object ProfilesInfo()=>new{active=library.ActiveViewer,profiles=library.Profiles};
    private async Task<object?> ProfileActionAsync(WebAction command)
    {
        if(command.Name=="profile-list")return ProfilesInfo();
        if(accountBusy||OwnedWindows.OfType<PlayerWindow>().Any())throw new InvalidDataException("Aguarde a sincronização ou feche o player antes de trocar o perfil do PC.");
        if(command.Name=="profile-add"){
            var name=command.Address?.Trim()??"";if(name.Length is <1 or >24||library.Profiles.Count>=8)throw new InvalidDataException("Use até 24 caracteres. São permitidos 8 perfis.");
            var added=new ViewerProfile(Guid.NewGuid().ToString("N"),name);library.Profiles.Add(added);
            try{library.Save();}catch{library.Profiles.Remove(added);throw;}Refresh();return ProfilesInfo();
        }
        if(command.Name=="profile-switch"){
            var previous=library.ActiveViewer;Viewers.Switch(library,command.Id??"");
            try{library.Save();}catch{Viewers.Switch(library,previous);throw;}Refresh();return ProfilesInfo();
        }
        throw new InvalidDataException("Perfil inválido.");
    }
    private string CheckViewer(string? id){id??=Viewers.Default;if(!library.Profiles.Any(p=>p.Id==id))throw new InvalidDataException("Perfil indisponível.");return id;}
}
