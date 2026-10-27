using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SkullgirlsAudio;

public record Entry(string Path, long Offset, long Size, int Alignment = 1, long SizeField = 0);
public sealed class Gfs
{
    public string FilePath { get; }
    public List<Entry> Entries { get; } = [];
    public int DataOffset { get; }
    private readonly long length;
    private readonly DateTime modified;
    public Gfs(string path)
    {
        FilePath = path;
        var stamp = new FileInfo(path); length = stamp.Length; modified = stamp.LastWriteTimeUtc;
        using var s = File.OpenRead(path);
        using var r = new BinaryReader(s);
        long Number(int bytes) { var b = r.ReadBytes(bytes); return bytes == 4 ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt64BigEndian(b); }
        string Text() { long n = Number(8); if (n < 0 || n > 32768 || n > length - s.Position) throw new InvalidDataException("Invalid archive string."); return new UTF8Encoding(false, true).GetString(r.ReadBytes((int)n)); }
        long start = Number(4), cursor = start; DataOffset = checked((int)start);
        if (Text() != "Reverge Package File" || Text() != "1.1") throw new InvalidDataException("Unsupported GFS version.");
        long count = Number(8);
        if (count < 0 || count > 1000000 || start < s.Position || start > length) throw new InvalidDataException("Invalid archive table.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string name = Text(); long sizeField = s.Position, size = Number(8), alignment = Number(4);
            if (size < 0 || alignment < 1 || alignment > 1048576 || !names.Add(name)) throw new InvalidDataException("Unsupported archive entry.");
            cursor = checked(cursor + (alignment - cursor % alignment) % alignment);
            if (cursor > length || size > length - cursor) throw new InvalidDataException("Truncated archive.");
            Entries.Add(new(name, cursor, size, (int)alignment, sizeField)); cursor += size;
        }
        if (s.Position > start) throw new InvalidDataException("Archive table overlaps audio.");
    }
    public byte[] Read(string path)
    {
        var entry = Entries.SingleOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Asset not present: " + path);
        if (entry.Size > 128 * 1024 * 1024) throw new NotSupportedException("Audio preview input exceeds 128 MB.");
        return ReadSlice(path, 0, (int)entry.Size);
    }
    public byte[] ReadSlice(string path, int offset, int size)
    {
        var stamp = new FileInfo(FilePath);
        if (stamp.Length != length || stamp.LastWriteTimeUtc != modified) throw new IOException("Archive changed. Reopen the installation before previewing.");
        var entry = Entries.SingleOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Asset not present: " + path);
        if (offset < 0 || size < 0 || size > 128 * 1024 * 1024 || offset > entry.Size || size > entry.Size - offset) throw new InvalidDataException("Invalid audio slice.");
        using var s = File.OpenRead(FilePath); s.Position = entry.Offset + offset;
        var bytes = new byte[size]; s.ReadExactly(bytes); return bytes;
    }
}

public record Track(string Name, string Stages, string[] Assets, bool Mapped,
    string Archive = "music-win.gfs", string Category = "Music", string Description = "", bool Bank = false, string Character = "")
{
    public override string ToString() => Name;
}

