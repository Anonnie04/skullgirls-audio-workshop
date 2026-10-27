using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsAudio;
using SkullgirlsAudioWorkshop;

static class EditorChecks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Rejected(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or OperationCanceledException) { return; } throw new Exception("Invalid operation was accepted."); }
    public static async Task Run(string game, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var library = new GameLibrary(game); string archive = library.Music.FilePath;
        string Digest(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)); }
        string originalHash = Digest(archive);
        // A deterministic synthetic mono source: DC offsets expose cuts/fades; tones expose resampling.
        string sourcePath = Path.Combine(output, "synthetic-source.wav");
        const int rate = 32000, frames = rate * 4;
        using (var file = File.Create(sourcePath)) using (var w = new BinaryWriter(file))
        {
            w.Write("RIFF"u8); w.Write(frames * 2 + 36); w.Write("WAVEfmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(rate); w.Write(rate * 2); w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8); w.Write(frames * 2);
            for (int i = 0; i < frames; i++) w.Write((short)(14000 + 4000 * Math.Sin(2 * Math.PI * 330 * i / rate)));
        }
        var source = AudioSource.Open(sourcePath); string sourceHash = Digest(sourcePath);
        var targets = library.Tracks.Where(t => t.Mapped).Select(t => new MusicTarget(library, t)).ToArray(); Assert(targets.Length == 24, "Expected all 24 targets to validate.");
        // Probe every observed rate/format through the actual Windows codec, including differing pair rates.
        foreach (var profile in targets.SelectMany(t => t.Formats))
        {
            var encoded = NativeMusicEncoder.Encode(new byte[1024 * 4], profile); var checkedAudio = ManagedDecoder.Decode(encoded);
            Assert(checkedAudio.Source.Frames == 1024 && checkedAudio.Source.Format.SequenceEqual(profile.Format), "A target's encoder profile failed.");
        }
        var target = targets.First(t => t.Formats[0].Rate != t.Formats[1].Rate);
        var edits = new EditSettings(.25, 1.25, 3.75, -6, .1, .1, .05);
        var rendered = MusicEditing.Render(source, target, edits);
        foreach (var segment in new[] { rendered.Intro, rendered.Loop })
        {
            Assert(segment.Frames % 128 == 0 && segment.Channels == 2, "Output not native-block-aligned stereo.");
            Assert(Math.Abs(segment.TimingAdjustmentMs) <= 128d / segment.Rate * 500 + .04, "Unexpected alignment change.");
            Assert(segment.ClippedSamples == 0, "Reduced gain clipped.");
            for (int i = 0; i < segment.Pcm.Length; i += 4) Assert(BitConverter.ToInt16(segment.Pcm, i) == BitConverter.ToInt16(segment.Pcm, i + 2), "Mono expansion differs between channels.");
        }
        Assert(BitConverter.ToInt16(rendered.Intro.Pcm, 0) == 0 && BitConverter.ToInt16(rendered.Loop.Pcm, rendered.Loop.Pcm.Length - 2) == 0, "Fades do not reach zero.");
        Assert(rendered.Loop.CrossfadeFrames == (int)Math.Round(.05 * target.Formats[1].Rate), "Crossfade length mismatch.");
        var clipping = MusicEditing.Render(source, target, edits with { GainDb = 24 }); Assert(clipping.Intro.ClippedSamples > 0, "Clipping was not detected.");
        var joined = MusicEditing.Join(rendered.Intro, rendered.Loop); Assert(joined.Rate == rendered.Loop.Rate && Math.Abs(joined.LoopStart - rendered.Intro.Seconds) <= 1d / joined.Rate, "Mixed-rate transition duration changed.");
        Rejected(() => MusicEditing.Render(source, target, edits with { Start = -1 }));
        Rejected(() => MusicEditing.Render(source, target, edits with { LoopStart = 4 }));
        Rejected(() => MusicEditing.Render(source, target, edits with { Crossfade = 2 }));
        Rejected(() => MusicEditing.Render(source, target, edits with { GainDb = double.NaN }));
        Rejected(() => MusicEditing.Render(source, target, edits, new CancellationToken(true)));
        Rejected(() => MusicGeneration.Generate(source, target, edits, game));
        var generated = MusicGeneration.Generate(source, target, edits, Path.Combine(output, "generated"));
        var results = new List<object>();
        for (int i = 0; i < generated.Files.Length; i++)
        {
            var native = Wave.Parse(File.ReadAllBytes(generated.Files[i])); var decoded = ManagedDecoder.Decode(File.ReadAllBytes(generated.Files[i]));
            Assert(BitConverter.ToInt32(native.Sampler!, 48) == native.Frames - 1 && BitConverter.ToInt32(native.Sampler!, 44) == 0, "Sampler bounds stale.");
            Assert(native.Format.SequenceEqual(target.Formats[i].Format), "Target format changed.");
            string decodedPath = Path.Combine(output, $"ffmpeg-{i}.pcm");
            var psi = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true };
            foreach (string arg in new[] { "-v", "error", "-xerror", "-y", "-i", generated.Files[i], "-f", "s16le", decodedPath }) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!; string errors = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert(process.ExitCode == 0, errors);
            Assert(File.ReadAllBytes(decodedPath).SequenceEqual(decoded.Pcm), "Independent FFmpeg output differs.");
            results.Add(new { file = generated.Files[i], native.Rate, native.Frames, native.Block, pcmSha256 = Convert.ToHexString(SHA256.HashData(decoded.Pcm)), ffmpegExact = true });
        }
        // Reject relinking a changed source instead of silently using the in-memory copy.
        Assert(Digest(sourcePath) == sourceHash, "Source changed during rendering or conversion.");
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        File.AppendAllText(sourcePath, "changed"); Rejected(() => source.AssertUnchanged()); File.WriteAllBytes(sourcePath, sourceBytes);
        Assert(Digest(archive) == originalHash, "Game music archive changed.");
        // Silent device check: intro must play once, followed by repeated loop without the intro.
        using (var player = new AudioPlayback(new byte[44100 * 4], 44100, 2))
        {
            player.Start(0, true, .6, 1, paused: true, includeIntro: true); Assert(player.Position == 0, "Intro skipped."); player.Resume();
            bool sawIntro = false, sawLoop = false, sawWrap = false; double last = 0;
            for (int i = 0; i < 55; i++)
            {
                double p = player.Position; if (p > .01 && p < .59) sawIntro = true; if (p >= .6) sawLoop = true;
                if (sawLoop) { Assert(p >= .6 && p < 1.001, "Repeated loop escaped into intro."); if (p < last - .05) sawWrap = true; }
                last = p; await Task.Delay(50);
            }
            Assert(sawIntro && sawLoop && sawWrap, "Intro-to-loop device transition was not observed.");
        }
        File.WriteAllText(Path.Combine(output, "editor-results.json"), JsonSerializer.Serialize(new { supportedTargets = targets.Length, syntheticSourceOriginalHash = sourceHash, cutsFadesCrossfadeGain = true, invalidEditsRejected = true, cancellation = true, staleSourceRejected = true, nativeMetadata = true, sourcePreservedDuringGeneration = true, gameMusicUnchanged = true, mixedRateAudition = true, silentIntroLoopDeviceTest = true, inGameVerified = false, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: 24 target profiles; DSP/edit validation; native conversion and exact FFmpeg comparison; source-change rejection; unchanged game archive; silent intro-to-loop transport.");
    }
}
