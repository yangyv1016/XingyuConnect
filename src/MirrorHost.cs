using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace XingyuConnect;

internal sealed class MirrorHost : Panel
{
    private nint childWindow;

    public Size ContentSize { get; private set; }

    public bool IsAttached => childWindow != 0 && IsWindow(childWindow);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? KeyboardFocusTarget { get; set; }

    public MirrorHost()
    {
        BackColor = Color.FromArgb(19, 24, 34);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public void Attach(int processId, string title)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            // scrcpy creates the SDL window hidden before receiving its first frame.
            // Reparenting it before SDL finishes initialization can crash its renderer.
            if (owner != processId || !IsWindowVisible(window))
                return true;
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title)
                return true;
            found = window;
            return false;
        }, 0);
        if (found == 0)
            return;

        GetClientRect(found, out var content);
        if (content.Right <= 0 || content.Bottom <= 0) return;
        ContentSize = new Size(content.Right, content.Bottom);

        var style = GetWindowLongPtr(found, -16).ToInt64();
        // SDL keeps processing real mouse/keyboard input in its own child window.
        SetWindowLongPtr(found, -16, new nint((style & ~0x80C40000L) | 0x40000000L));
        SetLastError(0);
        var previousParent = SetParent(found, Handle);
        if (previousParent == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将投屏窗口嵌入界面。");
        childWindow = found;
        SetWindowPos(childWindow, 0, 0, 0, ClientSize.Width, ClientSize.Height, 0x0024);
        ShowWindow(childWindow, 5);
    }

    public void Clear() => childWindow = 0;

    public void FocusMirror()
    {
        if (KeyboardFocusTarget is { IsDisposed: false, Visible: true } target) target.Focus();
        else if (IsAttached)
            SetFocus(childWindow);
    }

    public void ForwardPointer(int message, nint wParam, nint lParam)
    {
        if (!IsAttached) return;
        if (SendMessageTimeout(childWindow, (uint)message, wParam, lParam, 2, 200, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "投屏未响应鼠标操作。");
        if (GetAncestor(GetForegroundWindow(), 2) == GetAncestor(childWindow, 2)) FocusMirror();
    }

    public Task SetDeviceScreenOffAsync(bool screenOff)
        => SendShortcutAsync(0x4F, !screenOff);

    public async Task SendShortcutAsync(ushort key, bool shift = false)
    {
        if (!IsAttached) throw new IOException("投屏尚未连接，无法控制手机显示屏。");
        SetFocus(childWindow);
        // Only send the shortcut to the foreground scrcpy child, never to another app.
        if (GetAncestor(GetForegroundWindow(), 2) != GetAncestor(childWindow, 2) || GetFocus() != childWindow)
            throw new IOException("请先激活投屏窗口，再操作手机息屏按钮。");
        var keys = shift ? new ushort[] { 0xA4, 0xA0, key } : new ushort[] { 0xA4, key };
        var input = keys.Select(key => new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key } } })
            .Concat(keys.Reverse().Select(key => new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } })).ToArray();
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length)
        {
            var error = Marshal.GetLastWin32Error();
            var releases = input[keys.Length..];
            SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
            throw new Win32Exception(error, "无法向 scrcpy 发送显示屏快捷键。");
        }
        // SDL must consume the injected shortcut before focus returns to the IME control.
        await Task.Delay(50);
        if (!IsDisposed && IsAttached && GetAncestor(GetForegroundWindow(), 2) == GetAncestor(childWindow, 2)) FocusMirror();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsAttached)
            MoveWindow(childWindow, 0, 0, ClientSize.Width, ClientSize.Height, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        FocusMirror();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);
    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint error);
    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetFocus();
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out WindowRect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
}
