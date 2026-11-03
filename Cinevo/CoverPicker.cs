using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Cinevo;
public sealed class CoverPicker : Window
{
    public string SelectedPath { get; private set; } = "";
    public string SelectedSource { get; private set; } = "";
    public CoverPicker(Movie movie, CoverService service)
    {
        Title = "Escolher capa — " + movie.Title; Width = 760; Height = 590; MinWidth = 620; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new CancellationTokenSource(); Closed += (_, _) => cancel.Cancel();
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Encontre o pôster certo", FontSize = 24, Margin = new Thickness(0,0,0,8) });
        header.Children.Add(new TextBlock { Text = "Busca na Wikipédia. Ajuste o título se necessário.", Foreground = Brushes.Gray, Margin = new Thickness(0,0,0,14) });
        var searchRow = new DockPanel(); var search = new Button { Content = "Buscar", Margin = new Thickness(12,0,0,0) }; DockPanel.SetDock(search, Dock.Right); searchRow.Children.Add(search);
        var input = new TextBox { Text = movie.Title }; searchRow.Children.Add(input); header.Children.Add(searchRow);
        var status = new TextBlock { Text = "Escolha um resultado para usar como capa.", Foreground = Brushes.Gray, Margin = new Thickness(0,14,0,12) }; header.Children.Add(status);
        var apply = new Button { Content = "Usar capa selecionada", Margin = new Thickness(0,16,0,0) }; DockPanel.SetDock(apply, Dock.Bottom); panel.Children.Add(apply);
        var list = new ListBox { Background = new SolidColorBrush(Color.FromRgb(25,28,37)), BorderThickness = new Thickness(0), Foreground = Brushes.White }; panel.Children.Add(list);
        async Task Find()
        {
            if (!search.IsEnabled) return; search.IsEnabled = false; apply.IsEnabled = false; status.Text = "Buscando pôsteres…"; list.Items.Clear();
            try
            {
                var candidates = await service.SearchAsync(input.Text.Trim(), movie.Year, cancel.Token);
                foreach (var candidate in candidates)
                {
                    var row = new DockPanel { Margin = new Thickness(8) };
                    var image = new Image { Width = 62, Height = 85, Stretch = Stretch.UniformToFill, Margin = new Thickness(0,0,14,0) }; DockPanel.SetDock(image, Dock.Left); row.Children.Add(image);
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(new TextBlock { Text = candidate.Title, FontSize = 16, FontWeight = FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text = candidate.Description, TextWrapping = TextWrapping.Wrap, MaxHeight = 48, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0,6,0,0) }); row.Children.Add(text);
                    var item = new ListBoxItem { Content = row, Tag = candidate, HorizontalContentAlignment = HorizontalAlignment.Stretch }; list.Items.Add(item);
                    // Preview thumbnails use the same bounded, validated cache as the library.
                    try { var path = await service.DownloadAsync(candidate, System.IO.Path.Combine(Library.DataDirectory, "covers"), cancel.Token); image.Source = (ImageSource?)new CoverImageConverter().Convert(path, typeof(ImageSource), "", System.Globalization.CultureInfo.CurrentCulture); } catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or TaskCanceledException) { }
                }
                status.Text = candidates.Count == 0 ? "Nenhum pôster encontrado. Tente o título original ou escolha uma imagem do PC." : $"{candidates.Count} resultados · selecione o pôster correto.";
                apply.IsEnabled = candidates.Count > 0;
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or System.Text.Json.JsonException or OperationCanceledException) { status.Text = "A busca não está disponível agora. Tente novamente mais tarde."; }
            finally { search.IsEnabled = true; }
        }
        search.Click += async (_, _) => await Find(); Loaded += async (_, _) => await Find();
        apply.Click += async (_, _) => { if (list.SelectedItem is not ListBoxItem { Tag: CoverCandidate candidate }) { status.Text = "Selecione um pôster primeiro."; return; } apply.IsEnabled = false; try { SelectedPath = await service.DownloadAsync(candidate, System.IO.Path.Combine(Library.DataDirectory, "covers"), cancel.Token); SelectedSource = candidate.PageUrl; DialogResult = true; } catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or OperationCanceledException) { status.Text = "Não foi possível baixar este pôster."; apply.IsEnabled = true; } };
        Content = panel;
    }
}
