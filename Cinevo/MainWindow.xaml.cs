using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
namespace Cinevo;
public partial class MainWindow : Window
{
    private Library library = new();
    private string filter = "all";
    private CancellationTokenSource? scan;
    private bool ready;
    private string? updateInstaller;
    private bool installRequested;
    public MainWindow()
    {
        InitializeComponent();
        try { library = Library.Load(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cinevo — biblioteca preservada"); Application.Current.Shutdown(); return; }
        ready = true;
        VersionLabel.Text = $"Cinevo {Updater.CurrentVersion} · Windows";
        Refresh();
        Loaded += async (_, _) => { if (library.Folders.Count > 0) await ScanAsync(); if (library.AutoUpdate && Updater.IsInstalled) await CheckUpdate(false); };
        Closing += OnClosing;
    }
    private void Refresh()
    {
        if (!ready) return;
        var query = Search.Text.Trim();
        IEnumerable<Movie> result = library.Movies.Where(m => (filter switch { "favorites" => m.Favorite, "watched" => m.Watched, "unwatched" => !m.Watched, _ => true }) &&
            (query.Length == 0 || $"{m.Title} {m.Year} {m.Genre} {m.Path}".Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        result = Sort.SelectedIndex switch { 1 => result.OrderByDescending(m => m.Added), 2 => result.OrderByDescending(m => m.Year), 3 => result.OrderByDescending(m => m.Rating), _ => result.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase) };
        var list = result.ToList(); MovieList.ItemsSource = list;
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = library.Movies.Count == 0 ? "Adicione a pasta onde guarda seus filmes. O Cinevo organiza a coleção sem mover nenhum arquivo." : "Nenhum filme corresponde a esta busca ou filtro.";
        Stats.Text = $"{library.Movies.Count} filmes  ·  {library.Movies.Count(m => !m.Watched)} para assistir  ·  {library.Movies.Count(m => m.Favorite)} favoritos  ·  {library.Movies.Sum(m => m.Bytes) / 1073741824d:0.0} GB";
    }
    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (scan != null) return;
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta dos seus filmes", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var folder in dialog.FolderNames) if (!library.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) library.Folders.Add(folder);
        library.Save(); await ScanAsync();
    }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => scan?.Cancel();
    private async Task ScanAsync()
    {
        if (scan != null) return;
        scan = new CancellationTokenSource(); ScanButton.IsEnabled = false; CancelButton.Visibility = Visibility.Visible; Status.Text = "Buscando filmes nas suas pastas…";
        try
        {
            var result = await Task.Run(() => Scanner.Scan(library.Folders.ToArray(), scan.Token));
            var existing = library.Movies.ToDictionary(m => m.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var movie in result.Movies)
                if (existing.TryGetValue(movie.Path, out var saved)) { saved.Bytes = movie.Bytes; saved.Available = true; } else { library.Movies.Add(movie); existing[movie.Path] = movie; }
            var found = result.Movies.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var movie in library.Movies) movie.Available = found.Contains(movie.Path) || File.Exists(movie.Path);
            library.Save(); Refresh();
            Status.Text = result.Errors.Count == 0 ? $"Biblioteca atualizada · {result.Movies.Count} arquivos encontrados." : $"{result.Movies.Count} arquivos encontrados · {result.Errors.Count} locais não puderam ser lidos. Registros anteriores preservados.";
        }
        catch (OperationCanceledException) { Status.Text = "Busca cancelada. Sua biblioteca foi preservada."; }
        catch (Exception ex) { Status.Text = "Falha na busca: " + ex.Message; }
        finally { scan.Dispose(); scan = null; ScanButton.IsEnabled = true; CancelButton.Visibility = Visibility.Collapsed; }
    }
    private void Filter_Click(object sender, RoutedEventArgs e) { filter = (string)((Button)sender).Tag; PageTitle.Text = filter switch { "favorites" => "Seus favoritos", "watched" => "Já vistos, bem lembrados", "unwatched" => "Sua próxima sessão", _ => "Sua coleção" }; Refresh(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => Refresh();
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
    private void Play(Movie movie)
    {
        if (!File.Exists(movie.Path)) { MessageBox.Show("O arquivo não está disponível. Conecte o disco ou confira a pasta original.\n\n" + movie.Path, "Cinevo"); return; }
        try { Process.Start(new ProcessStartInfo(movie.Path) { UseShellExecute = true }); Status.Text = "Abrindo no player padrão: " + movie.Title; }
        catch (Exception ex) { MessageBox.Show("Escolha um player padrão para esse formato nas configurações do Windows.\n" + ex.Message, "Cinevo"); }
    }
    private void Play_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is Movie m) Play(m); }
    private void Movie_DoubleClick(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject d && FindButton(d)) return; if (MovieList.SelectedItem is Movie m) Play(m); }
    private static bool FindButton(DependencyObject d) { while (d != null) { if (d is Button) return true; d = System.Windows.Media.VisualTreeHelper.GetParent(d); } return false; }
    private void Movie_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && MovieList.SelectedItem is Movie m) { Play(m); e.Handled = true; } }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Movie movie) return;
        var dialog = new MovieDialog(movie) { Owner = this };
        if (dialog.ShowDialog() == true) { library.Save(); Refresh(); }
    }
    private void Folders_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window { Title = "Pastas da biblioteca", Width = 650, Height = 380, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Remover uma pasta retira seus registros do catálogo.\nOs vídeos no disco são preservados.", Margin = new Thickness(0, 0, 0, 18) });
        var list = new ListBox { ItemsSource = library.Folders.ToArray(), Height = 180 }; panel.Children.Add(list);
        var remove = new Button { Content = "Remover pasta selecionada", Margin = new Thickness(0, 16, 0, 0), IsEnabled = scan == null };
        remove.Click += (_, _) => { if (list.SelectedItem is not string path) return; if (MessageBox.Show("Remover esta pasta e os registros dos filmes dela do catálogo?\n" + path, "Cinevo", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            library.Folders.Remove(path);
            library.Movies.RemoveAll(m => !library.Folders.Any(root => IsInside(m.Path, root)));
            library.Save(); list.ItemsSource = library.Folders.ToArray(); Refresh(); };
        panel.Children.Add(remove); window.Content = panel; window.ShowDialog();
    }
    private static bool IsInside(string path, string root) => path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window { Title = "Configurações do Cinevo", Width = 560, Height = 360, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "Cinevo " + Updater.CurrentVersion, FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
        var auto = new CheckBox { Content = "Baixar atualizações e instalar ao fechar", IsChecked = library.AutoUpdate, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 20) };
        auto.Click += (_, _) => { library.AutoUpdate = auto.IsChecked == true; library.Save(); }; panel.Children.Add(auto);
        var check = new Button { Content = "Verificar atualização agora", Margin = new Thickness(0, 0, 0, 12) }; check.Click += async (_, _) => { window.Close(); await CheckUpdate(true); }; panel.Children.Add(check);
        var backup = new Button { Content = "Exportar cópia da biblioteca", Margin = new Thickness(0, 0, 0, 12) }; backup.Click += (_, _) => { var dialog = new SaveFileDialog { FileName = "Cinevo-biblioteca.json", Filter = "Biblioteca JSON|*.json" }; if (dialog.ShowDialog() == true) { library.Save(); File.Copy(Library.DataPath, dialog.FileName, true); } }; panel.Children.Add(backup);
        var data = new Button { Content = "Abrir pasta de dados" }; data.Click += (_, _) => { Directory.CreateDirectory(Library.DataDirectory); Process.Start(new ProcessStartInfo(Library.DataDirectory) { UseShellExecute = true }); }; panel.Children.Add(data);
        window.Content = panel; window.ShowDialog();
    }
    private bool checkingUpdate;
    private async Task CheckUpdate(bool manual)
    {
        if (checkingUpdate) return; checkingUpdate = true;
        try { if (manual) Status.Text = "Verificando atualizações…"; updateInstaller = await Updater.DownloadAsync(); if (updateInstaller != null) { UpdateButton.Visibility = Visibility.Visible; Status.Text = "Atualização pronta. Será instalada ao fechar o Cinevo."; } else if (manual) Status.Text = "Você está usando a versão mais recente."; }
        catch (Exception ex) { if (manual) Status.Text = "Não foi possível verificar a atualização: " + ex.Message; }
        finally { checkingUpdate = false; }
    }
    private void Update_Click(object sender, RoutedEventArgs e) { if (updateInstaller != null) { installRequested = true; Close(); } }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        scan?.Cancel();
        if (updateInstaller != null && (library.AutoUpdate || installRequested))
        {
            try { Process.Start(new ProcessStartInfo(updateInstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("A atualização não pôde iniciar: " + ex.Message, "Cinevo"); }
        }
    }
}
