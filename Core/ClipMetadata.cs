using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkullgirlsAudio;

public record ClipDetails(string Title, string Description, string Source, string Dialogue = "");

public sealed class ClipMetadata
{
    private readonly Dictionary<string, (string Key, string Text)> english = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> japanese = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> references = new(StringComparer.Ordinal);
    public ClipMetadata(Gfs? core)
    {
        if (core == null) return;
        Dictionary<string, string> Read(string path)
        {
            if (!core.Entries.Any(e => e.Path == path)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(core.Read(path)).TrimStart('\uFEFF')) ?? [];
        }
        var en = Read("core/localization/en_loc.json");
        foreach (var group in en.GroupBy(p => Normalize(p.Key)).Where(g => g.Key.Length > 0 && g.Count() == 1))
        { var pair = group.Single(); english.Add(group.Key, (pair.Key, pair.Value)); }
        foreach (var pair in Read("core/localization/jp_loc.json")) japanese[pair.Key] = pair.Value;
        foreach (var entry in core.Entries.Where(e => e.Path.StartsWith("core/arcade/story/") && e.Path.EndsWith(".ini") && e.Size < 1024 * 1024))
        {
            string? key = null;
            foreach (string raw in Encoding.UTF8.GetString(core.Read(entry.Path)).Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("Text: ", StringComparison.Ordinal))
                {
                    key = line[6..].Trim();
                    if (!references.TryGetValue(key, out var values)) references[key] = values = [];
                    if (!values.Contains(entry.Path)) values.Add(entry.Path);
                }
                else if (key != null && line.StartsWith("Header: ", StringComparison.Ordinal))
                {
                    string value = entry.Path + " · scene header: " + line[8..].Trim();
                    if (!references[key].Contains(value)) references[key].Add(value);
                }
            }
        }
    }
    private static string Normalize(string key) => Regex.Replace(key.ToLowerInvariant(), "[^a-z0-9]", "");
    public ClipDetails Describe(Track track, AudioSegment clip)
    {
        if (track.Mapped)
        {
            int i = Array.FindIndex(track.Assets, a => a.Equals(clip.Asset, StringComparison.OrdinalIgnoreCase));
            string role = track.Assets.Length == 2 ? i == 0 ? "Intro" : "Loop" : "Segment " + (i + 1);
            return new(track.Name + " · " + role, $"{role} asset for {track.Name}.\nStage references: {(track.Stages.Length == 0 ? "none listed" : track.Stages)}\nSource: core/music.ini → Track / Files.", "Game music configuration");
        }
        if (track.Archive.StartsWith("story_vo", StringComparison.OrdinalIgnoreCase))
        {
            string stem = Regex.Replace(Path.GetFileNameWithoutExtension(clip.Asset), @"_(EN|JP)$", "", RegexOptions.IgnoreCase);
            if (english.TryGetValue(Normalize(stem), out var match))
            {
                bool jp = track.Archive.Contains("_jp", StringComparison.OrdinalIgnoreCase);
                bool translated = jp && japanese.TryGetValue(match.Key, out _);
                string text = translated ? japanese[match.Key] : match.Text;
                string file = translated ? "jp_loc.json" : "en_loc.json";
                string language = jp && !translated ? "English reference text (Japanese translation unavailable)" : jp ? "Japanese game dialogue" : "English game dialogue";
                string refs = references.TryGetValue(match.Key, out var entries) ? "\nScene references:\n" + string.Join("\n", entries.Take(6)) + (entries.Count > 6 ? $"\n…and {entries.Count - 6} more" : "") : "";
                return new(track.Name, $"{language}:\n{text.Trim()}\n\nDialogue key: {match.Key}\nSource: core/localization/{file}{refs}\n\nLinked by a unique filename/key match. This is game text, not an audio transcription.", "Game localization · unique filename match", text.Trim());
            }
        }
        if (track.Bank)
        {
            string number = Regex.Match(clip.Title, @"\d{4}").Value;
            return new(track.Name + " · Clip " + number, $"Unidentified embedded WAV {number}.\n{track.Description}\n\nBank: {clip.Asset}\nByte offset: 0x{clip.Offset:X}\nNo verified sound-slot or spoken-line mapping is attached. You can save a personal label after identifying it.", "Bank identity known · individual clip unidentified");
        }
        return new(track.Name, track.Description + "\n\nAsset: " + clip.Asset, "Filename-derived label · meaning not independently verified");
    }
    public ClipDetails? StoryDetails(Track track) => track.Archive.StartsWith("story_vo", StringComparison.OrdinalIgnoreCase)
        ? Describe(track, new(track.Name, track.Assets[0], 0, null)) : null;
    public static string? EmbeddedTitle(byte[] bytes)
    {
        // Only import explicit RIFF INFO title fields. Production software and copyright
        // tags are not titles or descriptions of a spoken line.
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)) return null;
        for (long p = 12; p + 8 <= bytes.Length;)
        {
            uint length = BitConverter.ToUInt32(bytes, (int)p + 4); long start = p + 8, end = start + length;
            if (end > bytes.Length) return null;
            if (bytes.AsSpan((int)p, 4).SequenceEqual("LIST"u8) && length >= 4 && bytes.AsSpan((int)start, 4).SequenceEqual("INFO"u8))
                for (long q = start + 4; q + 8 <= end;)
                {
                    uint n = BitConverter.ToUInt32(bytes, (int)q + 4); if (q + 8 + n > end) break;
                    if (bytes.AsSpan((int)q, 4).SequenceEqual("INAM"u8) && n is > 0 and < 4096)
                    {
                        string value = Encoding.UTF8.GetString(bytes, (int)q + 8, (int)n).TrimEnd('\0').Trim();
                        if (value.Length > 0) return value;
                    }
                    q += 8L + n + n % 2;
                }
            p = end + length % 2;
        }
        return null;
    }
    public static string Identity(Track track, AudioSegment clip, ReadOnlySpan<byte> bytes) =>
        $"{track.Archive.ToLowerInvariant()}|{clip.Asset.ToLowerInvariant()}|{clip.Offset}|{Convert.ToHexString(SHA256.HashData(bytes))}";
}

