using System.Text.Json;
using SkullgirlsAudio;

if (args.Length == 2 && args[0] == "measure")
{
    using var plan = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[1], "playback-plan.json")));
    var record = plan.RootElement.GetProperty("record").Deserialize<InstallationRecord>()!;
    var original = new Gfs(plan.RootElement.GetProperty("backup").GetString()!);
    double Rms(byte[] wav)
    {
        var pcm = ManagedDecoder.Decode(wav).Pcm; double sum = 0;
        for (int i = 0; i < pcm.Length; i += 2) { double sample = BitConverter.ToInt16(pcm, i); sum += sample * sample; }
        return Math.Sqrt(sum / (pcm.Length / 2));
    }
    var files = Directory.GetFiles(Path.Combine(args[1], "generated"), "*.wav", SearchOption.AllDirectories);
    var measurements = record.Assets.Select(asset => {
        string file = files.Single(f => Path.GetFileName(f) == Path.GetFileName(asset));
        return new { asset, gainDb = 20 * Math.Log10(Rms(File.ReadAllBytes(file)) / Rms(original.Read(asset))) };
    }).ToArray();
    string json = JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(args[1], "measured-gain.json"), json); Console.WriteLine(json); return;
}

// Local diagnostic fixture. Preparation does not replace the active archive.
if (args.Length == 4 && args[0] is "prepare" or "prepare-menu")
{
    bool menu = args[0] == "prepare-menu";
    string game = Path.GetFullPath(args[1]), store = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
    ProjectFiles.OutsideGame(output, game); var engine = new MusicInstallation(store);
    MusicInstallation.GameClosed(); if (engine.Current(game) != null) throw new IOException("Restore/recover the current Workshop transaction first.");
    if (Directory.Exists(output)) throw new IOException("Use a fresh diagnostic output folder."); Directory.CreateDirectory(output);
    var library = new GameLibrary(game);
    var donor = new MusicTarget(library, library.Tracks.Single(t => t.Mapped && t.Name.Trim() == "Moonlit Melee"));
    // Explicit local diagnostic target; menu filenames are not in core/music.ini.
    // Keep this out of the public configuration-backed pair catalogue until runtime confirmed.
    var target = menu ? new MusicTarget(library, new Track("Main menu — Moonlit Melee diagnostic", "", ["music/menu_intro.wav", "music/menu_loop.wav"], true)) : donor;
    var intro = ManagedDecoder.Decode(donor.Originals[0]); var loop = ManagedDecoder.Decode(donor.Originals[1]); var combined = MusicEditing.Join(intro.Pcm, intro.Source.Rate, loop.Pcm, loop.Source.Rate, 2);
    string sourcePath = Path.Combine(output, "Moonlit-Melee-local-diagnostic.wav");
    using (var w = new BinaryWriter(File.Create(sourcePath)))
    { w.Write("RIFF"u8); w.Write(combined.Pcm.Length + 36); w.Write("WAVEfmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)2); w.Write(combined.Rate); w.Write(combined.Rate * 4); w.Write((ushort)4); w.Write((ushort)16); w.Write("data"u8); w.Write(combined.Pcm.Length); w.Write(combined.Pcm); }
    var source = AudioSource.Open(sourcePath); var edits = new EditSettings(0, combined.LoopStart, combined.Duration, menu ? 0 : -12);
    var pair = MusicGeneration.Generate(source, target, edits, Path.Combine(output, "generated")); var record = engine.Prepare(target, pair, source, edits);
    if (!menu) {
        var project = ProjectFiles.Capture(Guid.NewGuid(), target, target.ArchiveFolder, source, edits, new("Moonlit Melee — local -12 dB diagnostic", "Local diagnostic", "0.1", "Current local game audio attenuated by 12 dB. In-game playback is unverified."));
        ProjectFiles.Save(Path.Combine(output, "diagnostic.sgawproj"), project, source, false);
    }
    File.WriteAllText(Path.Combine(output, "playback-plan.json"), JsonSerializer.Serialize(new { schema = 1, record, backup = engine.BackupPath(record), store, intendedGainDb = menu ? 0 : -12, stage = menu ? "Main menu" : "New_Meridian", introSeconds = pair.Rendered.Intro.Seconds, loopSeconds = pair.Rendered.Loop.Seconds, activeArchiveChanged = false, inGameVerified = false,
        checklist = new[] { "Compare stage entry to baseline at unchanged music volume", "Listen through the intro transition", "Listen through at least two full loops", "Leave and re-enter stage", "Check an unaffected stage", "Close game and Restore; verify baseline playback" } }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { record.Id, record.State, record.Track, archive = MusicInstallation.ArchivePath(record), backup = engine.BackupPath(record), output, pair.Rendered.Intro.Seconds, loopSeconds = pair.Rendered.Loop.Seconds, activeArchiveChanged = false })); return;
}
if (args.Length == 2 && args[0] is "apply" or "restore" or "recover")
{
    using var plan = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[1], "playback-plan.json"))); var root = plan.RootElement;
    var record = root.GetProperty("record").Deserialize<InstallationRecord>()!; var engine = new MusicInstallation(root.GetProperty("store").GetString()!);
    var result = args[0] == "apply" ? engine.Apply(record.GameFolder, record.Id) : args[0] == "restore" ? engine.Restore(record.GameFolder, record.Id) : engine.Recover(record.GameFolder, record.Id);
    File.WriteAllText(Path.Combine(args[1], args[0] + "-result.json"), JsonSerializer.Serialize(new { record = result, archiveHash = ProjectFiles.Hash(MusicInstallation.ArchivePath(result)), inGameVerified = false }, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(result)); return;
}
throw new ArgumentException("Usage: PlaybackCheck prepare/prepare-menu GAME BACKUP_STORE FRESH_OUTPUT | apply/restore/recover/measure OUTPUT");
