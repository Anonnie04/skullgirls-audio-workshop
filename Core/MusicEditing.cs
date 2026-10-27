using System.Security.Cryptography;
using System.Text.Json;

namespace SkullgirlsAudio;

public record AudioSource(string Path, string Hash, Decoded Audio)
{
    public static AudioSource Open(string path, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        byte[] bytes;
        using (var input = File.OpenRead(path))
        {
            if (input.Length > 128 * 1024 * 1024) throw new IOException("Choose an audio file no larger than 128 MB.");
            bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new IOException("Source changed while reading. Import it again.");
        }
        var audio = SourceAudioDecoder.Decode(bytes, System.IO.Path.GetExtension(path), cancel);
        if (audio.Source.Seconds < .04) throw new InvalidDataException("The source must be at least 40 milliseconds long.");
        var source = new AudioSource(System.IO.Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(bytes)), audio);
        cancel.ThrowIfCancellationRequested(); source.AssertUnchanged(); return source;
    }
    public void AssertUnchanged()
    {
        using var file = File.OpenRead(Path);
        if (Convert.ToHexString(SHA256.HashData(file)) != Hash) throw new IOException("The source audio changed. Import it again before generating output.");
    }
}

public sealed class MusicTarget
{
    private readonly GameLibrary library;
    public Track Track { get; }
    public byte[][] Originals { get; }
    public Wave[] Formats { get; }
    public string[] Hashes { get; }
    public string GameFolder => library.Folder;
    public string ArchiveFolder => library.ArchiveFolder;
    public MusicTarget(GameLibrary library, Track track)
    {
        if (!track.Mapped || track.Assets.Length != 2 || track.Bank) throw new NotSupportedException("Choose a configuration-backed intro/loop music pair.");
        this.library = library; Track = track;
        Originals = track.Assets.Select(library.Archive(track.Archive).Read).ToArray();
        Formats = Originals.Select(Wave.Parse).ToArray();
        Hashes = Originals.Select(b => Convert.ToHexString(SHA256.HashData(b))).ToArray();
        foreach (var wave in Formats)
            if (wave.FormatTag != 2 || wave.Channels != 2 || wave.Block != 140 || wave.SamplesPerBlock != 128 || wave.Format.Length != 50
                || wave.Sampler is not { Length: 60 } sampler || BitConverter.ToUInt32(sampler, 28) != 1 || BitConverter.ToUInt32(sampler, 32) != 0
                || BitConverter.ToUInt32(sampler, 40) != 0 || BitConverter.ToUInt32(sampler, 44) != 0 || BitConverter.ToUInt32(sampler, 48) != wave.Frames - 1)
                throw new NotSupportedException("This target's block or sampler structure has not been validated for the editor.");
    }
    public void AssertUnchanged()
    {
        for (int i = 0; i < 2; i++)
            if (Convert.ToHexString(SHA256.HashData(library.Archive(Track.Archive).Read(Track.Assets[i]))) != Hashes[i])
                throw new IOException("The target archive changed. Reopen the editor from the library.");
    }
}

public record EditSettings(double Start, double LoopStart, double End, double GainDb = 0, double FadeIn = 0, double FadeOut = 0, double Crossfade = 0)
{
    public void Validate(AudioSource source)
    {
        if (new[] { Start, LoopStart, End, GainDb, FadeIn, FadeOut, Crossfade }.Any(v => !double.IsFinite(v))) throw new ArgumentException("Edit values must be finite.");
        if (Start < 0 || LoopStart - Start < .01 || End - LoopStart < .01 || End > source.Audio.Source.Seconds + .0000001)
            throw new ArgumentException("Select at least 10 ms for both intro and loop, within the source duration.");
        if (GainDb < -60 || GainDb > 24 || FadeIn < 0 || FadeIn > LoopStart - Start || FadeOut < 0 || FadeOut > End - LoopStart || Crossfade < 0 || Crossfade * 2 >= End - LoopStart)
            throw new ArgumentException("Gain must be -60 to +24 dB. Fades must fit their segment; loop crossfade must be shorter than half the loop.");
    }
}

