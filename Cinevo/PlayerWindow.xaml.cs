using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Cinevo;
public partial class PlayerWindow : Window
{
    private readonly Movie movie;
    private readonly Action save;
    private VlcEngine? engine;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool updating, seeking, resumed, tracksLoaded, fullscreen, closed, teardownStarted, teardownFinished;
    private long lastSaved;
    private WindowState previousState;
    public PlayerWindow(Movie movie, Action save)
    {
        this.movie = movie; this.save = save; InitializeComponent();
        MovieTitle.Text = movie.Title; Title = movie.Title + " — Cinevo";
        Loaded += (_, _) => Start(); timer.Tick += (_, _) => Tick();
        Closing += Player_Closing;
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }
    private async void Player_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (teardownFinished) return;
        e.Cancel = true;
        if (teardownStarted) return;
        teardownStarted = true; timer.Stop(); PlayerStatus.Text = "Encerrando a sessão…";
        try { CaptureProgress(); save(); }
        catch (Exception ex) { MessageBox.Show("Não foi possível salvar o progresso: " + ex.Message, "Cinevo"); }
        var releasing = engine; engine = null;
        // LibVLC waits for native video-window messages during stop. Keep WPF pumping them.
        try { await Task.Run(() => releasing?.Dispose()); }
        finally { Video.Dispose(); teardownFinished = true; Close(); }
    }
    private void Start()
    {
        try { engine = new VlcEngine(System.IO.Path.Combine(AppContext.BaseDirectory, "vlc")); engine.Open(movie.Path, Video.VideoHandle); engine.Volume = (int)Volume.Value; timer.Start(); }
        catch (Exception ex) { PlayerStatus.Text = "Não foi possível iniciar o vídeo. " + ex.Message; }
    }
    private static string TimeText(long ms) { var time = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}"; }
    private void Tick()
    {
        if (closed || engine == null) return;
        var state = engine.State;
        if (state == 7) { PlayerStatus.Text = "Não foi possível reproduzir este arquivo. Ele pode estar incompleto ou usar um formato não suportado."; timer.Stop(); return; }
        if (state == 6) { movie.Watched = true; movie.ResumeMilliseconds = 0; save(); PlayerStatus.Text = "Sessão concluída. Filme marcado como assistido."; PauseButton.Content = "▶  Rever"; timer.Stop(); return; }
        if (state is not (3 or 4)) { PlayerStatus.Text = "Carregando o vídeo…"; return; }
        var length = engine.Length;
        if (!resumed && length > 0) { resumed = true; if (!movie.Watched && movie.ResumeMilliseconds > 30000 && movie.ResumeMilliseconds < length - 10000) engine.Time = movie.ResumeMilliseconds; }
        PlayerStatus.Text = state == 4 ? "Em pausa · Espaço para continuar" : "Reproduzindo no Cinevo · Espaço pausa · ← → avançam 10s · F tela cheia";
        PauseButton.Content = state == 4 ? "▶  Continuar" : "Ⅱ  Pausar";
        updating = true;
        if (!seeking) { Timeline.Maximum = Math.Max(1, length); Timeline.Value = Math.Clamp(engine.Time, 0, Timeline.Maximum); }
        Elapsed.Text = TimeText(engine.Time); Total.Text = TimeText(length); updating = false;
        if (!tracksLoaded) { LoadTracks(); tracksLoaded = AudioTracks.Items.Count > 0; }
        if (Environment.TickCount64 - lastSaved > 10000) { CaptureProgress(); save(); lastSaved = Environment.TickCount64; }
    }
    private void LoadTracks()
    {
        if (engine == null) return; updating = true;
        var audio = engine.Tracks(true); var subtitles = engine.Tracks(false);
        if (audio.Count == 0) audio.Add(new(-1, "Sem áudio"));
        if (subtitles.Count == 0) subtitles.Add(new(-1, "Sem legendas"));
        AudioTracks.ItemsSource = audio; SubtitleTracks.ItemsSource = subtitles;
        AudioTracks.SelectedItem = audio.FirstOrDefault(t => t.Id == engine.CurrentAudioTrack) ?? audio[0];
        SubtitleTracks.SelectedItem = subtitles.FirstOrDefault(t => t.Id == engine.CurrentSubtitleTrack) ?? subtitles[0];
        updating = false;
    }
    private void CaptureProgress() { if (engine == null || engine.State is not (3 or 4)) return; movie.DurationMilliseconds = Math.Max(0, engine.Length); movie.ResumeMilliseconds = Math.Max(0, engine.Time); if (movie.DurationMilliseconds > 0 && movie.ResumeMilliseconds >= movie.DurationMilliseconds * .95) { movie.Watched = true; movie.ResumeMilliseconds = 0; } }
    private void Pause_Click(object sender, RoutedEventArgs e) { if (engine == null) return; if (engine.State == 6) { engine.Open(movie.Path, Video.VideoHandle); resumed = true; timer.Start(); } else engine.Pause(engine.State != 4); }
    private void Back_Click(object sender, RoutedEventArgs e) => Close();
    private void BackTen_Click(object sender, RoutedEventArgs e) => Seek(-10000);
    private void ForwardTen_Click(object sender, RoutedEventArgs e) => Seek(10000);
    private void Seek(long delta) { if (engine != null && engine.Length > 0) engine.Time = Math.Clamp(engine.Time + delta, 0, Math.Max(0, engine.Length - 1000)); }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (engine != null) engine.Volume = (int)e.NewValue; }
    private void Seek_Start(object sender, MouseButtonEventArgs e) => seeking = true;
    private void Seek_End(object sender, MouseButtonEventArgs e) { seeking = false; if (engine != null) engine.Time = (long)Timeline.Value; }
    private void Timeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!updating && !seeking && engine != null) engine.Time = (long)e.NewValue; }
    private void Audio_Changed(object sender, SelectionChangedEventArgs e) { if (!updating && AudioTracks.SelectedItem is MediaTrack track) engine?.AudioTrack(track.Id); }
    private void Subtitle_Changed(object sender, SelectionChangedEventArgs e) { if (!updating && SubtitleTracks.SelectedItem is MediaTrack track) engine?.SubtitleTrack(track.Id); }
    private void Subtitle_Click(object sender, RoutedEventArgs e) { if (engine == null) return; var d = new OpenFileDialog { Filter = "Legendas|*.srt;*.ass;*.ssa;*.vtt;*.sub" }; if (d.ShowDialog(this) == true) { if (engine.AddSubtitle(d.FileName) == 0) { LoadTracks(); PlayerStatus.Text = "Legenda carregada."; } else PlayerStatus.Text = "Não foi possível carregar esta legenda."; } }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ToggleFullscreen() { fullscreen = !fullscreen; if (fullscreen) { previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; Header.Visibility = Visibility.Collapsed; } else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = previousState; Header.Visibility = Visibility.Visible; } }
    private void Player_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is ComboBox or ComboBoxItem or System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key == Key.Space && e.OriginalSource is not Button) { Pause_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.F) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Escape) { if (fullscreen) ToggleFullscreen(); else Close(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && e.OriginalSource is not Slider) { Seek(e.Key == Key.Left ? -10000 : 10000); e.Handled = true; }
        else if (e.Key == Key.M && engine != null) { Volume.Value = Volume.Value > 0 ? 0 : 80; e.Handled = true; }
    }
}
