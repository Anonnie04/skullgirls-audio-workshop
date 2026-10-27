using SkullgirlsAudio;

namespace SkullgirlsAudioWorkshop;

internal sealed class ModProjectForm : Form
{
    private GameLibrary library;
    private readonly ListBox songs = new() { Dock = DockStyle.Fill, AccessibleName = "Songs in mod project", HorizontalScrollbar = true };
    private readonly ComboBox targets = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Music target to add" };
    private readonly TextBox name = new() { Text = "My music mod", Width = 270, MaxLength = 180, AccessibleName = "Mod name" }, author = new() { Width = 180, MaxLength = 180, AccessibleName = "Mod author" }, version = new() { Text = "1.0", Width = 80, MaxLength = 40, AccessibleName = "Mod version" }, description = new() { Dock = DockStyle.Fill, Multiline = true, MaxLength = 4000, PlaceholderText = "Mod description (optional)", AccessibleName = "Mod description" };
    private readonly CheckBox copySources = new() { Text = "Copy original sources with project", Checked = true, AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true }, details = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button add = Button("Add song…"), edit = Button("Edit song…"), import = Button("Import song project…"), remove = Button("Remove song"), open = Button("Open mod…"), recover = Button("Recover mod draft…"), save = Button("Save mod"), saveAs = Button("Save as…"), generate = Button("Generate all"), audition = Button("Play converted"), stop = Button("Stop"), folder = Button("Export mod folder…"), zip = Button("Export mod ZIP…"), install = Button("Apply / Restore…"), cancel = Button("Cancel operation");
    private readonly System.Windows.Forms.Timer draftTimer = new() { Interval = 1000 };
    private CancellationTokenSource? operation;
    private AudioPlayback? player;
    private PreparedSong[]? prepared;
    private Guid id = Guid.NewGuid();
    private LoadedMod? saved;
    private bool dirty, updating, closeApproved;
    private string draft = "", draftHash = "";
    internal static string DraftFolder => Path.Combine(AppContext.BaseDirectory, "mod-drafts");
    public bool InstallationChanged { get; private set; }
    private record SongItem(AudioProject Project)
    {
        public override string ToString() => $"{Project.Target.Name}   ·   {Project.Source.Name}   ·   {(Project.Edits.SkipIntro ? "full song from 0" : $"loop from {Project.Edits.LoopStart:N3}")} → {Project.Edits.End:N3} s";
    }
    public ModProjectForm(GameLibrary library)
    {
        this.library = library; ResetDraft(); Text = "Skullgirls Audio Workshop — Multi-song mod project";
        ClientSize = new(1180, 800); MinimumSize = new(1120, 720); StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10); BackColor = DecoTheme.Ink; ForeColor = DecoTheme.Ivory; DoubleBuffered = true; DecoTheme.StyleTitleBar(this);
        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 10 };
        root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Absolute, 44)); root.RowStyles.Add(new(SizeType.Absolute, 56)); root.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 56, 48, 48, 48, 32, 38 }) root.RowStyles.Add(new(SizeType.Absolute, height)); Controls.Add(root);
        root.Controls.Add(new Label { Text = "THE ALBUM ROOM  /  MULTI-SONG MOD PROJECT", Font = new("Georgia", 19, FontStyle.Bold), ForeColor = DecoTheme.Gold, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(Row(Caption("Name"), name, Caption("Author"), author, Caption("Version"), version), 0, 1);
        root.Controls.Add(description, 0, 2); root.Controls.Add(songs, 0, 3); root.Controls.Add(details, 0, 4);
        root.Controls.Add(Row(targets, add, edit, import, remove), 0, 5);
        root.Controls.Add(Row(open, recover, save, saveAs, generate, audition, stop), 0, 6);
        root.Controls.Add(Row(folder, zip, install, cancel, copySources), 0, 7);
        root.Controls.Add(new Label { Text = "One project, independent song settings, one verified music archive and one Restore operation. Generation is required after edits or reopening.", Dock = DockStyle.Fill }, 0, 8); root.Controls.Add(status, 0, 9);
        targets.Items.AddRange(library.Tracks.Where(t => t.Mapped && !t.Bank && t.Assets.Length == 2).OrderBy(t => t.Name).Cast<object>().ToArray()); if (targets.Items.Count > 0) targets.SelectedIndex = 0;
        foreach (var box in new[] { name, author, version, description }) box.TextChanged += (_, _) => Changed(false); copySources.CheckedChanged += (_, _) => Changed(false);
        songs.SelectedIndexChanged += (_, _) => { Stop(); UpdateSelection(); Ready(); };
        add.Click += (_, _) => EditSong(false); edit.Click += (_, _) => EditSong(true);
        remove.Click += (_, _) => { if (songs.SelectedIndex < 0) return; songs.Items.RemoveAt(songs.SelectedIndex); Changed(); UpdateSelection(); };
        import.Click += (_, _) => ImportSong(); open.Click += async (_, _) => await Open(false); recover.Click += async (_, _) => await Open(true);
        save.Click += async (_, _) => await Save(false); saveAs.Click += async (_, _) => await Save(true);
        generate.Click += async (_, _) =>
        {
            var snapshot = Snapshot(); prepared = null;
            IProgress<string> progress = new Progress<string>(message => { if (operation != null) status.Text = message; });
            await Work("Generating and verifying every song…", t => { var fresh = new GameLibrary(this.library.Folder); return (library: fresh, outputs: ModProjects.Generate(snapshot, fresh, Path.Combine(AppContext.BaseDirectory, "generated"), t, progress.Report)); }, result => { this.library = result.library; prepared = result.outputs; status.Text = $"Verified {result.outputs.Length} songs. Audition individual converted songs, then export or Apply."; });
        };
        audition.Click += (_, _) => Audition(); stop.Click += (_, _) => { Stop(); Ready(); status.Text = "Playback stopped."; }; folder.Click += async (_, _) => await Export(false); zip.Click += async (_, _) => await Export(true);
        install.Click += (_, _) =>
        {
            Stop(); var snapshot = Snapshot(); var ready = prepared;
            using var dialog = new InstallationDialog(this.library.Folder, prepareBatch: ready == null ? null : (engine, token) => engine.PrepareCollection(snapshot, ready, this.library, token)); dialog.ShowDialog(this);
            if (dialog.GameFilesChanged) { InstallationChanged = true; prepared = null; try { this.library = new GameLibrary(this.library.Folder); } catch (Exception ex) { status.Text = ex.Message; } status.Text = "Installation changed. Reopening/editing a song checks its target fingerprints; Generate all again before another export or Apply."; } Ready();
        };
        cancel.Click += (_, _) => operation?.Cancel(); draftTimer.Tick += (_, _) => { draftTimer.Stop(); SaveDraft(); };
        FormClosing += async (_, e) =>
        {
            if (operation != null) { e.Cancel = true; operation.Cancel(); status.Text = "Cancelling; close again when finished."; return; }
            if (!closeApproved && dirty)
            {
                var answer = MessageBox.Show(this, "Save this mod project before closing? No keeps a recovery draft.", "Unsaved mod project", MessageBoxButtons.YesNoCancel);
                if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
                if (answer == DialogResult.Yes) { e.Cancel = true; if (await Save(false)) { closeApproved = true; Close(); } return; }
                if (!SaveDraft()) { e.Cancel = true; return; }
            }
            Stop(); draftTimer.Dispose();
        };
        DecoTheme.Apply(this); Ready(); status.Text = "Add songs or open an existing .sgawmod collection.";
    }
    private ModProject Snapshot() => new(1, id, DateTimeOffset.UtcNow, new(name.Text, author.Text, version.Text, description.Text), songs.Items.Cast<SongItem>().Select(s => s.Project).ToArray(), copySources.Checked);
    private void Changed(bool invalidate = true)
    {
        if (updating) return; Stop(); if (invalidate) prepared = null; dirty = true; draftTimer.Stop(); draftTimer.Start(); status.Text = prepared == null ? "Mod changed. Generate all before exporting or applying." : "Mod details changed. Verified audio is retained."; Ready();
    }
    private void UpdateSelection()
    {
        details.Text = songs.SelectedItem is SongItem s ? $"{s.Project.Target.Archive}  /  {string.Join(", ", s.Project.Target.Assets)}\nSource: {s.Project.Source.Path}" : $"{songs.Items.Count} songs in this mod project.";
    }
    private void EditSong(bool existing)
    {
        try
        {
            library = new GameLibrary(library.Folder);
            AudioProject? song = existing ? (songs.SelectedItem as SongItem)?.Project : null;
            Track? track = song == null ? targets.SelectedItem as Track : ProjectFiles.ResolveTrack(song, library); if (track == null || existing && song == null) return;
            int index = song == null ? -1 : songs.SelectedIndex;
            if (song == null)
            {
                index = songs.Items.Cast<SongItem>().ToList().FindIndex(s => s.Project.Target.Assets.Intersect(track.Assets, StringComparer.OrdinalIgnoreCase).Any());
                if (index >= 0) { songs.SelectedIndex = index; song = ((SongItem)songs.Items[index]).Project; if (!song.Target.Assets.SequenceEqual(track.Assets, StringComparer.OrdinalIgnoreCase)) throw new IOException("This target shares an asset with an existing song. Choose which replacement to keep; both cannot be included."); }
            }
            var target = new MusicTarget(library, track);
            if (song != null && !ProjectFiles.TargetMatches(song, target, target.ArchiveFolder) && MessageBox.Show(this, "The target differs from this song's saved fingerprint. Review it using the current game audio?", "Changed target", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            LoadedProject? opening = song == null ? null : new(Path.Combine(AppContext.BaseDirectory, "mod-song.sgawproj"), "", song);
            using var editor = new MusicEditorForm(target, opening, collectionMode: true);
            if (editor.ShowDialog(this) != DialogResult.OK || editor.ModSong == null) return;
            if (index < 0) songs.Items.Add(new SongItem(editor.ModSong)); else songs.Items[index] = new SongItem(editor.ModSong);
            songs.SelectedIndex = index < 0 ? songs.Items.Count - 1 : index; Changed(); UpdateSelection();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void ImportSong()
    {
        using var dialog = new OpenFileDialog { Filter = "Single-song Workshop project|*.sgawproj" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var loaded = ProjectFiles.Load(dialog.FileName); var song = loaded.Project with { Source = loaded.Project.Source with { Path = ProjectFiles.SourcePath(loaded) } };
            var check = Snapshot() with { Songs = Snapshot().Songs.Append(song).ToArray() }; ModProjects.Validate(check);
            if (!Path.GetFullPath(song.Target.GameFolder).TrimEnd('\\').Equals(library.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a song project from this game installation.");
            songs.Items.Add(new SongItem(song)); songs.SelectedIndex = songs.Items.Count - 1; Changed(); UpdateSelection();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private async Task<bool> Save(bool choosePath)
    {
        if (operation != null) return false; string? path = saved?.Path;
        if (choosePath || path == null)
        {
            using var dialog = new SaveFileDialog { Filter = "Multi-song mod project|*.sgawmod", DefaultExt = "sgawmod", AddExtension = true, FileName = path == null ? "Music-mod.sgawmod" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false; path = dialog.FileName;
        }
        bool success = false; var snapshot = Snapshot(); var outputs = prepared; string? expected = saved != null && saved.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ? saved.Hash : null;
        await Work("Saving collection and verifying original source copies…", token => { ProjectFiles.OutsideGame(path, library.Folder); return ModProjects.Save(path, snapshot, expected, token); }, result =>
        {
            saved = result; var model = ModProjects.ResolveSources(result); LoadModel(model); dirty = false; RemoveDraft(); prepared = outputs?.Select((s, i) => s with { Project = model.Songs[i] }).ToArray(); status.Text = prepared == null ? "Mod project saved. Generate all before export or Apply." : "Mod project saved with original source copies; verified output retained."; success = true;
        }); return success;
    }
    private async Task Open(bool recovery)
    {
        if (dirty)
        {
            var answer = MessageBox.Show(this, "Save the current mod project first? No leaves a recovery draft.", "Open mod project", MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel || answer == DialogResult.Yes && !await Save(false)) return; if (answer == DialogResult.No && !SaveDraft()) return;
        }
        using var dialog = new OpenFileDialog { Filter = "Multi-song mod project|*.sgawmod", InitialDirectory = recovery && Directory.Exists(DraftFolder) ? DraftFolder : "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Work("Opening mod project…", _ => ModProjects.Load(dialog.FileName), result =>
        {
            var model = ModProjects.ResolveSources(result);
            if (model.Songs.Any(s => !Path.GetFullPath(s.Target.GameFolder).TrimEnd('\\').Equals(library.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) throw new IOException("Open this mod from its saved game installation.");
            bool actualRecovery = Path.GetDirectoryName(result.Path)!.Equals(Path.GetFullPath(DraftFolder), StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(result.Path), "N", out _);
            ResetDraft(); saved = actualRecovery ? null : result; LoadModel(model); dirty = actualRecovery; prepared = null;
            if (actualRecovery) { draft = result.Path; draftHash = result.Hash; }
            status.Text = "Mod opened. Edit a song to relink/review missing or changed sources. Generate all before export or Apply.";
        });
    }
    private void LoadModel(ModProject model)
    {
        updating = true; try { id = model.Id; name.Text = model.Details.Name; author.Text = model.Details.Author; version.Text = model.Details.Version; description.Text = model.Details.Description; copySources.Checked = model.CopySources; songs.Items.Clear(); foreach (var song in model.Songs) songs.Items.Add(new SongItem(song)); if (songs.Items.Count > 0) songs.SelectedIndex = 0; } finally { updating = false; } UpdateSelection();
    }
    private async Task Export(bool package)
    {
        if (prepared == null) return; string destination;
        if (package) { using var dialog = new SaveFileDialog { Filter = "Mod ZIP|*.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = false, FileName = MusicExport.SafeName(name.Text) + ".zip" }; if (dialog.ShowDialog(this) != DialogResult.OK) return; destination = dialog.FileName; }
        else { using var dialog = new FolderBrowserDialog { Description = "Select a parent folder; a new mod folder will be created." }; if (dialog.ShowDialog(this) != DialogResult.OK) return; destination = Path.Combine(dialog.SelectedPath, MusicExport.SafeName(name.Text) + "-" + Guid.NewGuid().ToString("N")); }
        var snapshot = Snapshot(); var ready = prepared;
        await Work("Exporting and verifying the complete mod…", t => ModProjects.Export(destination, package, snapshot, ready, library, t), result => status.Text = "Verified export: " + result);
    }
    private void Audition()
    {
        Stop(); if (prepared == null || songs.SelectedIndex < 0) return;
        try { var output = prepared[songs.SelectedIndex].Output; for (int i = 0; i < 2; i++) if (ProjectFiles.Hash(output.Files[i]) != output.Hashes[i]) throw new IOException("Generated audio changed. Generate again."); var first = ManagedDecoder.Decode(File.ReadAllBytes(output.Files[0])); var loop = ManagedDecoder.Decode(File.ReadAllBytes(output.Files[1])); var audio = MusicEditing.Join(first.Pcm, first.Source.Rate, loop.Pcm, loop.Source.Rate, 2); player = new(audio.Pcm, audio.Rate, audio.Channels); player.Start(0, true, audio.LoopStart, audio.Duration, includeIntro: true); status.Text = "Playing the selected song's actual converted intro and loop."; Ready(); } catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Stop() { var old = player; player = null; old?.Dispose(); }
    private async Task Work<T>(string message, Func<CancellationToken, T> action, Action<T> finish)
    {
        if (operation != null) return; Stop(); operation = new(); Ready(); status.Text = message;
        try { var result = await Task.Run(() => action(operation.Token)); finish(result); } catch (OperationCanceledException) { status.Text = "Cancelled. No active game files changed."; } catch (Exception ex) { status.Text = ex.Message; } finally { operation.Dispose(); operation = null; Ready(); }
    }
    private void Ready()
    {
        bool idle = operation == null; foreach (var control in new Control[] { targets, add, edit, import, remove, open, recover, save, saveAs, generate, audition, folder, zip, install, name, author, version, description, songs, copySources }) control.Enabled = idle;
        edit.Enabled = remove.Enabled = idle && songs.SelectedIndex >= 0; generate.Enabled = idle && songs.Items.Count > 0;
        folder.Enabled = zip.Enabled = idle && prepared != null; audition.Enabled = idle && prepared != null && songs.SelectedIndex >= 0; stop.Enabled = player != null; cancel.Enabled = !idle;
    }
    private void ResetDraft() { draft = Path.Combine(DraftFolder, Guid.NewGuid().ToString("N") + ModProjects.Extension); draftHash = ""; }
    private bool SaveDraft()
    {
        if (!dirty || updating) return true;
        try { ModProjects.WriteDraft(draft, Snapshot(), draftHash.Length == 0 ? null : draftHash); draftHash = ProjectFiles.Hash(draft); return true; } catch (Exception ex) { status.Text = "Recovery draft could not be saved: " + ex.Message; return false; }
    }
    private void RemoveDraft()
    {
        draftTimer.Stop(); try { if (File.Exists(draft) && draftHash.Length > 0 && ProjectFiles.Hash(draft) == draftHash && Path.GetDirectoryName(Path.GetFullPath(draft)) == Path.GetFullPath(DraftFolder) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(draft), "N", out _)) File.Delete(draft); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Retain a busy or externally changed draft. */ } ResetDraft();
    }
    private static Button Button(string text) => new DecoButton { Text = text, AutoSize = true, Padding = new(8, 4, 8, 4), Font = new("Bahnschrift", 9) };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 8, 0, 0) };
    private static BufferedFlowLayoutPanel Row(params Control[] controls) { var row = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; row.Controls.AddRange(controls); return row; }
}
