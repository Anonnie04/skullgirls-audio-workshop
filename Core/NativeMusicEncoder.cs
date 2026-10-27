using System.Runtime.InteropServices;
using System.Text;

namespace SkullgirlsAudio;

public static class NativeMusicEncoder
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public uint Size, Status; public UIntPtr User; public IntPtr Source; public uint SourceLength, SourceUsed;
        public UIntPtr SourceUser; public IntPtr Destination; public uint DestinationLength, DestinationUsed;
        public UIntPtr DestinationUser;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public UIntPtr[] Reserved;
    }
    [DllImport("msacm32.dll")] private static extern uint acmStreamOpen(out IntPtr stream, IntPtr driver, byte[] source, byte[] target, IntPtr filter, UIntPtr callback, UIntPtr instance, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamSize(IntPtr stream, uint input, out uint output, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamPrepareHeader(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamConvert(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamUnprepareHeader(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamClose(IntPtr stream, uint flags);
    private static void Check(uint result) { if (result != 0) throw new NotSupportedException($"Windows ADPCM encoder unavailable or rejected the target (ACM {result})."); }
    public static byte[] Encode(byte[] pcm, Wave target)
    {
        if (target.FormatTag != 2 || target.Block != 140 || target.Channels != 2 || target.SamplesPerBlock != 128 || target.Sampler?.Length != 60 || pcm.Length == 0 || pcm.Length % (target.SamplesPerBlock * 4) != 0)
            throw new ArgumentException("Encoding requires complete blocks for the validated stereo music profile.");
        using var format = new MemoryStream();
        using (var w = new BinaryWriter(format, Encoding.UTF8, true))
        { w.Write((ushort)1); w.Write((ushort)2); w.Write(target.Rate); w.Write(target.Rate * 4); w.Write((ushort)4); w.Write((ushort)16); w.Write((ushort)0); }
        Check(acmStreamOpen(out var stream, IntPtr.Zero, format.ToArray(), target.Format, IntPtr.Zero, UIntPtr.Zero, UIntPtr.Zero, 4));
        try
        {
            Check(acmStreamSize(stream, (uint)pcm.Length, out uint capacity, 0));
            if (capacity > 128 * 1024 * 1024) throw new IOException("Encoded output exceeds the editor limit.");
            var data = new byte[capacity]; var source = GCHandle.Alloc(pcm, GCHandleType.Pinned); var destination = GCHandle.Alloc(data, GCHandleType.Pinned);
            uint used;
            try
            {
                var h = new Header { Size = (uint)Marshal.SizeOf<Header>(), Source = source.AddrOfPinnedObject(), SourceLength = (uint)pcm.Length, Destination = destination.AddrOfPinnedObject(), DestinationLength = capacity, Reserved = new UIntPtr[10] };
                Check(acmStreamPrepareHeader(stream, ref h, 0));
                try
                {
                    Check(acmStreamConvert(stream, ref h, 4 | 16 | 32));
                    if (h.SourceUsed != pcm.Length || h.DestinationUsed != pcm.Length / 512 * 140) throw new InvalidDataException("Encoder did not produce the exact complete-block sample count.");
                    used = h.DestinationUsed;
                }
                finally { Check(acmStreamUnprepareHeader(stream, ref h, 0)); }
            }
            finally { source.Free(); destination.Free(); }
            byte[] sampler = (byte[])target.Sampler.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(sampler.AsSpan(48, 4), pcm.Length / 4 - 1);
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
            void Chunk(string id, byte[] bytes) { writer.Write(Encoding.ASCII.GetBytes(id)); writer.Write(bytes.Length); writer.Write(bytes); if (bytes.Length % 2 != 0) writer.Write((byte)0); }
            Chunk("fmt ", target.Format); Chunk("fact", BitConverter.GetBytes(pcm.Length / 4)); Chunk("smpl", sampler); Chunk("data", data.AsSpan(0, (int)used).ToArray());
            writer.Flush(); output.Position = 4; writer.Write((int)output.Length - 8); return output.ToArray();
        }
        finally { acmStreamClose(stream, 0); }
    }
}
