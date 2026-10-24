using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace XingyuConnect;

internal sealed class ModernTrayMenu : ContextMenuStrip
{
    public ModernTrayMenu()
    {
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = Color.White;
        ForeColor = Ui.Ink;
        ShowImageMargin = ShowCheckMargin = false;
        Renderer = new TrayRenderer(this);
        Items.Add(new ToolStripLabel("星语互联") { AutoSize = false });
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        Padding = new Padding((int)(8 * scale));
        foreach (ToolStripItem item in Items)
        {
            item.AutoSize = false;
            item.Size = new Size((int)(256 * scale), (int)((item is ToolStripLabel ? 62 : item is ToolStripSeparator ? 14 : 44) * scale));
        }
        base.OnOpening(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var shape = Ui.RoundedRectangle(new RectangleF(0, 0, Width, Height), 14 * DeviceDpi / 96f);
        var previous = Region;
        Region = new Region(shape);
        previous?.Dispose();
    }

    private sealed class TrayRenderer(ModernTrayMenu menu) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Color.White);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Ui.RoundedRectangle(new RectangleF(.5f, .5f, menu.Width - 1, menu.Height - 1), 14 * menu.DeviceDpi / 96f);
            using var pen = new Pen(Ui.Border);
            e.Graphics.DrawPath(pen, shape);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var scale = menu.DeviceDpi / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Ui.RoundedRectangle(new RectangleF(1, 2 * scale, e.Item.Width - 2, e.Item.Height - 4 * scale), 8 * scale);
            using var fill = new SolidBrush(e.Item.Tag is UiIcon.Power ? Color.FromArgb(255, 239, 240) : Ui.AccentLight);
            e.Graphics.FillPath(fill, shape);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var scale = menu.DeviceDpi / 96f;
            if (e.Item is ToolStripLabel)
            {
                e.Graphics.DrawImage(BrandAssets.Logo, new Rectangle((int)(12 * scale), (int)(10 * scale), (int)(34 * scale), (int)(34 * scale)));
                using var titleFont = new Font(menu.Font, FontStyle.Bold);
                using var subtitleFont = new Font(menu.Font.FontFamily, 8.5f);
                TextRenderer.DrawText(e.Graphics, e.Item.Text, titleFont, new Rectangle((int)(56 * scale), (int)(7 * scale), e.Item.Width - (int)(64 * scale), (int)(26 * scale)), Ui.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, "连接设备，让协作更轻松", subtitleFont, new Rectangle((int)(56 * scale), (int)(33 * scale), e.Item.Width - (int)(64 * scale), (int)(20 * scale)), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            var color = !e.Item.Enabled ? Color.FromArgb(165, 174, 188) : e.Item.Tag is UiIcon.Power ? Color.FromArgb(204, 65, 78) : e.Item.Selected ? Ui.Accent : Ui.Ink;
            if (e.Item.Tag is UiIcon icon)
                Ui.DrawIcon(e.Graphics, icon, new RectangleF(14 * scale, (e.Item.Height - 20 * scale) / 2, 20 * scale, 20 * scale), color);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, menu.Font, new Rectangle((int)(48 * scale), 0, e.Item.Width - (int)(60 * scale), e.Item.Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var inset = 12 * menu.DeviceDpi / 96f;
            using var pen = new Pen(Ui.Border);
            e.Graphics.DrawLine(pen, inset, e.Item.Height / 2f, e.Item.Width - inset, e.Item.Height / 2f);
        }
    }
}
