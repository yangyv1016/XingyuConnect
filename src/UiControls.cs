using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace XingyuConnect;

internal sealed class RoundedChoiceGroup : TableLayoutPanel
{
    private readonly RadioButton[] choices;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => Array.FindIndex(choices, choice => choice.Checked);
        set => choices[value].Checked = true;
    }
    public event EventHandler? SelectedIndexChanged;

    public RoundedChoiceGroup((string Title, string Detail)[] options, int selectedIndex)
    {
        ColumnCount = options.Length;
        RowCount = 1;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        choices = new RadioButton[options.Length];
        for (var index = 0; index < options.Length; index++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / options.Length));
            var option = options[index];
            var choice = new RoundedRadioButton(option.Title, option.Detail) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, index == options.Length - 1 ? 0 : 6, 2) };
            choice.CheckedChanged += (_, _) => { if (choice.Checked) SelectedIndexChanged?.Invoke(this, EventArgs.Empty); };
            choices[index] = choice;
            Controls.Add(choice, index, 0);
        }
        SelectedIndex = selectedIndex;
    }
}

internal sealed class RoundedRadioButton : RadioButton
{
    private readonly string detail;
    private bool hovered;

    public RoundedRadioButton(string title, string detail)
    {
        Text = title;
        this.detail = detail;
        AccessibleName = title + "，" + detail;
        Appearance = Appearance.Button;
        AutoSize = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var scale = DeviceDpi / 96f;
        graphics.Clear(Ui.BackgroundFor(this));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.RoundedRectangle(new RectangleF(1, 1, Width - 3, Height - 3), 9 * scale);
        using var fill = new SolidBrush(Checked ? Ui.AccentLight : hovered ? Color.FromArgb(247, 250, 255) : Color.White);
        using var edge = new Pen(Checked || Focused ? Ui.Accent : Ui.Border, Checked ? 1.4f : 1);
        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);
        var dot = new RectangleF(9 * scale, 13 * scale, 10 * scale, 10 * scale);
        graphics.DrawEllipse(edge, dot);
        if (Checked)
        {
            using var active = new SolidBrush(Ui.Accent);
            graphics.FillEllipse(active, RectangleF.Inflate(dot, -2.5f * scale, -2.5f * scale));
        }
        using var titleFont = new Font(Font, FontStyle.Bold);
        using var detailFont = new Font(Font.FontFamily, 8f);
        TextRenderer.DrawText(graphics, Text, titleFont, new Rectangle((int)(24 * scale), (int)(7 * scale), Width - (int)(27 * scale), (int)(23 * scale)), Checked ? Ui.Accent : Ui.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, detail, detailFont, new Rectangle((int)(3 * scale), (int)(33 * scale), Width - (int)(6 * scale), (int)(20 * scale)), Ui.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class SurfacePanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EdgeColor { get; set; } = Ui.Border;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dashed { get; set; }

    public SurfacePanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Padding = new Padding(20);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 14 * DeviceDpi / 96f);
        using var fill = new SolidBrush(FillColor);
        using var edge = new Pen(EdgeColor) { DashStyle = Dashed ? DashStyle.Dash : DashStyle.Solid };
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(edge, shape);
    }
}

internal sealed class IconBadge : Control
{
    private readonly UiIcon icon;
    private readonly bool brand;
    public IconBadge(UiIcon icon, bool brand = false)
    {
        this.icon = icon;
        this.brand = brand;
        Size = new Size(38, 38);
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (brand)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var size = Math.Min(Width, Height);
            e.Graphics.DrawImage(BrandAssets.Logo, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), 10 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Ui.AccentLight);
        e.Graphics.FillPath(fill, shape);
        Ui.DrawIcon(e.Graphics, icon, new RectangleF(Width * .23f, Height * .23f, Width * .54f, Height * .54f), Ui.Accent);
    }
}

internal sealed class ActionButton : Button
{
    private bool hovered;
    private bool pressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiIcon Icon { get; set; }

