using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkullgirlsAudio;

static class CollectionChecks
{
    public static void Run(string game, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); string run = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var library = new GameLibrary(game); string gameHash = ProjectFiles.Hash(library.Music.FilePath);
        var targets = library.Tracks.Where(t => t.Mapped).Take(2).Select(t => new MusicTarget(library, t)).ToArray();
        if (targets.Length != 2 || targets[0].Track.Assets.Intersect(targets[1].Track.Assets).Any()) throw new Exception("Collection fixture needs two non-overlapping targets.");
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(Action action, string message)
        { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException) { return; } throw new Exception(message); }
        string first = Path.Combine(run, "source.mp3"), second = Path.Combine(run, "source.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "音 test ; source.mp3"), first);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "音 test ; source.flac"), second);
        var source1 = AudioSource.Open(first); var source2 = AudioSource.Open(second);
        new EditSettings(.1, .11, .12).Validate(source1); // Decimal 10 ms cuts must survive floating-point subtraction.
        var a = ProjectFiles.Capture(Guid.NewGuid(), targets[0], targets[0].ArchiveFolder, source1, new(.1, .6, 1.9, -4, .02, .03, .02));
        var b = ProjectFiles.Capture(Guid.NewGuid(), targets[1], targets[1].ArchiveFolder, source2, new(.2, .7, 1.6, -2, .01, .02, .03, SkipIntro: true));
        var project = new ModProject(1, Guid.NewGuid(), DateTimeOffset.UtcNow, new("Two-song mod", "Fixture", "1.2", "Independent song settings"), [a, b]);
        string file = Path.Combine(run, "album.sgawmod"); var saved = ModProjects.Save(file, project); var reopened = ModProjects.ResolveSources(saved);
        Check(reopened.Songs.Length == 2 && reopened.Songs[0].Edits == a.Edits && reopened.Songs[1].Edits == b.Edits && reopened.Songs[1].Edits.SkipIntro, "Settings did not round-trip");
        Check(Path.GetExtension(reopened.Songs[0].Source.Path) == ".mp3" && Path.GetExtension(reopened.Songs[1].Source.Path) == ".flac", "Copied extensions lost");
        foreach (var song in reopened.Songs) Check(ProjectFiles.Hash(song.Source.Path) == song.Source.Hash, "Copied source differs");
        File.AppendAllText(file, " "); string externalHash = ProjectFiles.Hash(file); Reject(() => ModProjects.Save(file, reopened, saved.Hash), "Externally changed mod overwritten"); Check(ProjectFiles.Hash(file) == externalHash, "Conflict modified the external file");
        string relocated = Path.Combine(run, "relocated"); Directory.CreateDirectory(relocated); File.Copy(file, Path.Combine(relocated, "album.sgawmod")); Directory.CreateDirectory(Path.Combine(relocated, "album.sgawmod.sources"));
        foreach (string copied in Directory.GetFiles(file + ".sources")) File.Copy(copied, Path.Combine(relocated, "album.sgawmod.sources", Path.GetFileName(copied)));
        var moved = ModProjects.ResolveSources(ModProjects.Load(Path.Combine(relocated, "album.sgawmod"))); foreach (var song in moved.Songs) Check(ProjectFiles.Hash(song.Source.Path) == song.Source.Hash, "Relocated source failed");
        string draft = Path.Combine(run, "draft.sgawmod"); ModProjects.WriteDraft(draft, reopened); Check(ModProjects.Load(draft).Project.Songs.Length == 2, "Draft failed");
        string draftHash = ProjectFiles.Hash(draft); File.AppendAllText(draft, " "); Reject(() => ModProjects.WriteDraft(draft, reopened, draftHash), "Changed draft overwritten");
        Reject(() => ModProjects.Validate(project with { Songs = [a, a with { Id = Guid.NewGuid() }] }), "Conflicting assets accepted");
        Reject(() => ModProjects.Validate(project with { Songs = [a, b with { Target = b.Target with { ArchiveFolder = "other" } }] }), "Mixed folders accepted");
        Reject(() => ModProjects.Validate(project with { Songs = [a, b with { Target = b.Target with { Assets = ["music/../outside.wav", "music/other.wav"] } }] }), "Traversal accepted");
        Reject(() => ModProjects.Validate(project with { Schema = 99 }), "Unknown schema accepted");
        var legacy = JsonNode.Parse(JsonSerializer.Serialize(a))!; legacy["Edits"]!.AsObject().Remove("SkipIntro"); string legacyPath = Path.Combine(run, "legacy.sgawproj"); File.WriteAllText(legacyPath, legacy.ToJsonString()); Check(!ProjectFiles.Load(legacyPath).Project.Edits.SkipIntro, "Legacy project gained bypass");
        var generated = ModProjects.Generate(reopened, library, Path.Combine(run, "cache"));
        Check(generated[1].Output.Rendered.Intro.Frames == targets[1].Formats[0].SamplesPerBlock && ManagedDecoder.Decode(File.ReadAllBytes(generated[1].Output.Files[0])).Pcm.All(v => v == 0), "Bypass lead-in is not one silent native block");
        Check(Math.Abs(generated[1].Output.Rendered.Loop.Seconds - (b.Edits.End - b.Edits.Crossfade)) < .003, "Full-song crop duration incorrect");
        string zip = Path.Combine(run, "album.zip"), folder = Path.Combine(run, "album-folder"); ModProjects.Export(zip, true, reopened, generated, library); ModProjects.Export(folder, false, reopened, generated, library);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Check(archive.Entries.Count == 6, "Wrong mod entry count");
            foreach (var song in generated) for (int i = 0; i < 2; i++) { using var stream = archive.GetEntry("music-win.gfs/" + song.Output.Assets[i])!.Open(); Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)) == song.Output.Hashes[i], "ZIP audio differs"); Check(ProjectFiles.Hash(Path.Combine(folder, "music-win.gfs", song.Output.Assets[i])) == song.Output.Hashes[i], "Folder audio differs"); }
            using var report = new StreamReader(archive.GetEntry("export.json")!.Open()); string text = report.ReadToEnd(); Check(!text.Contains(run) && !text.Contains(game), "Private paths leaked into export");
            using var mod = archive.GetEntry("mod.json")!.Open(); using var json = JsonDocument.Parse(mod); Check(json.RootElement.GetProperty("name").GetString() == reopened.Details.Name, "Shared metadata lost");
        }
        Reject(() => ModProjects.Export(zip, true, reopened, generated, library), "Existing export overwritten");
        var changed = reopened with { Songs = [reopened.Songs[0] with { Edits = a.Edits with { GainDb = -5 } }, reopened.Songs[1]] };
        Reject(() => ModProjects.Export(Path.Combine(run, "stale.zip"), true, changed, generated, library), "Stale settings accepted");
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); Reject(() => ModProjects.Export(Path.Combine(run, "cancel.zip"), true, reopened, generated, library, canceled.Token), "Cancellation ignored");
        string copiedHash = ProjectFiles.Hash(reopened.Songs[1].Source.Path); File.AppendAllText(reopened.Songs[1].Source.Path, "tamper");
        Reject(() => ModProjects.Export(Path.Combine(run, "bad-source.zip"), true, reopened, generated, library), "Changed source accepted"); File.Copy(second, reopened.Songs[1].Source.Path, true);
        string generatedFile = generated[0].Output.Files[0]; byte[] originalGenerated = File.ReadAllBytes(generatedFile); File.AppendAllText(generatedFile, "tamper");
        Reject(() => ModProjects.Export(Path.Combine(run, "bad-output.zip"), true, reopened, generated, library), "Changed output accepted"); File.WriteAllBytes(generatedFile, originalGenerated);
        // Only isolated synthetic installations may be mutated by this test.
        if (new FileInfo(Path.Combine(game, "SkullGirls.exe")).Length == 0)
        {
            var engine = new MusicInstallation(Path.Combine(run, "backups")); var record = engine.PrepareCollection(reopened, generated, library);
            Check(record.Assets.Length == 4 && record.Songs?.Length == 2 && ProjectFiles.Hash(library.Music.FilePath) == gameHash, "Collection preparation changed active archive or lost songs");
            engine.Apply(game, record.Id); var installed = new Gfs(library.Music.FilePath);
            foreach (var song in generated) for (int i = 0; i < 2; i++) Check(installed.Read(song.Output.Assets[i]).SequenceEqual(File.ReadAllBytes(song.Output.Files[i])), "Applied song differs");
            engine.Restore(game, record.Id); Check(ProjectFiles.Hash(library.Music.FilePath) == gameHash, "Batch Restore not identical");
            library = new GameLibrary(game); engine = new MusicInstallation(Path.Combine(run, "restart-backups")); record = engine.PrepareCollection(reopened, generated, library);
            engine.Checkpoint = p => { if (p == "AfterApplyReplace") throw new IOException("Injected collection interruption"); }; Reject(() => engine.Apply(game, record.Id), "Interruption not injected");
            new MusicInstallation(Path.Combine(run, "restart-backups")).Recover(game, record.Id); Check(ProjectFiles.Hash(library.Music.FilePath) == gameHash, "Collection recovery failed");
        }
        // Source markers prove bypass begins at frame zero and crop excludes the loud tail.
        string markers = Path.Combine(run, "markers.wav");
        using (var writer = new BinaryWriter(File.Create(markers)))
        { writer.Write("RIFF"u8); writer.Write(44100 * 8 + 36); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(44100); writer.Write(176400); writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(44100 * 8); for (int frame = 0; frame < 88200; frame++) { short value = (short)(frame < 22050 ? 1200 : frame < 66150 ? -2400 : 12000); writer.Write(value); writer.Write(value); } }
        var markerSource = AudioSource.Open(markers); var bypass = MusicEditing.Render(markerSource, targets[0], new(.1, .7, 1.4, SkipIntro: true));
        Check(BitConverter.ToInt16(bypass.Loop.Pcm, 0) > 1000 && bypass.Loop.PeakBeforeClip < .15, "Bypass failed to start at zero or crop retained tail");
        Check(ProjectFiles.Hash(library.Music.FilePath) == gameHash && ProjectFiles.Hash(first) == source1.Hash && ProjectFiles.Hash(second) == source2.Hash, "Original sources or archive changed");
        File.WriteAllText(Path.Combine(output, "collection-results.json"), JsonSerializer.Serialize(new { run, songs = 2, settingsRoundTrip = true, originalFormatsPreserved = true, relocatedSources = true, draftsAndConflicts = true, legacyProjectCompatible = true, exportsVerified = true, staleAndTamperRejected = true, cropAndBypassRendered = true, syntheticApplyRestoreRecovery = new FileInfo(Path.Combine(game, "SkullGirls.exe")).Length == 0, originalAndSourceUnchanged = true, bypassInGameVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: multi-song projects, copied/relocated sources, drafts, folder/ZIP exports, stale/conflict checks, crop/bypass rendering and isolated batch Apply/Restore/recovery.");
    }
}
