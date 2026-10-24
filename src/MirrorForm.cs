using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace XingyuConnect;

internal sealed record MirrorPresentation(Rectangle Bounds, FormWindowState WindowState, bool FullScreen, bool ScreenOff);

internal sealed class MirrorForm : ModernForm
{
    private readonly DeviceBridge bridge;
    private readonly AndroidDevice device;
    private readonly int quality;
    private readonly int soundOutput;
    private readonly MirrorPresentation? presentation;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken shutdown;
    private readonly MirrorHost mirror = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label status = new();
    private readonly Label empty = new();
    private readonly TableLayoutPanel layout = new();
    private readonly List<Button> deviceButtons = [];
    private readonly MirrorButton fullScreenButton;
    private readonly MirrorButton screenPowerButton;
    private readonly MirrorButton keyboardButton;
    private AdbKeyboardSession? keyboard;
    private DirectKeyboardOverlay? directKeyboard;
    private Task keyboardTask = Task.CompletedTask;
    private bool keyboardBusy;
    private readonly ToolTip buttonTips = new() { InitialDelay = 350, ReshowDelay = 100 };
    private readonly Panel caption;
    private readonly string captureDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "星语互联");
    private Process? scrcpy;
    private bool fullScreen;
    private bool deviceScreenOff;
    private Size videoSize;
    private Size sourceResolution;
    private bool fittingAspect;
    protected override bool IsFullScreen => fullScreen;
    private Rectangle savedBounds;
    private FormWindowState savedWindowState;

    public string Serial => device.Serial;
    public AndroidDevice Device => device;
    public PhoneApp? App { get; }
    public bool IsAppWindow { get; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsConnected { get; private set; }
    public MirrorPresentation Presentation => new(fullScreen ? savedBounds : Bounds,
        fullScreen ? savedWindowState : WindowState, fullScreen, deviceScreenOff);
    public event Action<string>? LogMessage;
    public event Action? ConnectionChanged;
    public event Action<string>? FallbackRequested;

    public MirrorForm(DeviceBridge bridge, AndroidDevice device, int quality, int soundOutput, MirrorPresentation? presentation = null, PhoneApp? app = null, bool independent = true)
    {
        this.bridge = bridge;
        this.device = device;
        this.quality = quality;
        this.soundOutput = soundOutput;
        this.presentation = presentation;
        App = app;
        IsAppWindow = app is not null && independent;
        deviceScreenOff = presentation?.ScreenOff ?? false;
        shutdown = lifetime.Token;
        Text = (app?.Name ?? device.DisplayName) + " · 星语互联";
        Icon = BrandAssets.WindowIcon;
        BackColor = Color.FromArgb(16, 23, 34);
        ForeColor = Color.FromArgb(216, 224, 236);
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(454, 940);
        MinimumSize = new Size(320, 460);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(1);
        DoubleBuffered = true;
        layout.Dock = DockStyle.Fill;
        layout.Margin = Padding.Empty;
        layout.ColumnCount = 1;
        layout.RowCount = 3;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(layout);
        caption = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(14, 3, 4, 3), BackColor = Color.FromArgb(20, 30, 44) };
        var title = new Label { Text = app is null ? device.DisplayName + "   ·   " + device.Connection : app.Name + (IsAppWindow ? " · 独立窗口" : " · 普通投屏"), Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9.5f), AutoEllipsis = true };
        var windowActions = CreateWindowActions(true);
        windowActions.Padding = Padding.Empty;
        var logo = new PictureBox { Image = BrandAssets.Logo, SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Left, Width = 30, Padding = new Padding(0, 5, 6, 5), TabStop = false };
        caption.Controls.Add(title);
        caption.Controls.Add(logo);
        caption.Controls.Add(windowActions);
        EnableWindowDrag(caption, title, logo);
        layout.Controls.Add(caption, 0, 0);
        status.Text = "正在连接 · " + device.Serial;
        status.Visible = false;
        Controls.Add(status);
        empty.Text = "正在连接\n\n请确认设备的 USB 调试授权";
        empty.Dock = DockStyle.Fill;
        empty.ForeColor = Color.FromArgb(123, 137, 158);
        empty.Font = new Font("Microsoft YaHei UI", 10);
        empty.TextAlign = ContentAlignment.MiddleCenter;
        mirror.Controls.Add(empty);
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        workspace.Controls.Add(mirror, 0, 0);
        layout.Controls.Add(workspace, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(8, 8, 0, 0), Margin = Padding.Empty, BackColor = Color.FromArgb(20, 30, 44) };
        workspace.Controls.Add(tools, 1, 0);
        var navigation = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = tools.BackColor };
        var primaryRow = new TableLayoutPanel { Size = new Size(246, 58), ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = tools.BackColor };
        for (var index = 0; index < 3; index++) primaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        navigation.Controls.Add(primaryRow);
        navigation.Resize += (_, _) => primaryRow.Location = new Point((navigation.ClientSize.Width - primaryRow.Width) / 2, 2);
        layout.Controls.Add(navigation, 0, 2);
        var commands = new[] { ("返回", "4", MirrorIcon.Back), ("主页", "3", MirrorIcon.Home), ("多任务", "187", MirrorIcon.Tasks) };
        for (var index = 0; index < commands.Length; index++)
        {
            var (label, key, icon) = commands[index];
            var button = new MirrorButton(label, icon, true) { Dock = DockStyle.Fill, BackColor = tools.BackColor, Margin = new Padding(4) };
            var shortcut = new ushort[] { 0x42, 0x48, 0x53 }[index];
            button.Click += async (_, _) =>
            {
                if (!IsConnected) return;
                if (IsAppWindow)
                {
                    try { await mirror.SendShortcutAsync(shortcut); }
                    catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { LogMessage?.Invoke(ex.Message); }
                }
                else await SendCommandAsync(["shell", "input", "keyevent", key]);
            };
            primaryRow.Controls.Add(button, index, 0);
            deviceButtons.Add(button);
        }
        fullScreenButton = new MirrorButton("全屏", MirrorIcon.Fullscreen) { Size = new Size(38, 38), Margin = new Padding(2, 2, 2, 12), BackColor = tools.BackColor };
        buttonTips.SetToolTip(fullScreenButton, "全屏 / 退出全屏");
        fullScreenButton.Click += (_, _) => ToggleFullScreen();
        tools.Controls.Add(fullScreenButton);
        keyboardButton = new MirrorButton("中文", MirrorIcon.Keyboard, true) { Size = new Size(44, 58), BackColor = tools.BackColor };
        buttonTips.SetToolTip(keyboardButton, "开启电脑输入法直输 · 点击手机输入框后直接打字");
        keyboardButton.Click += async (_, _) => { if (!keyboardBusy) { keyboardTask = ToggleKeyboardAsync(); await keyboardTask; } };
        tools.Controls.Add(keyboardButton);
        deviceButtons.Add(keyboardButton);
        if (IsAppWindow)
        {
            var fallback = new MirrorButton("回退", MirrorIcon.Restore, true) { Size = new Size(44, 58), BackColor = tools.BackColor };
            buttonTips.SetToolTip(fallback, "改用普通投屏打开此应用");
            fallback.Click += (_, _) => FallbackRequested?.Invoke("已选择普通投屏打开此应用");
            tools.Controls.Add(fallback);
        }
        screenPowerButton = new MirrorButton(deviceScreenOff ? "点亮" : "息屏", deviceScreenOff ? MirrorIcon.ScreenOn : MirrorIcon.ScreenOff, true)
            { Size = new Size(44, 58), BackColor = tools.BackColor };
        buttonTips.SetToolTip(screenPowerButton, "关闭手机显示屏，继续投屏与操作；不发送锁屏键");
        screenPowerButton.Click += async (_, _) =>
        {
            var screenOff = !deviceScreenOff;
            screenPowerButton.Enabled = false;
            try
            {
                await mirror.SetDeviceScreenOffAsync(screenOff);
                if (shutdown.IsCancellationRequested) return;
                deviceScreenOff = screenOff;
                screenPowerButton.Text = deviceScreenOff ? "点亮" : "息屏";
                screenPowerButton.AccessibleName = deviceScreenOff ? "点亮手机屏幕" : "手机息屏，不锁屏";
                screenPowerButton.Icon = deviceScreenOff ? MirrorIcon.ScreenOn : MirrorIcon.ScreenOff;
                screenPowerButton.Invalidate();
                LogMessage?.Invoke(deviceScreenOff ? "已发送手机息屏指令，投屏继续保持唤醒。" : "已发送手机屏幕点亮指令。");
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, ex.Message, "手机屏幕", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { if (!shutdown.IsCancellationRequested) screenPowerButton.Enabled = IsConnected; }
        };
        tools.Controls.Add(screenPowerButton);
        screenPowerButton.Visible = !IsAppWindow;
        deviceButtons.Add(screenPowerButton);
        var secondaryCommands = new[] { ("音量 +", "24", MirrorIcon.VolumeUp), ("音量 −", "25", MirrorIcon.VolumeDown), ("电源 / 锁屏", "26", MirrorIcon.Power) };
        for (var index = 0; index < secondaryCommands.Length; index++)
        {
            var (label, key, icon) = secondaryCommands[index];
            var button = new MirrorButton(label, icon) { Size = new Size(38, 38), BackColor = tools.BackColor };
            if (IsAppWindow && icon == MirrorIcon.Power) button.Visible = false;
            buttonTips.SetToolTip(button, label);
            button.Click += async (_, _) => await SendCommandAsync(["shell", "input", "keyevent", key]);
            tools.Controls.Add(button);
            deviceButtons.Add(button);
        }
        var notifications = new MirrorButton("通知栏", MirrorIcon.Notifications) { Size = new Size(38, 38), Margin = new Padding(2, 12, 2, 2), BackColor = tools.BackColor };
        buttonTips.SetToolTip(notifications, "展开通知栏");
        notifications.Click += async (_, _) => await SendCommandAsync(["shell", "cmd", "statusbar", "expand-notifications"]);
        tools.Controls.Add(notifications);
        notifications.Visible = !IsAppWindow;
        deviceButtons.Add(notifications);
        var capture = new MirrorButton("截图", MirrorIcon.Capture) { Size = new Size(38, 38), BackColor = tools.BackColor };
        buttonTips.SetToolTip(capture, "截图到 图片 / 星语互联");
        capture.Click += async (_, _) => await CaptureAsync();
        tools.Controls.Add(capture);
        capture.Visible = !IsAppWindow;
        deviceButtons.Add(capture);
        foreach (var button in deviceButtons) button.Enabled = false;
        fullScreenButton.Enabled = false;
        Shown += (_, _) => Completion = StartMirrorAsync();
    }

    private async Task StartMirrorAsync()
    {
        using var priority = FileTransferBudget.ForDevice(bridge, device.Serial).ProtectInteractive();
        Process? process = null;
        var started = false;
        string? fallbackReason = null;
        try
        {
            if (IsAppWindow) sourceResolution = new Size(450, 800);
            else
            {
                try
                {
                    var sizes = Regex.Matches(await bridge.RunAsync(["-s", device.Serial, "shell", "wm", "size"], shutdown), @"(?:Physical|Override) size: (\d+)x(\d+)");
                    if (sizes.Count == 0) throw new IOException("手机未返回可识别的显示分辨率。");
                    var size = sizes[^1];
                    sourceResolution = new Size(int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value));
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                { LogMessage?.Invoke("读取手机分辨率失败，将按视频初始比例缩放：" + ex.Message); }
            }
            if (soundOutput == 2)
            {
                var version = await bridge.RunAsync(["-s", device.Serial, "shell", "getprop", "ro.build.version.sdk"], shutdown);
                if (!int.TryParse(version, out var apiLevel)) throw new IOException("无法读取设备 Android 版本，请检查设备连接。");
                if (apiLevel < 33) throw new IOException("此设备低于 Android 13，无法使用双端声音。请在启动器选择“手机播放”或“电脑播放”后重新连接。");
            }
            var title = "XingyuMirror-" + Guid.NewGuid().ToString("N");
            var startInfo = new ProcessStartInfo(bridge.ScrcpyPath)
            {
                WorkingDirectory = Path.GetDirectoryName(bridge.ScrcpyPath)!,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.Environment["ADB"] = bridge.AdbPath;
            foreach (var argument in new[] { "-s", device.Serial, "--window-title=" + title,
                "--window-borderless", "--window-width=400",
                "--keep-active", "--shortcut-mod=lalt",
                "--push-target=/sdcard/Download/", "--max-fps=" + (quality == 0 ? "30" : "60"),
                "--video-bit-rate=" + (quality == 0 ? "4M" : "8M") }) startInfo.ArgumentList.Add(argument);
            if (quality < 2) startInfo.ArgumentList.Add("--max-size=" + (quality == 0 ? "720" : "1080"));
            if (soundOutput == 0) startInfo.ArgumentList.Add("--no-audio");
            else startInfo.ArgumentList.Add("--audio-codec=aac");
            if (soundOutput == 2) startInfo.ArgumentList.Add("--audio-dup");
            if (deviceScreenOff) startInfo.ArgumentList.Add("--turn-screen-off");
            if (App is not null)
            {
                startInfo.ArgumentList.Add("--start-app=" + App.PackageName);
                if (IsAppWindow)
                {
                    startInfo.ArgumentList.Add("--new-display=450x800/240");
                    startInfo.ArgumentList.Add("--no-vd-destroy-content");
                }
            }
            process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => QueueLog(e.Data);
            process.ErrorDataReceived += (_, e) => QueueLog(e.Data);
            process.Start();
            started = true;
            scrcpy = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var deadline = Stopwatch.StartNew();
            while (!mirror.IsAttached)
            {
                shutdown.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("scrcpy 已退出，请查看“使用说明”中的运行日志；可尝试选择“手机播放”后重连。");
                mirror.Attach(process.Id, title);
                if (deadline.Elapsed.TotalSeconds > 25) throw new TimeoutException("投屏窗口启动超时，请检查设备连接和调试授权。");
                await Task.Delay(100, shutdown);
            }
            var workArea = Screen.FromControl(this).WorkingArea;
            if (sourceResolution.IsEmpty) sourceResolution = mirror.ContentSize;
            videoSize = (sourceResolution.Width > sourceResolution.Height) == (mirror.ContentSize.Width > mirror.ContentSize.Height)
                ? sourceResolution : new Size(sourceResolution.Height, sourceResolution.Width);
            var aspect = (double)videoSize.Width / videoSize.Height;
            var videoHeight = Math.Min(900, Math.Min(workArea.Height - 170, (int)((workArea.Width - 150) / aspect)));
            ClientSize = new Size(Math.Max(320, (int)(videoHeight * aspect) + 66), videoHeight + 106);
            Location = new Point(workArea.X + (workArea.Width - Width) / 2, workArea.Y + (workArea.Height - Height) / 2);
            if (presentation is not null)
            {
                Bounds = presentation.Bounds;
                WindowState = presentation.WindowState;
                if (presentation.FullScreen) ToggleFullScreen();
            }
            IsConnected = true;
            FitVideoAspect();
            empty.Visible = false;
            status.Text = device.Connection + " · " + device.Serial + " · " + new[] { "手机播放", "电脑播放", "双端播放" }[soundOutput];
            buttonTips.SetToolTip(caption, status.Text);
            foreach (var button in deviceButtons) button.Enabled = true;
            fullScreenButton.Enabled = true;
            LogMessage?.Invoke("独立操作窗口已连接：" + device.Serial + "，已启用投屏保活（支持无线、不需要充电）。");
            ConnectionChanged?.Invoke();
            await process.WaitForExitAsync(shutdown);
            if (!shutdown.IsCancellationRequested) throw new IOException("投屏已断开，退出码：" + process.ExitCode + "。请关闭操作窗口后重连。");
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            if (!shutdown.IsCancellationRequested)
            {
                var wasConnected = IsConnected;
                IsConnected = false;
                mirror.Clear();
                empty.Text = "连接已断开\n\n关闭此窗口后，可以从启动器重新连接";
                empty.Visible = true;
                status.Text = "连接失败 · 请查看“使用说明”中的运行日志";
                foreach (var button in deviceButtons) button.Enabled = false;
                ConnectionChanged?.Invoke();
                LogMessage?.Invoke(ex.Message);
                if (!wasConnected && IsAppWindow) fallbackReason = ex.Message;
                else if (!wasConnected) MessageBox.Show(this, ex.Message, "投屏连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (process is not null)
            {
                if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process.Dispose();
            }
            scrcpy = null;
            await keyboardTask;
            if (directKeyboard is not null) { await directKeyboard.Completion; directKeyboard.Dispose(); directKeyboard = null; mirror.KeyboardFocusTarget = null; }
            if (keyboard is not null)
            {
                try { await keyboard.DisposeAsync(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception)
                { LogMessage?.Invoke("恢复手机输入法未完成，记录已保留，下次启用中文时会重试：" + ex.Message); }
                keyboard = null;
            }
        }
        if (fallbackReason is not null && !shutdown.IsCancellationRequested) FallbackRequested?.Invoke(fallbackReason);
    }

    private async Task ToggleKeyboardAsync()
    {
        if (!IsConnected || keyboardBusy) return;
        keyboardBusy = true;
        keyboardButton.Enabled = false;
        try
        {
            if (keyboard is not null)
            {
                if (directKeyboard is not null)
                {
                    directKeyboard.Enabled = false;
                    await directKeyboard.Completion;
                    directKeyboard.Dispose();
                    directKeyboard = null;
                    mirror.KeyboardFocusTarget = null;
                }
                var previous = keyboard;
                keyboard = null;
                await previous.DisposeAsync();
                keyboardButton.Text = "中文";
                buttonTips.SetToolTip(keyboardButton, "开启电脑输入法直输 · 点击手机输入框后直接打字");
                LogMessage?.Invoke("已关闭电脑中文输入，手机输入法已恢复。");
                mirror.FocusMirror();
            }
            else
            {
                keyboardButton.Text = "准备";
                var session = new AdbKeyboardSession(bridge, device.Serial, IsAppWindow ? App!.PackageName : null);
                keyboard = session;
                try
                {
                    await session.StartAsync(shutdown);
                    if (shutdown.IsCancellationRequested) return;
                    var input = new DirectKeyboardOverlay
                    {
                        DispatchAsync = async command =>
                        {
                            if (shutdown.IsCancellationRequested || !IsConnected) return;
                            if (command.Kind == DirectInputKind.Text) await session.SendTextAsync(command.Text, shutdown);
                            else await session.SendKeyAsync(command.Code, command.Meta, shutdown);
                        },
                        ForwardPointer = mirror.ForwardPointer
                    };
                    directKeyboard = input;
                    input.InputFailed += ex =>
                    {
                        if (shutdown.IsCancellationRequested) return;
                        var message = "输入未完成：" + ex.Message;
                        LogMessage?.Invoke(message);
                        buttonTips.Show(message, keyboardButton, new Point(-200, 24), 4500);
                    };
                    mirror.Controls.Add(input);
                    input.BringToFront();
                    mirror.KeyboardFocusTarget = input;
                    input.Focus();
                }
                catch
                {
                    directKeyboard?.Dispose();
                    directKeyboard = null;
                    mirror.KeyboardFocusTarget = null;
                    keyboard = null;
                    await session.DisposeAsync();
                    throw;
                }
                if (shutdown.IsCancellationRequested) return;
                keyboardButton.Text = "关闭";
                buttonTips.SetToolTip(keyboardButton, "中文直输已开启 · 点手机输入框后使用电脑输入法 · 点击关闭并恢复手机输入法");
                LogMessage?.Invoke("电脑输入法直输已开启，点击手机输入框后直接打字（文字不写入运行日志）。");
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (!shutdown.IsCancellationRequested)
            {
                keyboardButton.Text = "重试";
                LogMessage?.Invoke("中文输入未就绪：" + ex.Message);
                buttonTips.Show("中文输入未就绪：" + ex.Message, keyboardButton, new Point(-200, 24), 4500);
            }
        }
        finally { keyboardBusy = false; if (!shutdown.IsCancellationRequested) { keyboardButton.Enabled = IsConnected; keyboardButton.Invalidate(); } }
    }

    private async Task SendCommandAsync(string[] arguments)
    {
        if (!IsConnected) return;
        try
        {
            await bridge.RunAsync(new[] { "-s", device.Serial }.Concat(arguments), shutdown);
            if (!shutdown.IsCancellationRequested) mirror.FocusMirror();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            if (!shutdown.IsCancellationRequested)
            {
                LogMessage?.Invoke(ex.Message);
                MessageBox.Show(this, ex.Message, "设备操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private async Task CaptureAsync()
    {
        if (!IsConnected) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Process? process = null;
        var started = false;
        try
        {
            Directory.CreateDirectory(captureDirectory);
            var localPath = Path.Combine(captureDirectory, "截图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            var startInfo = new ProcessStartInfo(bridge.AdbPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { "-s", device.Serial, "exec-out", "screencap", "-p" }) startInfo.ArgumentList.Add(argument);
            process = new Process { StartInfo = startInfo };
            process.Start();
            started = true;
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await using (var file = File.Create(localPath))
                await process.StandardOutput.BaseStream.CopyToAsync(file, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var error = await errorTask;
            if (process.ExitCode != 0) throw new IOException("截图失败：" + error.Trim());
            if (!shutdown.IsCancellationRequested)
            {
                status.Text = "截图已保存到 图片 / 星语互联";
                LogMessage?.Invoke("截图：" + localPath);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke("截图超时，请检查设备连接。");
            MessageBox.Show(this, "截图超时，请检查设备连接。", "截图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!shutdown.IsCancellationRequested)
            {
                LogMessage?.Invoke(ex.Message);
                MessageBox.Show(this, ex.Message, "截图失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (process is not null)
            {
                if (started && !process.HasExited) process.Kill(entireProcessTree: true);
                process.Dispose();
            }
        }
    }

    private void ToggleFullScreen()
    {
        fullScreen = !fullScreen;
        if (fullScreen)
        {
            savedWindowState = WindowState;
            savedBounds = Bounds;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
        }
        caption.Visible = !fullScreen;
        layout.RowStyles[0].Height = fullScreen ? 0 : 40;
        fullScreenButton.Text = fullScreen ? "退出全屏" : "全屏";
        fullScreenButton.Icon = fullScreen ? MirrorIcon.Restore : MirrorIcon.Fullscreen;
        fullScreenButton.Invalidate();
        if (!fullScreen)
        {
            Bounds = savedBounds;
            WindowState = savedWindowState;
        }
        UpdateWindowShape();
        mirror.FocusMirror();
    }

    private void QueueLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || shutdown.IsCancellationRequested || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (shutdown.IsCancellationRequested) return;
                LogMessage?.Invoke(message);
                var texture = Regex.Match(message, @"Texture(?: \([^)]*\))?: (\d+)x(\d+)");
                if (texture.Success && !sourceResolution.IsEmpty)
                {
                    // Hardware decoder textures include padding; use them only to detect rotation.
                    var landscape = int.Parse(texture.Groups[1].Value) > int.Parse(texture.Groups[2].Value);
                    videoSize = (sourceResolution.Width > sourceResolution.Height) == landscape
                        ? sourceResolution : new Size(sourceResolution.Height, sourceResolution.Width);
                    if (IsConnected) FitVideoAspect();
                }
                if (IsAppWindow && IsConnected && (message.Contains("Cannot create launch intent for app ", StringComparison.Ordinal) ||
                    message.Contains("No known display id to start app", StringComparison.Ordinal) || message.Contains("Could not invoke method", StringComparison.Ordinal)))
                    FallbackRequested?.Invoke("手机系统未能在独立窗口中打开应用");
            });
        }
        catch (InvalidOperationException) when (shutdown.IsCancellationRequested) { }
    }

    private void FitVideoAspect()
    {
        if (fittingAspect || fullScreen || WindowState != FormWindowState.Normal || videoSize.IsEmpty) return;
        fittingAspect = true;
        try
        {
            var chrome = new Size(Width - mirror.Width, Height - mirror.Height);
            var aspect = (double)videoSize.Width / videoSize.Height;
            var area = Screen.FromControl(this).WorkingArea;
            var height = Math.Min(mirror.Height, Math.Min(area.Height - chrome.Height, (area.Width - chrome.Width) / aspect));
            var minimumHeight = Math.Max(MinimumSize.Height - chrome.Height, (MinimumSize.Width - chrome.Width) / aspect);
            height = Math.Max(minimumHeight, height);
            Size = new Size((int)Math.Round(height * aspect) + chrome.Width, (int)Math.Round(height) + chrome.Height);
        }
        finally { fittingAspect = false; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsConnected) FitVideoAspect();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x214 && !fullScreen && WindowState == FormWindowState.Normal && !videoSize.IsEmpty) // WM_SIZING
        {
            var rectangle = Marshal.PtrToStructure<SizingRectangle>(message.LParam);
            var edge = (int)message.WParam;
            var chromeWidth = Width - mirror.Width;
            var chromeHeight = Height - mirror.Height;
            var aspect = (double)videoSize.Width / videoSize.Height;
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            var fromWidth = edge is 1 or 2 || edge is not 3 and not 6 && Math.Abs(width - Width) > Math.Abs(height - Height) * aspect;
            var videoHeight = fromWidth ? (width - chromeWidth) / aspect : height - chromeHeight;
            videoHeight = Math.Max(videoHeight, Math.Max(MinimumSize.Height - chromeHeight, (MinimumSize.Width - chromeWidth) / aspect));
            width = (int)Math.Round(videoHeight * aspect) + chromeWidth;
            height = (int)Math.Round(videoHeight) + chromeHeight;
            if (edge is 1 or 4 or 7) rectangle.Left = rectangle.Right - width;
            else rectangle.Right = rectangle.Left + width;
            if (edge is 3 or 4 or 5) rectangle.Top = rectangle.Bottom - height;
            else rectangle.Bottom = rectangle.Top + height;
            Marshal.StructureToPtr(rectangle, message.LParam, false);
            message.Result = 1;
            return;
        }
        base.WndProc(ref message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizingRectangle { public int Left, Top, Right, Bottom; }

    private void StopMirror()
    {
        IsConnected = false;
        if (!lifetime.IsCancellationRequested) lifetime.Cancel();
        if (scrcpy is { HasExited: false }) scrcpy.Kill(entireProcessTree: true);
        mirror.Clear();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopMirror();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { StopMirror(); lifetime.Dispose(); buttonTips.Dispose(); }
        base.Dispose(disposing);
    }

}
