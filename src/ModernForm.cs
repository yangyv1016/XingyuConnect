using System.Runtime.InteropServices;

namespace XingyuConnect;

internal class ModernForm : Form
{
    private MirrorButton? maximizeButton;
    protected virtual bool IsFullScreen => false;

    protected ModernForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        Icon = BrandAssets.WindowIcon;
        ShowInTaskbar = true;
        Padding = new Padding(1);
        DoubleBuffered = true;
    }

    protected FlowLayoutPanel CreateWindowActions(bool dark = false)
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 132,
            WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0) };
        foreach (var action in new[] { ("最小化", MirrorIcon.Minimize), ("最大化", MirrorIcon.Maximize), ("关闭", MirrorIcon.Close) })
        {
            if (action.Item2 == MirrorIcon.Minimize && !MinimizeBox || action.Item2 == MirrorIcon.Maximize && !MaximizeBox) continue;
            var button = new MirrorButton(action.Item1, action.Item2) { Size = new Size(40, 32),
                Margin = new Padding(2, 0, 2, 0), LightTheme = !dark,
                BackColor = dark ? Color.FromArgb(20, 30, 44) : Ui.Background };
            button.Click += (_, _) =>
            {
                if (action.Item2 == MirrorIcon.Close) Close();
                else if (action.Item2 == MirrorIcon.Minimize) WindowState = FormWindowState.Minimized;
                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            if (action.Item2 == MirrorIcon.Maximize) maximizeButton = button;
            actions.Controls.Add(button);
        }
        actions.Width = actions.Controls.Count * 44;
        return actions;
    }

    protected void EnableWindowDrag(params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || IsFullScreen) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
            };
            control.DoubleClick += (_, _) =>
            {
                if (!MaximizeBox || IsFullScreen) return;
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            };
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(Handle, ref margins);
        UpdateWindowShape();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (maximizeButton is not null)
        {
            maximizeButton.Icon = WindowState == FormWindowState.Maximized ? MirrorIcon.Restore : MirrorIcon.Maximize;
            maximizeButton.AccessibleName = WindowState == FormWindowState.Maximized ? "还原窗口" : "最大化";
            maximizeButton.Invalidate();
        }
        UpdateWindowShape();
    }

    protected void UpdateWindowShape()
    {
        if (!IsHandleCreated || Width < 1 || Height < 1) return;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var preference = IsFullScreen ? 1 : 2;
            DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            return;
        }
        // Windows 10 has no DWM corner preference. Clip only the outer frame.
        var previous = Region;
        if (WindowState == FormWindowState.Normal && !IsFullScreen)
        {
            using var shape = Ui.RoundedRectangle(new RectangleF(0, 0, Width, Height), 16 * DeviceDpi / 96f);
            Region = new Region(shape);
        }
        else Region = null;
        previous?.Dispose();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style = (parameters.Style & ~0x00C00000) | 0x000C0000;
            if (MinimizeBox) parameters.Style |= 0x00020000;
            if (MaximizeBox) parameters.Style |= 0x00010000;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x83 or 0x85) // Custom client area replaces the native title bar.
        {
            message.Result = 0;
            return;
        }
        if (message.Msg == 0x86)
        {
            message.Result = 1;
            return;
        }
        if (message.Msg == 0x24 && !IsFullScreen) // WM_GETMINMAXINFO: keep maximization inside the taskbar work area.
        {
            base.WndProc(ref message);
            var info = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
            var screen = Screen.FromHandle(Handle);
            info.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
            info.MaxSize = screen.WorkingArea.Size;
            Marshal.StructureToPtr(info, message.LParam, false);
            return;
        }
        if (message.Msg == 0x84 && !IsFullScreen && WindowState == FormWindowState.Normal)
        {
            var point = PointToClient(new Point(unchecked((short)message.LParam.ToInt64()), unchecked((short)(message.LParam.ToInt64() >> 16))));
            var border = (int)Math.Ceiling(6 * DeviceDpi / 96f);
            var left = point.X < border;
            var right = point.X >= ClientSize.Width - border;
            var top = point.Y < border;
            var bottom = point.Y >= ClientSize.Height - border;
            if (left || right || top || bottom)
            {
                message.Result = top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : 11;
                return;
            }
        }
        base.WndProc(ref message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public Point Reserved; public Size MaxSize; public Point MaxPosition; public Size MinTrackSize, MaxTrackSize; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