public record RenderedSegment(byte[] Pcm, int Rate, int Channels, int Frames, long ClippedSamples, double PeakBeforeClip, double RequestedSeconds, int CrossfadeFrames)
{
    public double Seconds => Frames / (double)Rate;
    public double TimingAdjustmentMs => (Seconds - RequestedSeconds) * 1000;
}
public record EditedPair(RenderedSegment Intro, RenderedSegment Loop);
public record Audition(byte[] Pcm, int Rate, int Channels, int IntroFrames)
{
    public double LoopStart => IntroFrames / (double)Rate;
    public double Duration => Pcm.Length / (double)(Rate * Channels * 2);
}

public static class MusicEditing
{
    // Polyphase, Hann-windowed sinc resampling. A low-pass cutoff is included when
    // reducing rate, unlike linear interpolation. Preview and conversion share this policy.
    public static float[] Resample(float[] input, int channels, int outputFrames, CancellationToken cancel = default)
    {
        int inputFrames = input.Length / channels;
        if (inputFrames < 1 || outputFrames < 1 || (long)outputFrames * channels > 32_000_000) throw new ArgumentException("Audio exceeds the editor's 32 million sample limit.");
        if (inputFrames == outputFrames) return (float[])input.Clone();
        const int radius = 16, phases = 1024, taps = radius * 2;
        double ratio = inputFrames / (double)outputFrames, cutoff = Math.Min(1, outputFrames / (double)inputFrames) * .95;
        var kernels = new double[phases][];
        for (int phase = 0; phase < phases; phase++)
        {
            var kernel = kernels[phase] = new double[taps]; double sum = 0;
            for (int tap = 0; tap < taps; tap++)
            {
                double distance = tap - radius + 1 - phase / (double)phases;
                double x = Math.PI * distance * cutoff;
                kernel[tap] = (Math.Abs(x) < 1e-12 ? 1 : Math.Sin(x) / x) * cutoff * (.5 + .5 * Math.Cos(Math.PI * distance / radius)); sum += kernel[tap];
            }
            for (int tap = 0; tap < taps; tap++) kernel[tap] /= sum;
        }
        var result = new float[checked(outputFrames * channels)];
        for (int frame = 0; frame < outputFrames; frame++)
        {
            if ((frame & 4095) == 0) cancel.ThrowIfCancellationRequested();
            double position = frame * ratio; int center = (int)position, phase = Math.Min(phases - 1, (int)((position - center) * phases));
            var kernel = kernels[phase];
            for (int channel = 0; channel < channels; channel++)
            {
                double sample = 0;
                for (int tap = 0; tap < taps; tap++) sample += input[Math.Clamp(center + tap - radius + 1, 0, inputFrames - 1) * channels + channel] * kernel[tap];
                result[frame * channels + channel] = (float)sample;
            }
        }
        return result;
    }
    public static EditedPair Render(AudioSource source, MusicTarget target, EditSettings edits, CancellationToken cancel = default)
    {
        edits.Validate(source);
        return new(RenderSegment(source.Audio, target.Formats[0], edits.Start, edits.LoopStart, edits.GainDb, edits.FadeIn, 0, 0, cancel),
            RenderSegment(source.Audio, target.Formats[1], edits.LoopStart, edits.End, edits.GainDb, 0, edits.FadeOut, edits.Crossfade, cancel));
    }
    private static RenderedSegment RenderSegment(Decoded source, Wave target, double start, double end, double db, double fadeIn, double fadeOut, double crossfade, CancellationToken cancel)
    {
        int first = (int)Math.Round(start * source.Source.Rate), last = Math.Min(source.Source.Frames, (int)Math.Round(end * source.Source.Rate));
        int frames = last - first, channels = target.Channels;
        var segment = new float[checked(frames * channels)]; double gain = Math.Pow(10, db / 20);
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 4095) == 0) cancel.ThrowIfCancellationRequested();
            for (int c = 0; c < channels; c++)
            {
                double sample;
                if (source.Source.Channels == channels) sample = BitConverter.ToInt16(source.Pcm, ((first + frame) * channels + c) * 2) / 32768d;
                else if (source.Source.Channels == 1) sample = BitConverter.ToInt16(source.Pcm, (first + frame) * 2) / 32768d;
                else sample = (BitConverter.ToInt16(source.Pcm, (first + frame) * 4) + BitConverter.ToInt16(source.Pcm, (first + frame) * 4 + 2)) / 65536d;
                segment[frame * channels + c] = (float)sample;
            }
        }
        int rawFrames = Math.Max(1, (int)Math.Round(frames / (double)source.Source.Rate * target.Rate));
        int blend = (int)Math.Round(crossfade * target.Rate);
        int finalRaw = rawFrames - blend;
        int aligned = Math.Max(target.SamplesPerBlock, checked((int)Math.Round(finalRaw / (double)target.SamplesPerBlock, MidpointRounding.AwayFromZero) * target.SamplesPerBlock));
        float[] samples = Resample(segment, channels, blend == 0 ? aligned : rawFrames, cancel);
        if (blend > 0)
        {
            if (blend * 2 >= rawFrames) throw new ArgumentException("Crossfade is too long after conversion.");
            var shortened = new float[checked(finalRaw * channels)]; Array.Copy(samples, blend * channels, shortened, 0, shortened.Length);
            for (int f = 0; f < blend; f++) for (int c = 0; c < channels; c++)
            {
                float mix = (f + 1f) / blend;
                shortened[(finalRaw - blend + f) * channels + c] = samples[(rawFrames - blend + f) * channels + c] * (1 - mix) + samples[f * channels + c] * mix;
            }
            samples = Resample(shortened, channels, aligned, cancel);
        }
        var pcm = new byte[checked(aligned * channels * 2)]; long clipped = 0; double peak = 0;
        for (int f = 0; f < aligned; f++)
        {
            if ((f & 4095) == 0) cancel.ThrowIfCancellationRequested();
            double envelope = gain;
            if (fadeIn > 0) envelope *= Math.Min(1, f / Math.Max(1, fadeIn * target.Rate));
            if (fadeOut > 0) envelope *= Math.Min(1, (aligned - 1 - f) / Math.Max(1, fadeOut * target.Rate));
            for (int c = 0; c < channels; c++)
            {
                double value = samples[f * channels + c] * envelope; peak = Math.Max(peak, Math.Abs(value)); if (value < -1 || value > 32767d / 32768) clipped++;
                short quantized = (short)Math.Clamp(Math.Round(value * 32768), short.MinValue, short.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan((f * channels + c) * 2, 2), quantized);
            }
        }
        return new(pcm, target.Rate, channels, aligned, clipped, peak, (end - start) - crossfade, blend);
    }
    public static Audition Join(RenderedSegment intro, RenderedSegment loop, CancellationToken cancel = default) => Join(intro.Pcm, intro.Rate, loop.Pcm, loop.Rate, loop.Channels, cancel);
    public static Audition Join(byte[] intro, int introRate, byte[] loop, int loopRate, int channels, CancellationToken cancel = default)
    {
        int frames = (int)Math.Round(intro.Length / (double)(channels * 2) * loopRate / introRate); byte[] first = intro;
        if (introRate != loopRate)
        {
            var floats = new float[intro.Length / 2]; for (int i = 0; i < floats.Length; i++) floats[i] = BitConverter.ToInt16(intro, i * 2) / 32768f;
            var resampled = Resample(floats, channels, frames, cancel); first = new byte[resampled.Length * 2];
            for (int i = 0; i < resampled.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(first.AsSpan(i * 2, 2), (short)Math.Clamp(Math.Round(resampled[i] * 32768d), short.MinValue, short.MaxValue));
        }
        var combined = new byte[checked(first.Length + loop.Length)]; first.CopyTo(combined, 0); loop.CopyTo(combined, first.Length);
        return new(combined, loopRate, channels, frames);
    }
}

