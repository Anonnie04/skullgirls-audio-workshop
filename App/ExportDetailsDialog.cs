using SkullgirlsAudio;

namespace SkullgirlsAudioWorkshop;

internal sealed class ExportDetailsDialog : Form
{
    private readonly TextBox name = Field("Mod name", 180), author = Field("Mod author", 180), version = Field("Mod version", 40);
    private readonly TextBox description = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000, AccessibleName = "Mod description" };
    public ProjectExport Details => new(name.Text.Trim(), author.Text.Trim(), version.Text.Trim(), description.Text.Trim());
    public ExportDetailsDialog(ProjectExport details, MusicTarget target, GeneratedPair? pair = null)
    {
        Text = pair == null ? "Skullgirls Audio Workshop — Mod details" : "Skullgirls Audio Workshop — Review export";
        ClientSize = new(780, 690); MinimumSize = new(680, 630); StartPosition = FormStartPosition.CenterParent;
        BackColor = DecoTheme.Ink; ForeColor = DecoTheme.Ivory; Font = new("Segoe UI", 10); DoubleBuffered = true; DecoTheme.StyleTitleBar(this);
        name.Text = details.Name; author.Text = details.Author; version.Text = details.Version; description.Text = details.Description;
        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), RowCount = 10, ColumnCount = 1 };
        foreach (int height in new[] { 44, 138, 24, 38, 24, 38, 24 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 40)); root.RowStyles.Add(new(SizeType.Absolute, 48)); Controls.Add(root);
        root.Controls.Add(new Label { Text = pair == null ? "THE RECORD SLEEVE" : "REVIEW YOUR RECORD", Font = new("Georgia", 20, FontStyle.Bold), ForeColor = DecoTheme.Gold, Dock = DockStyle.Fill }, 0, 0);
        string summary = $"{target.Track.Name}\n{target.Track.Archive}\nIntro: {target.Track.Assets[0]}\nLoop: {target.Track.Assets[1]}";
        if (pair != null) summary += $"\nIntro {pair.Rendered.Intro.Seconds:N3} s / Loop {pair.Rendered.Loop.Seconds:N3} s · {pair.Rendered.Intro.ClippedSamples + pair.Rendered.Loop.ClippedSamples:N0} pre-encode clipped samples\nBlock timing adjustments: intro {pair.Rendered.Intro.TimingAdjustmentMs:+0.000;-0.000;0} ms / loop {pair.Rendered.Loop.TimingAdjustmentMs:+0.000;-0.000;0} ms";
        root.Controls.Add(new Label { Text = summary, Dock = DockStyle.Fill, Font = new("Segoe UI", 9), AutoEllipsis = true }, 0, 1);
        root.Controls.Add(Caption("Name"), 0, 2); root.Controls.Add(name, 0, 3);
        var identityLabels = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; identityLabels.ColumnStyles.Add(new(SizeType.Percent, 60)); identityLabels.ColumnStyles.Add(new(SizeType.Percent, 40)); identityLabels.RowStyles.Add(new(SizeType.Percent, 100)); identityLabels.Controls.Add(Caption("Author"), 0, 0); identityLabels.Controls.Add(Caption("Version (for example 1.0 or 1.2.3)"), 1, 0); root.Controls.Add(identityLabels, 0, 4);
        var identity = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; identity.ColumnStyles.Add(new(SizeType.Percent, 60)); identity.ColumnStyles.Add(new(SizeType.Percent, 40)); identity.RowStyles.Add(new(SizeType.Percent, 100)); identity.Controls.Add(author, 0, 0); identity.Controls.Add(version, 1, 0); root.Controls.Add(identity, 0, 5);
        root.Controls.Add(Caption("Description"), 0, 6); root.Controls.Add(description, 0, 7);
        var message = new Label { Text = pair == null ? "These details are saved with the project." : "Export contains converted audio and metadata. Source audio and local file paths are excluded.\nIn-game playback and loader requirements remain unverified. Game files stay unchanged.", Dock = DockStyle.Fill, Font = new("Segoe UI", 9), AutoEllipsis = true }; root.Controls.Add(message, 0, 8);
        var actions = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill };
        var accept = new DecoButton { Text = pair == null ? "Save details" : "Continue to export…", AutoSize = true, Padding = new(10, 4, 10, 4) };
        var cancel = new DecoButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new(10, 4, 10, 4) };
        accept.Click += (_, _) => { try { MusicExport.ValidateDetails(Details); DialogResult = DialogResult.OK; } catch (Exception ex) { message.Text = ex.Message; } };
        actions.Controls.AddRange([accept, cancel]); root.Controls.Add(actions, 0, 9); AcceptButton = accept; CancelButton = cancel; DecoTheme.Apply(this);
    }
    private static TextBox Field(string title, int max) => new() { Dock = DockStyle.Fill, MaxLength = max, AccessibleName = title };
    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill };
}
