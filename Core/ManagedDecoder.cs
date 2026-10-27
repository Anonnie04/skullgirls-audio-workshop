// Adapted from Skullgirls Mod Manager Core/AssetPreviews.cs (GPL-3.0).
using System.Buffers.Binary;
using System.Text;
namespace SkullgirlsAudio;
public static class ManagedDecoder
{
    private record DecodedAudio(int Rate, int Channels, short[] Samples, string Format);
    private static int U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    private static int I32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static int S16(byte[] b, int offset) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(offset, 2));
    public static Decoded Decode(byte[] bytes)
    {
        var wave = Wave.Parse(bytes);
        var decoded = Wav(bytes);
        var pcm = new byte[checked(decoded.Samples.Length * 2)];
        Buffer.BlockCopy(decoded.Samples, 0, pcm, 0, pcm.Length);
        if (pcm.Length != wave.Frames * wave.Channels * 2) throw new InvalidDataException("Decoded sample count differs from the supported target format.");
        return new(wave, pcm);
    }
    private static DecodedAudio Wav(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("Invalid WAV header.");
        int fmt = -1, fmtSize = 0, data = -1, dataSize = 0, fact = 0;
        for (long p = 12; p + 8 <= bytes.Length;)
        {
            int size = I32(bytes, (int)p + 4); long next = p + 8 + size;
            if (size < 0 || next > bytes.Length) throw new InvalidDataException("Truncated WAV chunk.");
            string id = Encoding.ASCII.GetString(bytes, (int)p, 4);
            if (id == "fmt ") { fmt = (int)p + 8; fmtSize = size; }
            if (id == "data") { data = (int)p + 8; dataSize = size; }
            if (id == "fact" && size >= 4) fact = I32(bytes, (int)p + 8);
            p = next + (size & 1);
        }
        if (fmt < 0 || fmtSize < 16 || data < 0) throw new InvalidDataException("WAV format or data chunk missing.");
        int format = U16(bytes, fmt), channels = U16(bytes, fmt + 2), rate = I32(bytes, fmt + 4), block = U16(bytes, fmt + 12), bits = U16(bytes, fmt + 14);
        if (channels is not (1 or 2) || rate < 1000 || rate > 192000 || block <= 0) throw new NotSupportedException("Unsupported WAV channels, sample rate or block size.");
        long maxSamples = Math.Min(32_000_000, (long)rate * channels * 600);
        if (format == 1 && bits is 8 or 16)
        {
            int count = dataSize / (bits / 8);
            if (count > maxSamples) throw new InvalidDataException("Audio exceeds the 10 minute / 32 million sample preview limit.");
            var samples = new short[count];
            for (int i = 0; i < count; i++) samples[i] = bits == 16 ? (short)S16(bytes, data + i * 2) : (short)((bytes[data + i] - 128) << 8);
            return new(rate, channels, samples, $"WAV PCM {bits}-bit");
        }
        if (format != 2 || bits != 4 || fmtSize < 22) throw new NotSupportedException($"WAV codec {format} is not supported; PCM and Microsoft ADPCM are previewed.");
        int samplesPerBlock = U16(bytes, fmt + 18), coefficients = U16(bytes, fmt + 20);
        if (coefficients < 1 || coefficients > 256 || fmtSize < 22 + coefficients * 4 || block < channels * 7 || samplesPerBlock < 2
            || samplesPerBlock > 2 + (block - channels * 7) * 2 / channels) throw new InvalidDataException("Invalid Microsoft ADPCM format.");
        long capacity = ((dataSize + (long)block - 1) / block) * samplesPerBlock * channels;
        if (capacity > maxSamples) throw new InvalidDataException("Audio exceeds the 10 minute / 32 million sample preview limit.");
        var output = new List<short>((int)capacity);
        int[] adaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
        var predictor = new int[channels]; var delta = new int[channels]; var s1 = new int[channels]; var s2 = new int[channels];
        for (int start = data; start < data + dataSize; start += block)
        {
            int end = Math.Min(start + block, data + dataSize), p = start;
            if (end - start < channels * 7) throw new InvalidDataException("Truncated ADPCM block header.");
            for (int c = 0; c < channels; c++) { predictor[c] = bytes[p++]; if (predictor[c] >= coefficients) throw new InvalidDataException("Invalid ADPCM predictor."); }
            for (int c = 0; c < channels; c++) { delta[c] = Math.Max(16, U16(bytes, p)); p += 2; }
            for (int c = 0; c < channels; c++) { s1[c] = S16(bytes, p); p += 2; }
            for (int c = 0; c < channels; c++) { s2[c] = S16(bytes, p); p += 2; output.Add((short)s2[c]); }
            for (int c = 0; c < channels; c++) output.Add((short)s1[c]);
            int emitted = 2 * channels;
            while (p < end && emitted < samplesPerBlock * channels)
            {
                int value = bytes[p++];
                for (int half = 0; half < 2 && emitted < samplesPerBlock * channels; half++)
                {
                    int c = channels == 2 ? half : 0, nibble = half == 0 ? value >> 4 : value & 15, signed = nibble >= 8 ? nibble - 16 : nibble;
                    int coef = fmt + 22 + predictor[c] * 4;
                    long predicted = ((long)s1[c] * S16(bytes, coef) + (long)s2[c] * S16(bytes, coef + 2)) / 256 + (long)signed * delta[c];
                    int sample = (int)Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    s2[c] = s1[c]; s1[c] = sample; output.Add((short)sample); emitted++;
                    delta[c] = (int)Math.Clamp((long)adaptation[nibble] * delta[c] / 256, 16, int.MaxValue);
                }
            }
        }
        if (fact > 0 && (long)fact * channels < output.Count) output.RemoveRange(fact * channels, output.Count - fact * channels);
        return new(rate, channels, output.ToArray(), "WAV Microsoft ADPCM → PCM16");
    }
}