public record SavedClipLabel(string Title, string Description);
public sealed class ClipLabels
{
    private readonly string file;
    private Dictionary<string, SavedClipLabel> labels = new(StringComparer.Ordinal);
    public string? LoadWarning { get; private set; }
    public ClipLabels(string file)
    {
        this.file = Path.GetFullPath(file);
        if (!File.Exists(this.file)) return;
        try
        {
            if (new FileInfo(this.file).Length > 4 * 1024 * 1024) throw new IOException("Label catalog exceeds 4 MB.");
            labels = JsonSerializer.Deserialize<Dictionary<string, SavedClipLabel>>(File.ReadAllText(this.file)) ?? [];
            if (labels.Any(p => p.Value == null || string.IsNullOrWhiteSpace(p.Value.Title) || p.Value.Title.Length > 180 || p.Value.Description == null || p.Value.Description.Length > 4000)) throw new JsonException("Invalid saved label fields.");
        }
        catch (Exception ex) when (ex is IOException or JsonException) { labels = []; LoadWarning = "Saved labels could not be read: " + ex.Message; }
    }
    public SavedClipLabel? Find(string key) => labels.GetValueOrDefault(key);
    public void Save(string key, SavedClipLabel? label)
    {
        if (LoadWarning != null) throw new IOException("The existing label catalog needs review before it can be overwritten.");
        if (label != null && (string.IsNullOrWhiteSpace(label.Title) || label.Title.Length > 180 || label.Description.Length > 4000)) throw new ArgumentException("Enter a title up to 180 characters and description up to 4,000 characters.");
        var updated = new Dictionary<string, SavedClipLabel>(labels, StringComparer.Ordinal);
        if (label == null) updated.Remove(key); else updated[key] = label;
        string content = JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(content) > 4 * 1024 * 1024) throw new IOException("Label catalog exceeds 4 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, file, true); labels = updated; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
