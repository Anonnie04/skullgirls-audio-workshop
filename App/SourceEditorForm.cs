using SkullgirlsAudio;

namespace SkullgirlsAudioWorkshop;

internal sealed class SourceEditorForm : Form
{
    private readonly AudioSource source;
    private readonly EditSettings initial;
    private readonly EditorWaveform wave = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown start, loop, end;
    private readonly CheckBox skipIntro = new() { Text = "Start full song (skip intro)", AutoSize = true, AccessibleName = "Start full song and skip intro" };
    private readonly NumericUpDown zoom = new() { Minimum = 1, Maximum = 32, Value = 1, Width = 75, AccessibleName = "Source waveform zoom" };
    private readonly EditorScroll scroll = new() { Dock = DockStyle.Fill, AccessibleName = "Source waveform scroll" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true }, selection = new() { Dock = DockStyle.Fill };
    private readonly Button play = Button("Play intro → loop"), seam = Button("Check loop seam"), pause = Button("Pause"), stop = Button("Stop");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private AudioPlayback? player;
    private double playbackStart;
    private bool initializing = true;
    private long lastStatusUpdate;
    public EditSettings Result { get; private set; }
    public SourceEditorForm(AudioSource source, EditSettings edits, float[] peaks, string? displayName = null)
    {
        this.source = source; initial = Result = edits;
        Text = "Skullgirls Audio Workshop — Edit source / Side A"; ClientSize = new(1120, 740); MinimumSize = new(1040, 650); StartPosition = FormStartPosition.CenterParent;
        BackColor = DecoTheme.Ink; ForeColor = DecoTheme.Ivory; Font = new("Segoe UI", 10); DoubleBuffered = true; KeyPreview = true; DecoTheme.StyleTitleBar(this);
        start = Number("Source intro start", edits.Start); loop = Number("Source loop start", edits.LoopStart); end = Number("Source loop end", edits.End);
        skipIntro.Checked = edits.SkipIntro;
        wave.Peaks = peaks; wave.Duration = source.Audio.Source.Seconds; wave.Start = edits.Start; wave.Loop = edits.LoopStart; wave.End = edits.End; wave.Position = edits.Start;
        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), RowCount = 10, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.Absolute, 46)); root.RowStyles.Add(new(SizeType.Absolute, 50)); root.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 42, 46, 48, 32, 48, 38, 48 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "SIDE A  /  CUT THE SOURCE", Font = new("Georgia", 20, FontStyle.Bold), ForeColor = DecoTheme.Gold, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = $"{displayName ?? Path.GetFileName(source.Path)} · {wave.Duration:N3} seconds\nClick the waveform to place the cursor, then set a boundary. Intro plays once; the loop repeats.", Dock = DockStyle.Fill }, 0, 1);
        root.Controls.Add(wave, 0, 2);
        var navigation = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; navigation.ColumnStyles.Add(new(SizeType.Absolute, 190)); navigation.ColumnStyles.Add(new(SizeType.Percent, 100)); navigation.RowStyles.Add(new(SizeType.Percent, 100));
        navigation.Controls.Add(Row(Caption("Zoom"), zoom), 0, 0); navigation.Controls.Add(scroll, 1, 0); root.Controls.Add(navigation, 0, 3);
        Button markStart = Button("Intro starts here  [I]"), markLoop = Button("Loop starts here  [L]"), markEnd = Button("Crop end at cursor  [E]"), fullEnd = Button("Use full ending");
        root.Controls.Add(Row(markStart, markLoop, markEnd, fullEnd), 0, 4);
        root.Controls.Add(Row(Caption("Intro start (s)"), start, Caption("Loop start (s)"), loop, Caption("Crop end (s)"), end, skipIntro), 0, 5);
        root.Controls.Add(selection, 0, 6); root.Controls.Add(Row(play, seam, pause, stop), 0, 7);
        root.Controls.Add(status, 0, 8);
        Button apply = Button("Use these cuts"), cancel = Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(Row(apply, cancel, Caption("Source audio is preserved. Side B previews include gain, fades and crossfade.")), 0, 9);
        CancelButton = cancel;
        foreach (var n in new[] { start, loop, end }) n.ValueChanged += (_, _) => Changed();
        skipIntro.CheckedChanged += (_, _) => { if (skipIntro.Checked) wave.Position = 0; markStart.Enabled = markLoop.Enabled = !skipIntro.Checked; Changed(); }; fullEnd.Click += (_, _) => end.Value = (decimal)source.Audio.Source.Seconds;
        markStart.Enabled = markLoop.Enabled = !skipIntro.Checked;
        zoom.ValueChanged += (_, _) => View(); scroll.ValueChanged += (_, _) => View();
        wave.Clicked += seconds => { Stop(); wave.Position = seconds; status.Text = $"Cursor {seconds:N3} s — choose an intro or loop boundary."; };
        wave.MarkerMoved += (marker, seconds) => { var number = marker == WaveMarker.Intro ? start : marker == WaveMarker.Loop ? loop : end; number.Value = Math.Clamp((decimal)seconds, number.Minimum, number.Maximum); };
        markStart.Click += (_, _) => Mark(start); markLoop.Click += (_, _) => Mark(loop); markEnd.Click += (_, _) => Mark(end);
        play.Click += (_, _) => Play(false); seam.Click += (_, _) => Play(true);
        pause.Click += (_, _) => { try { if (player?.Paused == true) player.Resume(); else player?.Pause(); pause.Text = player?.Paused == true ? "Resume" : "Pause"; } catch (Exception ex) { Stop(); status.Text = ex.Message; } };
        stop.Click += (_, _) => { Stop(); status.Text = "Playback stopped."; };
        apply.Click += (_, _) =>
        {
            try { var cuts = Cuts(); cuts.Validate(source); source.AssertUnchanged(); Result = initial with { Start = cuts.Start, LoopStart = cuts.LoopStart, End = cuts.End, SkipIntro = cuts.SkipIntro }; DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        KeyDown += (_, e) =>
        {
            if (ActiveControl is NumericUpDown or TextBox) return;
            if (skipIntro.Checked && e.KeyCode is Keys.I or Keys.L) return;
            if (e.KeyCode == Keys.I) Mark(start); else if (e.KeyCode == Keys.L) Mark(loop); else if (e.KeyCode == Keys.E) Mark(end); else return;
            e.SuppressKeyPress = true;
        };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (player == null) return; double position = playbackStart + player.Position; wave.Position = position;
                if (Environment.TickCount64 - lastStatusUpdate < 150) return; lastStatusUpdate = Environment.TickCount64;
                status.Text = $"Source audition · {position:N3} s · {(player.Paused ? "paused" : !skipIntro.Checked && position < (double)loop.Value ? "intro" : "loop repeats")}";
            }
            catch (Exception ex) { Stop(); status.Text = ex.Message; }
        };
        FormClosing += (_, _) => { Stop(); timer.Dispose(); };
        DecoTheme.Apply(this); initializing = false; Changed();
    }
    private EditSettings Cuts() => new((double)start.Value, (double)loop.Value, (double)end.Value, SkipIntro: skipIntro.Checked);
    private NumericUpDown Number(string name, double value) => new() { AccessibleName = name, DecimalPlaces = 3, Increment = .01m, Width = 110, Maximum = (decimal)Math.Max(source.Audio.Source.Seconds, value), Value = (decimal)value };
    private void Mark(NumericUpDown number) => number.Value = Math.Clamp((decimal)wave.Position, number.Minimum, number.Maximum);
    private void Changed()
    {
        if (initializing) return; Stop(); start.Enabled = loop.Enabled = !skipIntro.Checked; wave.IntroMarkersEnabled = !skipIntro.Checked; wave.Start = skipIntro.Checked ? 0 : (double)start.Value; wave.Loop = skipIntro.Checked ? 0 : (double)loop.Value; wave.End = (double)end.Value; wave.Invalidate();
        selection.Text = $"INTRO   {wave.Start:N3} → {wave.Loop:N3} s   ({wave.Loop - wave.Start:N3} s)       LOOP   {wave.Loop:N3} → {wave.End:N3} s   ({wave.End - wave.Loop:N3} s)";
        if (skipIntro.Checked) selection.Text = $"FULL SONG   0 → {wave.End:N3} s repeats. The game receives a minimal silent lead-in; verify this behavior in-game.";
        try { Cuts().Validate(source); status.Text = "Cuts ready. Audition the intro transition or the repeating loop seam."; play.Enabled = seam.Enabled = true; }
        catch (ArgumentException ex) { status.Text = ex.Message; play.Enabled = seam.Enabled = false; }
    }
    private void View() { wave.Zoom = (double)zoom.Value; wave.Offset = (wave.Duration - wave.Duration / wave.Zoom) * scroll.Value / 1000; wave.Invalidate(); }
    private void Play(bool nearSeam)
    {
        Stop();
        try
        {
            var cuts = Cuts(); cuts.Validate(source);
            int rate = source.Audio.Source.Rate, channels = source.Audio.Source.Channels;
            int first = cuts.SkipIntro ? 0 : (int)Math.Round(cuts.Start * rate), last = Math.Min(source.Audio.Source.Frames, (int)Math.Round(cuts.End * rate));
            var pcm = source.Audio.Pcm.AsSpan(first * channels * 2, (last - first) * channels * 2).ToArray();
            playbackStart = first / (double)rate; double loopAt = cuts.SkipIntro ? 0 : (int)Math.Round(cuts.LoopStart * rate) / (double)rate - playbackStart;
            player = new(pcm, rate, channels);
            double from = nearSeam ? Math.Max(loopAt, player.Duration - .75) : 0;
            player.Start(from, true, loopAt, player.Duration, includeIntro: true); pause.Enabled = stop.Enabled = true; lastStatusUpdate = 0; timer.Start();
        }
        catch (Exception ex) { Stop(); status.Text = ex.Message; }
    }
    private void Stop() { timer.Stop(); var old = player; player = null; old?.Dispose(); pause.Text = "Pause"; pause.Enabled = stop.Enabled = false; }
    private static Button Button(string text) => new DecoButton { Text = text, AutoSize = true, Padding = new(9, 4, 9, 4), Font = new("Bahnschrift", 9) };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 8, 0, 0), Font = new("Segoe UI", 9) };
    private static BufferedFlowLayoutPanel Row(params Control[] controls) { var row = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; row.Controls.AddRange(controls); return row; }
}
