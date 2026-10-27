namespace SkullgirlsAudioWorkshop;

internal sealed class ClipLabelDialog : Form
{
    private readonly TextBox title = new() { Dock = DockStyle.Fill, MaxLength = 180, AccessibleName = "Clip title" };
    private readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000, AccessibleName = "Clip description" };
    public string ClipTitle => title.Text.Trim();
    public string ClipDescription => notes.Text.Trim();
    public ClipLabelDialog(string name, string description)
    {
        Text = "Label this audio clip"; ClientSize = new(620, 350); MinimumSize = new(500, 330); StartPosition = FormStartPosition.CenterParent;
        BackColor = DecoTheme.Surface; ForeColor = DecoTheme.Ivory; Font = new("Segoe UI", 10);
        DecoTheme.StyleTitleBar(this);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), RowCount = 6 };
        foreach (float height in new float[] { 28, 40, 28 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(new(SizeType.Absolute, 40));
        title.Text = name; notes.Text = description;
        layout.Controls.Add(new Label { Text = "Title", Dock = DockStyle.Fill }, 0, 0); layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(new Label { Text = "Description / identified line or use", Dock = DockStyle.Fill }, 0, 2); layout.Controls.Add(notes, 0, 3);
        layout.Controls.Add(new Label { Text = "Stored in the app's labels catalog, tied to this clip's content hash.\nPersonal labels remain separate from verified game metadata.", Dock = DockStyle.Fill, Font = new("Segoe UI", 9) }, 0, 4);
        var buttons = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new DecoButton { Text = "Save label", AutoSize = true, Padding = new(10, 4, 10, 4) }; var cancel = new DecoButton { Text = "Cancel", AutoSize = true, Padding = new(10, 4, 10, 4), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => { if (ClipTitle.Length == 0) { title.Focus(); return; } DialogResult = DialogResult.OK; };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 5); Controls.Add(layout); AcceptButton = save; CancelButton = cancel; DecoTheme.Apply(this);
    }
}
