using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XingyuConnect;

internal enum DirectInputKind { Text, Key }
internal sealed class DirectInputCommand(DirectInputKind kind)
{
    public DirectInputKind Kind { get; } = kind;
    public string Text { get; set; } = "";
    public int Code { get; init; }
    public int Meta { get; init; }
}

// A nearly transparent native edit control owns the Windows IME context.
// Pointer messages still go to the original SDL view; no global keyboard hook is installed.
internal sealed class DirectKeyboardOverlay : TextBox
{
    private readonly Queue<DirectInputCommand> commands = new();
    private DirectInputCommand? tail;
    private bool draining;
    private bool composing;
    private bool clearing;
    private bool inputLanguageChosen;
    private InputLanguage? previousLanguage;
    private InputLanguage? selectedLanguage;
    private Point candidatePoint = new(20, 20);
    private long compositionEnded;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public required Func<DirectInputCommand, Task> DispatchAsync { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public required Action<int, nint, nint> ForwardPointer { get; init; }
    public event Action<Exception>? InputFailed;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public DirectKeyboardOverlay()
    {
        Dock = DockStyle.Fill;
        Multiline = true;
        AcceptsTab = true;
        BorderStyle = BorderStyle.None;
        ScrollBars = ScrollBars.None;
        ImeMode = ImeMode.On;
        ShortcutsEnabled = false;
        Cursor = Cursors.Default;
        BackColor = Color.Black;
        ForeColor = Color.Black;
        AccessibleName = "手机画面中文直输";
    }

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x80000; return parameters; } // WS_EX_LAYERED
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Alpha must be nonzero for hit-testing; 1/255 keeps the original video visible.
        if (!SetLayeredWindowAttributes(Handle, 0, 1, 2)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建画面输入层。");
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (!inputLanguageChosen)
        {
            inputLanguageChosen = true;
            previousLanguage = InputLanguage.CurrentInputLanguage;
            selectedLanguage = previousLanguage.Culture.TwoLetterISOLanguageName == "zh" ? previousLanguage :
                InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().FirstOrDefault(language => language.Culture.TwoLetterISOLanguageName == "zh");
            if (selectedLanguage is not null) InputLanguage.CurrentInputLanguage = selectedLanguage;
            var context = ImmGetContext(Handle);
            if (context != 0) { ImmSetOpenStatus(context, true); ImmReleaseContext(Handle, context); }
        }
        PositionCandidate();
    }