public sealed class GameLibrary
{
    public static string ActiveArchiveFolder(string folder)
    {
        string loader = Path.Combine(folder, "CoreMessaging.dll"); bool recognized = false;
        if (File.Exists(loader)) { using var f = File.OpenRead(loader); recognized = Convert.ToHexString(SHA256.HashData(f)).Equals("014bfb8c48fa7afbcd585a34d814011b96b289c742482a01376f56aafa1e11d3", StringComparison.OrdinalIgnoreCase); }
        return recognized && Directory.Exists(Path.Combine(folder, "data02")) ? "data02" : "data01";
    }
    public string Folder { get; }
    public string ArchiveFolder { get; }
    public Gfs Music { get; }
    public List<Track> Tracks { get; } = [];
    public List<AudioArchive> Sources { get; } = [];
    public List<AudioArchive> Categories { get; } = [];
    public List<string> Warnings { get; } = [];
    private readonly Dictionary<string, Gfs> archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly ClipMetadata metadata;
    public GameLibrary(string folder)
    {
        Folder = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(Folder, "SkullGirls.exe"))) throw new DirectoryNotFoundException("Choose the folder containing SkullGirls.exe.");
        ArchiveFolder = ActiveArchiveFolder(Folder);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Folder, ArchiveFolder), "*.gfs").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try { var archive = new Gfs(file); archives.Add(Path.GetFileName(file), archive); }
            catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { Warnings.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        Music = archives.GetValueOrDefault("music-win.gfs") ?? throw new FileNotFoundException("Music archive not available.");
        var core = archives.GetValueOrDefault("core.gfs");
        try { metadata = new(core); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { metadata = new(null); Warnings.Add("Clip descriptions unavailable: " + ex.Message); }
        string? name = null; string stages = "";
        string config = "";
        try { if (core != null) config = Encoding.UTF8.GetString(core.Read("core/music.ini")); }
        catch (IOException ex) { Warnings.Add("Music mappings unavailable: " + ex.Message); }
        foreach (string raw in config.Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim(); if (line.StartsWith('#')) continue;
            int colon = line.IndexOf(':'); if (colon < 0) continue;
            string key = line[..colon], value = line[(colon + 1)..].Trim();
            if (key == "Track") { name = value; stages = ""; }
            else if (key == "Stage") stages = value;
            else if (key == "Files" && name != null)
            {
                Tracks.Add(new(name, stages, value.Split(',').Select(v => "music/" + v.Trim() + ".wav").ToArray(), true)); name = null;
            }
        }
        var mapped = Tracks.SelectMany(t => t.Assets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Tracks.AddRange(Music.Entries.Where(e => IsAudio(e.Path) && !mapped.Contains(e.Path)).Select(e => new Track(Readable(e.Path), "", [e.Path], false,
            Description: "Music asset. Track title and playback relationship have not been mapped.")));
        foreach (var pair in archives.OrderBy(p => p.Key == "music-win.gfs" ? 0 : 1).ThenBy(p => p.Key))
        {
            var (title, purpose) = Describe(pair.Key);
            if (pair.Key != "music-win.gfs")
                foreach (var e in pair.Value.Entries.Where(e => IsAudio(e.Path)))
                {
                    bool bank = IsBank(e.Path);
                    bool announcer = bank && Path.GetFileName(e.Path).StartsWith("hitspark.", StringComparison.OrdinalIgnoreCase)
                        || bank && Path.GetFileName(e.Path).StartsWith("hitspark_alt", StringComparison.OrdinalIgnoreCase);
                    string bankName = Readable(e.Path);
                    string character = CharacterFor(e.Path);
                    if (bank && !announcer) bankName = (character.Length > 0 ? character : Readable(e.Path).Split(" alt")[0])
                        + (Variant(e.Path) > 0 ? $" · Alternate bank {Variant(e.Path)}" : "");
                    if (!bank && pair.Key.StartsWith("story_vo", StringComparison.OrdinalIgnoreCase))
                    {
                        string file = Path.GetFileNameWithoutExtension(e.Path);
                        string language = pair.Key.Contains("_jp", StringComparison.OrdinalIgnoreCase) ? "Japanese" : "English";
                        file = System.Text.RegularExpressions.Regex.Replace(file, @"_(?:EN|JP)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        string prefix = CharacterIds.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => file.StartsWith(k, StringComparison.OrdinalIgnoreCase)) ?? "";
                        string rest = prefix.Length > 0 ? Humanize(file[prefix.Length..]).Trim() : Humanize(file);
                        bankName = (character.Length > 0 ? character + " · " : "") + rest + " · " + language;
                    }
                    if (!bank && pair.Key.Equals("ui-win.gfs", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = Path.GetFileNameWithoutExtension(e.Path).Split('.');
                        bankName = string.Join(" · ", parts.Select(p => CharacterIds.GetValueOrDefault(p.ToLowerInvariant(), UiWords.GetValueOrDefault(p.ToLowerInvariant(), Humanize(p)))));
                        if (parts[0].Equals("charconfirm", StringComparison.OrdinalIgnoreCase)) bankName += e.Path.Contains("/JP_chars/", StringComparison.OrdinalIgnoreCase) ? " · Japanese" : " · Default";
                    }
                    if (announcer)
                    {
                        string stem = Path.GetFileName(e.Path).Split('.')[0]; string variant = stem.Contains("_alt", StringComparison.OrdinalIgnoreCase) ? "Alternate " + stem[(stem.IndexOf("_alt", StringComparison.OrdinalIgnoreCase) + 4)..] : "Base";
                        bankName = "Announcers · " + (pair.Key.Contains("_jp", StringComparison.OrdinalIgnoreCase) ? "Japanese" : "Default") + " · " + variant;
                    }
                    var track = new Track(bankName, "", [e.Path], false, pair.Key, announcer ? "Announcers and shared effects" : title,
                        announcer ? "Announcer voices and shared combat effects are bundled in this hitspark bank. Select a numbered WAV. Exact voice-pack identities and clip-to-line mappings are not yet verified."
                        : bank ? "Sound bank containing character voices and/or combat effects. Select an embedded WAV by number; clip events and spoken-line meanings are not mapped."
                             : purpose + " Clip title is derived from its filename; exact spoken text and event triggers are not mapped.", bank, character);
                    var dialogue = metadata.StoryDetails(track);
                    if (dialogue?.Dialogue.Length > 0)
                    {
                        string preview = System.Text.RegularExpressions.Regex.Replace(dialogue.Dialogue, @"\s+", " ");
                        if (preview.Length > 65) preview = preview[..65] + "…";
                        track = track with { Name = track.Name + " — “" + preview + "”", Description = dialogue.Description };
                    }
                    Tracks.Add(track);
                }
            var items = Tracks.Where(t => t.Archive.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (items.Length > 0) Sources.Add(new(pair.Key, title, purpose, items));
        }
        Tracks.Sort(CompareTracks);
        for (int i = 0; i < Sources.Count; i++) { var source = Sources[i]; Sources[i] = source with { Tracks = source.Tracks.OrderBy(t => t, Comparer<Track>.Create(CompareTracks)).ToArray() }; }
        Sources.Sort((a, b) => { int c = ArchiveRank(a.Name).CompareTo(ArchiveRank(b.Name)); return c != 0 ? c : NaturalCompare(a.Title, b.Title); });
        Categories.AddRange(Sources);
        var announcers = Tracks.Where(t => t.Category == "Announcers and shared effects").ToArray();
        if (announcers.Length > 0) Categories.Insert(Math.Min(1, Categories.Count), new("characters-win.gfs / characters_jp-win.gfs", "Announcers",
            "Announcer voices share the hitspark banks with combat effects. Select a bank to browse its numbered clips. Variant and line names are not yet mapped.", announcers));
    }
    public Gfs Archive(string name) => archives[name];
    public AudioSegment[] Segments(Track track)
    {
        byte[]? bank = track.Bank ? Archive(track.Archive).Read(track.Assets[0]) : null;
        var clips = bank == null ? track.Assets.Select(a => new AudioSegment(a, a, 0, null)).ToArray()
            : BankWaves.Index(bank, track.Assets[0]);
        return clips.Select(c => { var info = metadata.Describe(track, c); return c with { Title = info.Title, Details = info,
            Identity = bank == null ? null : ClipMetadata.Identity(track, c, bank.AsSpan(c.Offset, c.Size!.Value)) }; }).ToArray();
    }
    public byte[] Read(Track track, AudioSegment segment) => segment.Size is int size
        ? Archive(track.Archive).ReadSlice(segment.Asset, segment.Offset, size) : Archive(track.Archive).Read(segment.Asset);
    private static bool IsBank(string name) => name.EndsWith(".snd-wav", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".snd-wav_jpvoice", StringComparison.OrdinalIgnoreCase);
    private static bool IsAudio(string name) => IsBank(name) || new[] { ".wav", ".ogg", ".mp3", ".flac", ".wem", ".bnk", ".fsb" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    private static string Readable(string path)
    {
        string name = path[(path.LastIndexOf('/') + 1)..]; int dot = name.LastIndexOf('.'); if (dot >= 0) name = name[..dot];
        return name.Length == 0 ? path : Humanize(name.Replace(".", " · "));
    }
    private static readonly Dictionary<string, string> CharacterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["annie"] = "Annie", ["beowulf"] = "Beowulf", ["bigband"] = "Big Band", ["blackdahlia"] = "Black Dahlia",
        ["bossmarie"] = "Marie (boss)", ["cerebella"] = "Cerebella", ["double"] = "Double", ["eliza"] = "Eliza",
        ["filia"] = "Filia", ["fukua"] = "Fukua", ["marie"] = "Marie", ["msfortune"] = "Ms. Fortune",
        ["painwheel"] = "Painwheel", ["parasoul"] = "Parasoul", ["peacock"] = "Peacock", ["robofortune"] = "Robo-Fortune",
        ["squigly"] = "Squigly", ["umbrella"] = "Umbrella", ["valentine"] = "Valentine"
    };
    private static readonly Dictionary<string, string> UiWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["charconfirm"] = "Character selection", ["charportrait"] = "Character portrait", ["stageselect"] = "Stage selection",
        ["selectconfirm"] = "Selection confirmation", ["selectslide"] = "Selection movement", ["teamiconbar"] = "Team icon bar",
        ["p1"] = "Player 1", ["p2"] = "Player 2", ["alt"] = "Alternate", ["ui"] = "UI"
    };
    private static string Humanize(string value)
    {
        value = System.Text.RegularExpressions.Regex.Replace(value.Replace('_', ' '), @"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", " ").Trim();
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }
    private static string CharacterFor(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string? key = CharacterIds.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => stem.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        if (key == null) key = stem.Split('.').FirstOrDefault(p => CharacterIds.ContainsKey(p));
        return key == null ? "" : CharacterIds[key];
    }
    private static int Variant(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path, @"_alt(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int n) ? n : 0;
    }
    private static int ArchiveRank(string name) => name.ToLowerInvariant() switch
    { "music-win.gfs" => 0, "story_vo-win.gfs" => 2, "story_vo_jp-win.gfs" => 3, "characters-win.gfs" => 4, "characters_jp-win.gfs" => 5, "ui-win.gfs" => 6, _ => 7 };
    private static int CompareTracks(Track a, Track b)
    {
        bool aa = a.Category == "Announcers and shared effects", ab = b.Category == "Announcers and shared effects";
        int c = (aa ? 1 : ArchiveRank(a.Archive)).CompareTo(ab ? 1 : ArchiveRank(b.Archive)); if (c != 0) return c;
        if (aa && ab) { c = a.Archive.Contains("_jp").CompareTo(b.Archive.Contains("_jp")); if (c != 0) return c; }
        c = NaturalCompare(a.Character, b.Character); if (c != 0) return c;
        if (a.Archive == "music-win.gfs") { c = b.Mapped.CompareTo(a.Mapped); if (c != 0) return c; }
        if (a.Bank && b.Bank) { c = Variant(a.Assets[0]).CompareTo(Variant(b.Assets[0])); if (c != 0) return c; }
        c = NaturalCompare(a.Name, b.Name); return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Assets[0], b.Assets[0]);
    }
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int startA = i, startB = j; while (i < a.Length && char.IsAsciiDigit(a[i])) i++; while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var da = a.AsSpan(startA, i - startA).TrimStart('0'); var db = b.AsSpan(startB, j - startB).TrimStart('0');
                int c = da.Length.CompareTo(db.Length); if (c == 0) c = da.SequenceCompareTo(db); if (c != 0) return c;
            }
            else { int c = char.ToUpperInvariant(a[i++]).CompareTo(char.ToUpperInvariant(b[j++])); if (c != 0) return c; }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
    private static (string Title, string Purpose) Describe(string name) => name.ToLowerInvariant() switch
    {
        "music-win.gfs" => ("Music", "Background music for stages, menus and other game sequences. Configuration-backed titles and stage references are shown where available."),
        "story_vo-win.gfs" => ("Story voices · English", "English dialogue clips for story scenes, intros and endings."),
        "story_vo_jp-win.gfs" => ("Story voices · Japanese", "Japanese dialogue clips for story scenes, intros and endings."),
        "ui-win.gfs" => ("UI and shared sound effects", "Interface sounds and shared effects, including menu cues, character-selection confirmations and crowd sounds. Individual uses depend on game events."),
        "characters-win.gfs" => ("Character voices and combat · Default", "Character voices and combat effects, including hitspark banks that bundle announcer voices with shared effects. Also available in the Announcers category."),
        "characters_jp-win.gfs" => ("Character voices and combat · Japanese", "Japanese character audio and hitspark announcer/shared-effect banks. The latter are also available in the Announcers category."),
        _ => ("Other audio · " + name, "Audio-bearing archive discovered in the selected installation. Its exact role has not been mapped.")
    };
}

public record AudioArchive(string Name, string Title, string Description, Track[] Tracks)
{
    public override string ToString() => $"{Title}  ·  {Tracks.Length:N0} items";
}
public record AudioSegment(string Title, string Asset, int Offset, int? Size, ClipDetails? Details = null, string? Identity = null)
{
    public override string ToString() => Title;
}
public static class BankWaves
{
    // Read-only inspection of self-contained RIFF/WAVE payloads. This does not parse
    // bank event tables or assign engine sound IDs, and must never be used to rebuild banks.
    public static AudioSegment[] Index(byte[] bytes, string asset)
    {
        var clips = new List<AudioSegment>(); int position = 0;
        while (position <= bytes.Length - 12)
        {
            int found = bytes.AsSpan(position).IndexOf("RIFF"u8); if (found < 0) break;
            position += found; if (bytes.Length - position < 12) break;
            long size = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4, 4)) + 8;
            if (size >= 12 && size <= bytes.Length - position && bytes.AsSpan(position + 8, 4).SequenceEqual("WAVE"u8))
            {
                // Validate RIFF chunk bounds independently of whether its codec is supported.
                long p = position + 12, end = position + size; bool valid = true, fmt = false, data = false;
                while (p < end)
                {
                    if (end - p < 8) { valid = false; break; }
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)p + 4, 4));
                    fmt |= bytes.AsSpan((int)p, 4).SequenceEqual("fmt "u8); data |= bytes.AsSpan((int)p, 4).SequenceEqual("data"u8);
                    p += 8L + length + length % 2; if (p > end) { valid = false; break; }
                }
                if (valid && fmt && data)
                {
                    clips.Add(new($"Embedded WAV {clips.Count + 1:0000}  ·  offset 0x{position:X}", asset, position, (int)size));
                    position += (int)size; continue;
                }
            }
            position += 4;
        }
        return clips.ToArray();
    }
}

