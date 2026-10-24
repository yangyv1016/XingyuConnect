using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace XingyuConnect;

internal sealed record AudioSource(string? Id, string Name, bool IsVirtual);

internal sealed class MainForm : ModernForm
{
    private readonly DeviceBridge bridge = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer deviceTimer = new() { Interval = 4000 };
    private readonly ListBox deviceList = new();
    private readonly Label deviceCount = new();
    private readonly Label emptyDevices = new();
    private readonly Label connectionHint = new();
    private readonly RoundedChoiceGroup quality = new([("流畅", "720p · 30帧"), ("均衡", "1080p · 60帧"), ("清晰", "原画 · 60帧")], 1);
    private readonly RoundedChoiceGroup soundOutput = new([("手机", "手机播放"), ("电脑", "电脑播放"), ("双端", "Android 13+")], 0);
    private readonly ToggleSwitch computerAudioToggle = new();
    private readonly Label computerAudioStatus = new();
    private readonly ModernComboBox computerAudioSource = new();
    private readonly ActionButton installAudioButton;
    private readonly Label status = new();
    private readonly Label transferStatus = new();

    private readonly ActionButton connectButton;
    private readonly ActionButton disconnectButton;
    private readonly ActionButton uploadButton;
    private readonly ActionButton wirelessButton;
    private readonly ActionButton browseFilesButton;
    private readonly ActionButton albumButton;
    private readonly ActionButton appsButton;
    private readonly Label settingsHint = new();
    private readonly List<Form> contentWindows = [];
    private readonly NotifyIcon trayIcon;
    private readonly ModernTrayMenu trayMenu = new();
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "last-device.txt");
    private MirrorForm? operationWindow;
    private AudioShareSession? computerAudio;
    private CancellationTokenSource? audioCancellation;
    private Task computerAudioTask = Task.CompletedTask;
    private Task disconnectTask = Task.CompletedTask;
    private bool audioBusy;
    private bool disconnecting;
    private bool resettingAudioToggle;
    private bool closing;
    private bool installingAudioDevice;
    private bool preferVirtualAudio;
    private string? preferredSerial;
    private string? lastScanError;
    private bool scanning;
    private bool transferring;
    private TaskCompletionSource? transferCompletion;
    private int mirrorSettingsRevision;
    private int mirrorOpenRevision;

    public MainForm()
    {
        Text = "星语互联 · 启动器";
        Icon = BrandAssets.WindowIcon;
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ClientSize = new Size(1140, 820);
        MinimumSize = new Size(1040, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(28, 0, 28, 12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new IconBadge(UiIcon.Devices, true) { Size = new Size(48, 48), Location = new Point(0, 25) });
        header.Controls.Add(new Label { Text = "星语互联", Font = new Font(Font.FontFamily, 22, FontStyle.Bold), AutoSize = true, Location = new Point(62, 19) });
        header.Controls.Add(new Label { Text = "连接设备，让协作更轻松", ForeColor = Ui.Muted, AutoSize = true, Location = new Point(64, 58) });
        var helpArea = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 124, Padding = new Padding(0, 27, 0, 0) };
        var help = Ui.Button("使用说明", 120);
        help.Icon = UiIcon.Help;
        help.Click += (_, _) => { using var guide = new ConnectionGuideForm(); guide.ShowDialog(this); };
        helpArea.Controls.Add(help);
        header.Controls.Add(helpArea);
        var windowActions = CreateWindowActions();
        windowActions.Padding = new Padding(0, 27, 0, 0);
        header.Controls.Add(windowActions);
        EnableWindowDrag(header.Controls.Cast<Control>().Where(control => control is Label or IconBadge).Prepend(header).ToArray());
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);
        var deviceCard = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 20, 0) };
        body.Controls.Add(deviceCard, 0, 0);
        var deviceArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        foreach (var height in new[] { 40f, 28f, 52f, 0f, 30f, 100f })
            deviceArea.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        deviceCard.Controls.Add(deviceArea);
        var deviceHeading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        deviceHeading.Controls.Add(new Label { Text = "我的设备", Font = new Font(Font.FontFamily, 12, FontStyle.Bold), AutoSize = true, Location = new Point(0, 9) });
        var refresh = Ui.Button("刷新", 78);
        refresh.Icon = UiIcon.Refresh;
        refresh.Dock = DockStyle.Right;
        refresh.Click += async (_, _) => await RefreshDevicesAsync();
        deviceHeading.Controls.Add(refresh);
        deviceArea.Controls.Add(deviceHeading, 0, 0);
        deviceCount.Text = "正在发现设备";
        deviceCount.ForeColor = Ui.Muted;
        deviceCount.Dock = DockStyle.Fill;
        deviceCount.TextAlign = ContentAlignment.MiddleLeft;
        deviceArea.Controls.Add(deviceCount, 0, 1);
        wirelessButton = Ui.Button("局域网连接", 250);
        wirelessButton.Icon = UiIcon.Wifi;
        wirelessButton.Dock = DockStyle.Fill;
        wirelessButton.Margin = new Padding(0, 4, 0, 8);
        wirelessButton.Click += async (_, _) => await OpenWirelessConnectionAsync();
        deviceArea.Controls.Add(wirelessButton, 0, 2);
        var deviceHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White };
        deviceList.Dock = DockStyle.Fill;
        deviceList.BackColor = Color.White;
        deviceList.BorderStyle = BorderStyle.None;
        deviceList.DrawMode = DrawMode.OwnerDrawFixed;
        deviceList.ItemHeight = 104;
        deviceList.IntegralHeight = false;
        deviceList.AccessibleName = "可用设备";
        deviceList.DrawItem += DrawDevice;
        deviceList.SelectedIndexChanged += (_, _) => UpdateControls();
        deviceList.DoubleClick += async (_, _) => await OpenMirrorAsync();
        emptyDevices.Text = "还没有连接设备\n\n插入 USB，或使用上方局域网连接";
        emptyDevices.Dock = DockStyle.Fill;
        emptyDevices.ForeColor = Ui.Muted;
        emptyDevices.TextAlign = ContentAlignment.MiddleCenter;
        deviceHost.Controls.AddRange([deviceList, emptyDevices]);
        deviceArea.Controls.Add(deviceHost, 0, 3);
        connectionHint.Dock = DockStyle.Fill;
        connectionHint.ForeColor = Ui.Muted;
        connectionHint.Font = new Font(Font.FontFamily, 8.5f);
        connectionHint.TextAlign = ContentAlignment.MiddleLeft;
        deviceArea.Controls.Add(connectionHint, 0, 4);
        var connectionButtons = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        connectionButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectionButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectButton = Ui.Button("打开操作窗口", 250, true);
        connectButton.Icon = UiIcon.Display;
        connectButton.Dock = DockStyle.Fill;
        connectButton.Margin = new Padding(0, 4, 0, 4);
        connectButton.Click += async (_, _) => await OpenMirrorAsync();
        disconnectButton = Ui.Button("断开连接", 250);
        disconnectButton.Dock = DockStyle.Fill;
        disconnectButton.Margin = new Padding(0, 4, 0, 0);
        disconnectButton.Click += async (_, _) => { disconnectTask = DisconnectAsync(); await disconnectTask; };
        connectionButtons.Controls.Add(connectButton, 0, 0);
        connectionButtons.Controls.Add(disconnectButton, 0, 1);
        deviceArea.Controls.Add(connectionButtons, 0, 5);

        var settingsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        body.Controls.Add(settingsScroll, 1, 0);
        var settings = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Height = 660, Margin = Padding.Empty };
        foreach (var height in new[] { 228f, 242f, 190f }) settings.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        settingsScroll.Controls.Add(settings);
        var displayCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        settings.Controls.Add(displayCard, 0, 0);
        var displayLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
        displayLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        displayLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        foreach (var height in new[] { 38f, 24f, 66f, 36f }) displayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        displayCard.Controls.Add(displayLayout);
        var displayHeading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        displayHeading.Controls.Add(new IconBadge(UiIcon.Display) { Size = new Size(32, 32) });
        displayHeading.Controls.Add(new Label { Text = "投屏设置", Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold), AutoSize = true, Location = new Point(44, 5) });
        displayLayout.Controls.Add(displayHeading, 0, 0);
        displayLayout.SetColumnSpan(displayHeading, 2);
        displayLayout.Controls.Add(new Label { Text = "画质与帧率", Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        displayLayout.Controls.Add(new Label { Text = "手机声音播放位置", Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(12, 0, 0, 0) }, 1, 1);
        quality.Dock = DockStyle.Fill;
        quality.Margin = new Padding(0, 2, 8, 0);
        displayLayout.Controls.Add(quality, 0, 2);
        soundOutput.Dock = DockStyle.Fill;
        soundOutput.Margin = new Padding(12, 2, 0, 0);
        displayLayout.Controls.Add(soundOutput, 1, 2);
        quality.SelectedIndexChanged += async (_, _) => await ApplyMirrorSettingsAsync();
        soundOutput.SelectedIndexChanged += async (_, _) => await ApplyMirrorSettingsAsync();
        settingsHint.Dock = DockStyle.Fill;
        settingsHint.ForeColor = Ui.Muted;
        settingsHint.Font = new Font(Font.FontFamily, 8.5f);
        settingsHint.TextAlign = ContentAlignment.MiddleLeft;
        displayLayout.Controls.Add(settingsHint, 0, 3);
        displayLayout.SetColumnSpan(settingsHint, 2);

        var audioCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        settings.Controls.Add(audioCard, 0, 1);
        var audioLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        foreach (var height in new[] { 38f, 24f, 38f, 42f, 44f }) audioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        audioCard.Controls.Add(audioLayout);
        var audioHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        audioHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        audioHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        audioHeading.Controls.Add(new IconBadge(UiIcon.Audio) { Size = new Size(32, 32), Margin = Padding.Empty }, 0, 0);
        computerAudioToggle.Text = "电脑声音 → 手机";
        computerAudioToggle.AccessibleName = "电脑声音传到手机";
        computerAudioToggle.Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold);
        computerAudioToggle.Dock = DockStyle.Fill;
        computerAudioToggle.Margin = Padding.Empty;
        computerAudioToggle.CheckedChanged += (_, _) =>
        {
            if (resettingAudioToggle) return;
            audioBusy = true;
            if (computerAudioToggle.Checked && deviceList.SelectedItem is AndroidDevice { State: "device" } device)
            {
                audioCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var source = (AudioSource)computerAudioSource.SelectedItem!;
                computerAudio = new AudioShareSession(bridge, device.Serial, source.Id);
                computerAudioTask = RunComputerAudioAsync(computerAudio, device, source.Name, audioCancellation.Token);
            }
            else
            {
                computerAudioStatus.Text = "正在关闭电脑音频…";
                audioCancellation?.Cancel();
            }
            UpdateControls();
        };
        audioHeading.Controls.Add(computerAudioToggle, 1, 0);
        audioLayout.Controls.Add(audioHeading, 0, 0);
        audioLayout.Controls.Add(new Label { Text = "电脑音频来源", Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        computerAudioSource.DropDownStyle = ComboBoxStyle.DropDownList;
        computerAudioSource.DisplayMember = nameof(AudioSource.Name);
        computerAudioSource.AccessibleName = "电脑音频来源";
        computerAudioSource.Dock = DockStyle.Fill;
        computerAudioSource.Margin = new Padding(0, 2, 0, 0);
        audioLayout.Controls.Add(computerAudioSource, 0, 2);
        var audioActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        installAudioButton = Ui.Button("初始化播放驱动", 168);
        installAudioButton.Height = 32;
        installAudioButton.Margin = new Padding(0, 0, 8, 0);
        installAudioButton.Click += async (_, _) => await InitializeAudioDriverAsync();
        var windowsAudioButton = Ui.Button("Windows 声音设置", 168);
        windowsAudioButton.Height = 32;
        windowsAudioButton.Margin = Padding.Empty;
        windowsAudioButton.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        audioActions.Controls.AddRange([installAudioButton, windowsAudioButton]);
        audioLayout.Controls.Add(audioActions, 0, 3);
        computerAudioStatus.Text = "独立传音，无需打开投屏\n手机播放自身声音时，可同时播放电脑声音";
        computerAudioStatus.ForeColor = Ui.Muted;
        computerAudioStatus.Dock = DockStyle.Fill;
        computerAudioStatus.AutoEllipsis = true;
        computerAudioStatus.TextAlign = ContentAlignment.MiddleLeft;
        computerAudioStatus.Font = new Font(Font.FontFamily, 8.8f);
        audioLayout.Controls.Add(computerAudioStatus, 0, 4);

        var fileCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        settings.Controls.Add(fileCard, 0, 2);
        var fileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        foreach (var height in new[] { 38f, 70f, 26f }) fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        fileCard.Controls.Add(fileLayout);
        var fileHeading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        fileHeading.Controls.Add(new IconBadge(UiIcon.Folder) { Size = new Size(32, 32) });
        fileHeading.Controls.Add(new Label { Text = "手机内容", Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold), AutoSize = true, Location = new Point(44, 5) });
        var contentActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 324, WrapContents = false, Margin = Padding.Empty };
        browseFilesButton = Ui.Button("浏览文件", 108);
        albumButton = Ui.Button("相册", 82);
        appsButton = Ui.Button("手机应用", 110);
        foreach (var button in new[] { browseFilesButton, albumButton, appsButton }) button.Height = 32;
        browseFilesButton.Click += (_, _) => OpenFiles(false);
        albumButton.Click += (_, _) => OpenFiles(true);
        appsButton.Click += (_, _) => OpenApps();
        contentActions.Controls.AddRange([browseFilesButton, albumButton, appsButton]);
        fileHeading.Controls.Add(contentActions);
        fileLayout.Controls.Add(fileHeading, 0, 0);
        var dropZone = new SurfacePanel { Dock = DockStyle.Fill, FillColor = Color.FromArgb(247, 250, 255), EdgeColor = Color.FromArgb(199, 215, 242), Dashed = true, AllowDrop = true, Padding = new Padding(16, 10, 12, 10), Margin = Padding.Empty };
        var dropLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        dropLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dropLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        var dropText = new Label { Text = "把文件拖到这里\n发送至手机 Download 文件夹", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Muted, Margin = Padding.Empty };
        uploadButton = Ui.Button("发送文件", 126);
        uploadButton.Anchor = AnchorStyles.None;
        uploadButton.Icon = UiIcon.Folder;
        uploadButton.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "选择要发送到设备的文件", Multiselect = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await UploadFilesAsync(dialog.FileNames);
        };
        dropLayout.Controls.Add(dropText, 0, 0);
        dropLayout.Controls.Add(uploadButton, 1, 0);
        dropZone.Controls.Add(dropLayout);
        foreach (var control in new Control[] { dropZone, dropLayout, dropText })
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) => e.Effect = deviceList.SelectedItem is AndroidDevice { State: "device" } && !transferring &&
                e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            control.DragDrop += async (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await UploadFilesAsync(files);
            };
        }
        fileLayout.Controls.Add(dropZone, 0, 1);
        transferStatus.Text = "选中设备即可传文件、查看相册和打开应用，无需先投屏";
        transferStatus.ForeColor = Ui.Muted;
        transferStatus.Dock = DockStyle.Fill;
        transferStatus.AutoEllipsis = true;
        transferStatus.TextAlign = ContentAlignment.MiddleLeft;
        transferStatus.Font = new Font(Font.FontFamily, 8.5f);
        fileLayout.Controls.Add(transferStatus, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
        status.Text = "正在发现设备…";
        status.Dock = DockStyle.Fill;
        status.ForeColor = Ui.Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        status.Font = new Font(Font.FontFamily, 8.8f);
        footer.Controls.Add(status);

        root.Controls.Add(footer, 0, 2);
        trayMenu.Items.Add(new ToolStripMenuItem("打开主窗口", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); }) { Tag = UiIcon.Home });
        var showMirror = trayMenu.Items.Add("显示投屏窗口", null, (_, _) =>
        {
            if (operationWindow is null) return;
            operationWindow.WindowState = FormWindowState.Normal;
            operationWindow.Activate();
        });
        showMirror.Tag = UiIcon.Display;
        trayMenu.Opening += (_, _) => showMirror.Enabled = operationWindow is not null;
        trayMenu.Items.Add(new ToolStripMenuItem("收起到托盘", null, (_, _) => Hide()) { Tag = UiIcon.Minimize });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("退出星语互联", null, (_, _) => Close()) { Tag = UiIcon.Power });
        trayIcon = new NotifyIcon { Text = "星语互联", Icon = BrandAssets.WindowIcon, ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        AcceptButton = connectButton;
        deviceTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        Shown += async (_, _) =>
        {
            try
            {
                if (File.Exists(settingsPath)) preferredSerial = (await File.ReadAllTextAsync(settingsPath, lifetime.Token)).Trim();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("读取上次设备失败：" + ex.Message); }
            await RefreshDevicesAsync();
            if (!lifetime.IsCancellationRequested) deviceTimer.Start();
        };
        UpdateControls();
    }

    private void DrawDevice(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var device = (AndroidDevice)deviceList.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var scale = e.Bounds.Height / 104f;
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillRectangle(Brushes.White, e.Bounds);
        using var shape = Ui.RoundedRectangle(new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 4 * scale, e.Bounds.Width - 3, e.Bounds.Height - 8 * scale), 10 * scale);
        using var fill = new SolidBrush(selected ? Ui.AccentLight : Color.FromArgb(249, 251, 254));
        using var edge = new Pen(selected ? Color.FromArgb(185, 207, 251) : Color.FromArgb(237, 241, 247));
        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);
        Ui.DrawIcon(graphics, UiIcon.Devices, new RectangleF(e.Bounds.X + 15 * scale, e.Bounds.Y + 25 * scale, 32 * scale, 38 * scale), selected ? Ui.Accent : Ui.Muted);
        var left = e.Bounds.X + (int)(62 * scale);
        using var titleFont = new Font(Font.FontFamily, 10, FontStyle.Bold);
        using var detailFont = new Font(Font.FontFamily, 8.5f);
        TextRenderer.DrawText(graphics, device.DisplayName, titleFont,
            new Rectangle(left, e.Bounds.Y + (int)(16 * scale), e.Bounds.Width - (int)(72 * scale), (int)(24 * scale)), Ui.Ink, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, device.Serial, detailFont,
            new Rectangle(left, e.Bounds.Y + (int)(43 * scale), e.Bounds.Width - (int)(72 * scale), (int)(20 * scale)), Ui.Muted, TextFormatFlags.EndEllipsis);
        var stateColor = device.State == "device" ? Ui.Success : Color.FromArgb(180, 118, 36);
        using var dot = new SolidBrush(stateColor);
        graphics.FillEllipse(dot, left + 2 * scale, e.Bounds.Y + 77 * scale, 6 * scale, 6 * scale);
        TextRenderer.DrawText(graphics, device.Connection + " · " + device.StateText, detailFont,
            new Rectangle(left + (int)(13 * scale), e.Bounds.Y + (int)(70 * scale), e.Bounds.Width - (int)(85 * scale), (int)(23 * scale)), stateColor, TextFormatFlags.EndEllipsis);
    }

    private async Task RefreshDevicesAsync()
    {
        if (scanning || lifetime.IsCancellationRequested) return;
        scanning = true;
        try
        {
            RefreshAudioSources();
            var devices = await bridge.GetDevicesAsync(lifetime.Token);
            deviceCount.Text = devices.Count == 0 ? "等待设备接入" : $"{devices.Count} 个连接 · 自动发现";
            lastScanError = null;
            if (!deviceList.Items.Cast<AndroidDevice>().SequenceEqual(devices))
            {
                var selected = (deviceList.SelectedItem as AndroidDevice)?.Serial ?? preferredSerial;
                deviceList.BeginUpdate();
                deviceList.Items.Clear();
                deviceList.Items.AddRange(devices.Cast<object>().ToArray());
                var index = devices.FindIndex(device => device.Serial == selected);
                deviceList.SelectedIndex = index >= 0 ? index : devices.Count > 0 ? 0 : -1;
                deviceList.EndUpdate();
            }
            if (operationWindow is null)
                status.Text = devices.Count == 0 ? "未发现设备 · 可用 USB 或点击“局域网连接”" : $"已发现 {devices.Count} 个设备连接 · 双击设备打开操作窗口";
            UpdateControls();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            status.Text = "设备扫描失败 · " + ex.Message;
            if (lastScanError != ex.Message) AppLog.Write(status.Text);
            lastScanError = ex.Message;
        }
        finally { scanning = false; }
    }

    private async Task ApplyMirrorSettingsAsync()
    {
        var revision = ++mirrorSettingsRevision;
        var window = operationWindow;
        if (window is null) return;
        if (window.IsAppWindow) return;
        try
        {
            await Task.Delay(180, lifetime.Token);
            if (revision != mirrorSettingsRevision || operationWindow != window) return;
            var presentation = window.Presentation;
            await OpenMirrorAsync(presentation, window.App, false, targetDevice: window.Device, forceRestart: true);
            AppLog.Write("正在应用投屏设置：" + quality.Controls[quality.SelectedIndex].Text + " / " + soundOutput.Controls[soundOutput.SelectedIndex].Text);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task DisconnectAsync()
    {
        if (disconnecting) return;
        disconnecting = true;
        ++mirrorOpenRevision;
        ++mirrorSettingsRevision;
        var window = operationWindow;
        UpdateControls();
        try
        {
            window?.Close();
            if (computerAudio is not null) computerAudioStatus.Text = "正在关闭电脑音频…";
            audioCancellation?.Cancel();
            await Task.WhenAll(window?.Completion ?? Task.CompletedTask, computerAudioTask);
            if (!lifetime.IsCancellationRequested) status.Text = "投屏与电脑传音已断开 · 可以重新选择设备";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (!lifetime.IsCancellationRequested)
            {
                status.Text = "断开未完成 · 请查看运行日志";
                AppLog.Write(ex.Message);
            }
        }
        finally
        {
            disconnecting = false;
            if (!lifetime.IsCancellationRequested) UpdateControls();
        }
    }

    private async Task OpenMirrorAsync(MirrorPresentation? presentation = null, PhoneApp? app = null, bool independent = true,
        AndroidDevice? targetDevice = null, AppPreferences? preferences = null, bool forceRestart = false)
    {
        if (disconnecting || lifetime.IsCancellationRequested) return;
        if ((targetDevice ?? deviceList.SelectedItem) is not AndroidDevice { State: "device" } device) return;
        var request = ++mirrorOpenRevision;
        if (operationWindow is not null)
        {
            if (!forceRestart && !operationWindow.Completion.IsCompleted && operationWindow.Serial == device.Serial && operationWindow.App?.PackageName == app?.PackageName && operationWindow.IsAppWindow == (app is not null && independent))
            {
                if (operationWindow.WindowState == FormWindowState.Minimized) operationWindow.WindowState = FormWindowState.Normal;
                operationWindow.Activate();
                return;
            }
            var previous = operationWindow;
            previous.Close();
            await previous.Completion;
            if (lifetime.IsCancellationRequested || request != mirrorOpenRevision) return;
        }
        var window = new MirrorForm(bridge, device, quality.SelectedIndex, soundOutput.SelectedIndex, presentation, app, independent);
        operationWindow = window;
        window.LogMessage += AppLog.Write;
        window.ConnectionChanged += () =>
        {
            if (operationWindow != window) return;
            status.Text = window.IsConnected ? "已连接 · " + device.Serial + " · 在独立操作窗口中使用" : "连接未建立 · 请查看运行日志";
            UpdateControls();
        };
        window.FormClosed += (_, _) =>
        {
            if (operationWindow != window) return;
            operationWindow = null;
            if (lifetime.IsCancellationRequested) return;
            status.Text = "操作窗口已关闭 · 可以重新选择设备";
            UpdateControls();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        };
        var fallingBack = false;
        window.FallbackRequested += async reason =>
        {
            if (fallingBack || lifetime.IsCancellationRequested || operationWindow != window || app is null) return;
            fallingBack = true;
            AppLog.Write(app.Name + " · 回退普通投屏：" + reason);
            if (preferences is not null)
            {
                try { await preferences.SaveAsync(app.PackageName, null, true, lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("未能保存应用打开方式：" + ex.Message); }
            }
            if (operationWindow != window || lifetime.IsCancellationRequested) return;
            foreach (var appsWindow in contentWindows.OfType<PhoneAppsForm>()) appsWindow.RenderApps();
            if (!lifetime.IsCancellationRequested) await OpenMirrorAsync(app: app, independent: false, targetDevice: device, preferences: preferences);
        };
        window.Show();
        status.Text = "正在打开独立操作窗口 · " + device.Serial;
        UpdateControls();
        preferredSerial = device.Serial;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            await File.WriteAllTextAsync(settingsPath, device.Serial, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("保存上次设备失败：" + ex.Message); }
    }

    private void OpenFiles(bool album)
    {
        if (deviceList.SelectedItem is not AndroidDevice { State: "device" } device) return;
        var window = new PhoneFilesForm(bridge, device, album);
        contentWindows.Add(window);
        window.LogMessage += AppLog.Write;
        window.FormClosed += (_, _) => contentWindows.Remove(window);
        window.Show();
    }

    private void OpenApps()
    {
        if (deviceList.SelectedItem is not AndroidDevice { State: "device" } device) return;
        var existing = contentWindows.OfType<PhoneAppsForm>().FirstOrDefault(window => window.Serial == device.Serial);
        if (existing is not null)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new PhoneAppsForm(bridge, device);
        contentWindows.Add(window);
        window.FormClosed += (_, _) => contentWindows.Remove(window);
        window.OpenRequested += async (app, independent) => await OpenMirrorAsync(app: app, independent: independent, targetDevice: device, preferences: window.Preferences);
        window.Show();
    }

    private async Task OpenWirelessConnectionAsync()
    {
        deviceTimer.Stop();
        try
        {
            using var dialog = new WirelessConnectionForm(bridge, deviceList.SelectedItem as AndroidDevice);
            dialog.LogMessage += AppLog.Write;
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ConnectedSerial is not string serial) return;
            while (scanning) await Task.Delay(50, lifetime.Token);
            await RefreshDevicesAsync();
            var device = deviceList.Items.Cast<AndroidDevice>().FirstOrDefault(device => device.Serial == serial && device.State == "device");
            if (device is null)
            {
                status.Text = "无线连接已不可用 · 请核对网络后重连";
                return;
            }
            deviceList.SelectedItem = device;
            preferredSerial = serial;
            status.Text = "局域网已连接 · " + serial + " · 可拔掉 USB，再打开操作窗口或传音";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                await File.WriteAllTextAsync(settingsPath, serial, lifetime.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("保存上次设备失败：" + ex.Message); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (!lifetime.IsCancellationRequested) deviceTimer.Start();
        }
    }

    private async Task UploadFilesAsync(string[] files)
    {
        if (deviceList.SelectedItem is not AndroidDevice { State: "device" } device || transferring) return;
        var content = new PhoneContentService(bridge, device.Serial);
        transferring = true;
        transferCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateControls();
        try
        {
            for (var index = 0; index < files.Length; index++)
            {
                if (!File.Exists(files[index])) throw new IOException("请拖入文件；当前版本不支持文件夹：" + files[index]);
                transferStatus.Text = $"发送中 {index + 1}/{files.Length} · {Path.GetFileName(files[index])}";
                var remotePath = "/sdcard/Download/" + Path.GetFileName(files[index]);
                await content.UploadAsync(files[index], remotePath, null, lifetime.Token);
                AppLog.Write("已发送：" + remotePath);
            }
            transferStatus.Text = $"已发送 {files.Length} 个文件到 Download";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            transferStatus.Text = "发送失败，请查看运行日志";
            AppLog.Write(ex.Message);
            if (!lifetime.IsCancellationRequested) MessageBox.Show(this, ex.Message, "文件发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            transferring = false;
            if (!lifetime.IsCancellationRequested) UpdateControls();
            transferCompletion.SetResult();
        }
    }

    private void UpdateControls()
    {
        var selected = deviceList.SelectedItem as AndroidDevice;
        var currentSelected = operationWindow is not null && operationWindow.Serial == selected?.Serial;
        var showCurrent = currentSelected && !operationWindow!.Completion.IsCompleted;
        connectButton.Enabled = !disconnecting && (showCurrent || selected is { State: "device" });
        connectButton.Text = currentSelected ? showCurrent ? "显示操作窗口" : "重新连接" :
            operationWindow is not null ? "切换操作设备" : "打开操作窗口";
        disconnectButton.Enabled = !disconnecting && (operationWindow is not null || computerAudio is not null);
        disconnectButton.Text = disconnecting ? "正在断开…" : "断开连接";
        deviceList.Visible = deviceList.Items.Count > 0;
        emptyDevices.Visible = deviceList.Items.Count == 0;
        wirelessButton.Enabled = !disconnecting && operationWindow is null && computerAudio is null;
        connectionHint.Text = wirelessButton.Enabled ? "支持 USB · Wi-Fi · Android 模拟器" : "设备可直接选择 · 改网络请先断开";
        computerAudioToggle.Enabled = !disconnecting && !audioBusy && !installingAudioDevice && computerAudioSource.SelectedItem is AudioSource &&
            (computerAudio is not null || deviceList.SelectedItem is AndroidDevice { State: "device" });
        computerAudioSource.Enabled = computerAudio is null && !installingAudioDevice;
        installAudioButton.Enabled = computerAudio is null && !installingAudioDevice;
        var deviceReady = deviceList.SelectedItem is AndroidDevice { State: "device" };
        uploadButton.Enabled = deviceReady && !transferring;
        browseFilesButton.Enabled = albumButton.Enabled = appsButton.Enabled = deviceReady;
        quality.Enabled = soundOutput.Enabled = operationWindow is not { IsAppWindow: true };
        settingsHint.Text = operationWindow is { IsAppWindow: true } ? "应用独立窗口沿用启动时设置 · 关闭窗口后可调整画质与声音" : "选择后自动应用 · 投屏保持唤醒，操作窗口可单独息屏";
    }

    private void RefreshAudioSources()
    {
        if (computerAudio is not null) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var sources = new List<AudioSource> { new(null, "镜像 Windows 默认输出", false) };
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (endpoint)
                {
                    var isVirtual = endpoint.DeviceFriendlyName == "VB-Audio Virtual Cable";
                    var initialized = isVirtual && Equals(endpoint.Properties[PropertyKeys.PKEY_Device_DeviceDesc].Value, AudioDriver.PlaybackName);
                    sources.Add(new AudioSource(endpoint.ID, initialized ? AudioDriver.PlaybackName : endpoint.FriendlyName, isVirtual));
                }
            }
            if (computerAudioSource.Items.Cast<AudioSource>().SequenceEqual(sources)) return;
            var previous = computerAudioSource.SelectedItem as AudioSource;
            var virtualSource = sources.FirstOrDefault(source => source.IsVirtual && source.Name == AudioDriver.PlaybackName) ??
                sources.FirstOrDefault(source => source.IsVirtual);
            computerAudioSource.Items.Clear();
            computerAudioSource.Items.AddRange(sources.Cast<object>().ToArray());
            computerAudioSource.SelectedItem = (previous is null || preferVirtualAudio ? virtualSource : null) ??
                sources.FirstOrDefault(source => source.Id == previous?.Id) ?? sources[0];
            if (virtualSource is not null) preferVirtualAudio = false;
        }
        catch (COMException ex)
        {
            computerAudioStatus.Text = "无法读取 Windows 音频设备：" + ex.Message;
        }
    }

    private async Task InitializeAudioDriverAsync()
    {
        installingAudioDevice = true;
        preferVirtualAudio = true;
        computerAudioStatus.Text = "正在初始化手机音频播放驱动…";
        UpdateControls();
        try
        {
            var source = computerAudioSource.Items.Cast<AudioSource>().FirstOrDefault(source => source.IsVirtual && source.Name == AudioDriver.PlaybackName) ??
                (computerAudioSource.SelectedItem is AudioSource { IsVirtual: true } selected ? selected : null) ??
                computerAudioSource.Items.Cast<AudioSource>().FirstOrDefault(source => source.IsVirtual);
            var exitCode = 0;
            if (source is not null) AudioDriver.Initialize(source.Id);
            else
            {
                var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
                };
                startInfo.ArgumentList.Add("--initialize-audio");
                using var initializer = Process.Start(startInfo)!;
                await initializer.WaitForExitAsync(lifetime.Token);
                exitCode = initializer.ExitCode;
            }
            RefreshAudioSources();
            var ready = computerAudioSource.Items.Cast<AudioSource>().FirstOrDefault(source => source.IsVirtual && source.Name == AudioDriver.PlaybackName);
            computerAudioStatus.Text = exitCode == 0 && ready is not null
                ? "播放驱动已就绪 · Windows 输出选择“手机音频”\n开启上方开关后，声音会传到所选手机"
                : "初始化未完成 · 请确认安装结果，按提示重启后重试";
            if (ready is not null) computerAudioSource.SelectedItem = ready;
            AppLog.Write("播放驱动初始化已退出，退出码：" + exitCode);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            computerAudioStatus.Text = "已取消播放驱动初始化";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or System.ComponentModel.Win32Exception)
        {
            computerAudioStatus.Text = "无法初始化播放驱动 · 请查看运行日志";
            AppLog.Write(ex.Message);
        }
        finally
        {
            installingAudioDevice = false;
            if (!lifetime.IsCancellationRequested) UpdateControls();
        }
    }

    private async Task RunComputerAudioAsync(AudioShareSession session, AndroidDevice device, string sourceName, CancellationToken cancellationToken)
    {
        using var priority = FileTransferBudget.ForDevice(bridge, device.Serial).ProtectInteractive();
        computerAudioStatus.Text = "正在连接电脑音频 · " + device.DisplayName;
        try
        {
            await session.StartAsync(cancellationToken);
            audioBusy = false;
            computerAudioStatus.Text = "已开启 · " + device.DisplayName + "\n音频来源：" + sourceName;
            AppLog.Write("AudioShare 2.2.2 已连接：" + sourceName + " → " + device.Serial);
            UpdateControls();
            await session.Completion.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            computerAudioStatus.Text = "电脑音频已关闭 · 手机声音设置保持不变";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            computerAudioStatus.Text = "电脑音频连接失败 · 请查看运行日志";
            AppLog.Write(ex.Message);
        }
        finally
        {
            try { await session.DisposeAsync(); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                AppLog.Write("清理音频通道：" + ex.Message);
            }
            computerAudio = null;
            audioCancellation!.Dispose();
            audioCancellation = null;
            audioBusy = false;
            resettingAudioToggle = true;
            computerAudioToggle.Checked = false;
            resettingAudioToggle = false;
            if (!lifetime.IsCancellationRequested) UpdateControls();
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (closing) { e.Cancel = true; return; }
        deviceTimer.Stop();
        lifetime.Cancel();
        var cleanupTasks = contentWindows.OfType<PhoneFilesForm>().Select(window => window.ActiveOperation).ToList();
        cleanupTasks.Add(computerAudioTask);
        cleanupTasks.Add(disconnectTask);
        cleanupTasks.Add(transferCompletion?.Task ?? Task.CompletedTask);
        if (operationWindow is not null) cleanupTasks.Add(operationWindow.Completion);
        operationWindow?.Close();
        foreach (var window in contentWindows.ToArray()) window.Close();
        if (cleanupTasks.Any(task => !task.IsCompleted))
        {
            e.Cancel = true;
            closing = true;
            await Task.WhenAll(cleanupTasks);
            closing = false;
            Close();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { trayIcon.Dispose(); trayMenu.Dispose(); deviceTimer.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
