using System.Buffers.Binary;
using System.Text;
using SkullgirlsAudio;

static class ReleaseChecks
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string game = Path.Combine(output, "synthetic-installation"); Directory.CreateDirectory(Path.Combine(game, "data01"));
        File.WriteAllBytes(Path.Combine(game, "SkullGirls.exe"), []);
        byte[] template;
        using (var s = new MemoryStream())
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write("RIFF"u8); w.Write(0); w.Write("WAVEfmt "u8); w.Write(50);
            w.Write((ushort)2); w.Write((ushort)2); w.Write(44100); w.Write(48234); w.Write((ushort)140); w.Write((ushort)4); w.Write((ushort)32); w.Write((ushort)128); w.Write((ushort)7);
            foreach (short coefficient in new short[] { 256, 0, 512, -256, 0, 0, 192, 64, 240, 0, 460, -208, 392, -232 }) w.Write(coefficient);
            byte[] sampler = new byte[60]; BinaryPrimitives.WriteInt32LittleEndian(sampler.AsSpan(8), 22675); BinaryPrimitives.WriteInt32LittleEndian(sampler.AsSpan(12), 60); BinaryPrimitives.WriteInt32LittleEndian(sampler.AsSpan(28), 1); BinaryPrimitives.WriteInt32LittleEndian(sampler.AsSpan(48), 127);
            w.Write("smpl"u8); w.Write(sampler.Length); w.Write(sampler);
            byte[] block = new byte[140]; block[2] = block[4] = 16; w.Write("data"u8); w.Write(block.Length); w.Write(block);
            w.Flush(); s.Position = 4; w.Write((int)s.Length - 8); template = s.ToArray();
        }
        WriteGfs(Path.Combine(game, "data01", "music-win.gfs"), new() { ["music/skull02_1.wav"] = template, ["music/skull02_2.wav"] = template, ["music/test_second_intro.wav"] = template, ["music/test_second_loop.wav"] = template, ["music/untouched.wav"] = template });
        WriteGfs(Path.Combine(game, "data01", "core.gfs"), new() { ["core/music.ini"] = Encoding.UTF8.GetBytes("Track: Moonlit Melee\nStage: Synthetic_Test\nFiles: skull02_1, skull02_2\nTrack: Synthetic Second\nStage: Synthetic_Second\nFiles: test_second_intro, test_second_loop\n") });
        ImportChecks.Run(game, Path.Combine(output, "imports"));
        ProjectChecks.Run(game, Path.Combine(output, "projects"));
        ExportChecks.Run(game, Path.Combine(output, "exports"));
        InstallationChecks.Run(game, Path.Combine(output, "installation"));
        CollectionChecks.Run(game, Path.Combine(output, "collections"));
        Console.WriteLine("Release fixtures passed without a real game installation or other Skullgirls tools.");
    }
    private static void WriteGfs(string path, Dictionary<string, byte[]> assets)
    {
        using var s = new MemoryStream();
        void I32(int value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, value); s.Write(b); }
        void I64(long value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, value); s.Write(b); }
        void Text(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); I64(bytes.Length); s.Write(bytes); }
        I32(0); Text("Reverge Package File"); Text("1.1"); I64(assets.Count);
        foreach (var p in assets) { Text(p.Key); I64(p.Value.Length); I32(1); }
        int offset = (int)s.Position; s.Position = 0; I32(offset); s.Position = offset;
        foreach (var p in assets) s.Write(p.Value); File.WriteAllBytes(path, s.ToArray());
    }
}
