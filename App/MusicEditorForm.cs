using SkullgirlsAudio;
using System.ComponentModel;

namespace SkullgirlsAudioWorkshop;

internal sealed class MusicEditorForm : Form
{
    private readonly MusicTarget target;
    private AudioSource? source;
    private GeneratedPair? generated;
    private AudioPlayback? player;
    private CancellationTokenSource? operation;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
    private readonly EditorWaveform wave = new() { Dock = DockStyle.Fill };
    private readonly Turntable sourceDisc = new() { Dock = DockStyle.Fill, Margin = new(0, 0, 10, 0) }, targetDisc = new() { Dock = DockStyle.Fill, Margin = new(0, 0, 10, 0) };
    private readonly NumericUpDown start = Number("Intro start"), loop = Number("Loop start"), end = Number("Loop end"), gain = Number("Gain dB", -60, 24), fadeIn = Number("Intro fade in"), fadeOut = Number("Loop fade out"), crossfade = Number("Loop crossfade"), zoom = Number("Waveform zoom", 1, 32);
    private readonly EditorScroll scroll = new() { Dock = DockStyle.Fill, AccessibleName = "Waveform scroll" };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = "Waveform click action" };
    private readonly Label sourceInfo = Label("Drop a song here or choose Import audio…\nWAV, MP3, FLAC, OGG/Opus, M4A/AAC, WMA or AIFF."), status = Label("Ready"), metrics = Label("Save a project to keep your source, cuts and processing settings.");
    private readonly TextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Verified output folder" };
    private readonly Button import = Button("Import audio…"), original = Button("Play original"), raw = Button("Play source"), preview = Button("Preview edits"), generate = Button("Generate & verify"), converted = Button("Play converted"), pause = Button("Pause"), stop = Button("Stop"), cancel = Button("Cancel operation");
    private readonly DecoToggleButton repeat = new() { Checked = true, AutoSize = true };
    private readonly CheckBox skipIntro = new() { Text = "Start full song (skip intro)", AutoSize = true, AccessibleName = "Start full song and skip intro" };
    private readonly ToolStripMenuItem useInMod = new("Use in mod project");
    private readonly bool collectionMode;
    public AudioProject? ModSong { get; private set; }
    private readonly Button editSource = Button("Edit source…");
    private readonly List<Control> editable = [];
    private bool updating, sourcePlayback;
    private double activeLoopStart;
    private Audition? activeAudio;
    private string playbackEdition = "Original target";
    private long lastStatusUpdate;
    private readonly ToolStripMenuItem saveProject = new("Save project") { ShortcutKeys = Keys.Control | Keys.S }, saveAs = new("Save as…") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S }, copySource = new("Copy source: ON") { CheckOnClick = true, Checked = true, ToolTipText = "Store a verified original-format source copy beside the project" };
    private readonly ToolStripLabel projectLabel = new("Unsaved project");
    private readonly ToolStripMenuItem exportMenu = new("Export"), editModDetails = new("Mod details…"), exportNative = new("Export WAVs…"), exportPackage = new("Export mod ZIP…");
    private readonly ToolStripMenuItem installation = new("Apply / Restore…");
    public bool InstallationChanged { get; private set; }
    private readonly System.Windows.Forms.Timer draftTimer = new() { Interval = 1000 };
    private Guid projectId = Guid.NewGuid();
    private string draftPath = Path.Combine(DraftFolder, Guid.NewGuid().ToString("N") + ProjectFiles.Extension);
    private string? draftHash;
    private LoadedProject? savedProject;
    private ProjectExport exportDetails = new();
    private string sourceName = "";
    private bool dirty, closingApproved;
    internal static string DraftFolder => Path.Combine(AppContext.BaseDirectory, "drafts");
    private readonly LoadedProject? opening;
    public MusicEditorForm(MusicTarget target, LoadedProject? opening = null, bool collectionMode = false)
    {
        this.collectionMode = collectionMode;
        this.target = target; this.opening = opening; exportDetails = new(target.Track.Name);
        Text = "Skullgirls Audio Workshop — Music replacement editor"; ClientSize = new(1200, 960); MinimumSize = new(1140, 900); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10); BackColor = DecoTheme.Ink; ForeColor = DecoTheme.Ivory; DoubleBuffered = true; DecoTheme.StyleTitleBar(this);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), RowCount = 9, ColumnCount = 1 };
        foreach (int height in new[] { 46, 300 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 44, 52, 52, 52, 90, 38 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(layout);
        var projectMenu = new MenuStrip { BackColor = DecoTheme.Surface, ForeColor = DecoTheme.Ivory, Padding = new(20, 5, 12, 5), Renderer = new ToolStripProfessionalRenderer(new DecoMenuColors()) };
        exportMenu.DropDownItems.AddRange([editModDetails, exportNative, exportPackage]);
        foreach (ToolStripItem item in exportMenu.DropDownItems) item.ForeColor = DecoTheme.Ivory;
        projectMenu.Items.AddRange([saveProject, saveAs, copySource, exportMenu, installation, useInMod, projectLabel]); Controls.Add(projectMenu); MainMenuStrip = projectMenu;
        useInMod.Visible = collectionMode; installation.Visible = !collectionMode;
        useInMod.Click += (_, _) => { try { if (source == null || operation != null) return; Settings().Validate(source); source.AssertUnchanged(); target.AssertUnchanged(); ModSong = Snapshot(); closingApproved = true; DialogResult = DialogResult.OK; Close(); } catch (Exception ex) { status.Text = ex.Message; } };
        installation.Click += (_, _) =>
        {
            Stop(); using var dialog = new InstallationDialog(target.GameFolder, target, generated, source, source == null ? null : Settings()); dialog.ShowDialog(this);
            if (dialog.GameFilesChanged) { InstallationChanged = true; generated = null; output.Text = ""; status.Text = "Installation changed. Save this project, close the editor and reopen the installation before generating again."; Ready(); }
        };
        saveProject.Click += async (_, _) => await SaveProject(false); saveAs.Click += async (_, _) => await SaveProject(true);
        editModDetails.Click += (_, _) => ReviewDetails(false); exportNative.Click += async (_, _) => await Export(false); exportPackage.Click += async (_, _) => await Export(true);
        copySource.CheckedChanged += (_, _) => { copySource.Text = copySource.Checked ? "Copy source: ON" : "Copy source: OFF"; if (source != null && !updating) MarkDirty(); };
        draftTimer.Tick += (_, _) => { draftTimer.Stop(); SaveDraft(); };
        Shown += async (_, _) => { if (this.opening != null) await RestoreProject(this.opening); };
        layout.Controls.Add(new Label { Text = "THE CUTTING ROOM   /   " + target.Track.Name, Font = new("Georgia", 18, FontStyle.Bold), ForeColor = DecoTheme.Gold, Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 0);
        var decks = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0, 0, 0, 12) };
        decks.ColumnStyles.Add(new(SizeType.Percent, 50)); decks.ColumnStyles.Add(new(SizeType.Percent, 50)); decks.RowStyles.Add(new(SizeType.Percent, 100));
        var targetInfo = Label($"{target.Track.Name}\n{target.Track.Archive}\n{string.Join("\n", target.Track.Assets)}\nIntro {target.Formats[0].Rate:N0} Hz / Loop {target.Formats[1].Rate:N0} Hz\nStereo MS ADPCM"); targetInfo.Font = new("Segoe UI", 9);
        sourceInfo.Font = new("Segoe UI", 9);
        sourceDisc.SetPalette(Color.FromArgb(35, 91, 141), Color.FromArgb(29, 57, 82), Color.FromArgb(16, 29, 46));
        var sourceDeck = Deck("SIDE A  /  SOURCE", sourceDisc, sourceInfo, [Row(import), Row(editSource), Row(raw)], new(0, 0, 8, 0));
        sourceDeck.BackColor = Color.FromArgb(20, 32, 48);
        EnableSourceDrop(sourceDeck); EnableSourceDrop(wave);
        decks.Controls.Add(sourceDeck, 0, 0);
        decks.Controls.Add(Deck("SIDE B  /  TARGET", targetDisc, targetInfo, [Row(original), Row(preview, converted), Row(generate)], new(8, 0, 0, 0)), 1, 0);
        sourceDisc.SetClip("Side A · Import a source"); targetDisc.SetClip("Side B · " + target.Track.Name);
        layout.Controls.Add(decks, 0, 1); layout.Controls.Add(wave, 0, 2);
        mode.Items.AddRange(["Seek source", "Set intro start", "Set loop start", "Set loop end"]); mode.SelectedIndex = 0; zoom.Value = 1;
        var navigation = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; navigation.ColumnStyles.Add(new(SizeType.Absolute, 450)); navigation.ColumnStyles.Add(new(SizeType.Percent, 100)); navigation.RowStyles.Add(new(SizeType.Percent, 100));
        navigation.Controls.Add(Row(Caption("Click:"), mode, Caption("Zoom"), zoom), 0, 0); navigation.Controls.Add(scroll, 1, 0); layout.Controls.Add(navigation, 0, 3);
        layout.Controls.Add(Row(Caption("Intro start (s)"), start, Caption("Loop start (s)"), loop, Caption("Crop end (s)"), end, skipIntro), 0, 4);
        layout.Controls.Add(Row(Caption("Gain (dB)"), gain, Caption("Intro fade (s)"), fadeIn, Caption("Loop fade (s)"), fadeOut, Caption("Crossfade (s)"), crossfade), 0, 5);
        layout.Controls.Add(Row(pause, stop, repeat, cancel, Caption("Crossfade shortens the loop by the overlap duration.")), 0, 6);
        var review = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; review.RowStyles.Add(new(SizeType.Percent, 100)); review.RowStyles.Add(new(SizeType.Absolute, 30)); review.Controls.Add(metrics, 0, 0); review.Controls.Add(output, 0, 1); layout.Controls.Add(review, 0, 7); layout.Controls.Add(status, 0, 8);
        editable.AddRange([import, original, raw, preview, generate, converted, editSource, start, loop, end, gain, fadeIn, fadeOut, crossfade, zoom, mode, scroll, wave]);
        foreach (var n in new[] { start, loop, end, gain, fadeIn, fadeOut, crossfade }) n.ValueChanged += (_, _) => Changed();
        skipIntro.CheckedChanged += (_, _) => { if (skipIntro.Checked) wave.Position = 0; Changed(); };
        zoom.ValueChanged += (_, _) => View(); scroll.ValueChanged += (_, _) => View();
        wave.Clicked += seconds =>
        {
            if (source == null || operation != null) return;
            if (mode.SelectedIndex == 0) { Stop(); wave.Position = seconds; status.Text = $"Source cursor: {seconds:N3} s"; }
            else { var number = mode.SelectedIndex == 1 ? start : mode.SelectedIndex == 2 ? loop : end; number.Value = Math.Clamp((decimal)seconds, number.Minimum, number.Maximum); }
        };
        wave.MarkerMoved += (marker, seconds) => { if (source == null || operation != null) return; var number = marker == WaveMarker.Intro ? start : marker == WaveMarker.Loop ? loop : end; number.Value = Math.Clamp((decimal)seconds, number.Minimum, number.Maximum); };
        import.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = SourceAudioDecoder.FileFilter, Title = "Import replacement source" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await ImportSource(dialog.FileName);
        };
        raw.Click += (_, _) => { if (source != null) Play(new(source.Audio.Pcm, source.Audio.Source.Rate, source.Audio.Source.Channels, 0), true, wave.Position); };
        editSource.Click += (_, _) =>
        {
            if (source == null) return; Stop();
            using var editor = new SourceEditorForm(source, Settings(), wave.Peaks, sourceName);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            updating = true; start.Value = (decimal)editor.Result.Start; loop.Value = (decimal)editor.Result.LoopStart; end.Value = (decimal)editor.Result.End; skipIntro.Checked = editor.Result.SkipIntro; updating = false;
            Changed(); targetDisc.SetClip("Side B · " + target.Track.Name); status.Text = "Source cuts updated. Preview edits or generate the replacement to hear all processing.";
        };
        preview.Click += async (_, _) =>
        {
            if (source == null) return; var audio = source; var edits = Settings();
            await Work("Rendering edited preview…", t => { audio.AssertUnchanged(); var pair = MusicEditing.Render(audio, target, edits, t); return (pair, audition: MusicEditing.Join(pair.Intro, pair.Loop, t)); }, result => { Report(result.pair, false); Play(result.audition, edition: "Edited preview"); });
        };
        generate.Click += async (_, _) =>
        {
            if (source == null) return; var audio = source; var edits = Settings(); generated = null; output.Text = "";
            await Work("Rendering, encoding, and independently decoding output…", t => MusicGeneration.Generate(audio, target, edits, Path.Combine(AppContext.BaseDirectory, "generated"), t), result =>
            { generated = result; output.Text = result.Folder; Report(result.Rendered, true); status.Text = "Generated files passed independent decoding. Use Play converted to review them."; });
        };
        converted.Click += (_, _) => { if (generated != null) Play(generated.Preview, edition: "Converted replacement"); };
        original.Click += async (_, _) => await Work("Decoding original intro and loop…", t =>
        { target.AssertUnchanged(); var a = ManagedDecoder.Decode(target.Originals[0]); var b = ManagedDecoder.Decode(target.Originals[1]); return MusicEditing.Join(a.Pcm, a.Source.Rate, b.Pcm, b.Source.Rate, 2, t); }, a => Play(a));
        pause.Click += (_, _) => { try { if (player?.Paused == true) player.Resume(); else player?.Pause(); pause.Text = player?.Paused == true ? "Resume" : "Pause"; UpdateDiscs(); lastStatusUpdate = 0; } catch (Exception ex) { Stop(); status.Text = ex.Message; } };
        stop.Click += (_, _) => { Stop(); status.Text = "Playback stopped."; }; cancel.Click += (_, _) => { operation?.Cancel(); status.Text = "Cancellation requested. Finishing the current step…"; };
        repeat.CheckedChanged += (_, _) => { if (activeAudio is { } a) Play(a, sourcePlayback, player?.Position ?? 0, playbackEdition); };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (player == null) return;
                double position = player.Position; if (sourcePlayback) wave.Position = position;
                UpdateDiscs();
                if (Environment.TickCount64 - lastStatusUpdate >= 150)
                { status.Text = $"{(sourcePlayback ? "Side A · Source" : "Side B · " + playbackEdition)} · {position:N3} / {player.Duration:N3} s · {(player.Paused ? "paused" : position < activeLoopStart ? "intro" : "loop")} · {(repeat.Checked ? "repeat on" : "repeat off")}"; lastStatusUpdate = Environment.TickCount64; }
                if (player.Finished) { Stop(); status.Text = "Playback complete."; }
            }
            catch (Exception ex) { Stop(); status.Text = ex.Message; }
        };
        FormClosing += async (_, e) =>
        {
            if (operation != null) { e.Cancel = true; operation.Cancel(); status.Text = "Cancelling; close again when the operation finishes."; return; }
            if (!closingApproved && dirty && source != null)
            {
                var answer = MessageBox.Show(this, "Save this project's changes before closing? No discards the changes and their recovery draft.", "Unsaved project", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
                if (answer == DialogResult.Yes) { e.Cancel = true; if (await SaveProject(false)) { closingApproved = true; Close(); } return; }
            }
            draftTimer.Stop(); RemoveDraft(); Stop(); timer.Dispose(); draftTimer.Dispose();
        };
        DecoTheme.Apply(this); Ready();
    }
    private static string? DroppedSource(IDataObject? data)
    {
        try
        {
            if (data?.GetDataPresent(DataFormats.FileDrop) != true || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
            string path = files[0]; return File.Exists(path) && SourceAudioDecoder.SupportsFile(path) ? path : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException) { return null; }
    }
    private void EnableSourceDrop(Control control)
    {
        control.AllowDrop = true;
        void Dragging(object? sender, DragEventArgs e) => e.Effect = operation == null && (e.AllowedEffect & DragDropEffects.Copy) != 0 && DroppedSource(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragEnter += Dragging; control.DragOver += Dragging;
        control.DragDrop += async (_, e) =>
        {
            if (operation != null) return;
            string? path = DroppedSource(e.Data);
            if (path == null) { status.Text = "Drop one supported audio file onto Side A or the waveform."; return; }
            await ImportSource(path);
        };
        foreach (Control child in control.Controls) EnableSourceDrop(child);
    }
    private async Task ImportSource(string path)
    {
        await Work("Reading and validating source…", token => { var loaded = AudioSource.Open(path, token); var peaks = loaded.Audio.Peaks(32768); token.ThrowIfCancellationRequested(); return (loaded, peaks); }, result =>
        {
            source = result.loaded; sourceName = Path.GetFileName(source.Path); generated = null; output.Text = ""; updating = true;
            foreach (var n in new[] { start, loop, end, fadeIn, fadeOut, crossfade }) n.Maximum = (decimal)source.Audio.Source.Seconds;
            start.Value = 0; loop.Value = Math.Min(10m, (decimal)source.Audio.Source.Seconds / 4); end.Value = (decimal)source.Audio.Source.Seconds; gain.Value = fadeIn.Value = fadeOut.Value = crossfade.Value = 0; skipIntro.Checked = false; zoom.Value = 1; scroll.Value = 0; updating = false;
            wave.Peaks = result.peaks; wave.Duration = source.Audio.Source.Seconds; wave.Position = 0; View(); Changed();
            sourceInfo.Text = $"{Path.GetFileName(source.Path)} · {source.Audio.Source.Seconds:N3} s · {source.Audio.Source.Rate:N0} Hz · {source.Audio.Source.Channels} channel(s)\n{source.Path}";
            sourceDisc.SetClip("Side A · " + Path.GetFileNameWithoutExtension(source.Path));
            status.Text = "Source imported. Set intro and loop boundaries, then preview or generate.";
        });
    }
    private EditSettings Settings() => new((double)start.Value, (double)loop.Value, (double)end.Value, (double)gain.Value, (double)fadeIn.Value, (double)fadeOut.Value, (double)crossfade.Value, skipIntro.Checked);
    private bool ReviewDetails(bool exporting)
    {
        if (source == null || operation != null) return false; Stop();
        using var dialog = new ExportDetailsDialog(exportDetails, target, exporting ? generated : null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (dialog.Details != exportDetails) { exportDetails = dialog.Details; MarkDirty(); } return true;
    }
    private async Task Export(bool package)
    {
        if (source == null || generated == null || operation != null || !ReviewDetails(true)) return;
        string path;
        if (package)
        {
            using var dialog = new SaveFileDialog { Filter = "Mod package ZIP|*.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = false, FileName = MusicExport.SafeName(exportDetails.Name) + "-" + exportDetails.Version + ".zip", Title = "Export mod ZIP — choose a new filename" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName;
        }
        else
        {
            using var dialog = new FolderBrowserDialog { Description = "Choose an export destination. A new folder will hold the WAV pair and export report.", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.SelectedPath;
        }
        var audio = source; var pair = generated; var edits = Settings(); var details = exportDetails; var id = projectId;
        await Work("Verifying audio and writing export…", token => package
            ? MusicExport.Package(path, pair, audio, target, edits, details, id, token)
            : MusicExport.Native(path, pair, audio, target, edits, details, id, token), result =>
        { output.Text = result; status.Text = package ? "Mod ZIP exported and verified. Import it into Manager to install; in-game playback still needs testing." : "Native WAV pair exported and verified. Game files unchanged."; }, committedResult: true);
    }
    private AudioProject Snapshot()
    {
        var snapshot = ProjectFiles.Capture(projectId, target, target.ArchiveFolder, source!, Settings(), exportDetails);
        return snapshot with { CopySourceOnSave = copySource.Checked, Source = snapshot.Source with { Name = sourceName } };
    }
    private void MarkDirty()
    {
        if (source == null || updating) return; dirty = true; projectLabel.Text = (collectionMode ? "Song in mod project" : savedProject == null ? "Unsaved project" : Path.GetFileName(savedProject.Path)) + " *";
        draftTimer.Stop(); draftTimer.Start();
    }
    private void SaveDraft()
    {
        if (!dirty || source == null) return;
        if (operation != null) { draftTimer.Start(); return; }
        try { ProjectFiles.WriteAtomic(draftPath, Snapshot(), draftHash); draftHash = ProjectFiles.Hash(draftPath); }
        catch (Exception ex) { status.Text = "Recovery draft could not be saved: " + ex.Message; }
    }
    private void RemoveDraft()
    {
        try
        {
            if (draftHash != null && File.Exists(draftPath) && ProjectFiles.Hash(draftPath) == draftHash
                && Path.GetDirectoryName(Path.GetFullPath(draftPath))!.Equals(Path.GetFullPath(DraftFolder), StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(Path.GetFileNameWithoutExtension(draftPath), "N", out _)) File.Delete(draftPath);
            draftHash = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Leave a recoverable copy if the file is busy. */ }
        draftPath = Path.Combine(DraftFolder, Guid.NewGuid().ToString("N") + ProjectFiles.Extension); draftHash = null;
    }
    private async Task<bool> SaveProject(bool choosePath)
    {
        if (source == null || operation != null) return false;
        ValidateChildren(); Stop(); string? path = savedProject?.Path;
        if (choosePath || path == null)
        {
            using var dialog = new SaveFileDialog { Filter = "Workshop project|*.sgawproj", DefaultExt = "sgawproj", AddExtension = true, InitialDirectory = Path.GetDirectoryName(savedProject?.Path ?? source.Path), FileName = path == null ? string.Join("_", target.Track.Name.Split(Path.GetInvalidFileNameChars())) + ".sgawproj" : Path.GetFileName(path), Title = "Save audio project" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false; path = dialog.FileName;
        }
        var snapshot = Snapshot(); var audio = source; bool copy = copySource.Checked, success = false;
        string? expected = savedProject != null && savedProject.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ? savedProject.Hash : null;
        draftTimer.Stop();
        await Work("Saving project and checking its source…", token => { token.ThrowIfCancellationRequested(); return ProjectFiles.Save(path, snapshot, audio, copy, expected); }, saved =>
        {
            savedProject = saved; dirty = false; projectLabel.Text = Path.GetFileName(saved.Path); RemoveDraft();
            if (copy) { source = audio with { Path = ProjectFiles.SourcePath(saved) }; sourceInfo.Text = $"{sourceName} · {audio.Audio.Source.Seconds:N3} s · copied with project\n{source.Path}"; }
            status.Text = "Project saved. " + (copy ? "A verified source copy is stored beside it." : "Source is referenced by path."); success = true;
        }, committedResult: true);
        if (!success && dirty) draftTimer.Start(); return success;
    }
    private async Task RestoreProject(LoadedProject loaded)
    {
        Stop(); string path;
        try { path = ProjectFiles.SourcePath(loaded); }
        catch (Exception ex) { status.Text = ex.Message; return; }
        if (!File.Exists(path))
        {
            using var relink = new OpenFileDialog { Filter = SourceAudioDecoder.FileFilter, Title = "Source missing — locate the project's audio" };
            if (relink.ShowDialog(this) != DialogResult.OK) { status.Text = "Project source is missing. Open the project again to relink it."; return; } path = relink.FileName;
        }
        await Work("Opening project source and checking hashes…", token => { var audio = AudioSource.Open(path, token); return (audio, peaks: audio.Audio.Peaks(32768)); }, result =>
        {
            bool changed = !result.audio.Hash.Equals(loaded.Project.Source.Hash, StringComparison.OrdinalIgnoreCase);
            if (changed && MessageBox.Show(this, "This source differs from the audio saved in the project. Use it and review all cuts and processing before generating again?", "Changed source — review required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            { status.Text = "Changed source was not accepted. Project file remains unchanged."; return; }
            source = result.audio; sourceName = loaded.Project.Source.Name.Length == 0 ? Path.GetFileName(source.Path) : loaded.Project.Source.Name; projectId = loaded.Project.Id; exportDetails = loaded.Project.Export; generated = null; output.Text = ""; updating = true;
            var e = loaded.Project.Edits;
            foreach (var n in new[] { start, loop, end, fadeIn, fadeOut, crossfade }) n.Maximum = Math.Max((decimal)source.Audio.Source.Seconds, (decimal)new[] { e.Start, e.LoopStart, e.End, e.FadeIn, e.FadeOut, e.Crossfade }.Max());
            start.Value = (decimal)e.Start; loop.Value = (decimal)e.LoopStart; end.Value = (decimal)e.End; gain.Value = (decimal)e.GainDb; fadeIn.Value = (decimal)e.FadeIn; fadeOut.Value = (decimal)e.FadeOut; crossfade.Value = (decimal)e.Crossfade; skipIntro.Checked = e.SkipIntro;
            copySource.Checked = loaded.Project.CopySourceOnSave; zoom.Value = 1; scroll.Value = 0; updating = false;
            wave.Peaks = result.peaks; wave.Duration = source.Audio.Source.Seconds; wave.Position = e.Start;
            sourceInfo.Text = $"{sourceName} · {source.Audio.Source.Seconds:N3} s · {source.Audio.Source.Rate:N0} Hz · {source.Audio.Source.Channels} channel(s)\n{source.Path}";
            sourceDisc.SetClip("Side A · " + Path.GetFileNameWithoutExtension(sourceName));
            bool recovery = Path.GetDirectoryName(loaded.Path)!.Equals(Path.GetFullPath(DraftFolder), StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(loaded.Path), "N", out _);
            if (recovery) { draftPath = loaded.Path; draftHash = loaded.Hash; } else if (!collectionMode) savedProject = loaded;
            View(); Changed();
            dirty = recovery || changed || !path.Equals(ProjectFiles.SourcePath(loaded), StringComparison.OrdinalIgnoreCase) || !ProjectFiles.TargetMatches(loaded.Project, target, target.ArchiveFolder);
            if (!dirty) { draftTimer.Stop(); projectLabel.Text = collectionMode ? "Song in mod project" : Path.GetFileName(loaded.Path); }
            status.Text = changed ? "Changed source accepted. Review all saved cuts and processing, then regenerate." : recovery ? "Recovery draft restored. Save a project to keep this work." : "Project opened. Regenerate converted audio before review or export.";
            metrics.Text = "Saved cuts and processing restored. Previous generated audio is not reused.";
        });
    }
    private void Changed()
    {
        if (updating) return; Stop(); generated = null; output.Text = ""; wave.IntroMarkersEnabled = !skipIntro.Checked; wave.Start = skipIntro.Checked ? 0 : (double)start.Value; wave.Loop = skipIntro.Checked ? 0 : (double)loop.Value; wave.End = (double)end.Value; wave.Invalidate();
        metrics.Text = "Edits changed. Generate again to verify converted output. Fades and crossfade are baked into the audio."; status.Text = "Edits changed. Converted preview requires regeneration."; MarkDirty(); Ready();
        if (skipIntro.Checked) metrics.Text += "\nFull-song mode starts at source 0 and repeats to the crop end. A minimal silent intro is generated; verify initial playback in-game.";
    }
    private void View() { wave.Zoom = (double)zoom.Value; wave.Offset = (wave.Duration - wave.Duration / wave.Zoom) * scroll.Value / 1000; wave.Invalidate(); }
    private void Report(EditedPair pair, bool verified)
    {
        long clipped = pair.Intro.ClippedSamples + pair.Loop.ClippedSamples;
        metrics.Text = $"{(verified ? "Converted output verified" : "Edited PCM preview")} · Intro {pair.Intro.Seconds:N3} s · Loop {pair.Loop.Seconds:N3} s · {clipped:N0} pre-encode clipped samples{(clipped > 0 ? " — reduce gain" : "")}\nBlock alignment by resampling: intro {pair.Intro.TimingAdjustmentMs:+0.000;-0.000;0} ms, loop {pair.Loop.TimingAdjustmentMs:+0.000;-0.000;0} ms. ADPCM is lossy; in-game playback is unverified.";
    }
    private async Task Work<T>(string message, Func<CancellationToken, T> work, Action<T> finish, bool committedResult = false)
    {
        if (operation != null) return; Stop(); operation = new(); var token = operation.Token; Ready(); status.Text = message;
        try { var result = await Task.Run(() => work(token)); if (!committedResult) token.ThrowIfCancellationRequested(); finish(result); }
        catch (OperationCanceledException) { status.Text = "Operation cancelled. Source and game files unchanged."; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { operation.Dispose(); operation = null; Ready(); }
    }
    private void Ready()
    {
        bool idle = operation == null; foreach (var c in editable) c.Enabled = idle;
        foreach (var c in new Control[] { raw, preview, generate, editSource, start, loop, end, gain, fadeIn, fadeOut, crossfade, zoom, scroll, mode, wave }) c.Enabled = idle && source != null;
        converted.Enabled = idle && generated != null; cancel.Enabled = !idle; pause.Enabled = stop.Enabled = player != null;
        saveProject.Enabled = saveAs.Enabled = copySource.Enabled = idle && source != null;
        exportMenu.Enabled = editModDetails.Enabled = idle && source != null; exportNative.Enabled = exportPackage.Enabled = idle && generated != null;
        installation.Enabled = idle;
        skipIntro.Enabled = useInMod.Enabled = idle && source != null; start.Enabled = loop.Enabled = idle && source != null && !skipIntro.Checked;
        wave.Enabled = idle;
    }
    private void Play(Audition audio, bool isSource = false, double seconds = 0, string edition = "Original target")
    {
        Stop();
        try
        {
            player = new(audio.Pcm, audio.Rate, audio.Channels); player.Start(seconds >= audio.Duration ? 0 : seconds, repeat.Checked, audio.LoopStart, audio.Duration, includeIntro: true);
            activeAudio = audio; activeLoopStart = audio.LoopStart; sourcePlayback = isSource; playbackEdition = edition;
            if (!isSource) targetDisc.SetClip("Side B · " + target.Track.Name + " · " + edition);
            UpdateDiscs(); timer.Start(); pause.Enabled = stop.Enabled = true;
        }
        catch (Exception ex) { Stop(); status.Text = ex.Message; }
    }
    private void Stop() { timer.Stop(); var previous = player; player = null; activeAudio = null; previous?.Dispose(); pause.Text = "Pause"; pause.Enabled = stop.Enabled = false; lastStatusUpdate = 0; UpdateDiscs(); }
    private void UpdateDiscs()
    {
        bool active = player != null; double position = player?.Position ?? 0, duration = player?.Duration ?? 0;
        sourceDisc.UpdatePlayback(active && sourcePlayback ? position : 0, source?.Audio.Source.Seconds ?? 0, active && sourcePlayback, active && sourcePlayback && player!.Paused);
        targetDisc.UpdatePlayback(active && !sourcePlayback ? position : 0, active && !sourcePlayback ? duration : target.Formats.Sum(f => f.Seconds), active && !sourcePlayback, active && !sourcePlayback && player!.Paused);
    }
    private static DecoFrame Deck(string title, Turntable disc, Label info, Control[] actions, Padding margin)
    {
        var frame = new DecoFrame(title) { Dock = DockStyle.Fill, Margin = margin };
        var content = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new(SizeType.Absolute, 196)); content.ColumnStyles.Add(new(SizeType.Percent, 100)); content.RowStyles.Add(new(SizeType.Percent, 100));
        var details = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, RowCount = actions.Length + 1, ColumnCount = 1 };
        details.RowStyles.Add(new(SizeType.Percent, 100)); details.Controls.Add(info, 0, 0);
        for (int i = 0; i < actions.Length; i++) { details.RowStyles.Add(new(SizeType.Absolute, 42)); details.Controls.Add(actions[i], 0, i + 1); }
        content.Controls.Add(disc, 0, 0); content.Controls.Add(details, 1, 0); frame.Controls.Add(content); return frame;
    }
    private static NumericUpDown Number(string name, decimal min = 0, decimal max = 3600) => new() { AccessibleName = name, DecimalPlaces = 3, Increment = .1m, Width = 80, Minimum = min, Maximum = max, Margin = new(4, 10, 12, 3) };
    private static Label Label(string text) => new() { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 10, 0, 0), Font = new("Segoe UI", 9) };
    private static Button Button(string text) => new DecoButton { Text = text, AutoSize = true, Font = new("Bahnschrift", 9), Padding = new(7, 4, 7, 4) };
    private static BufferedFlowLayoutPanel Row(params Control[] controls) { var row = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; row.Controls.AddRange(controls); return row; }
}

internal sealed class EditorScroll : Control
{
    private int value;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { int next = Math.Clamp(value, 0, 1000); if (next == this.value) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ValueChanged;
    public EditorScroll() { DoubleBuffered = true; TabStop = true; MinimumSize = new(100, 30); AccessibleRole = AccessibleRole.Slider; SetStyle(ControlStyles.Selectable | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    private void Pick(int x) => Value = (int)Math.Round(Math.Clamp((x - 12d) / Math.Max(1, Width - 24), 0, 1) * 1000);
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); Capture = true; Pick(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Pick(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Left) Value -= 25; if (e.KeyCode == Keys.Right) Value += 25; if (e.KeyCode == Keys.Home) Value = 0; if (e.KeyCode == Keys.End) Value = 1000; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(DecoTheme.Ink); using var track = new Pen(DecoTheme.Edge, 2); e.Graphics.DrawLine(track, 12, Height / 2, Math.Max(12, Width - 12), Height / 2);
        float x = 12 + Math.Max(0, Width - 24) * Value / 1000f; DecoTheme.Diamond(e.Graphics, x, Height / 2, 7, Enabled ? DecoTheme.Gold : DecoTheme.Edge);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, DecoTheme.Gold, DecoTheme.Ink);
    }
}

internal enum WaveMarker { Intro, Loop, End }

internal sealed class EditorWaveform : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float[] Peaks { get; set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Duration { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Zoom { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Offset { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Start { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Loop { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double End { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IntroMarkersEnabled { get; set; } = true;
    private WaveMarker? dragging, hovering;
    private double dragOriginal;
    private double position;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Position { get => position; set { if (position == value) return; position = value; Invalidate(); } }
    public event Action<double>? Clicked;
    public event Action<WaveMarker, double>? MarkerMoved;
    public EditorWaveform() { DoubleBuffered = true; SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AccessibleName = "Replacement source waveform"; }
    private float MarkerX(double seconds) => (float)((seconds - Offset) / (Duration / Zoom) * Math.Max(1, Width - 1));
    private double MarkerTime(WaveMarker marker) => marker == WaveMarker.Intro ? Start : marker == WaveMarker.Loop ? Loop : End;
    private WaveMarker? HitMarker(int x)
    {
        if (!Enabled || Duration <= 0 || Peaks.Length == 0) return null;
        WaveMarker? nearest = null; float distance = 9;
        foreach (var marker in IntroMarkersEnabled ? new[] { WaveMarker.Intro, WaveMarker.Loop, WaveMarker.End } : new[] { WaveMarker.End })
        { float px = MarkerX(MarkerTime(marker)); if (px < 0 || px > Width - 1) continue; float delta = Math.Abs(x - px); if (delta <= distance) { distance = delta; nearest = marker; } }
        return nearest;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (!Enabled || Duration <= 0 || e.Button != MouseButtons.Left) return; Focus();
        dragging = HitMarker(e.X);
        if (dragging is { } marker) { dragOriginal = MarkerTime(marker); Capture = true; Cursor = Cursors.SizeWE; Invalidate(); }
        else Clicked?.Invoke(Math.Clamp(Offset + Math.Clamp(e.X, 0, Math.Max(1, Width - 1)) / (double)Math.Max(1, Width - 1) * Duration / Zoom, 0, Duration));
    }
    private void MoveMarker(int x)
    {
        if (dragging is not { } marker) return;
        double seconds = Math.Clamp(Offset + Math.Clamp(x, 0, Math.Max(1, Width - 1)) / (double)Math.Max(1, Width - 1) * Duration / Zoom, 0, Duration);
        double minimum = marker == WaveMarker.Intro ? 0 : marker == WaveMarker.Loop ? Start + .01 : IntroMarkersEnabled ? Loop + .01 : .01;
        double maximum = marker == WaveMarker.Intro ? Loop - .01 : marker == WaveMarker.Loop ? End - .01 : Duration;
        minimum = Math.Max(0, minimum); maximum = Math.Min(Duration, maximum);
        if (maximum < minimum) return;
        MarkerMoved?.Invoke(marker, Math.Clamp(Math.Round(seconds, 3), minimum, maximum)); Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (dragging != null) { MoveMarker(e.X); return; }
        var next = HitMarker(e.X); Cursor = next != null ? Cursors.SizeWE : Cursors.Default;
        if (next != hovering) { hovering = next; Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    { base.OnMouseUp(e); if (e.Button != MouseButtons.Left || dragging == null) return; MoveMarker(e.X); dragging = null; Capture = false; hovering = HitMarker(e.X); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e)
    { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = null; Cursor = Cursors.Default; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e)
    { base.OnMouseLeave(e); if (dragging == null) { hovering = null; Cursor = Cursors.Default; Invalidate(); } }
    protected override bool IsInputKey(Keys keyData) => dragging != null && (keyData & Keys.KeyCode) == Keys.Escape || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    { if (e.KeyCode == Keys.Escape && dragging is { } marker) { dragging = null; Capture = false; MarkerMoved?.Invoke(marker, dragOriginal); e.Handled = e.SuppressKeyPress = true; Invalidate(); return; } base.OnKeyDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(DecoTheme.Field); if (Duration <= 0 || Peaks.Length == 0) { TextRenderer.DrawText(e.Graphics, "DROP A SONG HERE OR CHOOSE IMPORT AUDIO", Font, ClientRectangle, DecoTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        float X(double seconds) => (float)((seconds - Offset) / (Duration / Zoom) * (Width - 1));
        using var intro = new SolidBrush(Color.FromArgb(56, 41, 33)); using var loop = new SolidBrush(Color.FromArgb(68, 27, 42));
        float a = Math.Clamp(X(Start), 0, Width), b = Math.Clamp(X(Loop), 0, Width), c = Math.Clamp(X(End), 0, Width);
        if (b > a) e.Graphics.FillRectangle(intro, a, 0, b - a, Height); if (c > b) e.Graphics.FillRectangle(loop, b, 0, c - b, Height);
        using var pen = new Pen(DecoTheme.Gold);
        for (int x = 0; x < Width; x++)
        {
            int first = Math.Clamp((int)((Offset + x / (double)Width * Duration / Zoom) / Duration * Peaks.Length), 0, Peaks.Length - 1);
            int last = Math.Clamp((int)((Offset + (x + 1d) / Width * Duration / Zoom) / Duration * Peaks.Length), first + 1, Peaks.Length); float peak = 0;
            for (int i = first; i < last; i++) peak = Math.Max(peak, Peaks[i]); float height = peak * Math.Max(1, Height / 2 - 32); e.Graphics.DrawLine(pen, x, Height / 2 - height, x, Height / 2 + height);
        }
        var markers = IntroMarkersEnabled ? new[] { (WaveMarker.Intro, Start, "INTRO"), (WaveMarker.Loop, Loop, "LOOP"), (WaveMarker.End, End, "CROP END") } : new[] { (WaveMarker.Loop, 0d, "FULL SONG"), (WaveMarker.End, End, "CROP END") };
        foreach (var (kind, time, label) in markers)
        {
            float x = X(time); if (x < 0 || x >= Width) continue; bool movable = IntroMarkersEnabled || kind == WaveMarker.End;
            Color color = movable && (dragging == kind || hovering == kind) ? DecoTheme.Gold : DecoTheme.Ivory;
            using var marker = new Pen(color, dragging == kind ? 3 : 1); e.Graphics.DrawLine(marker, x, 0, x, Height);
            if (movable) { using var handle = new SolidBrush(color); e.Graphics.FillPolygon(handle, new[] { new PointF(x - 5, 20), new PointF(x + 5, 20), new PointF(x, 28) }); }
            string text = dragging == kind ? $"{label} {time:N3} s" : label;
            TextRenderer.DrawText(e.Graphics, text, Font, new Point(Math.Clamp((int)x + 3, 0, Math.Max(0, Width - TextRenderer.MeasureText(text, Font).Width - 4)), 4), color);
        }
        using var cursor = new Pen(Color.Coral, 2); float px = X(Position); if (px >= 0 && px < Width) e.Graphics.DrawLine(cursor, px, 22, px, Height - 23);
        TextRenderer.DrawText(e.Graphics, $"{Offset:N3} s — {Math.Min(Duration, Offset + Duration / Zoom):N3} s", Font, new Point(6, Height - 24), DecoTheme.Muted);
    }
}

