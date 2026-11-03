using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Cinevo;
public sealed class MovieDialog : Window
{
    public MovieDialog(Movie movie)
    {
        Title = "Detalhes do filme"; Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16,18,24)); Foreground = System.Windows.Media.Brushes.White; Width = 570; Height = 700; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(28) };
        TextBox Field(string label, string text) { stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 6) }); var input = new TextBox { Text = text }; stack.Children.Add(input); return input; }
        var title = Field("Título", movie.Title); var year = Field("Ano", movie.Year); var genre = Field("Gênero", movie.Genre);
        var notes = Field("Suas anotações", movie.Notes); notes.AcceptsReturn = true; notes.Height = 80; notes.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(new TextBlock { Text = "Avaliação", Margin = new Thickness(0, 12, 0, 6) });
        var rating = new ComboBox { ItemsSource = new[] { "Sem avaliação", "★", "★★", "★★★", "★★★★", "★★★★★" }, SelectedIndex = Math.Clamp(movie.Rating, 0, 5) }; stack.Children.Add(rating);
        var watched = new CheckBox { Content = "Já assisti", IsChecked = movie.Watched, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 16, 0, 8) }; stack.Children.Add(watched);
        var favorite = new CheckBox { Content = "Adicionar aos favoritos", IsChecked = movie.Favorite, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 14) }; stack.Children.Add(favorite);
        var coverPath = movie.Cover;
        var cover = new Button { Content = "Escolher capa do computador", Margin = new Thickness(0, 0, 0, 12) };
        cover.Click += (_, _) => { var d = new OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png;*.webp;*.bmp" }; if (d.ShowDialog() == true) { coverPath = d.FileName; cover.Content = "Capa selecionada"; } }; stack.Children.Add(cover);
        stack.Children.Add(new TextBlock { Text = movie.Path, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "Salvar", Background = System.Windows.Media.Brushes.MediumPurple, IsDefault = true };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(title.Text)) { title.Focus(); return; } movie.Title = title.Text.Trim(); movie.Year = year.Text.Trim(); movie.Genre = genre.Text.Trim(); movie.Notes = notes.Text; movie.Rating = rating.SelectedIndex; movie.Watched = watched.IsChecked == true; movie.Favorite = favorite.IsChecked == true;
            if (coverPath != movie.Cover && File.Exists(coverPath)) { var dir = System.IO.Path.Combine(Library.DataDirectory, "covers"); Directory.CreateDirectory(dir); var dest = System.IO.Path.Combine(dir, Guid.NewGuid() + System.IO.Path.GetExtension(coverPath)); File.Copy(coverPath, dest); movie.Cover = dest; }
            DialogResult = true; }; buttons.Children.Add(save);
        var reveal = new Button { Content = "Mostrar no Explorer" }; reveal.Click += (_, _) => { if (File.Exists(movie.Path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + movie.Path + "\"") { UseShellExecute = true }); }; buttons.Children.Add(reveal);
        var cancel = new Button { Content = "Cancelar", IsCancel = true }; buttons.Children.Add(cancel); stack.Children.Add(buttons);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
