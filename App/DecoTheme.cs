using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SkullgirlsAudioWorkshop;

internal static class DecoTheme
{
    public static readonly Color Ink = Color.FromArgb(20, 15, 19), Surface = Color.FromArgb(35, 24, 30), Field = Color.FromArgb(25, 19, 23);
    public static readonly Color Wine = Color.FromArgb(99, 31, 49), Gold = Color.FromArgb(207, 167, 98), Ivory = Color.FromArgb(244, 231, 202), Muted = Color.FromArgb(182, 159, 137), Edge = Color.FromArgb(112, 78, 53);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void StyleTitleBar(Form form)
    {
        form.HandleCreated += (_, _) =>
        {
            int dark = 1, caption = ColorTranslator.ToWin32(Surface), text = ColorTranslator.ToWin32(Ivory);
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        };
    }
    public static void Apply(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = Ivory;
            if (control is TextBox text) { text.BackColor = Field; text.BorderStyle = BorderStyle.None; text.Margin = new(6, 6, 6, 6); }
            else if (control is ComboBox combo) { combo.BackColor = Field; combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 24; combo.DrawItem += DrawCombo; }
            else if (control is NumericUpDown number) { number.BackColor = Field; number.BorderStyle = BorderStyle.None; }
            else if (control is ListBox list) { list.BackColor = Field; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 49; list.HorizontalScrollbar = false; list.DrawItem += DrawLibrary; }
            else if (control is Label label) { label.BackColor = parent.BackColor; if (label.Font.Size <= 10) label.ForeColor = Muted; }
            else if (control is TableLayoutPanel or FlowLayoutPanel) control.BackColor = parent.BackColor;
            Apply(control);
        }
        parent.Paint += (_, e) =>
        {
            using var pen = new Pen(Edge);
            foreach (Control child in parent.Controls)
                if (child.Visible && child is TextBox or ComboBox or NumericUpDown)
                { var bounds = child.Bounds; bounds.Inflate(2, 2); e.Graphics.DrawRectangle(pen, bounds); }
        };
    }
    private static void DrawCombo(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? Wine : Field); e.Graphics.FillRectangle(fill, e.Bounds);
        string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.Items[e.Index]?.ToString() ?? "" : combo.Text;
        var rect = e.Bounds; rect.Inflate(-8, 0);
        TextRenderer.DrawText(e.Graphics, text, combo.Font, rect, Ivory, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    private static void DrawLibrary(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list || e.Index < 0 || e.Index >= list.Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? Color.FromArgb(76, 29, 43) : e.Index % 2 == 0 ? Field : Surface); e.Graphics.FillRectangle(fill, e.Bounds);
        using var line = new Pen(selected ? Gold : Color.FromArgb(56, 40, 43));
        e.Graphics.DrawLine(line, e.Bounds.Left + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
        using var brass = new SolidBrush(Gold);
        if (selected) e.Graphics.FillRectangle(brass, e.Bounds.Left, e.Bounds.Top + 4, 3, e.Bounds.Height - 8);
        var disc = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 17, 14, 14); e.Graphics.DrawEllipse(line, disc); e.Graphics.FillEllipse(brass, disc.X + 5, disc.Y + 5, 4, 4);
        string title = list.Items[e.Index]?.ToString() ?? "", sub = "";
        if (list.Items[e.Index] is SkullgirlsAudio.Track track)
        {
            int quote = title.IndexOf(" — “", StringComparison.Ordinal);
            if (quote >= 0) { sub = title[(quote + 3)..]; title = title[..quote]; }
            else sub = track.Mapped ? "VERIFIED MUSIC TARGET" : track.Bank ? "SOUND BANK  /  " + track.Archive : track.Category;
        }
        var top = new Rectangle(e.Bounds.Left + 35, e.Bounds.Top + 5, Math.Max(1, e.Bounds.Width - 44), 21);
        var bottom = new Rectangle(top.X, e.Bounds.Top + 27, top.Width, 17);
        TextRenderer.DrawText(e.Graphics, title, list.Font, top, selected ? Ivory : Color.FromArgb(226, 210, 184), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var small = new Font("Segoe UI", 8);
        TextRenderer.DrawText(e.Graphics, sub, small, bottom, selected ? Gold : Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    public static void Diamond(Graphics g, float x, float y, float radius, Color color)
    { using var b = new SolidBrush(color); g.FillPolygon(b, new PointF[] { new(x, y - radius), new(x + radius, y), new(x, y + radius), new(x - radius, y) }); }
}

internal class DecoButton : Button
{
    private bool hover;
    public DecoButton() { DoubleBuffered = true; SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = DecoTheme.Surface; UseVisualStyleBackColor = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; MouseEnter += (_, _) => { hover = true; Invalidate(); }; MouseLeave += (_, _) => { hover = false; Invalidate(); }; }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text.ToUpperInvariant(), Font);
        return new(Math.Max(76, text.Width + Padding.Horizontal + 16), Math.Max(34, text.Height + Padding.Vertical + 8));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3); if (rect.Width < 4 || rect.Height < 4) return;
        bool primary = Text.Equals("Play", StringComparison.OrdinalIgnoreCase) || this is DecoToggleButton { Checked: true };
        using var path = new GraphicsPath(); const int cut = 5;
        path.AddPolygon([new(rect.Left + cut, rect.Top), new(rect.Right - cut, rect.Top), new(rect.Right, rect.Top + cut), new(rect.Right, rect.Bottom - cut), new(rect.Right - cut, rect.Bottom), new(rect.Left + cut, rect.Bottom), new(rect.Left, rect.Bottom - cut), new(rect.Left, rect.Top + cut)]);
        using var fill = new LinearGradientBrush(rect, primary || hover ? Color.FromArgb(113, 42, 57) : Color.FromArgb(54, 37, 43), DecoTheme.Surface, 90);
        using var border = new Pen(Enabled ? DecoTheme.Gold : DecoTheme.Edge); g.FillPath(fill, path); g.DrawPath(border, path);
        var textRect = ClientRectangle; textRect.Inflate(-8, -3);
        TextRenderer.DrawText(g, Text.ToUpperInvariant(), Font, textRect, Enabled ? DecoTheme.Ivory : DecoTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) { var focus = ClientRectangle; focus.Inflate(-5, -5); ControlPaint.DrawFocusRectangle(g, focus, DecoTheme.Gold, DecoTheme.Surface); }
    }
}

