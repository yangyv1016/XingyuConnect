using System.Diagnostics;

namespace XingyuConnect;

internal sealed class ConnectionGuideForm : ModernForm
{
    public ConnectionGuideForm()
    {
        Text = "星语互联 · 连接须知";
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ClientSize = new Size(720, 700);
        MinimumSize = new Size(660, 620);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 0, 24, 18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = new Label { Text = "连接前，先看这几件事", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Location = new Point(0, 21) };
        var subtitle = new Label { Text = "首次连接、无线重连，以及容易忽略的小提醒", AutoSize = true, ForeColor = Ui.Muted, Location = new Point(2, 58) };
        header.Controls.AddRange([title, subtitle, CreateWindowActions()]);
        EnableWindowDrag(header, title, subtitle);
        root.Controls.Add(header, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var sections = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Margin = Padding.Empty };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(sections);
        root.Controls.Add(scroll, 0, 1);
        var notes = new[]
        {
            ("第一次用 USB 连接", "在手机的开发者选项中开启“USB 调试”。插线后解锁手机，允许这台电脑调试。\n只充电、看不到设备时，换一根支持数据传输的线；能投屏但无法操作时，检查手机是否还要求开启“USB 调试（安全设置）”。"),
            ("想拔掉数据线使用", "手机和电脑连接同一路由器，电脑可以用网线。首次可先插 USB，在“局域网连接”中切换为无线，显示连接成功后再拔线。\n支持“无线调试”的手机也能使用配对码；配对成功后，要用无线调试主页面的连接地址，两个页面的端口不同。"),
            ("以前能连，现在找不到了", "手机重启、换 Wi-Fi 或关闭无线调试后，可能需要重新连接，地址或端口也可能变化。先解锁手机、检查调试开关，再到“局域网连接”中重连。\n访客 Wi-Fi 或路由器的设备隔离可能让手机与电脑互相找不到，请换到可互通的网络。"),
            ("声音和息屏，留意两点", "让手机播放电脑声音：首次初始化播放驱动，在 Windows 输出中选择“手机音频”，再开启传音；切回扬声器即可恢复电脑播放。\n播放驱动来自 VB-Audio 的 VB-CABLE（vb-cable.com），属于捐赠软件，欢迎支持作者。\n只想关闭手机显示屏，请用“息屏”；“电源 / 锁屏”可能锁定手机。投屏不会解除已有的密码锁。")
        };
        var cards = new List<(SurfacePanel Card, Label Heading, Label Body)>();
        foreach (var (heading, text) in notes)
        {
            var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 10) };
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var headingLabel = new Label { Text = heading, AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 7) };
            var bodyLabel = new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            content.Controls.Add(headingLabel);
            content.Controls.Add(bodyLabel);
            card.Controls.Add(content);
            sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sections.Controls.Add(card, 0, sections.RowCount++);
            cards.Add((card, headingLabel, bodyLabel));
        }
        void ResizeCards(object? sender, EventArgs e)
        {
            foreach (var (card, heading, body) in cards)
            {
                var width = Math.Max(1, sections.ClientSize.Width - card.Margin.Horizontal - card.Padding.Horizontal);
                card.Height = card.Padding.Vertical + heading.PreferredHeight + heading.Margin.Vertical +
                    TextRenderer.MeasureText(body.Text, body.Font, new Size(width, 0), TextFormatFlags.WordBreak).Height;
            }
        }
        sections.SizeChanged += ResizeCards;
        Shown += ResizeCards;
        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0) };
        var logs = Ui.Button("打开日志文件夹", 164);
        logs.Dock = DockStyle.Left;
        logs.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppLog.DirectoryPath);
                Process.Start(new ProcessStartInfo(AppLog.DirectoryPath) { UseShellExecute = true });
                if (AppLog.LastError is not null)
                    MessageBox.Show(this, "最近一次日志写入失败：" + AppLog.LastError, "运行日志", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { MessageBox.Show(this, "暂时无法打开日志文件夹：" + ex.Message, "运行日志", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        footer.Controls.Add(logs);
        var close = Ui.Button("知道了", 112, true);
        close.Dock = DockStyle.Right;
        close.DialogResult = DialogResult.OK;
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 2);
        AcceptButton = CancelButton = close;
    }
}
