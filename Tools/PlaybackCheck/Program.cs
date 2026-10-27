using System.Text.Json;
using SkullgirlsAudio;

if (args.Length == 5 && args[0] == "prepare-soundtrack")
{
    string game = Path.GetFullPath(args[1]), store = Path.GetFullPath(args[2]), inventory = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
    MusicInstallation.GameClosed(); ProjectFiles.OutsideGame(output, game); var engine = new MusicInstallation(store);
    if (engine.Current(game) != null) throw new IOException("Restore/recover the current transaction first.");
    if (Directory.Exists(output)) throw new IOException("Use a fresh test output folder."); Directory.CreateDirectory(output);
    var library = new GameLibrary(game); var all = library.Tracks.Where(t => t.Mapped && t.Assets.Length == 2 && t.Archive == "music-win.gfs").ToArray();
    var uses = all.SelectMany(t => t.Assets).GroupBy(a => a, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    var tracks = all.Where(t => t.Assets.All(a => uses[a] == 1)).OrderBy(t => t.Name.Trim() == "Moonlit Melee" ? 0 : t.Name.Trim() == "Clear Breeze" ? 1 : 2).ThenBy(t => t.Stages.Length == 0 ? 1 : 0).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    using var json = JsonDocument.Parse(File.ReadAllBytes(inventory));
    var files = json.RootElement.EnumerateArray().Where(e => e.GetProperty("withinImportLimit").GetBoolean()).Select(e => e.GetProperty("path").GetString()!).ToArray();
    if (files.Length < tracks.Length) throw new IOException("Not enough supported source songs for this test mapping.");
    var songs = new List<AudioProject>(); var mapping = new List<object>();
    double Rms(byte[] pcm) { double sum = 0; var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm); foreach (short value in samples) sum += (double)value * value; return Math.Sqrt(sum / Math.Max(1, samples.Length)); }
    for (int i = 0; i < tracks.Length; i++)
    {
        Console.WriteLine($"Inspecting {i + 1}/{tracks.Length}: {tracks[i].Name.Trim()} ← {Path.GetFileName(files[i])}");
        var source = AudioSource.Open(files[i]); var target = new MusicTarget(library, tracks[i]); int rate = source.Audio.Source.Rate, frames = source.Audio.Source.Frames;
        int last = frames - 1, earliest = Math.Max(0, frames - rate * 3);
        while (last > earliest && Math.Abs((int)BitConverter.ToInt16(source.Audio.Pcm, last * 4)) <= 12 && Math.Abs((int)BitConverter.ToInt16(source.Audio.Pcm, last * 4 + 2)) <= 12) last--;
        int endFrame = frames - last - 1 >= rate / 4 ? last + 1 : frames;
        if (target.Formats[1].Rate == rate) endFrame = Math.Max(128, endFrame / 128 * 128);
        double end = endFrame / (double)rate; bool skip = tracks[i].Name.Trim() != "Clear Breeze";
        double loopStart = Math.Round(rate * 8d / 128) * 128 / rate;
        double originalRms = Rms(ManagedDecoder.Decode(target.Originals[1]).Pcm), sourceRms = Rms(source.Audio.Pcm);
        double gain = sourceRms > 0 && originalRms > 0 ? Math.Clamp(20 * Math.Log10(originalRms / sourceRms), -18, 0) : -6;
        var edits = new EditSettings(0, loopStart, end, gain, .03, .03, 0, skip);
        var song = ProjectFiles.Capture(Guid.NewGuid(), target, target.ArchiveFolder, source, edits);
        songs.Add(song); mapping.Add(new { target = target.Track.Name.Trim(), stages = target.Track.Stages, source = Path.GetFileName(files[i]), originalSeconds = source.Audio.Source.Seconds, cropEndSeconds = end, trimmedSeconds = source.Audio.Source.Seconds - end, skipIntro = skip, loopStartSeconds = skip ? 0 : loopStart, gainDb = gain, assets = target.Track.Assets });
    }
    var project = new ModProject(1, Guid.NewGuid(), DateTimeOffset.UtcNow, new("Guilty Gear Strive — local final test", "Local test", "1.0", "21-pair local soundtrack replacement test. Full-song mode except Clear Breeze's separate intro/loop. Sources retained locally."), songs.ToArray(), true);
    string projectPath = Path.Combine(output, "Guilty-Gear-Final-Test.sgawmod");
    Console.WriteLine("Saving the collection and verified original-format source copies…");
    var saved = ModProjects.Save(projectPath, project); project = ModProjects.ResolveSources(saved);
    File.WriteAllText(Path.Combine(output, "song-mapping.json"), JsonSerializer.Serialize(mapping, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Generating and independently decoding all replacements…");
    var generated = ModProjects.Generate(project, library, Path.Combine(output, "generated"), progress: Console.WriteLine);
    if (generated.Any(s => s.Output.Rendered.Intro.ClippedSamples + s.Output.Rendered.Loop.ClippedSamples != 0)) throw new IOException("Clipping found; review processing before installing.");
    Console.WriteLine("Verifying the complete multi-song mod folder…");
    ModProjects.Export(Path.Combine(output, "mod-folder"), false, project, generated, library);
    Console.WriteLine("Preparing one backed-up and verified music archive…");
    var record = engine.PrepareCollection(project, generated, library);
    File.WriteAllText(Path.Combine(output, "playback-plan.json"), JsonSerializer.Serialize(new { schema = 1, record, store, projectPath, backup = engine.BackupPath(record), mapping, activeArchiveChanged = false, inGameVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { record.Id, record.State, songs = songs.Count, backup = engine.BackupPath(record), projectPath, output, originalHash = record.OriginalHash, installedHash = record.InstalledHash })); return;
}

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
