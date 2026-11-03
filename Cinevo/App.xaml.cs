using System.Threading;
using System.Windows;
namespace Cinevo;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(true, "Local\\Cinevo.Desktop", out var first);
        if (!first) { MessageBox.Show("O Cinevo já está aberto.", "Cinevo"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show("Não foi possível concluir a operação.\n" + args.Exception.Message, "Cinevo"); args.Handled = true; };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
