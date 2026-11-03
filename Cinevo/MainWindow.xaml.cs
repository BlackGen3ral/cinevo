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
    private readonly CoverService covers = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? coverSearch;
    private int columns = 4;
    private bool closed;
    public MainWindow()
    {
        InitializeComponent();
        try { library = Library.Load(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cinevo — biblioteca preservada"); Application.Current.Shutdown(); return; }
        ready = true;
        VersionLabel.Text = $"Cinevo {Updater.CurrentVersion} · Beta";
        Refresh();
        Loaded += async (_, _) => { var update = library.AutoUpdate && Updater.IsInstalled ? CheckUpdate(false) : Task.CompletedTask; if (library.Folders.Count > 0) await ScanAsync(); else if (library.AutoCovers) await FetchCoversAsync(); await update; };
        Closing += OnClosing;
    }
    private void Refresh()
    {
        if (!ready) return;
        var query = Search.Text.Trim();
        IEnumerable<Movie> result = library.Movies.Where(m => (filter switch { "favorites" => m.Favorite, "watched" => m.Watched, "unwatched" => !m.Watched, _ => true }) &&
            (query.Length == 0 || $"{m.Title} {m.Year} {m.Genre} {m.Path}".Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        result = Sort.SelectedIndex switch { 1 => result.OrderByDescending(m => m.Added), 2 => result.OrderByDescending(m => m.Year), 3 => result.OrderByDescending(m => m.Rating), _ => result.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase) };
        var list = result.ToList(); MovieList.ItemsSource = list.Chunk(columns).Select(chunk => new MovieRow(columns, chunk.ToList())).ToList();
        var featured = list.FirstOrDefault(m => m.ResumeMilliseconds > 30000 && !m.Watched) ?? list.FirstOrDefault(m => !m.Watched) ?? list.FirstOrDefault();
        HeroTitle.Text = featured?.Title ?? "Toda boa sessão começa aqui.";
        HeroMeta.Text = featured == null ? "Organize sua coleção e assista sem sair do Cinevo." : featured.Subtitle + (featured.Genre.Length > 0 ? "  ·  " + featured.Genre : "");
        HeroCover.DataContext = featured; HeroPlayButton.DataContext = featured; HeroDetailsButton.DataContext = featured;
        HeroPlayButton.Content = featured?.PlayLabel ?? "▶  Assistir";
        HeroPlayButton.Visibility = HeroDetailsButton.Visibility = featured == null ? Visibility.Collapsed : Visibility.Visible;
        HeroAddButton.Visibility = featured == null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new[] { NavAll, NavUnwatched, NavWatched, NavFavorites }) { var selected = (string)nav.Tag == filter; nav.Background = selected ? Brush("#30253F") : System.Windows.Media.Brushes.Transparent; nav.Foreground = Brush(selected ? "#D7C5FF" : "#9A9DB0"); }
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = library.Movies.Count == 0 ? "Adicione uma pasta. Os pôsteres chegam depois, e os filmes continuam exatamente onde estão." : "Nenhum filme corresponde a esta busca ou filtro.";
        Stats.Text = $"{library.Movies.Count} filmes  ·  {library.Movies.Count(m => !m.Watched)} para assistir  ·  {library.Movies.Count(m => m.Favorite)} favoritos  ·  {library.Movies.Sum(m => m.Bytes) / 1073741824d:0.0} GB";
    }
    private static System.Windows.Media.Brush Brush(string color) => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private void MovieList_SizeChanged(object sender, SizeChangedEventArgs e) { var next = Math.Max(2, (int)(e.NewSize.Width / 190)); if (next != columns) { columns = next; Refresh(); } }
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
        if (library.AutoCovers && !closed) await FetchCoversAsync();
    }
    private void Filter_Click(object sender, RoutedEventArgs e) { filter = (string)((Button)sender).Tag; PageTitle.Text = filter switch { "favorites" => "Seus favoritos", "watched" => "Já vistos, bem lembrados", "unwatched" => "Sua próxima sessão", _ => "Minha biblioteca" }; Refresh(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => Refresh();
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
    private void Play(Movie movie)
    {
        if (!File.Exists(movie.Path)) { MessageBox.Show("O arquivo não está disponível. Conecte o disco ou confira a pasta original.\n\n" + movie.Path, "Cinevo"); return; }
        var player = new PlayerWindow(movie, () => library.Save()) { Owner = this };
        player.ShowDialog(); Refresh();
    }
    private void Play_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is Movie m) Play(m); }
    private void Movie_DoubleClick(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject d && FindButton(d)) return; if (e.OriginalSource is FrameworkElement { DataContext: Movie m }) Play(m); }
    private static bool FindButton(DependencyObject d) { while (d != null) { if (d is Button) return true; d = System.Windows.Media.VisualTreeHelper.GetParent(d); } return false; }
    private void Movie_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && e.OriginalSource is FrameworkElement { DataContext: Movie m } && e.OriginalSource is not Button) { Play(m); e.Handled = true; } }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Movie movie) return;
        var dialog = new MovieDialog(movie, covers) { Owner = this };
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
        var window = new Window { Title = "Configurações do Cinevo", Width = 600, Height = 510, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = $"Cinevo {Updater.CurrentVersion} · Beta", FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
        var auto = new CheckBox { Content = "Baixar atualizações e instalar ao fechar", IsChecked = library.AutoUpdate, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 20) };
        auto.Click += (_, _) => { library.AutoUpdate = auto.IsChecked == true; library.Save(); }; panel.Children.Add(auto);
        var art = new CheckBox { Content = "Buscar capas automaticamente pelo título e ano", IsChecked = library.AutoCovers, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0,0,0,8) };
        art.Click += (_, _) => { library.AutoCovers = art.IsChecked == true; library.Save(); if (!library.AutoCovers) coverSearch?.Cancel(); }; panel.Children.Add(art);
        panel.Children.Add(new TextBlock { Text = "A busca envia somente título e ano à Wikipédia. Seus vídeos,\ncaminhos e anotações ficam neste computador.", Foreground = Brush("#9691A5"), FontSize = 11, Margin = new Thickness(0,0,0,20) });
        var check = new Button { Content = "Verificar atualização agora", Margin = new Thickness(0, 0, 0, 12) }; check.Click += async (_, _) => { window.Close(); await CheckUpdate(true); }; panel.Children.Add(check);
        var backup = new Button { Content = "Exportar cópia da biblioteca", Margin = new Thickness(0, 0, 0, 12) }; backup.Click += (_, _) => { var dialog = new SaveFileDialog { FileName = "Cinevo-biblioteca.json", Filter = "Biblioteca JSON|*.json" }; if (dialog.ShowDialog() == true) { library.Save(); File.Copy(Library.DataPath, dialog.FileName, true); } }; panel.Children.Add(backup);
        var data = new Button { Content = "Abrir pasta de dados" }; data.Click += (_, _) => { Directory.CreateDirectory(Library.DataDirectory); Process.Start(new ProcessStartInfo(Library.DataDirectory) { UseShellExecute = true }); }; panel.Children.Add(data);
        panel.Children.Add(new TextBlock { Text = "Player integrado com LibVLC / VideoLAN. Licenças e código-fonte\ndisponíveis na pasta vlc e na publicação do GitHub.", Foreground = Brush("#9691A5"), FontSize = 11, Margin = new Thickness(0,18,0,0) });
        window.Content = panel; window.ShowDialog();
    }
    private async void Covers_Click(object sender, RoutedEventArgs e) { if (coverSearch != null) coverSearch.Cancel(); else await FetchCoversAsync(true); }
    private async Task FetchCoversAsync(bool retry = false)
    {
        if (coverSearch != null || closed) return;
        var pending = library.Movies.Where(m => (m.Cover.Length == 0 || !File.Exists(m.Cover)) && (retry || DateTime.UtcNow - m.CoverLastAttempt > TimeSpan.FromDays(7))).ToList();
        if (pending.Count == 0) { if (retry) Status.Text = "Todas as capas disponíveis já estão salvas. Para trocar uma, abra os detalhes do filme."; return; }
        coverSearch = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); CoversButton.Content = "Cancelar capas";
        var found = 0; var attempted = 0; var failed = 0;
        try
        {
            foreach (var movie in pending)
            {
                coverSearch.Token.ThrowIfCancellationRequested();
                if (!library.Movies.Contains(movie)) continue;
                Status.Text = $"Buscando capas · {++attempted}/{pending.Count} · {movie.Title}";
                try
                {
                    var matches = await covers.SearchAsync(movie.Title, movie.Year, coverSearch.Token);
                    var best = matches.FirstOrDefault(c => c.Score >= 16);
                    if (best != null)
                    {
                        var path = await covers.DownloadAsync(best, System.IO.Path.Combine(Library.DataDirectory, "covers"), coverSearch.Token);
                        if (movie.Cover.Length == 0 || !File.Exists(movie.Cover)) { movie.Cover = path; movie.CoverSource = best.PageUrl; found++; }
                    }
                    movie.CoverLastAttempt = DateTime.UtcNow; library.Save();
                }
                catch (OperationCanceledException) when (coverSearch.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or System.Text.Json.JsonException or TaskCanceledException) { failed++; }
                await Task.Delay(250, coverSearch.Token);
            }
            Status.Text = $"{found} capas encontradas · {pending.Count - found} sem correspondência ou conexão. Você pode escolher outra capa nos detalhes.";
            if (failed == pending.Count) Status.Text = "Não foi possível acessar as capas agora. Sua coleção continua disponível; tente novamente depois.";
        }
        catch (OperationCanceledException) { if (!closed) Status.Text = "Busca de capas cancelada. As capas já salvas foram preservadas."; }
        finally { coverSearch.Dispose(); coverSearch = null; if (!closed) CoversButton.Content = "↻  Buscar capas"; }
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
        closed = true; lifetime.Cancel();
        if (updateInstaller != null && (library.AutoUpdate || installRequested))
        {
            try { Process.Start(new ProcessStartInfo(updateInstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("A atualização não pôde iniciar: " + ex.Message, "Cinevo"); }
        }
    }
}
