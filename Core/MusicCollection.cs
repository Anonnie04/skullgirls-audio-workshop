using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkullgirlsAudio;

public record ModProject(int Schema, Guid Id, DateTimeOffset Updated, ProjectExport Details, AudioProject[] Songs, bool CopySources = true);
public record LoadedMod(string Path, string Hash, ModProject Project);
public record PreparedSong(AudioProject Project, GeneratedPair Output);

public static class ModProjects
{
    public const string Extension = ".sgawmod";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static void Validate(ModProject p)
    {
        if (p.Schema != 1 || p.Id == Guid.Empty || p.Details == null || p.Songs == null || p.Songs.Length > 24) throw new InvalidDataException("Invalid mod project or unsupported schema (maximum 24 songs).");
        MusicExport.ValidateDetails(p.Details);
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ids = new HashSet<Guid>(); string? game = null, folder = null;
        foreach (var song in p.Songs)
        {
            if (song == null) throw new InvalidDataException("A song entry is missing.");
            ProjectFiles.Validate(song);
            string root = System.IO.Path.GetFullPath(song.Target.GameFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            game ??= root; folder ??= song.Target.ArchiveFolder;
            if (!ids.Add(song.Id) || !game.Equals(root, StringComparison.OrdinalIgnoreCase) || !folder.Equals(song.Target.ArchiveFolder, StringComparison.OrdinalIgnoreCase)
                || !song.Target.Archive.Equals("music-win.gfs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use distinct songs from one installation and active music archive.");
            foreach (string asset in song.Target.Assets)
                if (!asset.StartsWith("music/", StringComparison.Ordinal) || !asset.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || asset.Split('/').Any(s => s is ".." or "." or "" || s.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || s.EndsWith('.') || s.EndsWith(' ')) || !assets.Add(asset))
                    throw new InvalidDataException("Duplicate, conflicting or invalid music target: " + asset);
        }
    }
    public static LoadedMod Load(string path)
    {
        path = System.IO.Path.GetFullPath(path); var file = new FileInfo(path);
        if (!file.Exists || file.Length > 2 * 1024 * 1024) throw new IOException("Choose a mod project smaller than 2 MB.");
        byte[] bytes = File.ReadAllBytes(path); var p = JsonSerializer.Deserialize<ModProject>(bytes, Json) ?? throw new InvalidDataException("Empty mod project."); Validate(p);
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)), p);
    }
    // Loaded in-memory songs always have absolute source paths; disk projects may use relative copies.
    public static ModProject ResolveSources(LoadedMod loaded) => loaded.Project with { Songs = loaded.Project.Songs.Select(s => s with { Source = s.Source with { Path = System.IO.Path.GetFullPath(s.Source.Path, System.IO.Path.GetDirectoryName(loaded.Path)!) } }).ToArray() };
    public static void WriteDraft(string path, ModProject project, string? expectedHash = null)
    {
        Validate(project); path = System.IO.Path.GetFullPath(path);
        foreach (var song in project.Songs) ProjectFiles.OutsideGame(path, song.Target.GameFolder);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(project, Json);
        if (bytes.Length > 2 * 1024 * 1024) throw new IOException("Mod draft exceeds 2 MB.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (expectedHash != null && (!File.Exists(path) || ProjectFiles.Hash(path) != expectedHash)) throw new IOException("Mod draft changed outside this window.");
            if (File.Exists(path)) File.Replace(stage, path, null); else File.Move(stage, path);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    public static LoadedMod Save(string path, ModProject p, string? expectedHash = null, CancellationToken cancel = default)
    {
        Validate(p); path = System.IO.Path.GetFullPath(path);
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Mod project filenames must end in .sgawmod.");
        var songs = new List<AudioProject>();
        foreach (var song in p.Songs)
        {
            cancel.ThrowIfCancellationRequested(); ProjectFiles.OutsideGame(path, song.Target.GameFolder);
            string source = System.IO.Path.GetFullPath(song.Source.Path);
            if (new FileInfo(source).Length > 128 * 1024 * 1024) throw new IOException("A source exceeds the 128 MB import limit.");
            if (ProjectFiles.Hash(source) != song.Source.Hash) throw new IOException("A source changed. Edit/relink and review that song before saving.");
            string stored = source;
            if (p.CopySources)
            {
                string directory = path + ".sources"; Directory.CreateDirectory(directory);
                string copied = System.IO.Path.Combine(directory, song.Source.Hash + System.IO.Path.GetExtension(source).ToLowerInvariant());
                if (!File.Exists(copied))
                {
                    string temp = copied + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.Copy(source, temp, false); if (ProjectFiles.Hash(temp) != song.Source.Hash) throw new IOException("Source changed while copying."); File.Move(temp, copied, false); }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                if (ProjectFiles.Hash(copied) != song.Source.Hash) throw new IOException("A copied source differs. Choose another project filename.");
                stored = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(path)!, copied);
            }
            songs.Add(song with { Source = song.Source with { Path = stored, Copied = p.CopySources } });
        }
        cancel.ThrowIfCancellationRequested(); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(p with { Updated = DateTimeOffset.UtcNow, Songs = songs.ToArray() }, Json);
        if (bytes.Length > 2 * 1024 * 1024) throw new IOException("Mod project exceeds 2 MB.");
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            foreach (var song in p.Songs) if (ProjectFiles.Hash(song.Source.Path) != song.Source.Hash) throw new IOException("Source changed while saving.");
            if (expectedHash != null && (!File.Exists(path) || ProjectFiles.Hash(path) != expectedHash)) throw new IOException("Mod project changed outside this window. Use Save as.");
            cancel.ThrowIfCancellationRequested(); if (File.Exists(path)) File.Replace(stage, path, null); else File.Move(stage, path); return Load(path);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    public static MusicTarget Target(AudioProject song, GameLibrary library)
    {
        if (!System.IO.Path.GetFullPath(song.Target.GameFolder).TrimEnd('\\').Equals(library.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Song belongs to another game installation.");
        var target = new MusicTarget(library, ProjectFiles.ResolveTrack(song, library));
        if (!ProjectFiles.TargetMatches(song, target, target.ArchiveFolder)) throw new IOException("Target changed. Edit the song and explicitly review its current target before generating again.");
        return target;
    }
    public static PreparedSong[] Generate(ModProject p, GameLibrary library, string cache, CancellationToken cancel = default, Action<string>? progress = null)
    {
        Validate(p); if (p.Songs.Length == 0) throw new InvalidOperationException("Add at least one song.");
        var results = new List<PreparedSong>();
        foreach (var song in p.Songs)
        {
            cancel.ThrowIfCancellationRequested(); progress?.Invoke("Generating " + song.Target.Name);
            var source = AudioSource.Open(song.Source.Path, cancel); if (source.Hash != song.Source.Hash) throw new IOException("Source changed. Edit/relink the song first.");
            var pair = MusicGeneration.Generate(source, Target(song, library), song.Edits, cache, cancel);
            // Keep only small metadata and verified file references between songs, not all PCM buffers.
            results.Add(new(song, pair with { Preview = pair.Preview with { Pcm = [] }, Rendered = new(pair.Rendered.Intro with { Pcm = [] }, pair.Rendered.Loop with { Pcm = [] }) }));
        }
        return results.ToArray();
    }
    internal static Dictionary<string, byte[]> Verify(ModProject project, IReadOnlyList<PreparedSong> songs, GameLibrary library, CancellationToken cancel)
    {
        Validate(project);
        if (songs.Count == 0 || songs.Count != project.Songs.Length) throw new InvalidDataException("Generate every song before exporting or applying.");
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int n = 0; n < songs.Count; n++)
        {
            var prepared = songs[n]; var song = project.Songs[n];
            if (prepared.Project != song) throw new InvalidDataException("The mod project changed. Generate again.");
            var source = AudioSource.Open(song.Source.Path, cancel); if (source.Hash != song.Source.Hash) throw new IOException("Source changed. Review it and generate again.");
            var bytes = MusicExport.Verify(prepared.Output, source, Target(song, library), song.Edits, cancel);
            for (int i = 0; i < 2; i++) if (!assets.TryAdd(song.Target.Assets[i], bytes[i])) throw new InvalidDataException("Conflicting targets.");
        }
        return assets;
    }
    internal static void AssertInputs(ModProject project, GameLibrary library)
    {
        foreach (var song in project.Songs) { if (ProjectFiles.Hash(song.Source.Path) != song.Source.Hash) throw new IOException("Source changed during the operation."); Target(song, library).AssertUnchanged(); }
    }
    public static string Export(string destination, bool zip, ModProject project, IReadOnlyList<PreparedSong> songs, GameLibrary library, CancellationToken cancel = default)
    {
        destination = System.IO.Path.GetFullPath(destination); ProjectFiles.OutsideGame(destination, library.Folder);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new export path; existing exports are preserved.");
        if (zip && !destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a ZIP filename.");
        var assets = Verify(project, songs, library, cancel); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        string stage = destination + "." + Guid.NewGuid().ToString("N") + ".staging";
        var entries = assets.ToDictionary(a => "music-win.gfs/" + a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
        entries.Add("mod.json", JsonSerializer.SerializeToUtf8Bytes(new { id = "local.audio-workshop." + project.Id.ToString("N"), name = project.Details.Name, author = project.Details.Author, desc = project.Details.Description, version = project.Details.Version }, Json));
        entries.Add("export.json", JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, project.Id, metadata = project.Details, songs = songs.Select(s => new { s.Project.Target.Name, paths = s.Output.Assets, sourceHash = s.Output.SourceHash, edits = s.Output.Edits, outputHashes = s.Output.Hashes }), independentlyDecoded = true, inGameVerified = false }, Json));
        try
        {
            if (zip)
            {
                using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                { using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true)) foreach (var entry in entries) { cancel.ThrowIfCancellationRequested(); using var stream = archive.CreateEntry(entry.Key).Open(); stream.Write(entry.Value); } file.Flush(true); }
                using (var archive = ZipFile.OpenRead(stage))
                { if (archive.Entries.Count != entries.Count) throw new InvalidDataException("ZIP entry count mismatch."); foreach (var entry in entries) { using var stream = archive.GetEntry(entry.Key)!.Open(); if (!SHA256.HashData(stream).SequenceEqual(SHA256.HashData(entry.Value))) throw new InvalidDataException("ZIP payload differs."); } }
            }
            else
            {
                Directory.CreateDirectory(stage);
                foreach (var entry in entries) { cancel.ThrowIfCancellationRequested(); string path = System.IO.Path.Combine(stage, entry.Key.Replace('/', System.IO.Path.DirectorySeparatorChar)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllBytes(path, entry.Value); if (ProjectFiles.Hash(path) != Convert.ToHexString(SHA256.HashData(entry.Value))) throw new IOException("Folder payload differs."); }
            }
            AssertInputs(project, library); cancel.ThrowIfCancellationRequested(); if (zip) File.Move(stage, destination, false); else Directory.Move(stage, destination); return destination;
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
            if (Directory.Exists(stage) && System.IO.Path.GetDirectoryName(stage) == System.IO.Path.GetDirectoryName(destination) && System.IO.Path.GetFileName(stage).StartsWith(System.IO.Path.GetFileName(destination) + ".", StringComparison.Ordinal)) Directory.Delete(stage, true);
        }
    }
}
