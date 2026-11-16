using System.IO;
namespace Cinevo;
public partial class MainWindow
{
    private object ProfilesInfo()=>new{active=library.ActiveViewer,profiles=library.Profiles.Select(p=>new{p.Id,p.Name,avatarVersion=ProfilePhoto.Version(p.Id)})};
    private async Task<object?> ProfileActionAsync(WebAction command)
    {
        if(command.Name=="profile-list")return ProfilesInfo();
        if(command.Name is "profile-photo" or "profile-photo-remove"){
            var id=library.ActiveViewer;
            if(command.Name=="profile-photo-remove")ProfilePhoto.Remove(id);
            else{
                var picker=new Microsoft.Win32.OpenFileDialog{Title="Escolha sua foto de perfil",Filter="Imagens|*.jpg;*.jpeg;*.png;*.bmp",CheckFileExists=true};
                if(picker.ShowDialog()!=true)return ProfilesInfo();
                try{await ProfilePhoto.SaveAsync(id,picker.FileName);}
                catch(Exception ex)when(ex is NotSupportedException or System.IO.FileFormatException or ArgumentException){throw new InvalidDataException("Não foi possível abrir essa foto. Escolha uma imagem JPG ou PNG.");}
            }
            return ProfilesInfo();
        }
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
    private async Task<byte[]?> ReadProfilePhotoAsync(string id){await Dispatcher.InvokeAsync(()=>CheckViewer(id));return await ProfilePhoto.Read(id);}
    private string CheckViewer(string? id){id??=Viewers.Default;if(!library.Profiles.Any(p=>p.Id==id))throw new InvalidDataException("Perfil indisponível.");return id;}
}
