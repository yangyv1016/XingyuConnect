using System.Drawing.Drawing2D;

namespace XingyuConnect;

internal enum UiIcon { None, Devices, Wifi, Display, Audio, Folder, Refresh, Help, Home, Minimize, Power }

internal static class Ui
{
    public static readonly Color Background = Color.FromArgb(244, 247, 251);
    public static readonly Color Ink = Color.FromArgb(25, 38, 58);
    public static readonly Color Muted = Color.FromArgb(106, 120, 141);
    public static readonly Color Accent = Color.FromArgb(45, 102, 234);
    public static readonly Color AccentLight = Color.FromArgb(235, 242, 255);
    public static readonly Color Border = Color.FromArgb(226, 232, 241);
    public static readonly Color Success = Color.FromArgb(26, 146, 108);

    public static ActionButton Button(string text, int width, bool primary = false) => new()
    {
        Text = text, Width = width, Height = 40, Primary = primary,
        Cursor = Cursors.Hand, Margin = new Padding(3), Font = new Font("Microsoft YaHei UI", 9.5f)
    };

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color BackgroundFor(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is SurfacePanel surface) return surface.FillColor;
            if (parent.BackColor.A == 255) return parent.BackColor;
        }
        return Background;
    }

    public static void DrawIcon(Graphics graphics, UiIcon icon, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (icon)
        {
            case UiIcon.Devices:
                graphics.DrawRoundedRectangle(pen, new RectangleF(6, 2, 12, 20), new SizeF(2.5f, 2.5f));
                graphics.DrawLine(pen, 10, 5, 14, 5);
                graphics.DrawLine(pen, 11, 19, 13, 19);
                break;
            case UiIcon.Wifi:
                graphics.DrawArc(pen, 2, 4, 20, 20, 225, 90);
                graphics.DrawArc(pen, 6, 8, 12, 12, 225, 90);
                graphics.DrawArc(pen, 10, 12, 4, 4, 225, 90);
                using (var dot = new SolidBrush(color)) graphics.FillEllipse(dot, 11, 18, 2, 2);
                break;
            case UiIcon.Display:
                graphics.DrawRoundedRectangle(pen, new RectangleF(2, 3, 20, 14), new SizeF(2, 2));
                graphics.DrawLine(pen, 12, 17, 12, 21);
                graphics.DrawLine(pen, 8, 21, 16, 21);
                break;
            case UiIcon.Audio:
                graphics.DrawLines(pen, [new PointF(3, 9), new PointF(7, 9), new PointF(12, 5), new PointF(12, 19), new PointF(7, 15), new PointF(3, 15), new PointF(3, 9)]);
                graphics.DrawArc(pen, 11, 7, 8, 10, -65, 130);
                graphics.DrawArc(pen, 10, 3, 13, 18, -60, 120);
                break;
            case UiIcon.Folder:
                graphics.DrawLines(pen, [new PointF(3, 7), new PointF(3, 4), new PointF(9, 4), new PointF(12, 7), new PointF(21, 7), new PointF(21, 20), new PointF(3, 20), new PointF(3, 7), new PointF(21, 7)]);
                graphics.DrawLine(pen, 12, 17, 12, 10);
                graphics.DrawLines(pen, [new PointF(9, 13), new PointF(12, 10), new PointF(15, 13)]);
                break;
            case UiIcon.Refresh:
                graphics.DrawArc(pen, 4, 4, 16, 16, 30, 290);
                graphics.DrawLines(pen, [new PointF(20, 4), new PointF(20, 9), new PointF(15, 9)]);
                break;
            case UiIcon.Home:
                graphics.DrawLines(pen, [new PointF(3, 11), new PointF(12, 3), new PointF(21, 11)]);
                graphics.DrawLines(pen, [new PointF(5, 10), new PointF(5, 21), new PointF(10, 21), new PointF(10, 15), new PointF(14, 15), new PointF(14, 21), new PointF(19, 21), new PointF(19, 10)]);
                break;
            case UiIcon.Minimize:
                graphics.DrawRoundedRectangle(pen, new RectangleF(3, 4, 18, 16), new SizeF(2, 2));
                graphics.DrawLine(pen, 8, 16, 16, 16);
                break;
            case UiIcon.Power:
                graphics.DrawArc(pen, 4, 4, 16, 16, -55, 290);
                graphics.DrawLine(pen, 12, 2, 12, 11);
                break;
            case UiIcon.Help:
                graphics.DrawEllipse(pen, 3, 3, 18, 18);
                graphics.DrawArc(pen, 9, 7, 6, 6, 180, 270);
                graphics.DrawLine(pen, 12, 13, 12, 14);
                graphics.DrawLine(pen, 12, 17, 12, 17.2f);
                break;
        }
        graphics.Restore(state);
    }
}