    private void PositionCandidate()
    {
        if (!IsHandleCreated) return;
        var context = ImmGetContext(Handle);
        if (context == 0) return;
        try
        {
            var point = new Point(Math.Clamp(candidatePoint.X, 0, Math.Max(0, Width - 24)), Math.Clamp(candidatePoint.Y, 0, Math.Max(0, Height - 24)));
            SetCaretPos(point.X, point.Y);
            var composition = new CompositionForm { Style = 2, Position = point };
            ImmSetCompositionWindow(context, ref composition);
            var candidate = new CandidateForm { Style = 0x40, Position = new Point(point.X, point.Y + 22) };
            ImmSetCandidateWindow(context, ref candidate);
        }
        finally { ImmReleaseContext(Handle, context); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (!composing && !clearing && TextLength > 0) CommitText();
    }

    private void CommitText()
    {
        var text = Text;
        clearing = true;
        try { Clear(); }
        finally { clearing = false; }
        if (text.Length == 0) return;
        if (tail is { Kind: DirectInputKind.Text } && commands.Count > 0) tail.Text += text;
        else Enqueue(new DirectInputCommand(DirectInputKind.Text) { Text = text });
    }

    private void Enqueue(DirectInputCommand command)
    {
        if (IsDisposed) return;
        commands.Enqueue(command);
        tail = command;
        if (!draining) Completion = DrainAsync();
    }

    private async Task DrainAsync()
    {
        draining = true;
        try
        {
            while (commands.TryDequeue(out var command))
            {
                if (ReferenceEquals(tail, command)) tail = null;
                if (IsDisposed) { commands.Clear(); break; }
                try { await DispatchAsync(command); }
                catch (OperationCanceledException) { commands.Clear(); break; }
                catch (Exception ex) when (ex is IOException or TimeoutException or Win32Exception)
                { commands.Clear(); tail = null; InputFailed?.Invoke(ex); break; }
            }
        }
        finally { draining = false; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!composing)
        {
            if (e.Control && !e.Alt && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = e.Handled = true;
                try { if (Clipboard.ContainsText()) Enqueue(new DirectInputCommand(DirectInputKind.Text) { Text = Clipboard.GetText() }); }
                catch (ExternalException ex) { InputFailed?.Invoke(new IOException("暂时无法读取电脑剪贴板，请稍后重试。", ex)); }
            }
            else
            {
                var code = e.Control && !e.Alt && e.KeyCode is >= Keys.A and <= Keys.Z ? 29 + (int)e.KeyCode - (int)Keys.A : e.KeyCode switch
                {
                    Keys.Back => 67, Keys.Delete => 112, Keys.Enter => 66, Keys.Tab => 61, Keys.Escape => 111,
                    Keys.Left => 21, Keys.Right => 22, Keys.Up => 19, Keys.Down => 20, Keys.Home => 122, Keys.End => 123,
                    Keys.PageUp => 92, Keys.PageDown => 93, _ => 0
                };
                if (code != 0)
                {
                    e.SuppressKeyPress = e.Handled = true;
                    // Enter used to confirm an IME candidate must not also submit the phone editor.
                    if (e.KeyCode != Keys.Enter || Environment.TickCount64 - compositionEnded > 80)
                        Enqueue(new DirectInputCommand(DirectInputKind.Key) { Code = code, Meta = (e.Control ? 4096 : 0) | (e.Shift ? 1 : 0) | (e.Alt ? 2 : 0) });
                }
            }
        }
        base.OnKeyDown(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is >= 0x200 and <= 0x20E)
        {
            if (message.Msg is 0x201 or 0x204 or 0x207 or 0x20B)
            {
                candidatePoint = new Point(unchecked((short)message.LParam.ToInt64()), unchecked((short)(message.LParam.ToInt64() >> 16)));
                Focus();
                Capture = true;
                PositionCandidate();
            }
            // Mouse-up must reach SDL even if an earlier ADB text command fails.
            try { ForwardPointer(message.Msg, message.WParam, message.LParam); }
            catch (Exception ex) when (ex is IOException or Win32Exception) { InputFailed?.Invoke(ex); }
            if (message.Msg is 0x202 or 0x205 or 0x208 or 0x20C) Capture = false;
            message.Result = 0;
            return;
        }
        if (message.Msg == 0x7B) { message.Result = 0; return; } // no local edit-control context menu
        if (message.Msg == 0x10D) composing = true;
        base.WndProc(ref message);
        if (message.Msg is 0x10D or 0x10F) PositionCandidate();
        if (message.Msg == 0x10E)
        {
            composing = false;
            compositionEnded = Environment.TickCount64;
            CommitText();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed && previousLanguage is not null && selectedLanguage is not null && InputLanguage.CurrentInputLanguage.Equals(selectedLanguage))
            InputLanguage.CurrentInputLanguage = previousLanguage;
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionForm { public uint Style; public Point Position; public NativeRectangle Area; }
    [StructLayout(LayoutKind.Sequential)] private struct CandidateForm { public uint Index, Style; public Point Position; public NativeRectangle Area; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCaretPos(int x, int y);
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(nint context, bool open);
    [DllImport("imm32.dll")] private static extern bool ImmSetCompositionWindow(nint context, ref CompositionForm form);
    [DllImport("imm32.dll")] private static extern bool ImmSetCandidateWindow(nint context, ref CandidateForm form);
}
