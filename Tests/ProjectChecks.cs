using System.Text.Json;
using SkullgirlsAudio;

static class ProjectChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Rejected(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or NotSupportedException or JsonException) { return; }
        throw new Exception("Invalid project operation was accepted.");
    }
    public static void Run(string game, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); string run = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var lib = new GameLibrary(game); var target = new MusicTarget(lib, lib.Tracks.First(t => t.Mapped)); string gameHash = ProjectFiles.Hash(lib.Music.FilePath);
        string audioFile = Path.Combine(run, "source.wav");
        using (var w = new BinaryWriter(File.Create(audioFile)))
        {
            w.Write("RIFF"u8); w.Write(32000 * 2 * 2 + 36); w.Write("WAVEfmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(32000); w.Write(64000); w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8); w.Write(128000); w.Write(new byte[128000]);
        }
        var source = AudioSource.Open(audioFile); var edits = new EditSettings(.1, .5, 1.8, -4, .02, .04, .03);
        var export = new ProjectExport("Replacement", "Test", "1.2", "Round-trip fixture");
        var project = ProjectFiles.Capture(Guid.NewGuid(), target, target.ArchiveFolder, source, edits, export);
        string path = Path.Combine(run, "test.sgawproj"); var saved = ProjectFiles.Save(path, project, source, false);
        Check(saved.Project.Edits == edits && saved.Project.Export == export && ProjectFiles.SourcePath(saved) == audioFile, "Project settings did not round trip.");
        Check(ProjectFiles.ResolveTrack(saved.Project, lib).Assets.SequenceEqual(target.Track.Assets) && ProjectFiles.TargetMatches(project, target, target.ArchiveFolder), "Exact target did not resolve.");
        Check(!ProjectFiles.TargetMatches(project with { Target = project.Target with { Hashes = [new string('0', 64), new string('0', 64)] } }, target, target.ArchiveFolder), "Changed target was trusted.");
        string copiedPath = Path.Combine(run, "copy.sgawproj"); var copied = ProjectFiles.Save(copiedPath, project, source, true);
        Check(copied.Project.Source.Copied && copied.Project.Source.Name == "source.wav" && copied.Project.CopySourceOnSave && !Path.IsPathRooted(copied.Project.Source.Path) && ProjectFiles.Hash(ProjectFiles.SourcePath(copied)) == source.Hash, "Copied source failed validation.");
        string moved = Path.Combine(run, "relocated"); Directory.CreateDirectory(moved); string relocatedPath = Path.Combine(moved, "copy.sgawproj"); File.Copy(copiedPath, relocatedPath);
        string relocatedAudio = Path.GetFullPath(copied.Project.Source.Path, moved); Directory.CreateDirectory(Path.GetDirectoryName(relocatedAudio)!); File.Copy(ProjectFiles.SourcePath(copied), relocatedAudio);
        var relocated = ProjectFiles.Load(relocatedPath); Check(ProjectFiles.Hash(ProjectFiles.SourcePath(relocated)) == source.Hash, "Relative source failed after relocating project and source folder.");
        string draft = Path.Combine(run, "draft.sgawproj"); var unfinished = project with { Edits = edits with { LoopStart = 0 } }; ProjectFiles.WriteAtomic(draft, unfinished);
        Check(ProjectFiles.Load(draft).Project.Edits.LoopStart == 0, "Incomplete cuts were lost from draft.");
        File.WriteAllText(path + ".interrupted.tmp", "partial staged write"); Check(ProjectFiles.Load(path).Hash == saved.Hash, "Interrupted staging affected saved project.");
        var revised = ProjectFiles.Save(path, project with { Edits = edits with { GainDb = -8 } }, source, false, saved.Hash);
        Check(revised.Project.Edits.GainDb == -8 && revised.Hash != saved.Hash, "Atomic update did not replace project.");
        File.AppendAllText(path, "\n"); string external = File.ReadAllText(path); Rejected(() => ProjectFiles.Save(path, project, source, false, revised.Hash)); Check(File.ReadAllText(path) == external, "External project change was overwritten.");
        Rejected(() => ProjectFiles.Save(Path.Combine(game, "blocked.sgawproj"), project, source, true));
        Rejected(() => ProjectFiles.WriteAtomic(path, project with { Schema = 99 }));
        Rejected(() => ProjectFiles.WriteAtomic(path, project with { Edits = edits with { End = 4000 } }));
        string malformed = Path.Combine(run, "malformed.sgawproj"); File.WriteAllText(malformed, "{\"Schema\":1}"); Rejected(() => ProjectFiles.Load(malformed));
        File.WriteAllText(malformed, "{partial"); Rejected(() => ProjectFiles.Load(malformed));
        byte[] original = File.ReadAllBytes(audioFile); File.AppendAllText(audioFile, "changed"); Rejected(() => ProjectFiles.Save(path, project, source, false)); File.WriteAllBytes(audioFile, original);
        File.AppendAllText(ProjectFiles.SourcePath(copied), "changed"); string oldCopyProject = ProjectFiles.Hash(copiedPath); Rejected(() => ProjectFiles.Save(copiedPath, project, source, true, copied.Hash)); Check(ProjectFiles.Hash(copiedPath) == oldCopyProject, "Bad copied source replaced project.");
        var missing = saved with { Project = saved.Project with { Source = saved.Project.Source with { Path = "missing.wav" } } }; Check(!File.Exists(ProjectFiles.SourcePath(missing)), "Missing source was silently substituted.");
        Check(ProjectFiles.Hash(audioFile) == source.Hash && ProjectFiles.Hash(lib.Music.FilePath) == gameHash, "Source or game modified.");
        Check(!Directory.EnumerateFiles(run, "*.tmp", SearchOption.AllDirectories).Any(f => !f.EndsWith(".interrupted.tmp")), "Temporary project files leaked.");
        File.WriteAllText(Path.Combine(output, "project-results.json"), JsonSerializer.Serialize(new { schema = 1, settingsRoundTrip = true, exactTargetIdentity = true, changedTargetRejected = true, copiedSourceHash = true, relocatedRelativeSource = true, incompleteDraftRecovery = true, interruptedStaging = true, atomicReplace = true, externalChangeProtection = true, malformedAndFutureVersionsRejected = true, changedAndMissingSourcesDetected = true, corruptedCopyRejected = true, gameAndSourceUnchanged = true, run }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: project round-trip, copied/relocated sources, draft recovery, atomic updates, conflict/version/corruption checks; source and game unchanged.");
    }
}