internal sealed class DecoToggleButton : DecoButton
{
    private bool selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => selected;
        set { if (selected == value) return; selected = value; Text = value ? "Loop: on" : "Loop: off"; AccessibleDescription = value ? "Loop enabled: repeat the selected range, or the full clip by default." : "Loop disabled: play once."; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;
    public DecoToggleButton() { Text = "Loop: off"; AccessibleName = "Loop playback"; AccessibleDescription = "Toggle repeated playback of the selected repeat range, or the full clip by default."; }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
}
internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
}

internal sealed class DecoMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => DecoTheme.Surface;
    public override Color ImageMarginGradientBegin => DecoTheme.Surface;
    public override Color ImageMarginGradientMiddle => DecoTheme.Surface;
    public override Color ImageMarginGradientEnd => DecoTheme.Surface;
    public override Color MenuBorder => DecoTheme.Edge;
    public override Color MenuItemBorder => DecoTheme.Gold;
    public override Color MenuItemSelected => DecoTheme.Wine;
    public override Color MenuItemSelectedGradientBegin => DecoTheme.Wine;
    public override Color MenuItemSelectedGradientEnd => DecoTheme.Wine;
    public override Color MenuItemPressedGradientBegin => DecoTheme.Wine;
    public override Color MenuItemPressedGradientMiddle => DecoTheme.Wine;
    public override Color MenuItemPressedGradientEnd => DecoTheme.Wine;
}

