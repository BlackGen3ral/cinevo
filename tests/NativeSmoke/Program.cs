using System.Text;
using Cinevo;
var root = Path.Combine(Path.GetTempPath(), "Cinevo-player-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var video = Path.Combine(root, "Vídeo de teste.avi");
    CreateAvi(video);
    using var player = new VlcEngine(args[0], true);
    player.Open(video, IntPtr.Zero);
    for (var i = 0; i < 100 && (player.State != 3 || player.Time < 400); i++) { if (player.State == 7) throw new Exception("Falha ao decodificar o AVI"); Thread.Sleep(100); }
    if (player.State != 3 || player.Time < 300 || player.Length < 5000) throw new Exception($"Vídeo não avançou: state={player.State}, time={player.Time}, length={player.Length}");
    player.Pause(true); Thread.Sleep(300); if (player.State != 4) throw new Exception("Pausa não funcionou");
    player.Time = 4000; player.Pause(false); Thread.Sleep(700); if (player.Time < 3900) throw new Exception("Busca na linha do tempo não funcionou");
    Console.WriteLine($"PASS: LibVLC real decodificou AVI em caminho Unicode, reproduziu, pausou e avançou para {player.Time}ms.");
    var subtitle = Path.Combine(root, "Legenda de teste.srt"); File.WriteAllText(subtitle,"1\n00:00:01,000 --> 00:00:09,000\nCinevo - teste de legenda\n");
    if (player.AddSubtitle(subtitle) != 0) throw new Exception("Legenda externa não foi aceita");
    for (var i=0;i<20 && player.Tracks(false).Count<2;i++) Thread.Sleep(100);
    if (player.Tracks(false).Count<2) throw new Exception("Faixa de legenda não foi enumerada");
    Console.WriteLine("PASS: legenda SRT externa carregada e enumerada pelo LibVLC.");
    var audio = Path.Combine(root, "Áudio de teste.wav");
    using (var wav = new BinaryWriter(File.Create(audio))) { wav.Write(Encoding.ASCII.GetBytes("RIFF")); wav.Write(36 + 44100 * 2 * 5); wav.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(44100); wav.Write(88200); wav.Write((short)2); wav.Write((short)16); wav.Write(Encoding.ASCII.GetBytes("data")); wav.Write(44100 * 2 * 5); wav.Write(new byte[44100 * 2 * 5]); }
    player.Open(audio, IntPtr.Zero);
    for (var i=0;i<60 && (player.Time<300 || player.Tracks(true).Count<2);i++) Thread.Sleep(100);
    if (player.State!=3 || player.Tracks(true).Count<2) throw new Exception("Áudio PCM não foi decodificado");
    Console.WriteLine("PASS: LibVLC real reproduziu áudio PCM e enumerou as faixas.");
}
finally { Directory.Delete(root, true); }
static void CreateAvi(string path)
{
    const int width = 160, height = 96, fps = 10, frames = 100, frameSize = width * height * 3;
    using var writer = new BinaryWriter(File.Create(path));
    void Four(string s) => writer.Write(Encoding.ASCII.GetBytes(s));
    long Begin(string type, string? list = null) { Four(type); var pos = writer.BaseStream.Position; writer.Write(0); if (list != null) Four(list); return pos; }
    void End(long pos) { var end = writer.BaseStream.Position; writer.BaseStream.Position = pos; writer.Write((int)(end - pos - 4)); writer.BaseStream.Position = end; }
    var riff = Begin("RIFF", "AVI "); var hdrl = Begin("LIST", "hdrl");
    var avih = Begin("avih"); writer.Write(1000000 / fps); writer.Write(frameSize * fps); writer.Write(0); writer.Write(16); writer.Write(frames); writer.Write(0); writer.Write(1); writer.Write(frameSize); writer.Write(width); writer.Write(height); writer.Write(new byte[16]); End(avih);
    var strl = Begin("LIST", "strl"); var strh = Begin("strh"); Four("vids"); Four("DIB "); writer.Write(0); writer.Write((short)0); writer.Write((short)0); writer.Write(0); writer.Write(1); writer.Write(fps); writer.Write(0); writer.Write(frames); writer.Write(frameSize); writer.Write(-1); writer.Write(0); writer.Write((short)0); writer.Write((short)0); writer.Write((short)width); writer.Write((short)height); End(strh);
    var strf = Begin("strf"); writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24); writer.Write(0); writer.Write(frameSize); writer.Write(new byte[16]); End(strf); End(strl); End(hdrl);
    var movi = Begin("LIST", "movi"); var moviStart = writer.BaseStream.Position - 4; var indexes = new List<(int Offset,int Size)>();
    for (var frame = 0; frame < frames; frame++) { var offset = (int)(writer.BaseStream.Position - moviStart); var chunk = Begin("00db"); var pixels = new byte[frameSize]; for(var y=0;y<height;y++) for(var x=0;x<width;x++) { var i=(y*width+x)*3; pixels[i]=(byte)(frame*2); pixels[i+1]=(byte)(x*255/width); pixels[i+2]=(byte)(y*255/height); } writer.Write(pixels); End(chunk); indexes.Add((offset,frameSize)); }
    End(movi); var idx = Begin("idx1"); foreach(var item in indexes) { Four("00db"); writer.Write(16); writer.Write(item.Offset); writer.Write(item.Size); } End(idx); End(riff);
}
