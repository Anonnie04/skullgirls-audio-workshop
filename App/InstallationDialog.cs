using SkullgirlsAudio;

namespace SkullgirlsAudioWorkshop;

internal sealed class InstallationDialog : Form
{
    private readonly string game;
    internal static string BackupStore => Path.Combine(AppContext.BaseDirectory, "installation-backups");
    private readonly MusicInstallation engine = new(BackupStore);
    private readonly TextBox review = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Installation review and backup state" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button prepare = Button("Prepare Apply…"), applyPrepared = Button("Apply prepared…"), restore = Button("Restore pre-apply music"), recover = Button("Recover interrupted install"), cancel = Button("Cancel preparation"), launch = Button("Open current game");
    private InstallationRecord? current;
    private CancellationTokenSource? operation;
    private bool stateReadable;
    public bool GameFilesChanged { get; private set; }
    public InstallationDialog(string game, MusicTarget? target = null, GeneratedPair? pair = null, AudioSource? source = null, EditSettings? edits = null, Func<MusicInstallation, CancellationToken, InstallationRecord>? prepareBatch = null)
    {
        this.game = game; Text = "Skullgirls Audio Workshop — Apply / Restore"; ClientSize = new(930, 640); MinimumSize = new(850, 580); StartPosition = FormStartPosition.CenterParent;
        BackColor = DecoTheme.Ink; ForeColor = DecoTheme.Ivory; Font = new("Segoe UI", 10); DoubleBuffered = true; DecoTheme.StyleTitleBar(this);
        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Absolute, 64)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Absolute, 44)); Controls.Add(root);
        root.Controls.Add(new Label { Text = "THE PRESSING ROOM  /  APPLY & RESTORE", Font = new("Georgia", 19, FontStyle.Bold), ForeColor = DecoTheme.Gold, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "Close the game before installation, restore or recovery. Only music-win.gfs is supported.\nBackups capture your current pre-apply state and may already contain mods.\nConversion is verified; in-game playback and archive-location rules still need runtime validation.", Dock = DockStyle.Fill, Font = new("Segoe UI", 9) }, 0, 1);
        root.Controls.Add(review, 0, 2); root.Controls.Add(Row(prepare, applyPrepared, restore, recover), 0, 3); root.Controls.Add(Row(cancel, launch), 0, 4); root.Controls.Add(status, 0, 5);
        void Ready()
        {
            bool idle = operation == null; prepare.Enabled = idle && stateReadable && current == null && (prepareBatch != null || target != null && pair != null && source != null && edits != null);
            restore.Enabled = idle && stateReadable && current?.State == InstallState.Applied; recover.Enabled = idle && stateReadable && current != null && current.State != InstallState.Applied; cancel.Enabled = !idle; launch.Enabled = idle && stateReadable && (current == null || current.State == InstallState.Applied);
            applyPrepared.Enabled = idle && stateReadable && current?.State == InstallState.Prepared;
        }
        async Task Refresh()
        {
            try
            {
                current = await Task.Run(() => engine.Current(game)); stateReadable = true;
                review.Text = current == null ? $"Installation: {game}\r\nActive folder: {GameLibrary.ActiveArchiveFolder(game)} (loader detection rule)\r\n\r\nNo active Workshop transaction.\r\nGenerate and verify a music replacement, then Prepare Apply in the music editor.\r\nHistorical backups are retained separately from generated audio."
                    : $"State: {current.State}\r\nTrack: {current.Track}\r\nArchive: {MusicInstallation.ArchivePath(current)}\r\nAssets:\r\n  {string.Join("\r\n  ", current.Assets)}\r\n\r\nPre-apply SHA-256:\r\n{current.OriginalHash}\r\nInstalled SHA-256:\r\n{current.InstalledHash ?? "not staged yet"}\r\n\r\nBackup:\r\n{engine.BackupPath(current)}\r\n\r\nA verified preparation can be reviewed and applied, or recovered without installing. Interrupted commits must be recovered. Restore returns the verified pre-apply archive and refuses unrelated external changes.";
                if (current?.Edits is { } settings) review.Text += $"\r\n\r\nPrepared edits: intro {settings.Start:N3} → {settings.LoopStart:N3} s; loop {settings.LoopStart:N3} → {settings.End:N3} s; gain {settings.GainDb:N1} dB; fades {settings.FadeIn:N3}/{settings.FadeOut:N3} s; crossfade {settings.Crossfade:N3} s.";
                if (current?.Edits?.SkipIntro == true) review.Text += $"\r\nFull-song mode overrides intro cuts: source 0 → {current.Edits.End:N3} s repeats after a minimal silent lead-in. Verify initial playback in-game.";
                if (current?.Songs is { } songs) foreach (var song in songs) review.Text += $"\r\n\r\n{song.Name}: crop end {song.Edits.End:N3} s; gain {song.Edits.GainDb:N1} dB; skip intro {song.Edits.SkipIntro}.\r\nSource SHA-256: {song.SourceHash}";
            }
            catch (Exception ex) { stateReadable = false; review.Text = ex.Message; }
            Ready();
        }
        async Task Run(string message, Func<CancellationToken, InstallationRecord> action, bool canCancel)
        {
            if (operation != null) return; operation = new(); Ready(); cancel.Enabled = canCancel; status.Text = message;
            try { if (!canCancel) GameFilesChanged = true; current = await Task.Run(() => action(operation.Token)); status.Text = "Completed: " + current.State; }
            catch (OperationCanceledException) { status.Text = "Preparation cancelled. Active music archive unchanged."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operation.Dispose(); operation = null; await Refresh(); }
        }
        async Task ReviewApply()
        {
            if (current?.State != InstallState.Prepared) return;
            string processing = current.Songs == null ? $"Prepared gain: {current.Edits?.GainDb:N1} dB; skip intro: {current.Edits?.SkipIntro}" : $"{current.Songs.Length} songs, each with its own cuts and processing. One archive and one Restore operation.";
            var decision = MessageBox.Show(this, $"Apply {current.Track}?\n{processing}\n\n{MusicInstallation.ArchivePath(current)}\n\nOnly these assets change:\n{string.Join("\n", current.Assets)}\n\nVerified pre-apply backup:\n{engine.BackupPath(current)}\n\nIn-game playback is not yet verified. No recovers the prepared transaction without installing.", "Review prepared music replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            var id = current.Id;
            if (decision == DialogResult.Yes) await Run("Applying staged archive and verifying the installed result…", _ => engine.Apply(game, id), false);
            else await Run("Discarding preparation; retaining its backup…", _ => engine.Recover(game, id), false);
        }
        prepare.Click += async (_, _) => { if (prepareBatch == null && (target == null || pair == null || source == null || edits == null)) return; await Run("Creating and verifying the pre-apply backup and complete staged archive…", t => prepareBatch != null ? prepareBatch(engine, t) : engine.Prepare(target!, pair!, source!, edits!, t), true); await ReviewApply(); };
        applyPrepared.Click += async (_, _) => await ReviewApply();
        restore.Click += async (_, _) => { if (current == null) return; var id = current.Id; await Run("Restoring the verified pre-apply archive…", _ => engine.Restore(game, id), false); };
        recover.Click += async (_, _) => { if (current == null) return; var id = current.Id; await Run("Recovering the interrupted transaction to its pre-apply state…", _ => engine.Recover(game, id), false); };
        cancel.Click += (_, _) => operation?.Cancel();
        launch.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(game, "SkullGirls.exe")) { WorkingDirectory = game, UseShellExecute = true }); status.Text = "Game launched. Check stage entry, intro transition, two loops, exit/re-entry and an unaffected stage; then close the game and Restore."; } catch (Exception ex) { status.Text = ex.Message; } };
        FormClosing += (_, e) => { if (operation != null) { e.Cancel = true; operation.Cancel(); status.Text = "Operation is finishing; close again afterwards."; } };
        Shown += async (_, _) => await Refresh(); DecoTheme.Apply(this); Ready();
    }
    private static Button Button(string text) => new DecoButton { Text = text, AutoSize = true, Font = new("Bahnschrift", 9), Padding = new(8, 4, 8, 4) };
    private static BufferedFlowLayoutPanel Row(params Control[] controls) { var row = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; row.Controls.AddRange(controls); return row; }
}
