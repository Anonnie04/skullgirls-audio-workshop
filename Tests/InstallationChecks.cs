using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SkullgirlsAudio;

static class InstallationChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Rejected(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException) { return; } throw new Exception("Unsafe install operation accepted."); }
    public static void Run(string game, string output)
    {
        MusicInstallation.GameClosed(); output = Path.GetFullPath(output); Directory.CreateDirectory(output); string run = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var real = new GameLibrary(game); var track = real.Tracks.Where(t => t.Mapped).OrderBy(t => t.Name).First(); var original = new MusicTarget(real, track); string realHash = ProjectFiles.Hash(real.Music.FilePath), exeHash = ProjectFiles.Hash(Path.Combine(game, "SkullGirls.exe"));
        string audio = Path.Combine(run, "source.wav");
        using (var w = new BinaryWriter(File.Create(audio)))
        { w.Write("RIFF"u8); w.Write(192000 + 36); w.Write("WAVEfmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(32000); w.Write(64000); w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8); w.Write(192000); w.Write(new byte[192000]); }
        var source = AudioSource.Open(audio); var edits = new EditSettings(.1, .7, 2.8, -6, .02, .02, .03); string store = Path.Combine(run, "backups");
        var scenarios = new List<string>();
        (string game, string archive, MusicTarget target, GeneratedPair pair, MusicInstallation engine, string hash) Fixture(string label)
        {
            string folder = Path.Combine(run, label), archive = Path.Combine(folder, "data01", "music-win.gfs"); Directory.CreateDirectory(Path.GetDirectoryName(archive)!); File.WriteAllBytes(Path.Combine(folder, "SkullGirls.exe"), []);
            WriteGfs(archive, new() { [track.Assets[0]] = original.Originals[0], [track.Assets[1]] = original.Originals[1], ["music/preexisting-mod.wav"] = File.ReadAllBytes(audio) });
            var target = new MusicTarget(new GameLibrary(folder), track); var pair = MusicGeneration.Generate(source, target, edits, Path.Combine(run, "generated"));
            return (folder, archive, target, pair, new MusicInstallation(store), ProjectFiles.Hash(archive));
        }
        var first = Fixture("success"); var prepared = first.engine.Prepare(first.target, first.pair, source, edits);
        Check(ProjectFiles.Hash(first.archive) == first.hash && ProjectFiles.Hash(first.engine.BackupPath(prepared)) == first.hash, "Prepare changed active music or backup differs.");
        Rejected(() => first.engine.Prepare(first.target, first.pair, source, edits));
        var applied = first.engine.Apply(first.game, prepared.Id); Check(applied.State == InstallState.Applied && ProjectFiles.Hash(first.archive) == applied.InstalledHash, "Apply failed.");
        var installed = new Gfs(first.archive); for (int i = 0; i < 2; i++) Check(installed.Read(track.Assets[i]).SequenceEqual(File.ReadAllBytes(first.pair.Files[i])), "Installed replacement differs.");
        Check(installed.Read("music/preexisting-mod.wav").SequenceEqual(File.ReadAllBytes(audio)), "Preexisting mod payload changed.");
        first.engine.Restore(first.game, prepared.Id); Check(ProjectFiles.Hash(first.archive) == first.hash && first.engine.Current(first.game) == null && File.Exists(first.engine.BackupPath(prepared)), "Restore/baseline retention failed."); scenarios.Add("apply-restore-preexisting-mod-baseline");
        foreach (string phase in new[] { "Preparing", "BackupVerified", "Prepared", "BeforeApplyReplace", "AfterApplyReplace", "BeforeRestoreReplace", "AfterRestoreReplace" })
        {
            var f = Fixture("interrupted-" + phase); InstallationRecord r = null;
            f.engine.Checkpoint = p => { if (p == phase) throw new IOException("Simulated interruption: " + phase); };
            if (phase is "Preparing" or "BackupVerified" or "Prepared") Rejected(() => f.engine.Prepare(f.target, f.pair, source, edits));
            else
            {
                r = f.engine.Prepare(f.target, f.pair, source, edits);
                if (phase.Contains("Restore")) { f.engine.Apply(f.game, r.Id); Rejected(() => f.engine.Restore(f.game, r.Id)); }
                else Rejected(() => f.engine.Apply(f.game, r.Id));
            }
            f.engine.Checkpoint = null; r = f.engine.Current(f.game)!; Check(r != null, "Interrupted journal missing.");
            // Recreate the engine, as a fresh application process would.
            var restarted = new MusicInstallation(store); restarted.Recover(f.game, r.Id);
            Check(ProjectFiles.Hash(f.archive) == f.hash && restarted.Current(f.game) == null, "Restart recovery failed: " + phase);
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(f.archive)!, ".sgaw-*").Any(), "Staging residue after recovery: " + phase); scenarios.Add("recover-" + phase);
        }
        var partial = Fixture("partial-restore"); var pr = partial.engine.Prepare(partial.target, partial.pair, source, edits); partial.engine.Apply(partial.game, pr.Id);
        partial.engine.Checkpoint = p => { if (p == "BeforeRestoreReplace") throw new IOException("Interrupt"); }; Rejected(() => partial.engine.Restore(partial.game, pr.Id));
        File.WriteAllBytes(Path.Combine(partial.game, "data01", ".sgaw-" + pr.Id.ToString("N") + ".restore.stage"), [1, 2]);
        new MusicInstallation(store).Recover(partial.game, pr.Id); Check(ProjectFiles.Hash(partial.archive) == partial.hash, "Partial restore stage was not repaired."); scenarios.Add("partial-restore-stage");
        foreach (bool afterApply in new[] { false, true })
        {
            var f = Fixture("external-" + afterApply); var r = f.engine.Prepare(f.target, f.pair, source, edits); if (afterApply) f.engine.Apply(f.game, r.Id);
            File.AppendAllText(f.archive, "external tool change"); string externalHash = ProjectFiles.Hash(f.archive);
            if (afterApply) Rejected(() => f.engine.Restore(f.game, r.Id)); else Rejected(() => f.engine.Apply(f.game, r.Id));
            Rejected(() => new MusicInstallation(store).Recover(f.game, r.Id)); Check(ProjectFiles.Hash(f.archive) == externalHash && ProjectFiles.Hash(f.engine.BackupPath(r)) == f.hash, "External change overwritten."); scenarios.Add(afterApply ? "external-change-before-restore" : "stale-prepare");
        }
        var corrupt = Fixture("corrupt-backup"); var cr = corrupt.engine.Prepare(corrupt.target, corrupt.pair, source, edits); corrupt.engine.Apply(corrupt.game, cr.Id); string installedHash = ProjectFiles.Hash(corrupt.archive);
        File.AppendAllText(corrupt.engine.BackupPath(cr), "corrupted"); Rejected(() => corrupt.engine.Restore(corrupt.game, cr.Id)); Check(ProjectFiles.Hash(corrupt.archive) == installedHash, "Corrupt backup installed."); scenarios.Add("corrupt-backup-refused");
        var disk = Fixture("disk-space"); disk.engine.FreeBytes = _ => 0; Rejected(() => disk.engine.Prepare(disk.target, disk.pair, source, edits)); Check(disk.engine.Current(disk.game) == null && ProjectFiles.Hash(disk.archive) == disk.hash, "Disk failure changed installation."); scenarios.Add("insufficient-disk");
        var cancelled = Fixture("cancelled"); using var token = new CancellationTokenSource(); cancelled.engine.Checkpoint = p => { if (p == "BackupVerified") token.Cancel(); };
        Rejected(() => cancelled.engine.Prepare(cancelled.target, cancelled.pair, source, edits, token.Token)); Check(cancelled.engine.Current(cancelled.game) == null && ProjectFiles.Hash(cancelled.archive) == cancelled.hash, "Cancellation changed music."); scenarios.Add("cancel-after-backup");
        var race = Fixture("race"); var rr = race.engine.Prepare(race.target, race.pair, source, edits); race.engine.Checkpoint = p => { if (p == "BeforeApplyReplace") File.AppendAllText(race.archive, "raced"); };
        Rejected(() => race.engine.Apply(race.game, rr.Id)); Check(ProjectFiles.Hash(race.archive) != race.hash && new FileInfo(race.archive).Length > 0, "Last-moment change was overwritten."); scenarios.Add("last-moment-external-change");
        var competing = Fixture("concurrent"); var co = competing.engine.Prepare(competing.target, competing.pair, source, edits); competing.engine.Checkpoint = p => { if (p == "BeforeApplyReplace") Rejected(() => new MusicInstallation(store).Apply(competing.game, co.Id)); };
        competing.engine.Apply(competing.game, co.Id); competing.engine.Checkpoint = null; competing.engine.Restore(competing.game, co.Id); scenarios.Add("concurrent-writer-lock");
        var changedExe = Fixture("changed-exe"); var ce = changedExe.engine.Prepare(changedExe.target, changedExe.pair, source, edits); File.WriteAllText(Path.Combine(changedExe.game, "SkullGirls.exe"), "new build"); Rejected(() => changedExe.engine.Apply(changedExe.game, ce.Id)); changedExe.engine.Recover(changedExe.game, ce.Id); Check(ProjectFiles.Hash(changedExe.archive) == changedExe.hash && changedExe.engine.Current(changedExe.game) == null, "Changed executable was trusted or stale preparation could not be discarded."); scenarios.Add("changed-build-refused");
        var stageChanged = Fixture("changed-stage"); var sc = stageChanged.engine.Prepare(stageChanged.target, stageChanged.pair, source, edits); File.AppendAllText(Path.Combine(stageChanged.game, "data01", ".sgaw-" + sc.Id.ToString("N") + ".apply.stage"), "tampered"); Rejected(() => stageChanged.engine.Apply(stageChanged.game, sc.Id)); stageChanged.engine.Recover(stageChanged.game, sc.Id); Check(ProjectFiles.Hash(stageChanged.archive) == stageChanged.hash, "Changed stage installed."); scenarios.Add("changed-stage-refused");
        var restoreRace = Fixture("restore-race"); var re = restoreRace.engine.Prepare(restoreRace.target, restoreRace.pair, source, edits); restoreRace.engine.Apply(restoreRace.game, re.Id); restoreRace.engine.Checkpoint = p => { if (p == "BeforeRestoreReplace") File.AppendAllText(restoreRace.archive, "external change"); }; Rejected(() => restoreRace.engine.Restore(restoreRace.game, re.Id)); string restoreExternal = ProjectFiles.Hash(restoreRace.archive); restoreRace.engine.Checkpoint = null; Rejected(() => restoreRace.engine.Recover(restoreRace.game, re.Id)); Check(ProjectFiles.Hash(restoreRace.archive) == restoreExternal, "Restore race overwrote external data."); scenarios.Add("last-moment-restore-change");
        var journalBad = Fixture("journal-corruption"); var jb = journalBad.engine.Prepare(journalBad.target, journalBad.pair, source, edits); File.WriteAllText(Path.Combine(Path.GetDirectoryName(journalBad.engine.BackupPath(jb)), "journal.json"), "{partial");
        try { journalBad.engine.Apply(journalBad.game, jb.Id); throw new Exception("Bad journal accepted."); } catch (JsonException) { } Check(ProjectFiles.Hash(journalBad.archive) == journalBad.hash, "Malformed journal changed game."); scenarios.Add("malformed-journal-refused");
        string loader = Path.Combine(game, "CoreMessaging.dll");
        if (File.Exists(loader) && GameLibrary.ActiveArchiveFolder(game) == "data02")
        {
            var location = Fixture("changed-location"); var lo = location.engine.Prepare(location.target, location.pair, source, edits); Directory.CreateDirectory(Path.Combine(location.game, "data02")); File.Copy(loader, Path.Combine(location.game, "CoreMessaging.dll")); Rejected(() => location.engine.Apply(location.game, lo.Id)); location.engine.Recover(location.game, lo.Id); Check(ProjectFiles.Hash(location.archive) == location.hash && location.engine.Current(location.game) == null, "Changed active location trusted or stale preparation could not be discarded."); scenarios.Add("changed-active-folder-refused");
        }
        // Exercise the full real archive without writing to the installation.
        string noop = Path.Combine(run, "real-noop.gfs"); MusicRepack.Build(real.Music.FilePath, noop, new Dictionary<string, byte[]>()); Check(ProjectFiles.Hash(noop) == realHash, "Real no-op archive is not byte-identical.");
        string rebuilt = Path.Combine(run, "real-replacement.gfs"); var replacements = track.Assets.Select((asset, i) => (asset, i)).ToDictionary(p => p.asset, p => File.ReadAllBytes(first.pair.Files[p.i])); MusicRepack.Build(real.Music.FilePath, rebuilt, replacements); MusicRepack.Verify(real.Music.FilePath, rebuilt, replacements); scenarios.Add("real-archive-all-untouched-payloads");
        Check(ProjectFiles.Hash(real.Music.FilePath) == realHash && ProjectFiles.Hash(Path.Combine(game, "SkullGirls.exe")) == exeHash && ProjectFiles.Hash(audio) == source.Hash, "Real installation or source changed.");
        File.WriteAllText(Path.Combine(output, "installation-results.json"), JsonSerializer.Serialize(new { run, scenarios, realArchiveNoopExact = true, originalAndSourceUnchanged = true, liveGameInstalled = false, inGameVerified = false, backupStore = store }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {scenarios.Count} isolated installation/recovery scenarios; real archive no-op and payload verification; real game/source unchanged.");
    }
    static void WriteGfs(string path, Dictionary<string, byte[]> assets)
    {
        using var stream = new MemoryStream();
        void I32(int n) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, n); stream.Write(b); }
        void I64(long n) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, n); stream.Write(b); }
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); I64(bytes.Length); stream.Write(bytes); }
        I32(0); Text("Reverge Package File"); Text("1.1"); I64(assets.Count);
        foreach (var p in assets) { Text(p.Key); I64(p.Value.Length); I32(1); }
        int offset = checked((int)stream.Position); stream.Position = 0; I32(offset); stream.Position = offset; foreach (var p in assets) stream.Write(p.Value); File.WriteAllBytes(path, stream.ToArray());
    }
}
