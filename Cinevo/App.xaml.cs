using System.Threading;
using System.Windows;
namespace Cinevo;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        var testProfile = Environment.GetEnvironmentVariable("CINEVO_TEST_DATA");
        var suffix = string.IsNullOrEmpty(testProfile) ? "" : ".Test." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(testProfile)))[..12];
        instance = new Mutex(true, "Local\\Cinevo.Desktop" + suffix, out var first);
        if (!first) { MessageBox.Show("O Cinevo já está aberto.", "Cinevo"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show("Não foi possível concluir a operação.\n" + args.Exception.Message, "Cinevo"); args.Handled = true; };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
