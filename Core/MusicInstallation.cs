using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkullgirlsAudio;

public enum InstallState { Preparing, Prepared, Committing, Applied, Restoring, Restored, Aborted, Conflict }
public record InstallationRecord(int Schema, Guid Id, string GameFolder, string ArchiveFolder, string Track, string[] Assets, string OriginalHash, string? InstalledHash, InstallState State, DateTimeOffset Updated, string? GameExecutableHash = null, string? SourceHash = null, EditSettings? Edits = null);

// One active music transaction per installation. Backups are never treated as conversion cache.
public sealed class MusicInstallation
{
    private readonly string store;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal Action<string>? Checkpoint { get; set; }
    internal Func<string, long>? FreeBytes { get; set; }
    public MusicInstallation(string backupStore) { store = Path.GetFullPath(backupStore); }
    public static void GameClosed()
    {
        foreach (var process in Process.GetProcesses()) using (process)
            if (process.ProcessName.Equals("SkullGirls", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("SkullGirls64", StringComparison.OrdinalIgnoreCase)) throw new IOException("Close Skullgirls before preparing, applying, restoring or recovering music.");
    }
    private static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked installation or backup paths are unsupported: " + current);
    }
    private static string Canonical(string game) { string full = Path.GetFullPath(game); return full.Length == Path.GetPathRoot(full)!.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar); }
    private string Root(string game)
    {
        game = Canonical(game); ProjectFiles.OutsideGame(store, game); NoLinks(game); NoLinks(store);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.ToUpperInvariant()))); return Path.Combine(store, key);
    }
    private FileStream Lock(string game)
    {
        string root = Root(game); Directory.CreateDirectory(root);
        return new FileStream(Path.Combine(root, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private string Folder(InstallationRecord r) => Path.Combine(Root(r.GameFolder), r.ArchiveFolder, r.Id.ToString("N"));
    public string BackupPath(InstallationRecord r) => Path.Combine(Folder(r), "original.gfs");
    public static string ArchivePath(InstallationRecord r) => Path.Combine(r.GameFolder, r.ArchiveFolder, "music-win.gfs");
    private static string Stage(InstallationRecord r, bool restore = false) => Path.Combine(r.GameFolder, r.ArchiveFolder, ".sgaw-" + r.Id.ToString("N") + (restore ? ".restore.stage" : ".apply.stage"));
    private static string Previous(InstallationRecord r) => Path.Combine(r.GameFolder, r.ArchiveFolder, ".sgaw-" + r.Id.ToString("N") + ".previous");
    private static bool Digest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private InstallationRecord Read(string path, string game, string archiveFolder)
    {
        NoLinks(path); if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Installation journal is too large.");
        var r = JsonSerializer.Deserialize<InstallationRecord>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Installation journal is empty.");
        if (r.Schema != 1 || r.Id == Guid.Empty || !Enum.IsDefined(r.State) || r.ArchiveFolder != archiveFolder || !Canonical(r.GameFolder).Equals(Canonical(game), StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(Path.GetDirectoryName(path)) != r.Id.ToString("N") || !Digest(r.OriginalHash) || (r.InstalledHash != null && !Digest(r.InstalledHash)) || r.Assets?.Length != 2 || r.Track == null)
            throw new InvalidDataException("Installation journal needs manual review. No game files changed.");
        return r;
    }
    public InstallationRecord? Current(string game)
    {
        var records = new List<InstallationRecord>(); string root = Root(game);
        foreach (string folder in new[] { "data01", "data02" })
        {
            string directory = Path.Combine(root, folder); if (!Directory.Exists(directory)) continue; NoLinks(directory);
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(child), "N", out _)) throw new InvalidDataException("Unknown installation journal folder requires review.");
                string path = Path.Combine(child, "journal.json");
                if (File.Exists(path)) { var r = Read(path, game, folder); if (r.State is not (InstallState.Restored or InstallState.Aborted)) records.Add(r); }
            }
        }
        if (records.Count > 1) throw new InvalidDataException("Multiple active installation journals require review. No game files changed."); return records.SingleOrDefault();
    }
    private InstallationRecord Save(InstallationRecord r, InstallState state)
    {
        r = r with { State = state, Updated = DateTimeOffset.UtcNow }; string folder = Folder(r); Directory.CreateDirectory(folder); NoLinks(folder);
        string path = Path.Combine(folder, "journal.json"), temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, r, Json); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); return r;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Active(InstallationRecord r)
    {
        GameClosed(); NoLinks(ArchivePath(r));
        if (!File.Exists(Path.Combine(r.GameFolder, "SkullGirls.exe")) || GameLibrary.ActiveArchiveFolder(r.GameFolder) != r.ArchiveFolder) throw new IOException("The active archive location changed. Review the installation and restore its previous loader/location before continuing.");
        if (r.GameExecutableHash != null && Hash(Path.Combine(r.GameFolder, "SkullGirls.exe")) != r.GameExecutableHash) throw new IOException("The game executable changed after preparation. Review the game build before continuing.");
    }
    private void Space(string location, long required)
    {
        long free = FreeBytes?.Invoke(location) ?? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(location))!).AvailableFreeSpace;
        if (free < required + 16 * 1024 * 1024) throw new IOException("Insufficient free space for a verified backup/staged archive. Free disk space and retry.");
    }
    private static string Hash(string file) { NoLinks(file); return ProjectFiles.Hash(file); }
    private static void CopyFile(string from, string to, CancellationToken cancel)
    {
        NoLinks(from); NoLinks(to); using var input = File.OpenRead(from); using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None); MusicRepack.Copy(input, output, input.Length, cancel); output.Flush(true);
    }
    public InstallationRecord Prepare(MusicTarget target, GeneratedPair pair, AudioSource source, EditSettings edits, CancellationToken cancel = default)
    {
        using var operation = Lock(target.GameFolder); GameClosed();
        if (Current(target.GameFolder) != null) throw new IOException("Restore or recover the existing Workshop transaction before another Apply.");
        var data = MusicExport.Verify(pair, source, target, edits, cancel);
        var r = new InstallationRecord(1, Guid.NewGuid(), Canonical(target.GameFolder), target.ArchiveFolder, target.Track.Name, target.Track.Assets.ToArray(), Hash(Path.Combine(target.GameFolder, target.ArchiveFolder, "music-win.gfs")), null, InstallState.Preparing, DateTimeOffset.UtcNow, Hash(Path.Combine(target.GameFolder, "SkullGirls.exe")), source.Hash, edits);
        Active(r); long size = new FileInfo(ArchivePath(r)).Length; long estimate = size + data.Sum(b => (long)b.Length);
        bool sameDrive = Path.GetPathRoot(store)!.Equals(Path.GetPathRoot(r.GameFolder), StringComparison.OrdinalIgnoreCase);
        Space(r.GameFolder, sameDrive ? checked(size + estimate) : estimate); Space(store, sameDrive ? checked(size + estimate) : size);
        r = Save(r, InstallState.Preparing); Checkpoint?.Invoke("Preparing");
        try
        {
            CopyFile(ArchivePath(r), BackupPath(r), cancel);
            if (Hash(BackupPath(r)) != r.OriginalHash) throw new IOException("Pre-apply backup verification failed."); Checkpoint?.Invoke("BackupVerified");
            var replacements = target.Track.Assets.Select((asset, i) => (asset, i)).ToDictionary(p => p.asset, p => data[p.i]);
            MusicRepack.Build(BackupPath(r), Stage(r), replacements, cancel); MusicRepack.Verify(BackupPath(r), Stage(r), replacements, cancel);
            r = r with { InstalledHash = Hash(Stage(r)) }; target.AssertUnchanged(); source.AssertUnchanged();
            if (Hash(ArchivePath(r)) != r.OriginalHash) throw new IOException("Music archive changed during preparation.");
            r = Save(r, InstallState.Prepared); Checkpoint?.Invoke("Prepared"); return r;
        }
        catch (OperationCanceledException)
        {
            // This stage was freshly created by this operation; the active archive has not been replaced.
            if (File.Exists(Stage(r))) File.Delete(Stage(r)); Save(r, InstallState.Aborted); throw;
        }
    }
    private InstallationRecord Required(string game, Guid id)
    {
        var r = Current(game); if (r == null || r.Id != id) throw new IOException("Installation state changed. Refresh the installation review."); return r;
    }
    public InstallationRecord Apply(string game, Guid id)
    {
        using var operation = Lock(game); var r = Required(game, id); Active(r);
        if (r.State != InstallState.Prepared || r.InstalledHash == null) throw new IOException("Prepare and verify the replacement before Apply.");
        if (Hash(ArchivePath(r)) != r.OriginalHash) { Save(r, InstallState.Conflict); throw new IOException("Music changed after review. Apply refused; backup retained."); }
        if (Hash(Stage(r)) != r.InstalledHash || Hash(BackupPath(r)) != r.OriginalHash) throw new IOException("Stage or backup changed. Recover before proceeding.");
        NoLinks(Previous(r)); if (File.Exists(Previous(r))) throw new IOException("The rollback filename already exists. Review/recover this transaction before Apply.");
        r = Save(r, InstallState.Committing); Checkpoint?.Invoke("BeforeApplyReplace"); Active(r);
        if (Hash(ArchivePath(r)) != r.OriginalHash || Hash(Stage(r)) != r.InstalledHash || Hash(BackupPath(r)) != r.OriginalHash) throw new IOException("Apply inputs changed immediately before replacement. Recover before proceeding.");
        NoLinks(Previous(r)); if (File.Exists(Previous(r))) throw new IOException("The rollback filename changed immediately before Apply. Use Recover.");
        File.Replace(Stage(r), ArchivePath(r), Previous(r)); Checkpoint?.Invoke("AfterApplyReplace");
        if (Hash(ArchivePath(r)) != r.InstalledHash || Hash(Previous(r)) != r.OriginalHash) throw new IOException("Installed archive verification failed. Use Recover.");
        r = Save(r, InstallState.Applied); Clean(r); return r;
    }
    private void Clean(InstallationRecord r)
    {
        if (r.State is InstallState.Aborted or InstallState.Restored)
            foreach (string stage in new[] { Stage(r), Stage(r, true) }) { NoLinks(stage); if (File.Exists(stage)) File.Delete(stage); }
        foreach (var file in new[] { (Stage(r), r.InstalledHash), (Stage(r, true), r.OriginalHash), (Previous(r), r.OriginalHash) })
            if (file.Item2 != null && File.Exists(file.Item1) && Hash(file.Item1) == file.Item2 && File.Exists(BackupPath(r)) && Hash(BackupPath(r)) == r.OriginalHash) File.Delete(file.Item1);
    }
    private InstallationRecord RestoreInternal(InstallationRecord r)
    {
        Active(r);
        if (r.InstalledHash == null || Hash(ArchivePath(r)) != r.InstalledHash) { Save(r, InstallState.Conflict); throw new IOException("Music differs from Workshop's installed archive. Restore refused to preserve external changes."); }
        if (Hash(BackupPath(r)) != r.OriginalHash) throw new IOException("Restore backup is missing or changed. No game files changed.");
        Space(r.GameFolder, new FileInfo(BackupPath(r)).Length); r = Save(r, InstallState.Restoring);
        string stage = Stage(r, true);
        if (File.Exists(stage) && Hash(stage) != r.OriginalHash) { NoLinks(stage); File.Delete(stage); }
        if (!File.Exists(stage)) CopyFile(BackupPath(r), stage, CancellationToken.None);
        if (Hash(stage) != r.OriginalHash || Hash(ArchivePath(r)) != r.InstalledHash) throw new IOException("Restore inputs changed. Use Recover.");
        Checkpoint?.Invoke("BeforeRestoreReplace"); Active(r);
        if (Hash(ArchivePath(r)) != r.InstalledHash || Hash(stage) != r.OriginalHash) throw new IOException("Restore inputs changed immediately before replacement. Use Recover.");
        File.Replace(stage, ArchivePath(r), null); Checkpoint?.Invoke("AfterRestoreReplace");
        if (Hash(ArchivePath(r)) != r.OriginalHash) throw new IOException("Restored archive verification failed. Use Recover.");
        r = Save(r, InstallState.Restored); Clean(r); return r;
    }
    public InstallationRecord Restore(string game, Guid id)
    {
        using var operation = Lock(game); var r = Required(game, id);
        if (r.State != InstallState.Applied) throw new IOException("Use Recover for an interrupted or conflicting transaction."); return RestoreInternal(r);
    }
    public InstallationRecord Recover(string game, Guid id)
    {
        using var operation = Lock(game); var r = Required(game, id); GameClosed(); string current = Hash(ArchivePath(r));
        if (r.State == InstallState.Applied) throw new IOException("This installation completed. Use Restore to return to the pre-apply state.");
        if (current == r.OriginalHash) { var state = r.State == InstallState.Restoring ? InstallState.Restored : InstallState.Aborted; r = Save(r, state); Clean(r); return r; }
        if (r.InstalledHash != null && current == r.InstalledHash) return RestoreInternal(r);
        Save(r, InstallState.Conflict); throw new IOException("Unexplained external archive changes prevent recovery. Backups retained; no archive was overwritten.");
    }
}
