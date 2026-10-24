using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace XingyuConnect;

internal enum MirrorIcon { Back, Home, Tasks, VolumeUp, VolumeDown, Power, ScreenOff, ScreenOn, Notifications, Capture, Fullscreen, Restore, Minimize, Maximize, Close, Keyboard }

internal sealed class MirrorButton : Button
{
    private bool hovered;
    private bool pressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MirrorIcon Icon { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowLabel { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LightTheme { get; init; }

    public MirrorButton(string text, MirrorIcon icon, bool showLabel = false)
    {
        Text = text;
        AccessibleName = text;
        Icon = icon;
        ShowLabel = showLabel;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.FromArgb(19, 22, 29);
        ForeColor = Color.FromArgb(190, 199, 214);
        Cursor = Cursors.Hand;
        Margin = new Padding(3);
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        var scale = DeviceDpi / 96f;
        if (ShowLabel || (hovered || Focused) && Enabled)
        {
            using var fill = new SolidBrush(Icon == MirrorIcon.Close ? Color.FromArgb(194, 58, 73) : LightTheme
                ? pressed ? Color.FromArgb(213, 225, 246) : hovered || Focused ? Ui.AccentLight : Ui.Background :
                !Enabled ? Color.FromArgb(25, 35, 49) : pressed ? Color.FromArgb(35, 81, 169) :
                hovered || Focused ? Color.FromArgb(37, 65, 105) : Color.FromArgb(30, 44, 63));
            using var hoverShape = Ui.RoundedRectangle(new RectangleF(1, 1, Width - 3, Height - 3), 9 * scale);
            graphics.FillPath(fill, hoverShape);
        }
        var color = !Enabled ? Ui.Muted : LightTheme
            ? Icon == MirrorIcon.Close && (hovered || Focused) ? Color.White : Ui.Ink
            : hovered || Focused ? Color.White : ForeColor;
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var x = (Width - 20 * scale) / 2f;
        var y = ShowLabel ? 6 * scale : (Height - 20 * scale) / 2f;
        var origin = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(scale, scale);
        switch (Icon)
        {
            case MirrorIcon.Back: graphics.DrawLines(pen, [new PointF(12, 3), new PointF(5, 10), new PointF(12, 17)]); break;
            case MirrorIcon.Home: graphics.DrawEllipse(pen, 3, 3, 14, 14); break;
            case MirrorIcon.Tasks: graphics.DrawRoundedRectangle(pen, new RectangleF(3, 3, 14, 14), new SizeF(3, 3)); break;
            case MirrorIcon.VolumeUp:
            case MirrorIcon.VolumeDown:
                graphics.DrawLines(pen, [new PointF(3, 7), new PointF(7, 7), new PointF(11, 3), new PointF(11, 17), new PointF(7, 13), new PointF(3, 13), new PointF(3, 7)]);
                graphics.DrawLine(pen, 14, 10, 19, 10);
                if (Icon == MirrorIcon.VolumeUp) graphics.DrawLine(pen, 16.5f, 7.5f, 16.5f, 12.5f);
                break;
            case MirrorIcon.Power:
                graphics.DrawArc(pen, 3, 3, 14, 14, -55, 290);
                graphics.DrawLine(pen, 10, 1, 10, 9);
                break;
            case MirrorIcon.ScreenOff:
            case MirrorIcon.ScreenOn:
                graphics.DrawRoundedRectangle(pen, new RectangleF(5, 1, 10, 18), new SizeF(2, 2));
                graphics.DrawLine(pen, 9, 16, 11, 16);
                if (Icon == MirrorIcon.ScreenOff) graphics.DrawLine(pen, 2, 2, 18, 18);
                else graphics.DrawEllipse(pen, 8, 6, 4, 4);
                break;
            case MirrorIcon.Notifications:
                graphics.DrawArc(pen, 5, 3, 10, 11, 180, 180);
                graphics.DrawLines(pen, [new PointF(5, 8), new PointF(5, 12), new PointF(3, 15), new PointF(17, 15), new PointF(15, 12), new PointF(15, 8)]);
                graphics.DrawArc(pen, 8, 15, 4, 3, 0, 180);
                break;
            case MirrorIcon.Capture:
                graphics.DrawLines(pen, [new PointF(2, 6), new PointF(6, 6), new PointF(7, 3), new PointF(13, 3), new PointF(14, 6), new PointF(18, 6), new PointF(18, 17), new PointF(2, 17), new PointF(2, 6)]);
                graphics.DrawEllipse(pen, 7, 7, 6, 6);
                break;
            case MirrorIcon.Fullscreen:
                graphics.DrawLines(pen, [new PointF(3, 8), new PointF(3, 3), new PointF(8, 3)]);
                graphics.DrawLines(pen, [new PointF(12, 3), new PointF(17, 3), new PointF(17, 8)]);
                graphics.DrawLines(pen, [new PointF(17, 12), new PointF(17, 17), new PointF(12, 17)]);
                graphics.DrawLines(pen, [new PointF(8, 17), new PointF(3, 17), new PointF(3, 12)]);
                break;
            case MirrorIcon.Restore:
                graphics.DrawRectangle(pen, 3, 6, 11, 11);
                graphics.DrawLines(pen, [new PointF(7, 3), new PointF(17, 3), new PointF(17, 13)]);
                break;
            case MirrorIcon.Minimize: graphics.DrawLine(pen, 5, 11, 15, 11); break;
            case MirrorIcon.Maximize: graphics.DrawRectangle(pen, 5, 5, 10, 10); break;
            case MirrorIcon.Keyboard:
                graphics.DrawRoundedRectangle(pen, new RectangleF(1, 4, 18, 12), new SizeF(2, 2));
                for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 4; column++) graphics.DrawLine(pen, 4 + column * 4, 7 + row * 3, 4.4f + column * 4, 7 + row * 3);
                graphics.DrawLine(pen, 6, 13, 14, 13);
                break;
            case MirrorIcon.Close:
                graphics.DrawLine(pen, 5, 5, 15, 15);
                graphics.DrawLine(pen, 15, 5, 5, 15);
                break;
        }
        graphics.Restore(origin);
        if (ShowLabel)
        {
            using var font = new Font("Microsoft YaHei UI", 8.3f);
            TextRenderer.DrawText(graphics, Text, font, new Rectangle(0, Height - (int)(22 * scale), Width, (int)(20 * scale)), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
