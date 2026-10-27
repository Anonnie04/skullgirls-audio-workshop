using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkullgirlsAudio;

public static class MusicExport
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static void ValidateDetails(ProjectExport details)
    {
        if (string.IsNullOrWhiteSpace(details.Name) || details.Name.Length > 180 || details.Author == null || details.Author.Length > 180 || details.Description == null || details.Description.Length > 4000
            || !Version.TryParse(details.Version, out var version) || version.Major < 0 || version.Minor < 0 || details.Version.Length > 40)
            throw new ArgumentException("Enter a name (1–180 characters), author (up to 180), description (up to 4,000) and a numeric version such as 1.0 or 1.2.3.");
    }
    private static string SafePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException("Invalid replacement asset path.");
        return normalized;
    }
    public static string SafeName(string title)
    {
        string name = new(title.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').Take(80).ToArray());
        return "Workshop-" + name.Trim('-');
    }
    internal static byte[][] Verify(GeneratedPair generated, AudioSource source, MusicTarget target, EditSettings edits, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested(); source.AssertUnchanged(); target.AssertUnchanged(); edits.Validate(source);
        if (generated.Edits != edits || generated.SourceHash != source.Hash || !generated.TargetHashes.SequenceEqual(target.Hashes) || generated.Archive != target.Track.Archive || !generated.Assets.SequenceEqual(target.Track.Assets)
            || generated.Files.Length != 2 || generated.Hashes.Length != 2) throw new InvalidDataException("Generated audio no longer matches these edits or this target. Generate and review again.");
        string archive = SafePath(target.Track.Archive); if (archive.Contains('/') || !archive.EndsWith(".gfs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid target archive name.");
        var names = target.Track.Assets.Select(SafePath).ToArray(); if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2) throw new InvalidDataException("Duplicate target assets.");
        var bytes = new byte[2][]; var segments = new[] { generated.Rendered.Intro, generated.Rendered.Loop };
        for (int i = 0; i < 2; i++)
        {
            cancel.ThrowIfCancellationRequested();
            if (new FileInfo(generated.Files[i]).Length > 128 * 1024 * 1024) throw new IOException("Generated audio exceeds the export limit.");
            bytes[i] = File.ReadAllBytes(generated.Files[i]);
            if (Convert.ToHexString(SHA256.HashData(bytes[i])) != generated.Hashes[i]) throw new InvalidDataException("A generated WAV changed. Generate and review again.");
            var decoded = ManagedDecoder.Decode(bytes[i]); var wave = decoded.Source;
            if (!wave.Format.SequenceEqual(target.Formats[i].Format) || wave.Frames != segments[i].Frames || wave.Sampler is not { Length: 60 } sampler || BitConverter.ToInt32(sampler, 44) != 0 || BitConverter.ToInt32(sampler, 48) != wave.Frames - 1)
                throw new InvalidDataException("Generated audio failed export verification.");
        }
        return bytes;
    }
    private static byte[] Report(GeneratedPair pair, MusicTarget target, EditSettings edits, ProjectExport details, Guid projectId) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = 1, projectId, created = DateTimeOffset.UtcNow, metadata = details, target = target.Track.Name, archive = target.Track.Archive, paths = target.Track.Assets, originalHashes = target.Hashes,
        sourceHash = pair.SourceHash, edits, outputHashes = pair.Hashes,
        intro = new { pair.Rendered.Intro.Frames, pair.Rendered.Intro.Rate, pair.Rendered.Intro.TimingAdjustmentMs, pair.Rendered.Intro.ClippedSamples },
        loop = new { pair.Rendered.Loop.Frames, pair.Rendered.Loop.Rate, pair.Rendered.Loop.TimingAdjustmentMs, pair.Rendered.Loop.ClippedSamples },
        independentlyDecoded = true, inGameVerified = false, gameFilesModified = false,
        playbackNote = "Conversion verified; actual in-game playback and any game-side loader requirements still need validation."
    }, Json);
    public static string Native(string parent, GeneratedPair pair, AudioSource source, MusicTarget target, EditSettings edits, ProjectExport details, Guid projectId, CancellationToken cancel = default)
    {
        ValidateDetails(details); parent = Path.GetFullPath(parent); ProjectFiles.OutsideGame(parent, target.GameFolder);
        var bytes = Verify(pair, source, target, edits, cancel); Directory.CreateDirectory(parent);
        string final = Path.Combine(parent, SafeName(details.Name) + "-" + Guid.NewGuid().ToString("N")), stage = final + ".staging"; Directory.CreateDirectory(stage);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                cancel.ThrowIfCancellationRequested(); string file = Path.Combine(stage, SafePath(target.Track.Archive), SafePath(target.Track.Assets[i]).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes[i]);
                if (ProjectFiles.Hash(file) != pair.Hashes[i]) throw new IOException("Exported WAV failed checksum verification.");
            }
            File.WriteAllBytes(Path.Combine(stage, "export.json"), Report(pair, target, edits, details, projectId));
            source.AssertUnchanged(); target.AssertUnchanged(); cancel.ThrowIfCancellationRequested(); Directory.Move(stage, final); return final;
        }
        finally
        {
            string fullStage = Path.GetFullPath(stage), scope = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Directory.Exists(fullStage) && fullStage.StartsWith(scope, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullStage) == Path.GetFileName(final) + ".staging") Directory.Delete(fullStage, true);
        }
    }
    public static string Package(string path, GeneratedPair pair, AudioSource source, MusicTarget target, EditSettings edits, ProjectExport details, Guid projectId, CancellationToken cancel = default)
    {
        ValidateDetails(details); path = Path.GetFullPath(path); ProjectFiles.OutsideGame(path, target.GameFolder);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .zip filename.");
        if (File.Exists(path)) throw new IOException("An export already exists at that path. Choose a new filename; existing exports are preserved.");
        var bytes = Verify(pair, source, target, edits, cancel); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".staging";
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { id = "local.audio-workshop." + projectId.ToString("N"), name = details.Name, author = details.Author, desc = details.Description, version = details.Version, url = (string?)null }, Json);
        string[] names = target.Track.Assets.Select(a => SafePath(target.Track.Archive) + "/" + SafePath(a)).ToArray();
        try
        {
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
                {
                    void Entry(string name, byte[] data) { cancel.ThrowIfCancellationRequested(); using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); entry.Write(data); }
                    Entry("mod.json", metadata); Entry("export.json", Report(pair, target, edits, details, projectId));
                    for (int i = 0; i < 2; i++) Entry(names[i], bytes[i]);
                }
                file.Flush(true);
            }
            using (var zip = ZipFile.OpenRead(stage))
            {
                if (zip.Entries.Count != 4) throw new InvalidDataException("Unexpected package contents.");
                for (int i = 0; i < 2; i++)
                {
                    cancel.ThrowIfCancellationRequested(); using var stream = zip.GetEntry(names[i])?.Open() ?? throw new InvalidDataException("Package asset is missing.");
                    if (Convert.ToHexString(SHA256.HashData(stream)) != pair.Hashes[i]) throw new InvalidDataException("Package WAV checksum mismatch.");
                }
                using var mod = zip.GetEntry("mod.json")!.Open(); using var json = JsonDocument.Parse(mod);
                if (json.RootElement.GetProperty("name").GetString() != details.Name || json.RootElement.GetProperty("version").GetString() != details.Version) throw new InvalidDataException("Package metadata verification failed.");
            }
            source.AssertUnchanged(); target.AssertUnchanged(); cancel.ThrowIfCancellationRequested(); File.Move(stage, path, false); return path;
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
