using System.Diagnostics;
using System.Text;

namespace SkullgirlsAudio;

// Source imports only. Native game decoding/encoding retains its independent format guards.
public static class SourceAudioDecoder
{
    public const string FileFilter = "Supported audio|*.wav;*.wave;*.mp3;*.flac;*.ogg;*.oga;*.opus;*.m4a;*.aac;*.wma;*.aiff;*.aif";
    public static string BackendPath => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
    private static readonly Dictionary<string, string> Demuxers = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wav"] = "wav", [".wave"] = "wav", [".mp3"] = "mp3", [".flac"] = "flac",
        [".ogg"] = "ogg", [".oga"] = "ogg", [".opus"] = "ogg", [".m4a"] = "mov",
        [".aac"] = "aac", [".wma"] = "asf", [".aiff"] = "aiff", [".aif"] = "aiff"
    };
    public static bool SupportsFile(string path) => Demuxers.ContainsKey(Path.GetExtension(path));
    public static Decoded Decode(byte[] bytes, string extension, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        if (!Demuxers.TryGetValue(extension, out string? demuxer)) throw new NotSupportedException("Choose WAV, MP3, FLAC, OGG/Opus, M4A/AAC, WMA or AIFF audio.");
        if (demuxer == "wav")
        {
            try { return ManagedDecoder.Decode(bytes); }
            catch (NotSupportedException) { /* Higher-depth/float WAV uses the source decoder. */ }
        }
        if (!File.Exists(BackendPath)) throw new IOException("The bundled audio decoder is missing. Restore the application's tools/ffmpeg folder.");
        return DecodeExternal(bytes, demuxer, cancel).GetAwaiter().GetResult();
    }
    private static async Task<Decoded> DecodeExternal(byte[] bytes, string demuxer, CancellationToken cancel)
    {
        // A seekable private snapshot supports M4A files with their index at the end.
        string input = Path.Combine(Path.GetTempPath(), "sgaw-import-" + Guid.NewGuid().ToString("N") + ".audio");
        try
        {
            await File.WriteAllBytesAsync(input, bytes, cancel);
            var start = new ProcessStartInfo(BackendPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-xerror", "-max_alloc", "134217728", "-threads", "1", "-protocol_whitelist", "file,pipe", "-f", demuxer, "-err_detect", "explode", "-i", input,
                "-map", "0:a:0", "-vn", "-sn", "-dn", "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1" }) start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("Could not start the bundled audio decoder.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = DrainErrors(process.StandardError);
            try
            {
                using var output = new MemoryStream(); byte[] buffer = new byte[65536]; int read;
                while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (output.Length + read > 64_000_000) throw new InvalidDataException("Decoded audio exceeds the 32 million sample limit (about six minutes in stereo). Use a shorter source.");
                    output.Write(buffer, 0, read);
                }
                await process.WaitForExitAsync(timeout.Token); string error = await errors;
                if (process.ExitCode != 0) throw new InvalidDataException("Audio could not be decoded. The file may be damaged, protected, or use an unsupported codec. " + error);
                byte[] pcm = output.ToArray();
                if (pcm.Length == 0 || pcm.Length % 4 != 0) throw new InvalidDataException("The source contains no complete stereo audio frames.");
                using var wave = new MemoryStream(); using var writer = new BinaryWriter(wave);
                writer.Write("RIFF"u8); writer.Write(pcm.Length + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(44100); writer.Write(176400); writer.Write((ushort)4); writer.Write((ushort)16);
                writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
                return new(Wave.Parse(wave.ToArray()), pcm);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new IOException("Audio import timed out. Try a shorter source or another encoding."); }
            finally
            {
                if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
                await errors;
            }
        }
        finally { if (File.Exists(input)) File.Delete(input); }
    }
    private static async Task<string> DrainErrors(StreamReader reader)
    {
        var text = new StringBuilder(); char[] buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) if (text.Length < 2048) text.Append(buffer, 0, Math.Min(count, 2048 - text.Length));
        return text.ToString().Trim();
    }
}
