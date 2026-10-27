using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkullgirlsAudio;

public record ProjectTarget(string GameFolder, string ArchiveFolder, string Archive, string Name, string[] Assets, string[] Hashes);
public record ProjectSource(string Path, string Hash, bool Copied, string Name = "");
public record ProjectExport(string Name = "", string Author = "", string Version = "1.0", string Description = "");
public record AudioProject(int Schema, Guid Id, DateTimeOffset Updated, ProjectTarget Target, ProjectSource Source, EditSettings Edits, ProjectExport Export, bool CopySourceOnSave = true);
public record LoadedProject(string Path, string Hash, AudioProject Project);

public static class ProjectFiles
{
    public const string Extension = ".sgawproj";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static AudioProject Capture(Guid id, MusicTarget target, string archiveFolder, AudioSource source, EditSettings edits, ProjectExport? export = null) => new(1, id, DateTimeOffset.UtcNow,
        new(target.GameFolder, archiveFolder, target.Track.Archive, target.Track.Name, target.Track.Assets, target.Hashes), new(source.Path, source.Hash, false, Path.GetFileName(source.Path)), edits, export ?? new(target.Track.Name));
    public static LoadedProject Load(string path)
    {
        path = Path.GetFullPath(path); var file = new FileInfo(path);
        if (!file.Exists || file.Length > 1024 * 1024) throw new IOException("Choose a Workshop project smaller than 1 MB.");
        byte[] bytes = File.ReadAllBytes(path);
        var project = JsonSerializer.Deserialize<AudioProject>(bytes, Json) ?? throw new InvalidDataException("Empty project."); Validate(project);
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)), project);
    }
    public static void Validate(AudioProject p)
    {
        bool Digest(string? s) => s?.Length == 64 && s.All(Uri.IsHexDigit);
        if (p.Schema != 1) throw new NotSupportedException($"Project version {p.Schema} is not supported. The file was not changed.");
        if (p.Id == Guid.Empty || p.Target == null || p.Source == null || p.Edits == null || p.Export == null
            || string.IsNullOrWhiteSpace(p.Target.GameFolder) || string.IsNullOrWhiteSpace(p.Target.ArchiveFolder) || string.IsNullOrWhiteSpace(p.Target.Archive) || string.IsNullOrWhiteSpace(p.Target.Name)
            || p.Target.Assets?.Length != 2 || p.Target.Assets.Any(string.IsNullOrWhiteSpace) || p.Target.Hashes?.Length != 2 || p.Target.Hashes.Any(h => !Digest(h))
            || string.IsNullOrWhiteSpace(p.Source.Path) || !Digest(p.Source.Hash) || p.Source.Name == null || p.Export.Name == null || p.Export.Author == null || p.Export.Version == null || p.Export.Description == null)
            throw new InvalidDataException("Project fields are missing or invalid.");
        var e = p.Edits;
        if (new[] { e.Start, e.LoopStart, e.End, e.FadeIn, e.FadeOut, e.Crossfade }.Any(v => !double.IsFinite(v) || v < 0 || v > 3600) || !double.IsFinite(e.GainDb) || e.GainDb < -60 || e.GainDb > 24)
            throw new InvalidDataException("Project edit values are outside supported limits.");
        // Incomplete boundary selections may be saved as drafts. Rendering still requires EditSettings.Validate.
    }
    public static string SourcePath(LoadedProject loaded) => Path.GetFullPath(loaded.Project.Source.Path, Path.GetDirectoryName(loaded.Path)!);
    public static Track ResolveTrack(AudioProject p, GameLibrary library) => library.Tracks.FirstOrDefault(t => t.Mapped && !t.Bank && t.Archive.Equals(p.Target.Archive, StringComparison.OrdinalIgnoreCase)
        && t.Assets.SequenceEqual(p.Target.Assets, StringComparer.OrdinalIgnoreCase)) ?? throw new NotSupportedException("The project's exact music target is not present in this installation.");
    public static bool TargetMatches(AudioProject p, MusicTarget target, string archiveFolder) => p.Target.ArchiveFolder.Equals(archiveFolder, StringComparison.OrdinalIgnoreCase)
        && p.Target.Archive.Equals(target.Track.Archive, StringComparison.OrdinalIgnoreCase) && p.Target.Assets.SequenceEqual(target.Track.Assets, StringComparer.OrdinalIgnoreCase) && p.Target.Hashes.SequenceEqual(target.Hashes, StringComparer.OrdinalIgnoreCase);
    public static void OutsideGame(string path, string gameFolder)
    {
        string game = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(game, StringComparison.OrdinalIgnoreCase)) throw new IOException("Save projects and copied sources outside the game installation.");
    }
    public static LoadedProject Save(string path, AudioProject project, AudioSource source, bool copySource, string? expectedHash = null)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Workshop project filenames must end in .sgawproj.");
        OutsideGame(path, project.Target.GameFolder); source.AssertUnchanged(); Validate(project);
        string sourceName = project.Source.Name.Length == 0 ? Path.GetFileName(source.Path) : project.Source.Name;
        var stored = project with { Updated = DateTimeOffset.UtcNow, Source = new(source.Path, source.Hash, false, sourceName), CopySourceOnSave = copySource };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (copySource)
        {
            string folder = path + ".sources"; OutsideGame(folder, project.Target.GameFolder); Directory.CreateDirectory(folder);
            string copied = Path.Combine(folder, source.Hash + Path.GetExtension(source.Path).ToLowerInvariant());
            if (!File.Exists(copied))
            {
                string stage = copied + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(source.Path, stage, false); if (Hash(stage) != source.Hash) throw new IOException("Source changed while copying."); File.Move(stage, copied, false); }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            }
            if (Hash(copied) != source.Hash) throw new IOException("The existing copied source differs. Choose a new project filename.");
            stored = stored with { Source = new(Path.GetRelativePath(Path.GetDirectoryName(path)!, copied), source.Hash, true, sourceName) };
        }
        source.AssertUnchanged(); WriteAtomic(path, stored, expectedHash); return Load(path);
    }
    public static void WriteAtomic(string path, AudioProject project, string? expectedHash = null)
    {
        Validate(project); path = Path.GetFullPath(path); OutsideGame(path, project.Target.GameFolder);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, Json)); if (bytes.Length > 1024 * 1024) throw new IOException("Project exceeds 1 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            if (expectedHash != null && (!File.Exists(path) || Hash(path) != expectedHash)) throw new IOException("The project changed outside this window. Use Save as to preserve both versions.");
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
