using System.Runtime.InteropServices;

namespace SkullgirlsAudioWorkshop;

// Adapted from Skullgirls Mod Manager App/AudioPlayback.cs (GPL-3.0).
// Keep PCM alive until waveOut releases its buffers. Device position drives the UI.
internal sealed class AudioPlayback : IDisposable
{
    private IntPtr device, samples;
    private readonly IntPtr[] headers = new IntPtr[2];
    private readonly bool[] prepared = new bool[2];
    private readonly int rate, channels, totalFrames;
    private int firstFrame, loopStart, loopEnd, leadFrames;
    private bool looping, disposed;
    public double Duration => totalFrames / (double)rate;
    public bool Finished => !looping && ((!Active && firstFrame >= totalFrames) || (Active && (Marshal.PtrToStructure<WaveHeader>(headers[0]).Flags & 1) != 0));
    public bool Paused { get; private set; }
    public bool Active { get; private set; }
    public double Position
    {
        get
        {
            if (!Active) return firstFrame / (double)rate;
            var time = new WaveTime { Type = 4 };
            Check(waveOutGetPosition(device, ref time, (uint)Marshal.SizeOf<WaveTime>()));
            if (time.Type != 4) throw new InvalidOperationException("Audio device did not return a byte position.");
            long frames = time.Value / (channels * 2);
            if (!looping) return Math.Min(Duration, (firstFrame + frames) / (double)rate);
            return frames < leadFrames ? (firstFrame + frames) / (double)rate
                : (loopStart + (frames - leadFrames) % (loopEnd - loopStart)) / (double)rate;
        }
    }
    public AudioPlayback(SkullgirlsAudio.Decoded audio) : this(audio.Pcm, audio.Source.Rate, audio.Source.Channels) { }
    public AudioPlayback(byte[] pcm, int sampleRate, int channelCount)
    {
        if (sampleRate < 1000 || sampleRate > 192000 || channelCount is not (1 or 2) || pcm.Length == 0 || pcm.Length % (channelCount * 2) != 0) throw new ArgumentException("Invalid PCM preview.");
        rate = sampleRate; channels = channelCount; totalFrames = pcm.Length / (channels * 2);
        try
        {
            var format = new WaveFormat { Format = 1, Channels = (ushort)channels, Rate = (uint)rate,
                BytesPerSecond = (uint)(rate * channels * 2), Align = (ushort)(channels * 2), Bits = 16 };
            Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
            samples = Marshal.AllocHGlobal(pcm.Length);
            Marshal.Copy(pcm, 0, samples, pcm.Length);
            for (int i = 0; i < headers.Length; i++) headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        }
        catch { Dispose(); throw; }
    }
    public void Start(double seconds, bool repeat = false, double start = 0, double? end = null, bool paused = false, bool includeIntro = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!double.IsFinite(seconds) || !double.IsFinite(start) || (end.HasValue && !double.IsFinite(end.Value))) throw new ArgumentException("Invalid playback position.");
        int rangeStart = (int)Math.Clamp(Math.Round(start * rate), 0, totalFrames);
        int rangeEnd = (int)Math.Clamp(Math.Round((end ?? Duration) * rate), 0, totalFrames);
        if (repeat && rangeEnd <= rangeStart) throw new ArgumentException("Repeat end must be after repeat start.");
        ReleaseBuffer();
        loopStart = rangeStart; loopEnd = rangeEnd; looping = repeat;
        firstFrame = (int)Math.Clamp(Math.Round(seconds * rate), 0, totalFrames);
        if (repeat && ((!includeIntro && firstFrame < loopStart) || firstFrame >= loopEnd)) firstFrame = loopStart;
        leadFrames = repeat && firstFrame != loopStart ? loopEnd - firstFrame : 0;
        Paused = paused;
        if (!repeat && firstFrame == totalFrames) return;
        // Queue while paused so a seek cannot briefly emit audio before re-pausing.
        Check(waveOutPause(device));
        if (repeat)
        {
            if (leadFrames > 0) Queue(0, firstFrame, leadFrames, false);
            Queue(leadFrames > 0 ? 1 : 0, loopStart, loopEnd - loopStart, true);
        }
        else Queue(0, firstFrame, totalFrames - firstFrame, false);
        Active = true;
        if (!paused) Check(waveOutRestart(device));
    }
    private void Queue(int index, int frame, int count, bool repeat)
    {
        var header = headers[index];
        Marshal.StructureToPtr(new WaveHeader { Data = IntPtr.Add(samples, checked(frame * channels * 2)), Length = (uint)checked(count * channels * 2) }, header, false);
        Check(waveOutPrepareHeader(device, header, (uint)Marshal.SizeOf<WaveHeader>())); prepared[index] = true;
        if (repeat)
        {
            var value = Marshal.PtrToStructure<WaveHeader>(header); value.Flags |= 4 | 8; value.Loops = uint.MaxValue;
            Marshal.StructureToPtr(value, header, false);
        }
        Check(waveOutWrite(device, header, (uint)Marshal.SizeOf<WaveHeader>()));
    }
    public void Pause() { if (!Active) return; Check(waveOutPause(device)); Paused = true; }
    public void Resume() { if (!Active) return; Check(waveOutRestart(device)); Paused = false; }
    private void ReleaseBuffer()
    {
        if (device == IntPtr.Zero) return;
        Check(waveOutReset(device)); Active = false;
        for (int i = 0; i < headers.Length; i++)
            if (prepared[i]) { Check(waveOutUnprepareHeader(device, headers[i], (uint)Marshal.SizeOf<WaveHeader>())); prepared[i] = false; }
    }
    public void Dispose()
    {
        if (disposed) return;
        // Do not free buffers while a device might still own them if reset/unprepare fails.
        ReleaseBuffer();
        if (device != IntPtr.Zero) { Check(waveOutClose(device)); device = IntPtr.Zero; }
        for (int i = 0; i < headers.Length; i++) if (headers[i] != IntPtr.Zero) { Marshal.FreeHGlobal(headers[i]); headers[i] = IntPtr.Zero; }
        if (samples != IntPtr.Zero) { Marshal.FreeHGlobal(samples); samples = IntPtr.Zero; }
        Paused = false; disposed = true;
    }
    private static void Check(uint result)
    {
        if (result == 0) return;
        var message = new System.Text.StringBuilder(256); waveOutGetErrorText(result, message, message.Capacity);
        throw new InvalidOperationException($"Audio output: {message} ({result})");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Format, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveTime { public uint Type, Value, Padding; }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr handle, ref WaveTime time, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutPause(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutRestart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetErrorTextW")] private static extern uint waveOutGetErrorText(uint error, System.Text.StringBuilder text, int size);
}
