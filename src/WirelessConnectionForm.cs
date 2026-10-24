namespace XingyuConnect;

internal sealed record WirelessService(string Name, string Type, string Endpoint)
{
    public string DisplayName => $"{(Type == "_adb-tls-pairing._tcp" ? "配对" : "连接")} · {Endpoint} · {Name}";
}

internal sealed class WirelessConnectionForm : ModernForm
{
    private readonly DeviceBridge bridge;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ModernTabs tabs = new() { Dock = DockStyle.Fill, Enabled = false };
    private readonly ModernComboBox usbDevices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ModernComboBox connectionAddress = new() { Dock = DockStyle.Fill, AccessibleName = "无线连接地址" };
    private readonly TextBox pairingAddress = new() { Dock = DockStyle.Fill, PlaceholderText = "例如 192.168.0.45:37123", AccessibleName = "配对地址" };
    private readonly TextBox pairingCode = new() { Dock = DockStyle.Fill, MaxLength = 6, PlaceholderText = "6 位配对码", AccessibleName = "配对码" };
    private readonly ListBox services = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DisplayMember = nameof(WirelessService.DisplayName) };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted };
    private readonly string historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "wireless-devices.txt");
    private bool busy;

    public string? ConnectedSerial { get; private set; }
    public event Action<string>? LogMessage;

    public WirelessConnectionForm(DeviceBridge bridge, AndroidDevice? selectedDevice)
    {
        this.bridge = bridge;
        Text = "星语互联 · 局域网连接";
        Icon = BrandAssets.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ClientSize = new Size(760, 660);
        MinimumSize = new Size(740, 680);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 4 };
        foreach (var height in new[] { 100f, 0f, 96f, 44f })
            root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        heading.Controls.Add(CreateWindowActions());
        heading.Controls.Add(new IconBadge(UiIcon.Wifi, true) { Location = new Point(0, 6), Size = new Size(44, 44) });
        heading.Controls.Add(new Label { Text = "连接，让距离更近", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Location = new Point(58, 4) });
        heading.Controls.Add(new Label { Text = "手机和电脑连接同一路由器，电脑也可使用网线。", ForeColor = Ui.Muted, AutoSize = true, Location = new Point(60, 46) });
        EnableWindowDrag(heading.Controls.Cast<Control>().Where(control => control is Label or IconBadge).Prepend(heading).ToArray());
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(status, 0, 2);
        var closeArea = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Ui.Button("关闭", 90);
        close.Margin = Padding.Empty;
        close.DialogResult = DialogResult.Cancel;
        closeArea.Controls.Add(close);
        root.Controls.Add(closeArea, 0, 3);
        CancelButton = close;
        var usbTab = new TabPage("USB 一键转无线") { BackColor = Ui.Background };
        var pairTab = new TabPage("无线调试配对") { BackColor = Ui.Background };
        var directTab = new TabPage("直接连接 / 发现") { BackColor = Ui.Background };
        tabs.TabPages.AddRange([usbTab, pairTab, directTab]);
        var usbSurface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(22) };
        var pairSurface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(22) };
        var directSurface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(22) };
        usbTab.Controls.Add(usbSurface);
        pairTab.Controls.Add(pairSurface);
        directTab.Controls.Add(directSurface);

        var usbLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        foreach (var height in new[] { 112f, 46f, 50f, 0f })
            usbLayout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        usbSurface.Controls.Add(usbLayout);
        usbLayout.Controls.Add(new Label
        {
            Text = "适合没有“无线调试”入口的手机。\n1. 首次用 USB 连接并允许调试；手机连接 Wi-Fi。\n2. 点击下方按钮，软件读取 Wi-Fi 地址并启用 ADB TCP/IP 5555。\n3. 成功后可拔掉 USB；重启手机后可能需要重新启用。",
            Dock = DockStyle.Fill
        }, 0, 0);
        var usbRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        usbRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        usbRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        usbDevices.DisplayMember = nameof(AndroidDevice.DisplayName);
        usbDevices.AccessibleName = "待转无线的 USB 手机";
        usbDevices.Margin = new Padding(0, 8, 8, 0);
        usbRow.Controls.Add(usbDevices, 0, 0);
        var refreshUsb = Ui.Button("刷新", 84);
        refreshUsb.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await LoadUsbDevicesAsync(selectedDevice?.Serial);
            return null;
        }, "正在查找 USB 手机…");
        usbRow.Controls.Add(refreshUsb, 1, 0);
        usbLayout.Controls.Add(usbRow, 0, 1);
        var convert = Ui.Button("切换并连接无线", 200, true);
        convert.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            if (usbDevices.SelectedItem is not AndroidDevice device)
                throw new ArgumentException("未找到可用的 USB 真机，请连接手机并允许 USB 调试。");
            return await bridge.SwitchUsbToWirelessAsync(device, lifetime.Token);
        }, "正在读取 Wi-Fi 地址、启用 TCP/IP 并核对设备…");
        usbLayout.Controls.Add(convert, 0, 2);
        usbLayout.Controls.Add(new Label { Text = "仅在可信局域网使用。此方式会重启手机 ADB；请先关闭投屏和传音。", Dock = DockStyle.Fill, ForeColor = Ui.Muted }, 0, 3);

        var pairLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 100f, 24f, 36f, 36f, 50f, 0f })
            pairLayout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        pairSurface.Controls.Add(pairLayout);
        pairLayout.Controls.Add(new Label
        {
            Text = "Android 11+ 且系统提供“无线调试”时，无需 USB。\n在手机“开发者选项 → 无线调试”中，打开“使用配对码配对设备”。\n填入该弹窗的 IP:端口和配对码，保持弹窗打开。",
            Dock = DockStyle.Fill
        }, 0, 0);
        pairLayout.Controls.Add(new Label { Text = "配对地址（包含配对端口）", Dock = DockStyle.Fill, ForeColor = Ui.Muted }, 0, 1);
        pairLayout.Controls.Add(pairingAddress, 0, 2);
        pairLayout.Controls.Add(pairingCode, 0, 3);
        var pair = Ui.Button("配对此电脑", 160, true);
        pair.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await bridge.PairWirelessAsync(pairingAddress.Text, pairingCode.Text.Trim(), lifetime.Token);
            pairingCode.Clear();
            status.Text = "配对成功。回到手机“无线调试”主页面，使用其 IP:端口连接，或在“直接连接 / 发现”中刷新。";
            LogMessage?.Invoke("无线调试配对成功：" + pairingAddress.Text.Trim());
            tabs.SelectedTab = directTab;
            return null;
        }, "正在配对，请保持手机上的配对码弹窗打开…");
        pairLayout.Controls.Add(pair, 0, 4);
        pairLayout.Controls.Add(new Label { Text = "配对端口 ≠ 连接端口。配对成功后还需连接；配对码不会保存。", Dock = DockStyle.Fill, ForeColor = Ui.Muted }, 0, 5);

        var directLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        foreach (var height in new[] { 54f, 40f, 50f, 34f, 0f })
            directLayout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        directSurface.Controls.Add(directLayout);
        directLayout.Controls.Add(new Label { Text = "输入已启用 ADB 的手机地址，或从历史记录中选择。\n无线调试使用主页面连接端口；USB 转无线默认使用 5555。", Dock = DockStyle.Fill }, 0, 0);
        connectionAddress.DropDownStyle = ComboBoxStyle.DropDown;
        connectionAddress.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        connectionAddress.AutoCompleteSource = AutoCompleteSource.ListItems;
        directLayout.Controls.Add(connectionAddress, 0, 1);
        var networkActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var connect = Ui.Button("连接地址", 140, true);
        connect.Click += async (_, _) => await RunOperationAsync(
            async () => await bridge.ConnectWirelessAsync(connectionAddress.Text, lifetime.Token), "正在连接并检查 ADB 状态…");
        var discover = Ui.Button("发现无线调试设备", 180);
        discover.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            var output = await bridge.RunAsync(["mdns", "services"], lifetime.Token);
            services.Items.Clear();
            foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 3) continue;
                var type = fields[1].TrimEnd('.');
                if (type is "_adb-tls-pairing._tcp" or "_adb-tls-connect._tcp" or "_adb._tcp")
                {
                    var service = new WirelessService(fields[0], type, fields[2]);
                    if (!services.Items.Contains(service)) services.Items.Add(service);
                }
            }
            status.Text = services.Items.Count == 0
                ? "未发现广播。请打开手机无线调试；路由器可能限制 mDNS，可直接输入 IP:端口连接。"
                : "选择发现的条目会填入对应地址。配对广播会跳到配对页；连接广播仍需点击“连接地址”。";
            return null;
        }, "正在读取 ADB mDNS 广播…");
        networkActions.Controls.AddRange([connect, discover]);
        directLayout.Controls.Add(networkActions, 0, 2);
        directLayout.Controls.Add(new Label { Text = "发现的设备（支持广播的无线调试）", Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.BottomLeft }, 0, 3);
        services.SelectedIndexChanged += (_, _) =>
        {
            if (services.SelectedItem is not WirelessService service) return;
            if (service.Type == "_adb-tls-pairing._tcp")
            {
                pairingAddress.Text = service.Endpoint;
                tabs.SelectedTab = pairTab;
                pairingCode.Focus();
            }
            else connectionAddress.Text = service.Endpoint;
        };
        directLayout.Controls.Add(services, 0, 4);

        Shown += async (_, _) => await RunOperationAsync(async () =>
        {
            try
            {
                if (File.Exists(historyPath)) connectionAddress.Items.AddRange(await File.ReadAllLinesAsync(historyPath, lifetime.Token));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogMessage?.Invoke("读取无线地址记录失败：" + ex.Message); }
            if (selectedDevice?.Connection == "无线") connectionAddress.Text = selectedDevice.Serial.Contains(':') ? selectedDevice.Serial : "";
            else if (connectionAddress.Items.Count > 0) connectionAddress.SelectedIndex = 0;
            await LoadUsbDevicesAsync(selectedDevice?.Serial);
            if (usbDevices.Items.Count == 0 || selectedDevice?.Connection == "无线") tabs.SelectedTab = directTab;
            return null;
        }, "正在读取设备和历史地址…");
    }

    private async Task LoadUsbDevicesAsync(string? selectedSerial)
    {
        var devices = await bridge.GetDevicesAsync(lifetime.Token);
        usbDevices.Items.Clear();
        usbDevices.Items.AddRange(devices.Where(device => device.Connection == "USB" && device.State == "device").Cast<object>().ToArray());
        if (usbDevices.Items.Count > 0)
            usbDevices.SelectedItem = usbDevices.Items.Cast<AndroidDevice>().FirstOrDefault(device => device.Serial == selectedSerial) ?? usbDevices.Items[0];
        status.Text = usbDevices.Items.Count == 0
            ? "未找到已授权的 USB 真机。也可以直接连接已启用无线调试的手机。"
            : "请选择一种方式连接。转无线成功后会自动选中无线设备，可拔掉 USB。";
    }

    private async Task RunOperationAsync(Func<Task<string?>> operation, string message)
    {
        if (busy) return;
        busy = true;
        tabs.Enabled = false;
        status.Text = message;
        try
        {
            var serial = await operation();
            if (serial is null) return;
            ConnectedSerial = serial;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
                var history = new[] { serial }.Concat(connectionAddress.Items.Cast<string>()).Distinct().Take(10);
                await File.WriteAllLinesAsync(historyPath, history, lifetime.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogMessage?.Invoke("连接成功，但保存无线地址失败：" + ex.Message); }
            LogMessage?.Invoke("局域网连接已就绪：" + serial);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ArgumentException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            status.Text = "连接未完成：" + ex.Message + "\n请核对手机调试授权、IP 和端口；访客 Wi-Fi / AP 隔离可能阻止设备互通。";
            LogMessage?.Invoke(status.Text);
        }
        finally
        {
            busy = false;
            if (!lifetime.IsCancellationRequested) tabs.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        lifetime.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lifetime.Dispose();
        base.Dispose(disposing);
    }
}
