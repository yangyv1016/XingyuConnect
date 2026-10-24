using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XingyuConnect;

internal sealed class AdbKeyboardSession : IAsyncDisposable
{
    public const string ImeId = "com.android.adbkeyboard/.AdbIME";
    private const string FullImeId = "com.android.adbkeyboard/com.android.adbkeyboard.AdbIME";
    private sealed record RestoreState(string PreviousIme, bool WasEnabled);
    private readonly DeviceBridge bridge;
    private readonly string serial;
    private string? statePath;
    private readonly string? targetPackage;
    private FileStream? ownership;
    private int disposed;
    public bool IsReady { get; private set; }

    public AdbKeyboardSession(DeviceBridge bridge, string serial, string? targetPackage = null)
    {
        this.bridge = bridge;
        this.serial = serial;
        this.targetPackage = targetPackage;
    }

    private static bool IsAdbKeyboard(string ime) => ime is ImeId or FullImeId;

    public async Task StartAsync(CancellationToken token)
    {
        var identity = await bridge.RunAsync(["-s", serial, "shell", "getprop", "ro.serialno"], token);
        // MuMu omits ro.serialno; Android ID still identifies its duplicate ADB transports.
        if (identity.Length == 0 || identity == "unknown")
        {
            var androidId = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "android_id"], token);
            identity = androidId.Length == 0 || androidId == "null" ? serial : "android:" + androidId;
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "keyboard-" + key + ".json");
        statePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        { throw new IOException("另一个操控窗口正在使用这台设备的中文输入，请先关闭它的中文输入。", ex); }
        await RestorePendingAsync(path, token);
        var current = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "default_input_method"], token);
        if (current.Length == 0 || current == "null") throw new IOException("无法确认手机原输入法，尚未切换中文输入。");
        var installed = await bridge.RunAsync(["-s", serial, "shell", "ime", "list", "-a", "-s"], token);
        if (!installed.Split('\n', StringSplitOptions.TrimEntries).Any(IsAdbKeyboard))
        {
            var apk = Path.Combine(AppContext.BaseDirectory, "adbkeyboard", "ADBKeyboard.apk");
            if (!File.Exists(apk)) throw new FileNotFoundException("安装包缺少 ADBKeyboard，请保留完整的软件文件夹。", apk);
            var result = await bridge.RunAsync(["-s", serial, "install", apk], token, 60);
            if (!result.Contains("Success", StringComparison.Ordinal)) throw new IOException("手机未完成中文输入法安装：" + result);
        }
        var enabled = await bridge.RunAsync(["-s", serial, "shell", "ime", "list", "-s"], token);
        var saved = new RestoreState(current, enabled.Split('\n', StringSplitOptions.TrimEntries).Any(IsAdbKeyboard));
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(saved), token);
        File.Move(temporary, path, true);
        await bridge.RunAsync(["-s", serial, "shell", "ime", "enable", ImeId], token);
        await bridge.RunAsync(["-s", serial, "shell", "ime", "set", ImeId], token);
        current = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "default_input_method"], token);
        if (!IsAdbKeyboard(current)) throw new IOException("手机没有切换到中文输入法，请检查手机的安装与输入法授权提示。");
        IsReady = true;
    }

    public async Task SendTextAsync(string text, CancellationToken token)
    {
        if (!IsReady) throw new IOException("中文输入尚未就绪，请重新启用。");
        if (text.Length == 0) return;
        if (text.Length > 4000) throw new IOException("文字较长，请分段写入，每次最多 4000 个字符。");
        await CheckEditorAsync(token);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));
        await bridge.RunAsync(["-s", serial, "shell", "am", "broadcast", "--receiver-foreground", "-p", "com.android.adbkeyboard",
            "-a", "ADB_INPUT_B64", "--es", "msg", encoded], token);
    }

    private async Task CheckEditorAsync(CancellationToken token)
    {
        var current = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "default_input_method"], token);
        if (!IsAdbKeyboard(current)) throw new IOException("手机输入法已经切换，请关闭并重新打开“中文”，再试一次。");
        var inputState = await bridge.RunAsync(["-s", serial, "shell", "dumpsys", "activity", "service", ImeId], token);
        var inputType = Regex.Match(inputState, @"mInputEditorInfo:\s*inputType=0x([0-9a-fA-F]+)");
        if (!inputState.Contains("mInputStarted=true", StringComparison.Ordinal) || inputState.Contains("mStartedInputConnection=null", StringComparison.Ordinal) ||
            !inputType.Success || Convert.ToUInt32(inputType.Groups[1].Value, 16) == 0)
            throw new IOException("请先点击手机画面里的输入框，再输入文字。");
        if (targetPackage is not null && !Regex.IsMatch(inputState, @"\bpackageName=" + Regex.Escape(targetPackage) + @"(?:\s|$)"))
            throw new IOException("手机当前焦点不在这个应用，请先点击当前应用的输入框。");
    }

    public async Task SendKeyAsync(int code, int meta, CancellationToken token)
    {
        if (!IsReady) throw new IOException("中文输入尚未就绪，请重新启用。");
        await CheckEditorAsync(token);
        if (meta == 0)
            await bridge.RunAsync(["-s", serial, "shell", "am", "broadcast", "--receiver-foreground", "-p", "com.android.adbkeyboard", "-a", "ADB_INPUT_CODE", "--ei", "code", code.ToString()], token);
        else
            await bridge.RunAsync(["-s", serial, "shell", "am", "broadcast", "--receiver-foreground", "-p", "com.android.adbkeyboard", "-a", "ADB_INPUT_TEXT", "--es", "mcode", meta + "," + code], token);
    }

    private async Task RestorePendingAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return;
        RestoreState saved;
        try
        {
            saved = JsonSerializer.Deserialize<RestoreState>(await File.ReadAllTextAsync(path, token))
                ?? throw new InvalidDataException("手机输入法恢复记录为空，请保留原文件并检查：" + path);
        }
        catch (JsonException ex) { throw new IOException("手机输入法恢复记录损坏，未改动输入法：" + path, ex); }
        if (string.IsNullOrWhiteSpace(saved.PreviousIme) || !saved.PreviousIme.Contains('/'))
            throw new InvalidDataException("手机输入法恢复记录不完整，未改动输入法：" + path);
        var current = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "default_input_method"], token);
        if (IsAdbKeyboard(current) && !IsAdbKeyboard(saved.PreviousIme))
        {
            await bridge.RunAsync(["-s", serial, "shell", "ime set " + PhoneContentService.QuoteShell(saved.PreviousIme)], token);
            current = await bridge.RunAsync(["-s", serial, "shell", "settings", "get", "secure", "default_input_method"], token);
            if (current != saved.PreviousIme) throw new IOException("手机原输入法未能恢复，已保留恢复记录，请重新连接后重试。");
        }
        // A manual input-method change takes precedence over this session's saved selection.
        if (!saved.WasEnabled && !IsAdbKeyboard(current))
            await bridge.RunAsync(["-s", serial, "shell", "ime", "disable", ImeId], token);
        File.Delete(path);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        IsReady = false;
        if (ownership is null) return;
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await RestorePendingAsync(statePath!, cleanup.Token);
        }
        finally { await ownership.DisposeAsync(); ownership = null; }
    }
}
