using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsAudio;

if (args.Length == 2 && args[0] == "--release") { ReleaseChecks.Run(args[1]); return; }
if (args.Length == 3 && args[0] == "--imports") { ImportChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--installation") { InstallationChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--exports") { ExportChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--projects") { ProjectChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--editor") { await EditorChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--catalog") { CatalogChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--organization") { OrganizationChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--metadata") { MetadataChecks.Run(args[1], args[2]); return; }
if (args.Length != 2) throw new ArgumentException("Usage: Tests GAME OUTPUT_DIRECTORY");
Directory.CreateDirectory(args[1]);
if (args[0] != "--transport")
{
string Digest(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
void ExpectRejected(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException or ArgumentException) { return; } throw new Exception("Malformed input was accepted."); }
var lib = new GameLibrary(args[0]);
string archive = lib.Music.FilePath, originalHash = Digest(archive);
if (lib.Tracks.Count(t => t.Mapped) != 24 || lib.Music.Entries.Count != 100) throw new Exception("Local fixture inventory changed; review research assumptions.");
foreach (var t in lib.Tracks.Where(t => t.Mapped)) foreach (string asset in t.Assets)
    if (!lib.Music.Entries.Any(e => e.Path.Equals(asset, StringComparison.OrdinalIgnoreCase))) throw new Exception("Unresolved track reference.");
var results = new List<object>();
foreach (var e in lib.Music.Entries)
{
    var bytes = lib.Music.Read(e.Path); var wave = Wave.Parse(bytes); var decoded = ManagedDecoder.Decode(bytes);
    if (decoded.Pcm.Length != wave.Frames * wave.Channels * 2 || decoded.Peaks().Length != 1500) throw new Exception("Decode mismatch.");
    var pcmWave = decoded.AsWave();
    if (BitConverter.ToInt32(pcmWave, 40) != decoded.Pcm.Length || pcmWave.Length != decoded.Pcm.Length + 44) throw new Exception("PCM preview header invalid.");
    results.Add(new { e.Path, wave.Rate, wave.Frames, pcmBytes = decoded.Pcm.Length, pcmSha256 = Convert.ToHexString(SHA256.HashData(decoded.Pcm)) });
    if (e.Path == "music/skull02_1.wav") File.WriteAllBytes(Path.Combine(args[1], "managed-preview.wav"), pcmWave);
}
var valid = lib.Music.Read(lib.Music.Entries[0].Path);
ExpectRejected(() => Wave.Parse(valid[..^1]));
var invalid = (byte[])valid.Clone(); invalid[20] = 1; ExpectRejected(() => Wave.Parse(invalid));
invalid = (byte[])valid.Clone(); invalid[32] = 0; invalid[33] = 0; ExpectRejected(() => Wave.Parse(invalid));
string truncated = Path.Combine(args[1], "truncated.gfs"); File.WriteAllBytes(truncated, new byte[4]); ExpectRejected(() => new Gfs(truncated));
if (Digest(archive) != originalHash) throw new Exception("Installed archive changed.");
File.WriteAllText(Path.Combine(args[1], "decoder-results.json"), JsonSerializer.Serialize(new { decodedAssets = results.Count, malformedInputsRejected = 4, gameMusicUnchanged = true, assets = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: 100 assets decoded through copied managed decoder; metadata/sample counts validated; four malformed inputs rejected; music archive unchanged.");
}

// Exercise the real output device using silence only.
using (var transport = new SkullgirlsAudioWorkshop.AudioPlayback(new byte[44100 * 2 * 2 * 3], 44100, 2))
{
    transport.Start(0);
    for (int i = 0; i < 30 && transport.Position <= 0; i++) await Task.Delay(100);
    if (transport.Position <= 0) throw new Exception($"Device position did not advance: active={transport.Active}, finished={transport.Finished}.");
    transport.Pause(); double paused = transport.Position; await Task.Delay(160);
    if (!transport.Paused || Math.Abs(transport.Position - paused) > .03) throw new Exception("Pause did not hold.");
    transport.Start(1.1, paused: true); await Task.Delay(120);
    if (!transport.Paused || Math.Abs(transport.Position - 1.1) > .03) throw new Exception("Paused seek failed.");
    transport.Resume(); await Task.Delay(180); if (transport.Position <= 1.1) throw new Exception("Resume failed.");
    transport.Start(.6, true, .4, .7); await Task.Delay(700);
    double previous = transport.Position; int wraps = 0;
    for (int i = 0; i < 8; i++) { double p = transport.Position; if (p < .4 || p >= .701) throw new Exception("Native loop escaped selection."); if (p < previous - .04) wraps++; previous = p; await Task.Delay(80); }
    if (wraps == 0) throw new Exception("Loop did not wrap on the device.");
    transport.Start(3); if (!transport.Finished) throw new Exception("Seek to end did not finish.");
    transport.Start(2.95); await Task.Delay(250); if (!transport.Finished) throw new Exception("Natural completion not detected.");
    try { transport.Start(0, true, 1, .5); throw new Exception("Invalid range accepted."); } catch (ArgumentException) { }
    transport.Dispose(); transport.Dispose();
}
File.WriteAllText(Path.Combine(args[1], "transport-results.json"), JsonSerializer.Serialize(new { silentDeviceTest = true, position = true, pause = true, pausedSeek = true, resume = true, queuedRangeRepeat = true, naturalCompletion = true, seekToEnd = true, invalidRangeRejected = true, doubleDispose = true }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: silent device transport — progress, pause, paused seek, resume, queued repeat, completion, invalid range and disposal.");
