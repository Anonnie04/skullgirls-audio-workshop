using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SkullgirlsAudio;

static class ExportChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Rejected(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException) { return; }
        throw new Exception("Invalid export operation was accepted.");
    }
    public static void Run(string game, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); string run = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var library = new GameLibrary(game); var target = new MusicTarget(library, library.Tracks.First(t => t.Mapped)); string gameHash = ProjectFiles.Hash(library.Music.FilePath);
        string sourceFile = Path.Combine(run, "source.wav");
        using (var w = new BinaryWriter(File.Create(sourceFile)))
        {
            w.Write("RIFF"u8); w.Write(128000 + 36); w.Write("WAVEfmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(32000); w.Write(64000); w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8); w.Write(128000);
            for (int i = 0; i < 64000; i++) w.Write((short)(6000 * Math.Sin(i * 2 * Math.PI * 440 / 32000)));
        }
        var source = AudioSource.Open(sourceFile); var edits = new EditSettings(.1, .6, 1.9, -6, .02, .02, .03); var details = new ProjectExport("Blue record — 青", "Workshop test", "1.2.3", "Intro and loop replacement\nUnicode metadata fixture."); var id = Guid.NewGuid();
        var pair = MusicGeneration.Generate(source, target, edits, Path.Combine(run, "cache"));
        string native = MusicExport.Native(run, pair, source, target, edits, details, id);
        for (int i = 0; i < 2; i++) Check(ProjectFiles.Hash(Path.Combine(native, target.Track.Archive, target.Track.Assets[i])) == pair.Hashes[i], "Native WAV changed on export.");
        Check(Directory.GetFiles(native, "*", SearchOption.AllDirectories).Length == 3, "Native export has unexpected contents.");
        string zipPath = Path.Combine(run, "replacement.zip"); MusicExport.Package(zipPath, pair, source, target, edits, details, id);
        string zipHash = ProjectFiles.Hash(zipPath);
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Check(zip.Entries.Count == 4 && zip.GetEntry("enabled") == null, "ZIP has unexpected or enabling contents.");
            using var stream = zip.GetEntry("mod.json")!.Open(); using var doc = JsonDocument.Parse(stream); var root = doc.RootElement;
            Check(root.GetProperty("name").GetString() == details.Name && root.GetProperty("author").GetString() == details.Author && root.GetProperty("desc").GetString() == details.Description && root.GetProperty("version").GetString() == details.Version && root.GetProperty("id").GetString() == "local.audio-workshop." + id.ToString("N"), "ZIP metadata differs.");
            for (int i = 0; i < 2; i++)
            {
                using var audio = zip.GetEntry(target.Track.Archive + "/" + target.Track.Assets[i])!.Open(); using var bytes = new MemoryStream(); audio.CopyTo(bytes);
                Check(bytes.ToArray().SequenceEqual(File.ReadAllBytes(pair.Files[i])), "ZIP changed the verified WAV bytes."); ManagedDecoder.Decode(bytes.ToArray());
            }
            using var report = zip.GetEntry("export.json")!.Open(); using var reader = new StreamReader(report); string text = reader.ReadToEnd();
            Check(!text.Contains(JsonSerializer.Serialize(sourceFile).Trim('"'), StringComparison.OrdinalIgnoreCase) && !text.Contains(JsonSerializer.Serialize(game).Trim('"'), StringComparison.OrdinalIgnoreCase) && !text.Contains("source.wav"), "Export leaked a local source path or source file.");
            using var reportJson = JsonDocument.Parse(text); Check(!reportJson.RootElement.GetProperty("inGameVerified").GetBoolean(), "Export claims in-game validation.");
        }
        Rejected(() => MusicExport.Package(zipPath, pair, source, target, edits, details, id)); Check(ProjectFiles.Hash(zipPath) == zipHash, "Existing export overwritten.");
        Rejected(() => MusicExport.Package(Path.Combine(run, "invalid.zip"), pair, source, target, edits, details with { Version = "latest" }, id));
        Rejected(() => MusicExport.Native(game, pair, source, target, edits, details, id));
        Rejected(() => MusicExport.Package(Path.Combine(game, "blocked.zip"), pair, source, target, edits, details, id));
        Rejected(() => MusicExport.Package(Path.Combine(run, "stale.zip"), pair, source, target, edits with { GainDb = -4 }, details, id));
        Rejected(() => MusicExport.Package(Path.Combine(run, "cancelled.zip"), pair, source, target, edits, details, id, new CancellationToken(true)));
        Rejected(() => MusicExport.Native(Path.Combine(run, "cancelled"), pair, source, target, edits, details, id, new CancellationToken(true)));
        var project = ProjectFiles.Capture(id, target, target.ArchiveFolder, source, edits, details); var saved = ProjectFiles.Save(Path.Combine(run, "export-details.sgawproj"), project, source, false); Check(saved.Project.Export == details, "Export details not preserved in project.");
        File.AppendAllText(pair.Files[0], "tampered"); Rejected(() => MusicExport.Package(Path.Combine(run, "tampered.zip"), pair, source, target, edits, details, id));
        Check(ProjectFiles.Hash(zipPath) == zipHash && ProjectFiles.Hash(sourceFile) == source.Hash && ProjectFiles.Hash(library.Music.FilePath) == gameHash, "Source/game/export changed unexpectedly.");
        Check(!Directory.EnumerateFileSystemEntries(run, "*.staging", SearchOption.AllDirectories).Any() && !File.Exists(Path.Combine(run, "stale.zip")) && !File.Exists(Path.Combine(run, "tampered.zip")), "Failed export published partial output.");
        File.WriteAllText(Path.Combine(output, "export-results.json"), JsonSerializer.Serialize(new { native, package = zipPath, metadataRoundTrip = true, exactNativeAndZipWavBytes = true, independentDecode = true, noSourceOrLocalPaths = true, existingExportPreserved = true, staleEditsRejected = true, tamperedWavRejected = true, cancellation = true, gameDestinationRejected = true, noPartialOutput = true, sourceAndGameUnchanged = true, inGameVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: native and ZIP exports, metadata, exact verified audio, conflict/stale/tamper/cancel checks; source and game unchanged.");
    }
}
