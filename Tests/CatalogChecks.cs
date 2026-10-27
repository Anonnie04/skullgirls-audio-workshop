using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsAudio;

internal static class CatalogChecks
{
    public static void Run(string game, string output)
    {
        Directory.CreateDirectory(output); string samples = Path.Combine(output, "samples"); Directory.CreateDirectory(samples);
        var library = new GameLibrary(game);
        string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)); }
        var before = library.Sources.ToDictionary(s => s.Name, s => Hash(library.Archive(s.Name).FilePath));
        if (library.Sources.Count != 6 || library.Warnings.Count != 0) throw new Exception("Local audio archive inventory changed; review discovery.");
        var seen = new HashSet<string>(); var decoded = new List<object>(); var sources = new List<object>(); int headers = 0, banks = 0;
        foreach (var source in library.Sources)
        {
            int sourceClips = 0, sourceBanks = 0, plain = 0;
            foreach (var track in source.Tracks)
            {
                var segments = library.Segments(track);
                if (track.Bank) { banks++; sourceBanks++; if (segments.Length == 0) throw new Exception("Bank has no readable WAVs: " + track.Name); }
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i]; string key = track.Archive + "/" + segment.Asset + "/" + segment.Offset;
                    if (!seen.Add(key)) continue;
                    byte[] bytes = library.Read(track, segment); Wave wave = Wave.Parse(bytes); headers++; sourceClips++;
                    bool sample = track.Bank ? i == 0 || i == segments.Length - 1 : plain++ < 3;
                    if (!sample) continue;
                    var audio = ManagedDecoder.Decode(bytes);
                    if (audio.Pcm.Length != wave.Frames * wave.Channels * 2) throw new Exception("PCM frame mismatch.");
                    string file = $"clip-{decoded.Count:000}.wav"; File.WriteAllBytes(Path.Combine(samples, file), bytes);
                    decoded.Add(new { Path = key, sourceFile = file, pcmBytes = audio.Pcm.Length, pcmSha256 = Convert.ToHexString(SHA256.HashData(audio.Pcm)), wave.Channels, wave.Codec });
                }
            }
            sources.Add(new { source.Name, source.Title, items = source.Tracks.Length, embeddedBanks = sourceBanks, audioClips = sourceClips });
            Console.WriteLine($"{source.Title}: {sourceClips:N0} WAVs, {sourceBanks} banks");
        }
        if (headers != 21542 || banks != 49) throw new Exception($"Local clip inventory changed: {headers} clips, {banks} banks.");
        foreach (var s in library.Sources) if (before[s.Name] != Hash(library.Archive(s.Name).FilePath)) throw new Exception("Installed archive changed.");
        // RIFF signatures inside truncated/malformed data must not become selectable clips.
        if (BankWaves.Index("xxRIFF\xff\xff\xff\xffWAVE"u8.ToArray(), "fixture").Length != 0) throw new Exception("Truncated bank candidate accepted.");
        File.WriteAllText(Path.Combine(output, "catalog-results.json"), JsonSerializer.Serialize(new { audioArchives = sources, headerChecks = headers, banks, gameArchivesUnchanged = true, assets = decoded }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {headers:N0} WAV headers, {decoded.Count} representative decodes, {banks} banks, six installed archive hashes unchanged.");
    }
}