internal sealed class DecoFrame : Panel
{
    private readonly string caption;
    public DecoFrame(string caption) { this.caption = caption; DoubleBuffered = true; BackColor = DecoTheme.Surface; Padding = new(12, 35, 12, 12); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var outer = new Pen(DecoTheme.Gold); using var inner = new Pen(DecoTheme.Edge);
        g.DrawRectangle(outer, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        g.DrawRectangle(inner, 5, 5, Math.Max(1, Width - 11), Math.Max(1, Height - 11));
        using var font = new Font("Bahnschrift", 9);
        TextRenderer.DrawText(g, caption, font, new Rectangle(18, 11, Width - 36, 19), DecoTheme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        foreach (int x in new[] { 7, Width - 8 }) foreach (int y in new[] { 7, Height - 8 }) DecoTheme.Diamond(g, x, y, 4, DecoTheme.Gold);
    }
}

internal sealed class DecoHeader : Control
{
    public DecoHeader() { DoubleBuffered = true; BackColor = DecoTheme.Ink; AccessibleName = "Skullgirls Audio Workshop — Art Deco record player"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var gold = new Pen(DecoTheme.Gold); using var dark = new Pen(DecoTheme.Edge);
        float cx = Width / 2f;
        for (int i = 0; i < 3; i++) { float y = Height - 8 - i * 4; g.DrawLine(i == 0 ? gold : dark, 20 + i * 15, y, cx - 40 - i * 6, y); g.DrawLine(i == 0 ? gold : dark, cx + 40 + i * 6, y, Width - 20 - i * 15, y); }
        DecoTheme.Diamond(g, cx, Height - 12, 7, DecoTheme.Gold); DecoTheme.Diamond(g, cx - 22, Height - 12, 3, DecoTheme.Gold); DecoTheme.Diamond(g, cx + 22, Height - 12, 3, DecoTheme.Gold);
        var badge = new Rectangle(20, 10, 58, 58); g.DrawEllipse(gold, badge); g.DrawEllipse(dark, 24, 14, 50, 50);
        for (int i = 0; i < 4; i++) g.DrawEllipse(dark, 28 + i * 4, 18 + i * 4, 42 - i * 8, 42 - i * 8);
        DecoTheme.Diamond(g, 49, 39, 8, DecoTheme.Gold);
        using var small = new Font("Bahnschrift", 10); using var title = new Font("Bahnschrift SemiCondensed", 26, FontStyle.Bold);
        TextRenderer.DrawText(g, "SKULLGIRLS", small, new Rectangle(94, 8, Width - 188, 20), DecoTheme.Gold, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "AUDIO WORKSHOP", title, new Rectangle(94, 28, Width - 188, 44), DecoTheme.Ivory, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        for (int i = 0; i < 3; i++) { int x = Width - 75 + i * 7; g.DrawLine(gold, x, 24 + i * 5, x, 61 - i * 5); }
    }
}

internal sealed class Turntable : Control
{
    private bool playing, paused;
    private double position, duration;
    private string clipTitle = "Choose a record";
    private Color labelColor = DecoTheme.Wine, cabinetTop = Color.FromArgb(77, 35, 43), cabinetBottom = Color.FromArgb(32, 21, 27);
    private double angle, lastFrame;
    private readonly System.Diagnostics.Stopwatch animationClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 30 };
    public Turntable()
    {
        DoubleBuffered = true; SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = DecoTheme.Surface; AccessibleName = "Rotating vinyl preview display";
        animationTimer.Tick += (_, _) => { AdvanceRotation(); Invalidate(); };
    }
    private void AdvanceRotation()
    {
        double now = animationClock.Elapsed.TotalSeconds;
        if (playing && !paused) angle = (angle + Math.Max(0, now - lastFrame) * 200) % 360;
        lastFrame = now;
    }
    public void UpdatePlayback(double seconds, double length, bool running, bool held)
    {
        bool changed = position != seconds || duration != length || playing != running || paused != held;
        AdvanceRotation(); position = seconds; duration = length; playing = running; paused = held;
        if (running && !held) animationTimer.Start(); else animationTimer.Stop();
        // The animation timer owns rendering while running; avoid two repaint streams.
        if (changed && (!running || held)) Invalidate();
    }
    public void SetClip(string title)
    {
        if (title == clipTitle) return;
        clipTitle = title; AccessibleName = "Record preview: " + title; Invalidate();
    }
    public void SetPalette(Color label, Color top, Color bottom)
    {
        labelColor = label; cabinetTop = top; cabinetBottom = bottom; Invalidate();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { animationTimer.Stop(); animationTimer.Dispose(); animationClock.Stop(); }
        base.Dispose(disposing);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var box = new Rectangle(5, 6, Math.Max(1, Width - 11), Math.Max(1, Height - 12));
        using var cabinet = new LinearGradientBrush(box, cabinetTop, cabinetBottom, 100);
        g.FillRectangle(cabinet, box); using var edge = new Pen(DecoTheme.Gold); g.DrawRectangle(edge, box);
        float diameter = Math.Min(Width - 42, Height - 105), x = (Width - diameter) / 2f - 4, y = 23;
        if (diameter < 20) return;
        float centerX = x + diameter / 2, centerY = y + diameter / 2, label = diameter * .43f;
        using var shadow = new SolidBrush(Color.FromArgb(14, 9, 13)); g.FillEllipse(shadow, x - 5, y - 2, diameter + 10, diameter + 10);
        var vinyl = g.Save(); g.TranslateTransform(centerX, centerY); g.RotateTransform((float)angle);
        float radius = diameter / 2;
        using (var shape = new GraphicsPath())
        {
            shape.AddEllipse(-radius, -radius, diameter, diameter);
            using var surface = new PathGradientBrush(shape) { CenterColor = Color.FromArgb(41, 34, 44), SurroundColors = [Color.FromArgb(9, 9, 12)] };
            g.FillPath(surface, shape);
        }
        using var groove = new Pen(Color.FromArgb(53, 46, 56), .7f);
        for (int i = 0; i < 25; i++) { float r = radius - 4 - i * (diameter * .3f / 25); g.DrawEllipse(groove, -r, -r, r * 2, r * 2); }
        // Asymmetric groove details and the printed label rotate with the vinyl.
        using var etching = new Pen(Color.FromArgb(110, 115, 100, 116), 1.1f);
        for (int i = 0; i < 4; i++) { float r = radius - 9 - i * 3; g.DrawArc(etching, -r, -r, r * 2, r * 2, 205 + i * 6, 28); }
        using var marker = new Pen(DecoTheme.Gold, 2); g.DrawLine(marker, 0, -radius + 3, 0, -radius + 10);
        using var labelFill = new SolidBrush(labelColor); g.FillEllipse(labelFill, -label / 2, -label / 2, label, label);
        g.DrawEllipse(edge, -label / 2 + 3, -label / 2 + 3, label - 6, label - 6);
        using var labelFont = new Font("Bahnschrift", 6, FontStyle.Bold); using var labelInk = new SolidBrush(DecoTheme.Ivory);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        string printedTitle = clipTitle; int quote = printedTitle.IndexOf(" — “", StringComparison.Ordinal); if (quote >= 0) printedTitle = printedTitle[..quote];
        g.DrawString(printedTitle.Replace(" · ", "\n"), labelFont, labelInk, new RectangleF(-label / 2 + 6, -label / 2 + 9, label - 12, label - 18), centered);
        using var spindle = new SolidBrush(DecoTheme.Gold); g.FillEllipse(spindle, -2, -2, 4, 4);
        g.Restore(vinyl);
        // Stationary lighting crosses the moving grooves, like a lacquered record.
        var lighting = g.Save();
        using (var ring = new GraphicsPath(FillMode.Alternate))
        {
            ring.AddEllipse(x, y, diameter, diameter); ring.AddEllipse(centerX - label / 2, centerY - label / 2, label, label);
            g.SetClip(ring, CombineMode.Intersect);
            using var sheen = new LinearGradientBrush(new RectangleF(x, y, diameter, diameter), Color.Transparent, Color.Transparent, 35);
            sheen.InterpolationColors = new ColorBlend
            {
                Positions = [0, .18f, .31f, .42f, .6f, .72f, .83f, 1],
                Colors = [Color.Transparent, Color.Transparent, Color.FromArgb(54, 240, 221, 184), Color.Transparent, Color.Transparent, Color.FromArgb(30, 178, 190, 211), Color.Transparent, Color.Transparent]
            };
            g.FillEllipse(sheen, x, y, diameter, diameter);
        }
        g.Restore(lighting);
        using var rim = new Pen(Color.FromArgb(127, 154, 129, 96), 1.3f); g.DrawArc(rim, x, y, diameter, diameter, 200, 105);
        float armX = Width - 29, armY = 28, progress = (float)Math.Clamp(position / Math.Max(.001, duration), 0, 1);
        using var arm = new Pen(DecoTheme.Gold, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var contact = new PointF(centerX + diameter * (.36f - progress * .13f), centerY + diameter * .22f);
        g.DrawLines(arm, [new(armX, armY), new(armX - 8, armY + diameter * .5f), contact]);
        using var hardware = new SolidBrush(DecoTheme.Gold); g.FillEllipse(hardware, armX - 7, armY - 7, 14, 14);
        g.FillRectangle(hardware, contact.X - 6, contact.Y - 1, 12, 10);
        using var nameFont = new Font("Bahnschrift", 8);
        TextRenderer.DrawText(g, clipTitle, nameFont, new Rectangle(14, Height - 77, Width - 28, 37), DecoTheme.Ivory, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string state = paused ? "STYLUS HELD  /  PAUSED" : playing ? "ON AIR  /  PLAYING" : "STYLUS READY";
        TextRenderer.DrawText(g, state, nameFont, new Rectangle(8, Height - 35, Width - 16, 22), DecoTheme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        foreach (var point in new[] { new Point(13, 14), new Point(Width - 14, 14), new Point(13, Height - 15), new Point(Width - 14, Height - 15) }) { g.DrawEllipse(edge, point.X - 2, point.Y - 2, 4, 4); }
    }
}