public record GeneratedPair(string Folder, string[] Files, string[] Hashes, Audition Preview, EditedPair Rendered, EditSettings Edits, string SourceHash, string[] TargetHashes, string[] Assets, string Archive);
public static class MusicGeneration
{
    public static GeneratedPair Generate(AudioSource source, MusicTarget target, EditSettings edits, string cacheRoot, CancellationToken cancel = default)
    {
        source.AssertUnchanged(); target.AssertUnchanged();
        string root = Path.GetFullPath(cacheRoot), game = Path.GetFullPath(target.GameFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(game, StringComparison.OrdinalIgnoreCase)) throw new IOException("Generated output must be outside the game installation.");
        var rendered = MusicEditing.Render(source, target, edits, cancel);
        Directory.CreateDirectory(root); string folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string[] files = new string[2], hashes = new string[2]; var decoded = new Decoded[2];
            var segments = new[] { rendered.Intro, rendered.Loop };
            for (int i = 0; i < 2; i++)
            {
                cancel.ThrowIfCancellationRequested(); byte[] bytes = NativeMusicEncoder.Encode(segments[i].Pcm, target.Formats[i]);
                files[i] = Path.Combine(folder, Path.GetFileName(target.Track.Assets[i])); File.WriteAllBytes(files[i], bytes);
                // Verify the actual generated file through a separate decoder.
                byte[] saved = File.ReadAllBytes(files[i]); decoded[i] = ManagedDecoder.Decode(saved); var format = decoded[i].Source;
                if (format.Rate != segments[i].Rate || format.Channels != segments[i].Channels || format.Frames != segments[i].Frames || format.Block != target.Formats[i].Block || !format.Format.SequenceEqual(target.Formats[i].Format)
                    || format.Sampler is not { Length: 60 } sampler || BitConverter.ToInt32(sampler, 44) != 0 || BitConverter.ToInt32(sampler, 48) != segments[i].Frames - 1) throw new InvalidDataException("Generated output failed format or loop metadata verification.");
                hashes[i] = Convert.ToHexString(SHA256.HashData(saved));
            }
            cancel.ThrowIfCancellationRequested(); source.AssertUnchanged(); target.AssertUnchanged();
            File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new { schema = 1, source = source.Path, sourceHash = source.Hash, target = target.Track.Name, archive = target.Track.Archive, paths = target.Track.Assets, originalHashes = target.Hashes, edits,
                intro = new { rendered.Intro.Frames, rendered.Intro.Rate, rendered.Intro.TimingAdjustmentMs, rendered.Intro.ClippedSamples, rendered.Intro.PeakBeforeClip },
                loop = new { rendered.Loop.Frames, rendered.Loop.Rate, rendered.Loop.TimingAdjustmentMs, rendered.Loop.CrossfadeFrames, rendered.Loop.ClippedSamples, rendered.Loop.PeakBeforeClip },
                hashes, independentDecodePassed = true, inGameVerified = false, alignmentPolicy = "Nearest complete native block by resampling; no appended silence. smpl end updated to final frame." }, new JsonSerializerOptions { WriteIndented = true }));
            return new(folder, files, hashes, MusicEditing.Join(decoded[0].Pcm, decoded[0].Source.Rate, decoded[1].Pcm, decoded[1].Source.Rate, 2, cancel), rendered, edits, source.Hash, target.Hashes.ToArray(), target.Track.Assets.ToArray(), target.Track.Archive);
        }
        catch
        {
            // This folder is always a freshly generated GUID below the checked cache root.
            if (Path.GetDirectoryName(Path.GetFullPath(folder)) == root && Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) Directory.Delete(folder, true);
            throw;
        }
    }
}
