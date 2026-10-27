using System.Text.Json;
using SkullgirlsAudio;

static class ImportChecks
{
    public static void Run(string game, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var lib = new GameLibrary(game); string archiveHash = ProjectFiles.Hash(lib.Music.FilePath);
        var target = new MusicTarget(lib, lib.Tracks.First(t => t.Mapped && t.Name.Trim() == "Moonlit Melee"));
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or OperationCanceledException) { return; }
            throw new Exception(message);
        }
        var cases = new[] {
            ("mp3", "libmp3lame", "2"), ("flac", "flac", "2"), ("ogg", "libvorbis", "2"),
            ("opus", "libopus", "2"), ("m4a", "aac", "2"), ("aac", "aac", "2"),
            ("wma", "wmav2", "2"), ("aiff", "pcm_s16be", "2"), ("24.wav", "pcm_s24le", "2"),
            ("float.wav", "pcm_f32le", "2"), ("mono.mp3", "libmp3lame", "1"), ("surround.flac", "flac", "6")
        };
        var results = new List<object>();
        foreach (var item in cases)
        {
            string path = Path.Combine(output, "音 test ; source." + item.Item1);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", Path.GetFileName(path)), path, true);
            string hash = ProjectFiles.Hash(path); var source = AudioSource.Open(path);
            Check(source.Audio.Source.Rate == 44100 && source.Audio.Source.Channels == 2, "Import normalization failed");
            Check(Math.Abs(source.Audio.Source.Seconds - 2) < .12 && source.Audio.Peaks().Max() > .01, "Wrong duration or silent import");
            var edits = new EditSettings(0, .5, 1.8);
            var generated = MusicGeneration.Generate(source, target, edits, Path.Combine(output, "converted"));
            Check(generated.Files.All(f => Wave.Parse(File.ReadAllBytes(f)).FormatTag == 2), "Native generation failed");
            string projectPath = path + ".sgawproj";
            var project = ProjectFiles.Capture(Guid.NewGuid(), target, target.ArchiveFolder, source, edits);
            var saved = ProjectFiles.Save(projectPath, project, source, true);
            string copied = ProjectFiles.SourcePath(saved); var reopened = AudioSource.Open(copied);
            Check(Path.GetExtension(copied) == Path.GetExtension(path) && reopened.Hash == hash, "Project did not preserve the original format and bytes");
            Check(ProjectFiles.Hash(path) == hash && source.Audio.Pcm.SequenceEqual(reopened.Audio.Pcm), "Source changed or project decode differs");
            results.Add(new { format = item.Item1, inputChannels = item.Item3, seconds = source.Audio.Source.Seconds, imported = true, generated = true, copiedProjectReopened = true });
        }
        string broken = Path.Combine(output, "broken.mp3"); File.WriteAllText(broken, "invalid audio");
        Reject(() => AudioSource.Open(broken), "Damaged input accepted");
        string unknown = Path.Combine(output, "unsupported.txt"); File.WriteAllText(unknown, "invalid audio");
        Reject(() => AudioSource.Open(unknown), "Unsupported extension accepted");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Reject(() => AudioSource.Open(broken, canceled.Token), "Canceled import accepted");
        string longFile = Path.Combine(output, "too-long.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "too-long.flac"), longFile, true);
        Reject(() => AudioSource.Open(longFile), "Oversized decoded audio was silently truncated");
        using var cancelRunning = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        Reject(() => AudioSource.Open(longFile, cancelRunning.Token), "Running import ignored cancellation");
        string huge = Path.Combine(output, "oversize.wav"); using (var file = File.Create(huge)) file.SetLength(128L * 1024 * 1024 + 1);
        Reject(() => AudioSource.Open(huge), "Oversized input accepted"); File.Delete(huge);
        Check(ProjectFiles.Hash(lib.Music.FilePath) == archiveHash, "Game archive changed");
        File.WriteAllText(Path.Combine(output, "import-results.json"), JsonSerializer.Serialize(new { formats = results, corruptRejected = true, unknownRejected = true, cancellationPassed = true, decodedLimitPassed = true, inputLimitPassed = true, gameUnchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Imports: 12 format/layout cases, native generation and project reopen passed; error/cancellation/size guards passed.");
    }
}