    public ActionButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Ui.BackgroundFor(this));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fillColor = !Enabled ? Color.FromArgb(242, 244, 248) : Primary
            ? pressed ? Color.FromArgb(29, 75, 186) : hovered ? Color.FromArgb(34, 86, 211) : Ui.Accent
            : pressed ? Color.FromArgb(224, 233, 249) : hovered ? Ui.AccentLight : Color.FromArgb(247, 249, 252);
        var textColor = !Enabled ? Color.FromArgb(151, 162, 180) : Primary ? Color.White : Ui.Ink;
        using var shape = Ui.RoundedRectangle(new RectangleF(1, 1, Width - 2, Height - 2), 8 * DeviceDpi / 96f);
        using var fill = new SolidBrush(fillColor);
        using var edge = new Pen(Focused && Enabled ? Ui.Accent : Primary && Enabled ? fillColor : Ui.Border);
        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);
        var textBounds = new Rectangle(10, 0, Width - 20, Height);
        if (Icon != UiIcon.None)
        {
            var iconSize = (int)(18 * DeviceDpi / 96f);
            var textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            var textWidth = Math.Min(textSize.Width, Math.Max(0, Width - iconSize - 32));
            var start = Math.Max(12, (Width - textWidth - iconSize - 8) / 2);
            Ui.DrawIcon(graphics, Icon, new RectangleF(start, (Height - iconSize) / 2f, iconSize, iconSize), textColor);
            textBounds = new Rectangle(start + iconSize + 8, 0, textWidth, Height);
        }
        TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -5, -5), textColor, fillColor);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AutoSize = false;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Ui.BackgroundFor(this));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(Width - 44 * scale, (Height - 24 * scale) / 2, 42 * scale, 24 * scale);
        using var shape = Ui.RoundedRectangle(track, 12 * scale);
        using var fill = new SolidBrush(Checked ? Enabled ? Ui.Accent : Color.FromArgb(145, 174, 237) : Color.FromArgb(207, 216, 230));
        using var knob = new SolidBrush(Color.White);
        graphics.FillPath(fill, shape);
        graphics.FillEllipse(knob, track.X + (Checked ? 21 : 3) * scale, track.Y + 3 * scale, 18 * scale, 18 * scale);
        TextRenderer.DrawText(graphics, Text, Font, new Rectangle(0, 0, Math.Max(0, (int)track.X - 12), Height), Enabled ? Ui.Ink : Ui.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}

internal sealed class ModernComboBox : ComboBox
{
    public ModernComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Font.Height + 12;
        BackColor = Color.White;
        ForeColor = Ui.Ink;
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 12; }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using var fill = new SolidBrush(selected ? Ui.AccentLight : Color.White);
        e.Graphics.FillRectangle(fill, e.Bounds);
        var text = e.Index >= 0 ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -9, 0), Enabled ? selected ? Ui.Accent : Ui.Ink : Ui.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x0F) return;
        using var graphics = Graphics.FromHwnd(Handle);
        var arrowWidth = (int)(30 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Enabled ? Color.White : Color.FromArgb(247, 249, 252));
        graphics.FillRectangle(fill, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
        using var edge = new Pen(Focused ? Ui.Accent : Ui.Border);
        graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var x = Width - arrowWidth / 2f;
        var y = Height / 2f;
        using var arrow = new Pen(Ui.Muted, 1.5f);
        graphics.DrawLines(arrow, [new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2)]);
    }
}

internal sealed class ModernTabs : TabControl
{
    public ModernTabs()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(190, 44);
        Padding = new Point(16, 6);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.Background);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Ui.Background);
        for (var index = 0; index < TabPages.Count; index++)
        {
            var bounds = GetTabRect(index);
            var selected = index == SelectedIndex;
            if (selected)
            {
                using var shape = Ui.RoundedRectangle(RectangleF.Inflate(bounds, -3, -4), 8 * DeviceDpi / 96f);
                using var fill = new SolidBrush(Color.White);
                e.Graphics.FillPath(fill, shape);
            }
            TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds, selected ? Ui.Accent : Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
