using SkullgirlsAudio;

namespace SkullgirlsAudioWorkshop;

public sealed class Workshop : Form
{
    private static readonly Color Ink = DecoTheme.Ink, Paper = DecoTheme.Ivory, Gold = DecoTheme.Gold, PanelColor = DecoTheme.Surface;
    private readonly TextBox folder = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Game installation" };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search titles, characters, or internal paths", AccessibleName = "Search library" };
    private readonly ComboBox sources = new() { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, DropDownWidth = 620, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Audio category and archive" };
    private readonly Label sourceDescription = new() { Dock = DockStyle.Fill, Font = new("Segoe UI", 9), AutoEllipsis = true };
    private readonly ListBox tracks = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, HorizontalScrollbar = true, AccessibleName = "Audio library" };
    private readonly Label heading = new() { Dock = DockStyle.Fill, Text = "Choose a record", Font = new Font("Georgia", 21, FontStyle.Bold), AutoEllipsis = true };
    private readonly TextBox description = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Text = "Open a Skullgirls installation to inspect its audio archives.", AccessibleName = "Clip description and metadata sources" };
    private readonly ComboBox segments = new() { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Audio segment" };
    private readonly Label properties = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "Ready", ForeColor = Gold };
    private readonly Label count = new() { Dock = DockStyle.Fill };
    private readonly Label installationNotice = new() { Text = "THE MERIDIAN RECORD ROOM     /     MUSIC · VOICES · SOUND EFFECTS", Dock = DockStyle.Fill, Font = new("Bahnschrift", 9), ForeColor = DecoTheme.Gold, AutoEllipsis = true };
    private readonly Button open = MakeButton("Open installation"), play = MakeButton("Play"), pause = MakeButton("Pause"), stop = MakeButton("Stop"), go = MakeButton("Seek"), fullRange = MakeButton("Full segment");
    private readonly Button editLabel = MakeButton("Label clip"), resetLabel = MakeButton("Reset label"), replaceMusic = MakeButton("Replace music…");
    private readonly Button openProject = MakeButton("Open project…"), recoverDraft = MakeButton("Recover draft…");
    private readonly Button installation = MakeButton("Apply / Restore…");
    private readonly Button modProjects = MakeButton("Mod projects…");
    private readonly ClipLabels clipLabels = new(Path.Combine(AppContext.BaseDirectory, "labels", "clip-labels.json"));
    private string? currentClipKey;
    private ClipDetails? currentDetails;
    private bool updatingLabels;
    private readonly DecoToggleButton repeat = new() { AutoSize = true, Font = new("Bahnschrift", 9), Padding = new(10, 4, 10, 4) };
    private readonly NumericUpDown seek = Number("Seek seconds"), rangeStart = Number("Repeat start seconds"), rangeEnd = Number("Repeat end seconds");
    private readonly Waveform waveform = new() { Dock = DockStyle.Fill };
    private readonly Turntable turntable = new() { Dock = DockStyle.Fill, Margin = new(14, 0, 0, 8) };
    private readonly System.Windows.Forms.Timer endTimer = new() { Interval = 30 };
    private GameLibrary? library;
    private Decoded? decoded;
    private AudioPlayback? player;
    private int generation;
    private bool closing;
    private readonly object decodeLock = new();
    private bool updatingRange;
    private bool loadingSources;
    private long lastStatusUpdate;
    private bool lastStatusPaused;
    public Workshop(string? initial)
    {
        Text = "Skullgirls Audio Workshop — Audio library"; ClientSize = new(1380, 960); MinimumSize = new(1240, 900); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ink; ForeColor = Paper; Font = new("Segoe UI", 10);
        DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DecoTheme.StyleTitleBar(this);
        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(26), ColumnCount = 1, RowCount = 5, BackColor = Ink };
        root.RowStyles.Add(new(SizeType.Absolute, 100)); root.RowStyles.Add(new(SizeType.Absolute, 50)); root.RowStyles.Add(new(SizeType.Absolute, 34)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 32)); Controls.Add(root);
        root.Controls.Add(new DecoHeader { Dock = DockStyle.Fill }, 0, 0);
        var location = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 }; location.ColumnStyles.Add(new(SizeType.Percent, 100)); foreach (int width in new[] { 185, 145, 145, 145, 175 }) location.ColumnStyles.Add(new(SizeType.Absolute, width)); location.Controls.Add(folder); location.Controls.Add(open); location.Controls.Add(openProject); location.Controls.Add(recoverDraft); location.Controls.Add(modProjects); location.Controls.Add(installation); root.Controls.Add(location, 0, 1);
        root.Controls.Add(installationNotice, 0, 2);
        var content = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; content.ColumnStyles.Add(new(SizeType.Absolute, 390)); content.ColumnStyles.Add(new(SizeType.Percent, 100)); root.Controls.Add(content, 0, 3);
        var leftFrame = new DecoFrame("THE RECORD LIBRARY") { Dock = DockStyle.Fill, Margin = new(0, 0, 20, 0) };
        var left = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Margin = new(0) };
        foreach (float h in new float[] { 40, 88, 42, 32 }) left.RowStyles.Add(new(SizeType.Absolute, h)); left.RowStyles.Add(new(SizeType.Percent, 100));
        left.Controls.Add(sources, 0, 0); left.Controls.Add(sourceDescription, 0, 1); left.Controls.Add(search, 0, 2); left.Controls.Add(count, 0, 3); left.Controls.Add(tracks, 0, 4); leftFrame.Controls.Add(left); content.Controls.Add(leftFrame, 0, 0);
        var rightFrame = new DecoFrame("NOW ON THE PLATTER") { Dock = DockStyle.Fill };
        var right = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, RowCount = 9, ColumnCount = 2, Padding = new(7), BackColor = PanelColor };
        right.ColumnStyles.Add(new(SizeType.Percent, 100)); right.ColumnStyles.Add(new(SizeType.Absolute, 240));
        foreach (float h in new float[] { 66, 100, 40, 92 }) right.RowStyles.Add(new(SizeType.Absolute, h)); right.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (float h in new float[] { 48, 46, 46, 42 }) right.RowStyles.Add(new(SizeType.Absolute, h));
        right.Controls.Add(heading, 0, 0); right.Controls.Add(description, 0, 1); right.Controls.Add(segments, 0, 2); right.Controls.Add(properties, 0, 3); right.Controls.Add(waveform, 0, 4);
        right.Controls.Add(turntable, 1, 0); right.SetRowSpan(turntable, 4); right.SetColumnSpan(waveform, 2);
        var actions = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; actions.Controls.AddRange([play, pause, stop, repeat, editLabel, resetLabel]); right.Controls.Add(actions, 0, 5); right.SetColumnSpan(actions, 2);
        var seeking = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; seeking.Controls.AddRange([Caption("Seek (s)"), seek, go, replaceMusic]); right.Controls.Add(seeking, 0, 6); right.SetColumnSpan(seeking, 2);
        var range = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; range.Controls.AddRange([Caption("Repeat (s)"), rangeStart, Caption("to"), rangeEnd, fullRange]); right.Controls.Add(range, 0, 7); right.SetColumnSpan(range, 2);
        var footnote = new Label { Text = "Click the waveform to seek. Space: play / pause. Escape: stop.\nRepeat boundaries affect preview only; no game or source audio is changed.", Dock = DockStyle.Fill, Font = new("Segoe UI", 9), ForeColor = DecoTheme.Muted };
        right.Controls.Add(footnote, 0, 8); right.SetColumnSpan(footnote, 2);
        rightFrame.Controls.Add(right); content.Controls.Add(rightFrame, 1, 0); root.Controls.Add(status, 0, 4);
        Style(this); SetReady(false); KeyPreview = true;
        open.Click += async (_, _) => { using var dialog = new FolderBrowserDialog { Description = "Select the folder containing SkullGirls.exe", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) await LoadGame(dialog.SelectedPath); };
        openProject.Click += async (_, _) => await OpenProject(false); recoverDraft.Click += async (_, _) => await OpenProject(true);
        installation.Click += async (_, _) => { if (library == null) return; Stop(); string game = library.Folder; using var dialog = new InstallationDialog(game); dialog.ShowDialog(this); if (dialog.GameFilesChanged) await LoadGame(game); };
        search.TextChanged += (_, _) => Filter(); tracks.SelectedIndexChanged += (_, _) => SelectTrack(); segments.SelectedIndexChanged += async (_, _) => { if (!updatingLabels) await LoadSegment(); };
        sources.SelectedIndexChanged += (_, _) => { if (!loadingSources) Filter(); };
        modProjects.Click += async (_, _) => { if (library == null) { status.Text = "Open a game installation first."; return; } Stop(); using var mod = new ModProjectForm(library); mod.ShowDialog(this); if (mod.InstallationChanged) await LoadGame(library.Folder); };
        play.Click += (_, _) => Play(); pause.Click += (_, _) => Pause(); stop.Click += (_, _) => Stop();
        editLabel.Click += (_, _) => EditLabel();
        replaceMusic.Click += async (_, _) =>
        {
            if (library == null || tracks.SelectedItem is not Track { Mapped: true } track) return;
            Stop(); Enabled = false; status.Text = "Loading the music editor target…";
            try { var target = await Task.Run(() => new MusicTarget(library, track)); Enabled = true; using var editor = new MusicEditorForm(target); editor.ShowDialog(this); if (editor.InstallationChanged) await LoadGame(target.GameFolder); else status.Text = "Music editor closed. Game files unchanged."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { Enabled = true; }
        };
        resetLabel.Click += (_, _) => { if (currentClipKey == null) return; try { clipLabels.Save(currentClipKey, null); ShowDetails(); } catch (Exception ex) { status.Text = ex.Message; } };
        go.Click += (_, _) => Seek((double)seek.Value); waveform.SeekRequested += Seek;
        repeat.CheckedChanged += (_, _) => RestartRange(); rangeStart.ValueChanged += (_, _) => RestartRange(); rangeEnd.ValueChanged += (_, _) => RestartRange();
        fullRange.Click += (_, _) => { if (decoded == null) return; updatingRange = true; rangeStart.Value = 0; rangeEnd.Value = rangeEnd.Maximum; updatingRange = false; RestartRange(); };
        endTimer.Tick += (_, _) => UpdatePlayback();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Stop(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Space && ActiveControl is not TextBox and not NumericUpDown and not ComboBox) { if (player == null) Play(); else Pause(); e.SuppressKeyPress = true; } };
        FormClosing += (_, _) => { closing = true; generation++; Stop(); endTimer.Dispose(); };
        Shown += async (_, _) =>
        {
            status.Text = "Looking for the current Steam installation…";
            string? target = initial ?? await Task.Run(GameLocation.Find);
            if (closing) return;
            if (!string.IsNullOrWhiteSpace(target)) await LoadGame(target);
            else status.Text = "Current Steam installation not found. Use Open installation to choose a folder.";
        };
    }
    private static Button MakeButton(string text) => new DecoButton { Text = text, AutoSize = true, Height = 34, Font = new("Bahnschrift", 9), Padding = new(11, 4, 11, 4), AccessibleName = text };
    private async Task OpenProject(bool recovery)
    {
        if (recovery && (!Directory.Exists(MusicEditorForm.DraftFolder) || !Directory.EnumerateFiles(MusicEditorForm.DraftFolder, "*.sgawproj").Any()))
        { status.Text = "No recovery drafts are available."; return; }
        using var dialog = new OpenFileDialog { Filter = "Workshop project|*.sgawproj", Title = recovery ? "Recover a draft — select by last modified time" : "Open audio project", InitialDirectory = recovery ? MusicEditorForm.DraftFolder : "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Stop(); Enabled = false; status.Text = "Reading project and verifying its target…";
        try
        {
            var result = await Task.Run(() =>
            {
                var loaded = ProjectFiles.Load(dialog.FileName); var game = library ?? new GameLibrary(loaded.Project.Target.GameFolder);
                var track = ProjectFiles.ResolveTrack(loaded.Project, game); return (loaded, target: new MusicTarget(game, track));
            });
            Enabled = true;
            if (!ProjectFiles.TargetMatches(result.loaded.Project, result.target, result.target.ArchiveFolder)
                && MessageBox.Show(this, "The active archive folder or target audio differs from this project's saved fingerprint. Open using the current target and review before generating?", "Changed game target — review required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            { status.Text = "Changed target was not accepted. Project file unchanged."; return; }
            using var editor = new MusicEditorForm(result.target, result.loaded); editor.ShowDialog(this); if (editor.InstallationChanged) await LoadGame(result.target.GameFolder); else status.Text = "Project editor closed. Game files unchanged.";
        }
        catch (Exception ex) { status.Text = "Could not open project: " + ex.Message; }
        finally { Enabled = true; }
    }
    private static NumericUpDown Number(string name) => new() { AccessibleName = name, DecimalPlaces = 3, Increment = 0.1m, Width = 105, Maximum = 3600 };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 7, 0, 0) };
    private void SetReady(bool ready) { play.Enabled = go.Enabled = seek.Enabled = repeat.Enabled = rangeStart.Enabled = rangeEnd.Enabled = fullRange.Enabled = editLabel.Enabled = ready; replaceMusic.Enabled = ready && tracks.SelectedItem is Track { Mapped: true, Bank: false, Assets.Length: 2 }; pause.Enabled = stop.Enabled = false; resetLabel.Enabled = ready && currentClipKey != null && clipLabels.Find(currentClipKey) != null; }
    private static void Style(Control parent)
    {
        DecoTheme.Apply(parent);
    }
    private void Stop()
    {
        endTimer.Stop(); var previous = player; player = null;
        try { previous?.Dispose(); } catch (Exception ex) { if (!closing) status.Text = "Could not release audio: " + ex.Message; }
        waveform.Position = 0; pause.Enabled = stop.Enabled = false; pause.Text = "Pause";
        lastStatusUpdate = 0;
        turntable.UpdatePlayback(0, decoded?.Source.Seconds ?? 0, false, false);
        if (!closing && decoded != null) status.Text = "Preview stopped. Browsing does not modify game files.";
    }
    private async Task LoadGame(string path)
    {
        Stop(); generation++; decoded = null; SetReady(false); open.Enabled = false; status.Text = "Indexing audio archives and music configuration…";
        turntable.SetClip("Opening the record library…");
        library = null; tracks.Items.Clear(); segments.Items.Clear(); sources.Items.Clear(); sourceDescription.Text = ""; waveform.Values = []; properties.Text = ""; heading.Text = "Opening installation";
        try
        {
            var loaded = await Task.Run(() => new GameLibrary(path)); if (closing) return;
            library = loaded; folder.Text = Path.Combine(loaded.Folder, loaded.ArchiveFolder);
            try
            {
                var transaction = new MusicInstallation(InstallationDialog.BackupStore).Current(loaded.Folder);
                installationNotice.Text = transaction == null ? "THE MERIDIAN RECORD ROOM     /     MUSIC · VOICES · SOUND EFFECTS"
                    : transaction.State == InstallState.Prepared ? "VERIFIED MUSIC PREPARATION READY — OPEN APPLY / RESTORE TO REVIEW"
                    : transaction.State == InstallState.Applied ? "WORKSHOP MUSIC REPLACEMENT APPLIED — OPEN APPLY / RESTORE TO RETURN TO THE PRE-APPLY STATE"
                    : "INSTALLATION RECOVERY REQUIRED — OPEN APPLY / RESTORE BEFORE ANOTHER INSTALL";
            }
            catch (Exception) { installationNotice.Text = "INSTALLATION JOURNAL NEEDS REVIEW — OPEN APPLY / RESTORE; INSTALLATION WRITES ARE BLOCKED"; }
            loadingSources = true; sources.Items.Add($"All audio · {loaded.Sources.Count} archives"); foreach (var source in loaded.Categories) sources.Items.Add(source); sources.SelectedIndex = 0; loadingSources = false;
            status.Text = $"{loaded.Sources.Count} audio archives indexed. No files modified."; Filter();
            if (Directory.Exists(MusicEditorForm.DraftFolder) && Directory.EnumerateFiles(MusicEditorForm.DraftFolder, "*.sgawproj").Any()) status.Text = "Recovery drafts are available. Use Recover draft to reopen unfinished work.";
        }
        catch (Exception ex) { if (!closing) { heading.Text = "Installation unavailable"; description.Text = "Choose a supported installation to try again."; status.Text = ex.Message; } }
        finally { if (!closing) open.Enabled = true; }
    }
    private void Filter()
    {
        if (library == null) return;
        var source = sources.SelectedItem as AudioArchive;
        sourceDescription.Text = source == null ? $"All discovered audio from {library.Sources.Count} archives: music, announcers, story dialogue, UI sounds and character sound banks. Banks expand when selected." + (library.Warnings.Count == 0 ? "" : $" {library.Warnings.Count} archive(s) could not be indexed.") : source.Description + "\n" + source.Name;
        var query = search.Text.Trim(); tracks.BeginUpdate(); tracks.Items.Clear();
        foreach (var t in (source?.Tracks ?? library.Tracks.ToArray()).Where(t => (t.Name + " " + t.Description + " " + t.Stages + " " + t.Category + " " + t.Archive + " " + string.Join(' ', t.Assets)).Contains(query, StringComparison.OrdinalIgnoreCase))) tracks.Items.Add(t);
        tracks.EndUpdate(); count.Text = $"{tracks.Items.Count:N0} results · {library.Sources.Count} audio archives";
        if (tracks.Items.Count > 0) tracks.SelectedIndex = 0; else { Stop(); generation++; decoded = null; SetReady(false); heading.Text = "No matching audio"; turntable.SetClip("No clip selected"); description.Text = "Try another title, character or internal path."; segments.Items.Clear(); waveform.Values = []; properties.Text = ""; }
    }
    private async void SelectTrack()
    {
        Stop(); int request = ++generation; decoded = null; SetReady(false); waveform.Values = []; properties.Text = ""; segments.Items.Clear();
        turntable.SetClip("Choose a clip");
        if (tracks.SelectedItem is not Track track || library == null) return;
        var selectedLibrary = library;
        heading.Text = track.Name; description.Text = track.Category + " · " + track.Archive + "\n" + (track.Mapped ? "Configuration-backed pair. Stage references: " + (track.Stages.Length == 0 ? "none listed" : track.Stages) : track.Description);
        try
        {
            if (track.Bank) status.Text = "Indexing embedded WAV clips…";
            var items = await Task.Run(() => { lock (decodeLock) { if (request != Volatile.Read(ref generation)) throw new OperationCanceledException(); return selectedLibrary.Segments(track); } });
            if (closing || request != generation) return;
            foreach (var segment in items) { var saved = segment.Identity == null ? null : clipLabels.Find(segment.Identity); segments.Items.Add(saved == null ? segment : segment with { Title = saved.Title }); }
            if (items.Length > 0) segments.SelectedIndex = 0;
            else { properties.Text = "No validated embedded WAV clips found."; status.Text = "This bank layout is not supported for preview."; }
        }
        catch (Exception ex) { if (!closing && request == generation) { properties.Text = "Audio unavailable"; status.Text = ex.Message; } }
    }
    private async Task LoadSegment()
    {
        Stop(); int request = ++generation; decoded = null; currentClipKey = null; currentDetails = null; SetReady(false); waveform.Values = []; properties.Text = "";
        if (segments.SelectedItem is not AudioSegment asset || tracks.SelectedItem is not Track track || library == null) return;
        turntable.SetClip("Loading clip…");
        var selectedLibrary = library; status.Text = "Decoding original audio…";
        try
        {
            var result = await Task.Run(() =>
            {
                lock (decodeLock)
                {
                    // Avoid retaining several large PCM buffers when selection changes rapidly.
                    if (request != Volatile.Read(ref generation)) throw new OperationCanceledException();
                    var bytes = selectedLibrary.Read(track, asset); var audio = ManagedDecoder.Decode(bytes);
                    return (audio, peaks: audio.Peaks(), key: ClipMetadata.Identity(track, asset, bytes), title: ClipMetadata.EmbeddedTitle(bytes));
                }
            });
            if (closing || request != generation) return;
            decoded = result.audio; waveform.Values = result.peaks; var w = decoded.Source; waveform.Duration = w.Seconds;
            turntable.UpdatePlayback(0, w.Seconds, false, false);
            currentClipKey = result.key; currentDetails = asset.Details ?? new(track.Name, track.Description, "Filename-derived label");
            if (result.title != null) currentDetails = currentDetails with { Description = "Embedded WAV title: " + result.title + "\n\n" + currentDetails.Description, Source = "Embedded RIFF title; " + currentDetails.Source };
            ShowDetails();
            updatingRange = true; seek.Maximum = rangeStart.Maximum = rangeEnd.Maximum = (decimal)w.Seconds; seek.Value = rangeStart.Value = 0; rangeEnd.Value = rangeEnd.Maximum; updatingRange = false;
            waveform.RangeStart = 0; waveform.RangeEnd = w.Seconds;
            properties.Text = $"{w.Codec}  ·  {w.Rate:N0} Hz  ·  {w.Channels} channels\n{w.Seconds:N3} seconds  ·  {w.Frames:N0} frames  ·  {w.Block}-byte blocks\n{asset.Asset}";
            SetReady(true); status.Text = clipLabels.LoadWarning ?? "Original decoded. Clip metadata attached in catalog; game audio unchanged.";
        }
        catch (Exception ex) { if (!closing && request == generation) { properties.Text = "Unsupported or unavailable audio"; status.Text = ex.Message; } }
    }
    private void ShowDetails()
    {
        if (currentDetails == null || currentClipKey == null || tracks.SelectedItem is not Track track) return;
        var saved = clipLabels.Find(currentClipKey);
        heading.Text = saved?.Title ?? currentDetails.Title;
        turntable.SetClip(heading.Text);
        description.Text = (track.Category + " · " + track.Archive + "\n"
            + (saved == null ? "" : "Personal label:\n" + saved.Description + "\n\nGame metadata:\n")
            + currentDetails.Source + "\n" + currentDetails.Description).Replace("\r\n", "\n").Replace("\n", "\r\n");
        resetLabel.Enabled = saved != null;
        if (segments.SelectedItem is AudioSegment selected && selected.Title != heading.Text)
        {
            updatingLabels = true;
            try { segments.Items[segments.SelectedIndex] = selected with { Title = heading.Text }; }
            finally { updatingLabels = false; }
        }
    }
    private void EditLabel()
    {
        if (currentClipKey == null || currentDetails == null) return;
        Stop(); var saved = clipLabels.Find(currentClipKey);
        using var dialog = new ClipLabelDialog(saved?.Title ?? currentDetails.Title, saved?.Description ?? "");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { clipLabels.Save(currentClipKey, new(dialog.ClipTitle, dialog.ClipDescription)); ShowDetails(); status.Text = "Clip label saved alongside the app. Audio bytes are unchanged."; }
        catch (Exception ex) { status.Text = "Could not save label: " + ex.Message; }
    }
    private void Play()
    {
        if (decoded == null) return;
        try
        {
            if (player?.Paused == true) player.Resume();
            else
            {
                double from = waveform.Position >= decoded.Source.Seconds ? 0 : waveform.Position;
                Stop(); player = new AudioPlayback(decoded);
                player.Start(from, repeat.Checked, (double)rangeStart.Value, (double)rangeEnd.Value);
            }
            pause.Text = "Pause"; pause.Enabled = stop.Enabled = true; endTimer.Start(); UpdatePlayback();
        }
        catch (Exception ex) { Stop(); status.Text = "Playback unavailable: " + ex.Message; }
    }
    private void Pause()
    {
        if (player == null) return;
        try { if (player.Paused) player.Resume(); else player.Pause(); pause.Text = player.Paused ? "Resume" : "Pause"; lastStatusUpdate = 0; UpdatePlayback(); }
        catch (Exception ex) { Stop(); status.Text = "Playback unavailable: " + ex.Message; }
    }
    private void Seek(double seconds)
    {
        if (decoded == null) return;
        try
        {
            seconds = Math.Clamp(seconds, 0, decoded.Source.Seconds);
            if (player != null) { player.Start(seconds, repeat.Checked, (double)rangeStart.Value, (double)rangeEnd.Value, player.Paused); lastStatusUpdate = 0; UpdatePlayback(); }
            else { waveform.Position = seconds; stop.Enabled = true; status.Text = $"Ready at {Time(seconds)}. Press Play."; }
        }
        catch (Exception ex) { Stop(); status.Text = "Seek unavailable: " + ex.Message; }
    }
    private void RestartRange()
    {
        if (updatingRange || decoded == null) return;
        waveform.RangeStart = (double)rangeStart.Value; waveform.RangeEnd = (double)rangeEnd.Value; waveform.Invalidate();
        bool valid = rangeEnd.Value > rangeStart.Value;
        play.Enabled = !repeat.Checked || valid;
        if (!valid && repeat.Checked) { Stop(); status.Text = "Repeat end must be after repeat start."; return; }
        if (player != null)
        {
            try { double position = player.Position; player.Start(position, repeat.Checked, (double)rangeStart.Value, (double)rangeEnd.Value, player.Paused); lastStatusUpdate = 0; UpdatePlayback(); }
            catch (Exception ex) { Stop(); status.Text = "Repeat unavailable: " + ex.Message; }
        }
    }
    private void UpdatePlayback()
    {
        if (player == null || decoded == null) return;
        try
        {
            if (player.Finished) { double duration = decoded.Source.Seconds; Stop(); waveform.Position = duration; status.Text = $"Finished  {Time(duration)}"; return; }
            waveform.Position = player.Position;
            turntable.UpdatePlayback(waveform.Position, decoded.Source.Seconds, true, player.Paused);
            long now = Environment.TickCount64;
            if (lastStatusUpdate == 0 || now - lastStatusUpdate >= 150 || lastStatusPaused != player.Paused)
            {
                status.Text = $"{(player.Paused ? "Paused" : "Playing")}  {Time(waveform.Position)} / {Time(decoded.Source.Seconds)}{(repeat.Checked ? "  ·  Loop on (selected range)" : "")}";
                lastStatusUpdate = now; lastStatusPaused = player.Paused;
            }
        }
        catch (Exception ex) { Stop(); status.Text = "Playback unavailable: " + ex.Message; }
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.fff");
}

internal sealed class Waveform : Control
{
    private float[] values = [];
    private double position;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Duration { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double RangeStart { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double RangeEnd { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Position { get => position; set { double next = Math.Clamp(value, 0, Math.Max(0, Duration)); if (next == position) return; position = next; Invalidate(); } }
    public event Action<double>? SeekRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float[] Values { get => values; set { values = value; Invalidate(); } }
    public Waveform() { DoubleBuffered = true; SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AccessibleName = "Original audio waveform; click to seek"; Cursor = Cursors.Hand; }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && values.Length > 0) SeekRequested?.Invoke(Math.Clamp((e.X - 12d) / Math.Max(1, Width - 24), 0, 1) * Duration); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(DecoTheme.Field);
        using var line = new Pen(DecoTheme.Edge); e.Graphics.DrawLine(line, 12, Height / 2, Width - 12, Height / 2);
        e.Graphics.DrawRectangle(line, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        if (values.Length == 0) { TextRenderer.DrawText(e.Graphics, "Waveform preview", Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        float X(double seconds) => 12 + (float)(Math.Clamp(seconds / Math.Max(.001, Duration), 0, 1) * Math.Max(1, Width - 24));
        using var selected = new SolidBrush(Color.FromArgb(70, DecoTheme.Wine));
        if (RangeEnd > RangeStart) e.Graphics.FillRectangle(selected, X(RangeStart), 6, X(RangeEnd) - X(RangeStart), Math.Max(1, Height - 12));
        using var pen = new Pen(DecoTheme.Gold);
        for (int x = 12; x < Width - 12; x++) { int a = (x - 12) * values.Length / Math.Max(1, Width - 24), b = Math.Max(a + 1, (x - 11) * values.Length / Math.Max(1, Width - 24)); float peak = 0; for (int i = a; i < Math.Min(b, values.Length); i++) peak = Math.Max(peak, values[i]); float h = peak * Math.Max(1, Height - 24) / 2; e.Graphics.DrawLine(pen, x, Height / 2f - h, x, Height / 2f + h); }
        using var cursor = new Pen(DecoTheme.Ivory, 2); e.Graphics.DrawLine(cursor, X(Position), 6, X(Position), Height - 6);
    }
}

