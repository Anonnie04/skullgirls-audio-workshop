using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkullgirlsAudio;

public static class MusicRepack
{
    public static void Build(string original, string output, IReadOnlyDictionary<string, byte[]> replacements, CancellationToken cancel = default)
    {
        var index = new Gfs(original);
        if (index.DataOffset > 16 * 1024 * 1024 || index.Entries.Count == 0 || index.Entries.Any(e => e.Alignment != 1 || !e.Path.StartsWith("music/", StringComparison.Ordinal) || !e.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            || index.Entries[^1].Offset + index.Entries[^1].Size != new FileInfo(original).Length) throw new NotSupportedException("Apply supports the validated uncompressed, alignment-1 music archive layout only.");
        if (replacements.Keys.Any(k => !index.Entries.Any(e => e.Path == k))) throw new InvalidDataException("Replacement is outside the music archive.");
        using var input = File.OpenRead(original); byte[] header = new byte[index.DataOffset]; input.ReadExactly(header);
        foreach (var e in index.Entries) if (replacements.TryGetValue(e.Path, out var data)) BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(checked((int)e.SizeField), 8), data.Length);
        using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None); target.Write(header);
        foreach (var e in index.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            if (replacements.TryGetValue(e.Path, out var data)) target.Write(data);
            else { input.Position = e.Offset; Copy(input, target, e.Size, cancel); }
        }
        target.Flush(true);
    }
    public static void Verify(string original, string built, IReadOnlyDictionary<string, byte[]> replacements, CancellationToken cancel = default)
    {
        var before = new Gfs(original); var after = new Gfs(built);
        if (before.Entries.Count != after.Entries.Count || before.DataOffset != after.DataOffset) throw new InvalidDataException("Repacked archive index differs.");
        using var a = File.OpenRead(original); using var b = File.OpenRead(built);
        for (int i = 0; i < before.Entries.Count; i++)
        {
            var old = before.Entries[i]; var next = after.Entries[i]; cancel.ThrowIfCancellationRequested();
            if (old.Path != next.Path || old.Alignment != next.Alignment) throw new InvalidDataException("Archive order or alignment changed.");
            byte[] expected = replacements.TryGetValue(old.Path, out var bytes) ? SHA256.HashData(bytes) : Digest(a, old, cancel);
            if (next.Size != (bytes?.Length ?? old.Size) || !expected.SequenceEqual(Digest(b, next, cancel))) throw new InvalidDataException("Archive payload differs: " + old.Path);
        }
    }
    private static byte[] Digest(Stream input, Entry entry, CancellationToken cancel)
    {
        input.Position = entry.Offset; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; long left = entry.Size;
        while (left > 0) { cancel.ThrowIfCancellationRequested(); int n = input.Read(buffer, 0, (int)Math.Min(left, buffer.Length)); if (n == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, n); left -= n; } return hash.GetHashAndReset();
    }
    internal static void Copy(Stream input, Stream output, long left, CancellationToken cancel)
    {
        var buffer = new byte[1024 * 1024]; while (left > 0) { cancel.ThrowIfCancellationRequested(); int n = input.Read(buffer, 0, (int)Math.Min(left, buffer.Length)); if (n == 0) throw new EndOfStreamException(); output.Write(buffer, 0, n); left -= n; }
    }
}
