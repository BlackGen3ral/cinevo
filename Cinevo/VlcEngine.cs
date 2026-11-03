using System.Runtime.InteropServices;
namespace Cinevo;
public sealed class VlcEngine : IDisposable
{
    private static bool configured;
    private IntPtr instance;
    private IntPtr player;
    public VlcEngine(string runtimeDirectory, bool headless = false)
    {
        Configure(runtimeDirectory);
        var args = new List<string> { "--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--avcodec-hw=any", "--quiet", "--no-media-library" };
        if (headless) args.AddRange(["--vout=dummy", "--aout=dummy"]);
        var values = args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var argv = Marshal.AllocHGlobal(IntPtr.Size * values.Length);
        try { Marshal.Copy(values, 0, argv, values.Length); instance = Native.libvlc_new(values.Length, argv); }
        finally { foreach (var value in values) Marshal.FreeCoTaskMem(value); Marshal.FreeHGlobal(argv); }
        if (instance == IntPtr.Zero) throw new InvalidOperationException("Não foi possível iniciar o motor de vídeo.");
        player = Native.libvlc_media_player_new(instance);
        if (player == IntPtr.Zero) { Native.libvlc_release(instance); instance = IntPtr.Zero; throw new InvalidOperationException("Não foi possível criar o player."); }
    }
    private static void Configure(string directory)
    {
        if (configured) return;
        if (!System.IO.File.Exists(System.IO.Path.Combine(directory, "libvlc.dll"))) throw new System.IO.FileNotFoundException("O motor de vídeo está ausente. Reinstale o Cinevo pelo instalador oficial.");
        Native.SetDllDirectory(directory);
        NativeLibrary.SetDllImportResolver(typeof(VlcEngine).Assembly, (name, _, _) => name == "libvlc" ? NativeLibrary.Load(System.IO.Path.Combine(directory, "libvlc.dll")) : IntPtr.Zero);
        configured = true;
    }
    public void Open(string path, IntPtr hwnd)
    {
        var media = Native.libvlc_media_new_path(instance, path);
        if (media == IntPtr.Zero) throw new InvalidOperationException("Não foi possível abrir o arquivo de vídeo.");
        try { Native.libvlc_media_player_set_media(player, media); Native.libvlc_media_player_set_hwnd(player, hwnd); Native.libvlc_video_set_mouse_input(player, 0); Native.libvlc_video_set_key_input(player, 0); if (Native.libvlc_media_player_play(player) != 0) throw new InvalidOperationException("O vídeo não pôde ser iniciado."); }
        finally { Native.libvlc_media_release(media); }
    }
    public int State => player == IntPtr.Zero ? 5 : Native.libvlc_media_player_get_state(player);
    public long Time { get => Native.libvlc_media_player_get_time(player); set => Native.libvlc_media_player_set_time(player, value); }
    public long Length => Native.libvlc_media_player_get_length(player);
    public void Pause(bool pause) => Native.libvlc_media_player_set_pause(player, pause ? 1 : 0);
    public int Volume { get => Native.libvlc_audio_get_volume(player); set => Native.libvlc_audio_set_volume(player, Math.Clamp(value, 0, 100)); }
    public void AudioTrack(int id) => Native.libvlc_audio_set_track(player, id);
    public void SubtitleTrack(int id) => Native.libvlc_video_set_spu(player, id);
    public int CurrentAudioTrack => Native.libvlc_audio_get_track(player);
    public int CurrentSubtitleTrack => Native.libvlc_video_get_spu(player);
    public int AddSubtitle(string path) => Native.libvlc_media_player_add_slave(player, 0, new Uri(path).AbsoluteUri, true);
    public List<MediaTrack> Tracks(bool audio)
    {
        var head = audio ? Native.libvlc_audio_get_track_description(player) : Native.libvlc_video_get_spu_description(player);
        var tracks = new List<MediaTrack>();
        try { for (var item = head; item != IntPtr.Zero;) { var track = Marshal.PtrToStructure<TrackDescription>(item); tracks.Add(new(track.Id, Marshal.PtrToStringUTF8(track.Name) ?? "Faixa " + track.Id)); item = track.Next; } }
        finally { if (head != IntPtr.Zero) Native.libvlc_track_description_list_release(head); }
        return tracks;
    }
    public void Dispose() { if (player != IntPtr.Zero) { Native.libvlc_media_player_stop(player); Native.libvlc_media_player_release(player); player = IntPtr.Zero; } if (instance != IntPtr.Zero) { Native.libvlc_release(instance); instance = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct TrackDescription { public int Id; public IntPtr Name; public IntPtr Next; }
    private static class Native
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetDllDirectory(string path);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr libvlc_new(int argc, IntPtr argv);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_release(IntPtr instance);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr libvlc_media_player_new(IntPtr instance);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_release(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr libvlc_media_new_path(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_release(IntPtr media);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_set_media(IntPtr player, IntPtr media);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_set_hwnd(IntPtr player, IntPtr hwnd);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_media_player_play(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_stop(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_media_player_get_state(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern long libvlc_media_player_get_time(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern long libvlc_media_player_get_length(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_set_time(IntPtr player, long time);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_media_player_set_pause(IntPtr player, int paused);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_audio_get_volume(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_audio_set_volume(IntPtr player, int volume);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_video_set_mouse_input(IntPtr player, uint enabled);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_video_set_key_input(IntPtr player, uint enabled);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr libvlc_audio_get_track_description(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr libvlc_video_get_spu_description(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern void libvlc_track_description_list_release(IntPtr list);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_audio_set_track(IntPtr player, int track);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_audio_get_track(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_video_get_spu(IntPtr player);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_video_set_spu(IntPtr player, int track);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] internal static extern int libvlc_media_player_add_slave(IntPtr player, int type, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri, [MarshalAs(UnmanagedType.I1)] bool select);
    }
}
public record MediaTrack(int Id, string Name) { public override string ToString() => Name; }
