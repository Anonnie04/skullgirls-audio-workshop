using System.Runtime.InteropServices;
using System.Text;

namespace SkullgirlsAudio;

public sealed class Wave
{
    public byte[] Format { get; private set; } = [];
    public byte[] Data { get; private set; } = [];
    public int Channels => BitConverter.ToUInt16(Format, 2);
    public int FormatTag => BitConverter.ToUInt16(Format, 0);
    public int Bits => BitConverter.ToUInt16(Format, 14);
    public string Codec => FormatTag == 2 ? "Microsoft ADPCM" : $"PCM {Bits}-bit";
    public int Rate => BitConverter.ToInt32(Format, 4);
    public int Block => BitConverter.ToUInt16(Format, 12);
    public int SamplesPerBlock => FormatTag == 2 ? BitConverter.ToUInt16(Format, 18) : 1;
    private int? factFrames;
    public int Frames => factFrames ?? checked(Data.Length / Block * SamplesPerBlock);
    public double Seconds => (double)Frames / Rate;
    public bool HasSampler { get; private set; }
    public byte[]? Sampler { get; private set; }
    public static Wave Parse(byte[] bytes)
    {
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE" || BitConverter.ToUInt32(bytes, 4) != bytes.Length - 8) throw new InvalidDataException("Invalid RIFF WAVE.");
        var result = new Wave(); var chunks = new HashSet<string>();
        for (long pos = 12; pos < bytes.Length;)
        {
            if (bytes.Length - pos < 8) throw new InvalidDataException("Truncated WAV chunk header.");
            string id = Encoding.ASCII.GetString(bytes, (int)pos, 4); uint size = BitConverter.ToUInt32(bytes, (int)pos + 4); pos += 8;
            if (size > bytes.Length - pos || ((id is "fmt " or "data" or "fact") && !chunks.Add(id))) throw new InvalidDataException("Invalid WAV chunk.");
            if (id == "fmt ") result.Format = bytes.AsSpan((int)pos, (int)size).ToArray();
            if (id == "data") result.Data = bytes.AsSpan((int)pos, (int)size).ToArray();
            if (id == "smpl") { result.HasSampler = true; result.Sampler = bytes.AsSpan((int)pos, (int)size).ToArray(); }
            if (id == "fact" && size >= 4) { uint frames = BitConverter.ToUInt32(bytes, (int)pos); if (frames > 0 && frames <= int.MaxValue) result.factFrames = (int)frames; }
            pos += size + size % 2;
        }
        if (result.Format.Length < 16 || result.Channels is not (1 or 2) || result.Rate < 1000 || result.Rate > 192000 || result.Block == 0 || result.Data.Length == 0 || result.Data.Length % result.Block != 0)
            throw new NotSupportedException("Unsupported or incomplete WAV channels, sample rate or blocks.");
        if (result.FormatTag == 1)
        {
            result.factFrames = null;
            if (result.Bits is not (8 or 16) || result.Block != result.Channels * result.Bits / 8) throw new NotSupportedException("Preview supports PCM8/16 only.");
        }
        else if (result.FormatTag == 2)
        {
            if (result.Bits != 4 || result.Format.Length < 22 || result.Block < result.Channels * 7 || result.SamplesPerBlock < 2 || result.SamplesPerBlock > 2 + (result.Block - result.Channels * 7) * 2 / result.Channels)
                throw new InvalidDataException("Invalid Microsoft ADPCM block format.");
            int coefficients = BitConverter.ToUInt16(result.Format, 20);
            if (coefficients < 1 || coefficients > 256 || result.Format.Length < 22 + coefficients * 4) throw new InvalidDataException("Invalid ADPCM coefficients.");
            if (result.factFrames > (long)result.Data.Length / result.Block * result.SamplesPerBlock) throw new InvalidDataException("Invalid decoded sample count.");
        }
        else throw new NotSupportedException($"WAV codec {result.FormatTag} is not supported. Preview supports PCM8/16 and Microsoft ADPCM.");
        return result;
    }
}

public record Decoded(Wave Source, byte[] Pcm)
{
    public byte[] AsWave()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write("RIFF"u8); w.Write(Pcm.Length + 36); w.Write("WAVEfmt "u8); w.Write(16);
        w.Write((ushort)1); w.Write((ushort)Source.Channels); w.Write(Source.Rate);
        w.Write(Source.Rate * Source.Channels * 2); w.Write((ushort)(Source.Channels * 2)); w.Write((ushort)16);
        w.Write("data"u8); w.Write(Pcm.Length); w.Write(Pcm); return s.ToArray();
    }
    public float[] Peaks(int bins = 1500)
    {
        float[] peaks = new float[bins]; int samples = Pcm.Length / 2;
        for (int i = 0; i < samples; i++) { int bin = (int)((long)i * bins / samples); peaks[bin] = Math.Max(peaks[bin], Math.Abs((int)BitConverter.ToInt16(Pcm, i * 2)) / 32768f); }
        return peaks;
    }
}

public static class AcmDecoder
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
    [DllImport("msacm32.dll")] private static extern uint acmStreamPrepareHeader(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamConvert(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamUnprepareHeader(IntPtr stream, ref Header header, uint flags);
    [DllImport("msacm32.dll")] private static extern uint acmStreamClose(IntPtr stream, uint flags);
    private static void Check(uint value) { if (value != 0) throw new NotSupportedException($"Windows ADPCM decoder unavailable or rejected this asset (ACM {value})."); }
    public static Decoded Decode(Wave wave)
    {
        using var fmt = new MemoryStream(); using (var w = new BinaryWriter(fmt, Encoding.UTF8, true))
        { w.Write((ushort)1); w.Write((ushort)wave.Channels); w.Write(wave.Rate); w.Write(wave.Rate * wave.Channels * 2); w.Write((ushort)(wave.Channels * 2)); w.Write((ushort)16); w.Write((ushort)0); }
        Check(acmStreamOpen(out var stream, IntPtr.Zero, wave.Format, fmt.ToArray(), IntPtr.Zero, UIntPtr.Zero, UIntPtr.Zero, 4));
        var pcm = new byte[checked(wave.Frames * wave.Channels * 2)];
        var source = GCHandle.Alloc(wave.Data, GCHandleType.Pinned); var destination = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try
        {
            var h = new Header { Size = (uint)Marshal.SizeOf<Header>(), Source = source.AddrOfPinnedObject(), SourceLength = (uint)wave.Data.Length, Destination = destination.AddrOfPinnedObject(), DestinationLength = (uint)pcm.Length, Reserved = new UIntPtr[10] };
            Check(acmStreamPrepareHeader(stream, ref h, 0));
            try { Check(acmStreamConvert(stream, ref h, 4 | 16 | 32)); if (h.SourceUsed != wave.Data.Length || h.DestinationUsed != pcm.Length) throw new InvalidDataException("Decoded sample count differs from the archive format."); }
            finally { Check(acmStreamUnprepareHeader(stream, ref h, 0)); }
        }
        finally { source.Free(); destination.Free(); acmStreamClose(stream, 0); }
        return new(wave, pcm);
    }
}
