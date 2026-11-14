using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Cinevo;
public sealed class MovieDialog : Window
{
    public MovieDialog(Movie movie, CoverService covers, Action<Movie,Window,string>? download = null, Action<Movie>? addImported = null)
    {
        Title = "Detalhes do filme"; Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16,18,24)); Foreground = System.Windows.Media.Brushes.White; Width = 620; Height = 820; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(28) };
        if (movie.ImportedOnly && addImported != null)
        {
            var add = new Button { Content = "Adicionar à biblioteca", Margin = new Thickness(0, 0, 0, 12) };
            add.Click += (_, _) => { addImported(movie); add.Content = "Na biblioteca"; add.IsEnabled = false; };
            stack.Children.Add(add);
        }
        if(movie.IsOnline)
        {
            if(movie.Description.Length>0)stack.Children.Add(new TextBlock{Text=movie.Description,TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.LightGray,Margin=new Thickness(0,0,0,15)});
            if(RemoteFiles.IsPublicHttps(movie.SourceUrl)){var origin=new Button{Content="Abrir página original ↗",Margin=new Thickness(0,0,0,12)};origin.Click+=(_,_)=>Process.Start(new ProcessStartInfo(movie.SourceUrl){UseShellExecute=true});stack.Children.Add(origin);}
            AddDownloads(stack, movie, download);
        }
        TextBox Field(string label, string text) { stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 6) }); var input = new TextBox { Text = text }; stack.Children.Add(input); return input; }
        var title = Field("Título", movie.Title); var year = Field("Ano", movie.Year); var genre = Field("Gênero", movie.Genre);
        var synopsis = Field("Sinopse personalizada", movie.CustomSynopsis);
        synopsis.MaxLength = 3000;
        synopsis.Height = 74;
        synopsis.AcceptsReturn = true;
        synopsis.TextWrapping = TextWrapping.Wrap;
        var trailer = Field("Link do trailer no YouTube", movie.TrailerUrl);
        trailer.MaxLength = 512;
        var trailerHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Salmon };
        stack.Children.Add(trailerHint);
        var tmdb = Field("Link da ficha no TMDB (opcional)", movie.TmdbUrl);
        tmdb.MaxLength = 512;
        var notes = Field("Suas anotações", movie.Notes); notes.AcceptsReturn = true; notes.Height = 80; notes.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(new TextBlock { Text = "Avaliação", Margin = new Thickness(0, 12, 0, 6) });
        var rating = new ComboBox { ItemsSource = new[] { "Sem avaliação", "★", "★★", "★★★", "★★★★", "★★★★★" }, SelectedIndex = Math.Clamp(movie.Rating, 0, 5) }; stack.Children.Add(rating);
        var watched = new CheckBox { Content = "Já assisti", IsChecked = movie.Watched, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 16, 0, 8) }; stack.Children.Add(watched);
        var favorite = new CheckBox { Content = "Adicionar aos favoritos", IsChecked = movie.Favorite, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 14) }; stack.Children.Add(favorite);
        var coverPath = movie.Cover;
        var coverSource = movie.CoverSource;
        var lookup = new Button { Content = "Buscar / corrigir capa pelo nome", Margin = new Thickness(0,0,0,12) };
        lookup.Click += (_, _) => { var draft = new Movie { Title = title.Text.Trim(), Year = year.Text.Trim() }; var picker = new CoverPicker(draft, covers) { Owner = this }; if (picker.ShowDialog() == true) { coverPath = picker.SelectedPath; coverSource = picker.SelectedSource; lookup.Content = "Nova capa selecionada — clique em Salvar"; } }; stack.Children.Add(lookup);
        var cover = new Button { Content = "Escolher capa do computador", Margin = new Thickness(0, 0, 0, 12) };
        cover.Click += (_, _) => { var d = new OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp" }; if (d.ShowDialog() == true) { coverPath = d.FileName; coverSource = ""; cover.Content = "Capa selecionada"; } }; stack.Children.Add(cover);
        if (Uri.TryCreate(movie.CoverSource, UriKind.Absolute, out var source) && source.Scheme == "https" && source.Host.EndsWith(".wikipedia.org", StringComparison.OrdinalIgnoreCase)) { var credit = new Button { Content = "Ver origem da capa na Wikipédia", Margin = new Thickness(0,0,0,12) }; credit.Click += (_, _) => Process.Start(new ProcessStartInfo(source.AbsoluteUri) { UseShellExecute = true }); stack.Children.Add(credit); }
        stack.Children.Add(new TextBlock { Text = movie.Path, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "Salvar", Background = System.Windows.Media.Brushes.MediumPurple, IsDefault = true };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(title.Text)) { title.Focus(); return; }
            var video = MovieTrailer.FromUrl(trailer.Text);
            var tmdbRoute = TmdbClient.RouteFromUrl(tmdb.Text);
            if (tmdb.Text.Trim().Length > 0 && tmdbRoute == null)
            {
                trailerHint.Text = "Cole o endereço da página de um filme, série ou temporada do TMDB.";
                tmdb.Focus();
                return;
            }
            if (trailer.Text.Trim().Length > 0 && video == null)
            {
                trailerHint.Text = "Cole o endereço do vídeo no YouTube ou deixe o campo vazio para buscar automaticamente.";
                trailer.Focus();
                return;
            }
            movie.TrailerUrl = video == null ? "" : "https://www.youtube.com/watch?v=" + video.VideoId;
            movie.TmdbUrl = tmdbRoute == null ? "" : "https://www.themoviedb.org/" + tmdbRoute;
            movie.CustomSynopsis = synopsis.Text.Trim();
            movie.Title = title.Text.Trim(); movie.Year = year.Text.Trim(); movie.Genre = genre.Text.Trim(); movie.Notes = notes.Text; movie.Rating = rating.SelectedIndex; movie.Watched = watched.IsChecked == true; movie.Favorite = favorite.IsChecked == true;
            if (coverPath != movie.Cover && File.Exists(coverPath)) { var dir = System.IO.Path.Combine(Library.DataDirectory, "covers"); Directory.CreateDirectory(dir); var dest = System.IO.Path.Combine(dir, Guid.NewGuid() + System.IO.Path.GetExtension(coverPath)); File.Copy(coverPath, dest); movie.Cover = dest; }
            movie.CoverSource = coverSource;
            DialogResult = true; }; buttons.Children.Add(save);
        var reveal = new Button { Content = "Mostrar no Explorer" }; reveal.Click += (_, _) => { if (File.Exists(movie.Path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + movie.Path + "\"") { UseShellExecute = true }); }; buttons.Children.Add(reveal);
        var cancel = new Button { Content = "Cancelar", IsCancel = true }; buttons.Children.Add(cancel); stack.Children.Add(buttons);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void AddDownloads(StackPanel stack, Movie movie, Action<Movie, Window, string>? download)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 18) };
        panel.Children.Add(new TextBlock { Text = "Baixar este conteúdo", FontWeight = FontWeights.SemiBold, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) });
        var status = new TextBlock { Text = "Consultando opções da origem…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(status);
        var buttons = new StackPanel();
        panel.Children.Add(buttons);
        stack.Children.Add(panel);

        void ShowOption(PortalDownload option)
        {
            var torrent = option.Kind == "torrent" && option.Url.Length <= 4096 &&
                System.Text.RegularExpressions.Regex.IsMatch(option.Url, @"\Amagnet:\?xt=urn:btih:(?:[a-f0-9]{40}|[a-z2-7]{32})(?:&|\z)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                !option.Url.Any(char.IsControl);
            if (!torrent && !RemoteFiles.IsPublicHttps(option.Url)) return;
            var suffix = torrent ? " · Abrir no cliente torrent" : option.Kind == "file" ? "" : " ↗";
            var button = new Button { Content = option.Label + suffix, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8), Background = System.Windows.Media.Brushes.MediumPurple };
            button.Click += (_, _) =>
            {
                try
                {
                    if (option.Kind == "file") download?.Invoke(movie, this, option.Url);
                    else Process.Start(new ProcessStartInfo(option.Url) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    status.Text = torrent ? "Configure um cliente torrent no Windows para abrir links magnet." : "O Windows não conseguiu abrir o link.";
                }
            };
            buttons.Children.Add(button);
        }

        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        Loaded += async (_, _) =>
        {
            var options = new List<PortalDownload>();
            var unavailable = false;
            try { options = await PortalService.GetDownloadsAsync(movie.PortalId, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
            { unavailable = true; }
            if (token.IsCancellationRequested) return;
            if (movie.DownloadUrl.Length > 0 && !options.Any(option => option.Url == movie.DownloadUrl))
                options.Insert(0, new PortalDownload(movie.DownloadUrl, "Baixar no PC", "file"));
            foreach (var option in options) ShowOption(option);
            status.Text = buttons.Children.Count > 0 ? "" : unavailable
                ? "Não foi possível consultar as opções. Confira a página original."
                : "Nenhuma opção encontrada. Confira a página original.";
        };
    }
}
