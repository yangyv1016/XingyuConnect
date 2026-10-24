using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace XingyuConnect;

internal sealed record AndroidDevice(string Serial, string State, string Model)
{
    public string Connection => Serial.StartsWith("emulator-", StringComparison.Ordinal) ||
        Serial.StartsWith("127.0.0.1:", StringComparison.Ordinal) ? "模拟器" :
        Serial.Contains(':') || Serial.EndsWith("._adb-tls-connect._tcp", StringComparison.Ordinal) ? "无线" : "USB";

    public string StateText => State switch
    {
        "device" => "可连接",
        "unauthorized" => "请在手机上允许调试",
        "offline" => "设备离线",
        _ => State
    };

    public string DisplayName => Model.Length > 0 ? Model.Replace('_', ' ') : Serial;
}

internal sealed class DeviceBridge
{
    public string ScrcpyPath { get; } = Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe");
    public string AdbPath { get; } = Path.Combine(AppContext.BaseDirectory, "scrcpy", "adb.exe");

    public async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken,
        int timeoutSeconds = 15)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var startInfo = new ProcessStartInfo(AdbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new IOException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
            return output.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"设备在 {timeoutSeconds} 秒内未响应，请检查连接后重试。");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    public async Task<List<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var output = await RunAsync(["devices", "-l"], cancellationToken);
        var devices = new List<AndroidDevice>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("List of devices", StringComparison.Ordinal) || line.StartsWith('*'))
                continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            var model = parts.Skip(2).FirstOrDefault(part => part.StartsWith("model:", StringComparison.Ordinal));
            devices.Add(new AndroidDevice(parts[0], parts[1], model is null ? "" : model[6..]));
        }
        return devices;
    }

    public static string NormalizeEndpoint(string input, bool requirePort = false)
    {
        var value = input.Trim();
        if (!requirePort && IPAddress.TryParse(value, out var address))
            return new IPEndPoint(address, 5555).ToString();
        if (IPEndPoint.TryParse(value, out var endpoint) && endpoint.Port > 0)
            return endpoint.ToString();
        throw new ArgumentException(requirePort
            ? "请输入手机显示的 IP:端口，例如 192.168.0.45:37123；配对端口和连接端口不同。"
            : "请输入 IP 或 IP:端口，例如 192.168.0.45:5555。IPv6 带端口时请使用 [地址]:端口。");
    }

    public async Task<string> ConnectWirelessAsync(string input, CancellationToken cancellationToken)
    {
        var endpoint = NormalizeEndpoint(input);
        var devices = await GetDevicesAsync(cancellationToken);
        if (devices.Any(device => device.Serial == endpoint && device.State == "offline"))
            await RunAsync(["disconnect", endpoint], cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            var result = await RunAsync(["connect", endpoint], cancellationToken, 8);
            // ADB 的 connect/pair 失败响应也可能以退出码 0 返回，不能只检查进程退出码。
            if (!result.StartsWith("connected to ", StringComparison.Ordinal) &&
                !result.StartsWith("already connected to ", StringComparison.Ordinal))
                throw new IOException(result);
            try
            {
                await RunAsync(["-s", endpoint, "wait-for-device"], cancellationToken, 10);
                return endpoint;
            }
            catch (TimeoutException) when (attempt == 0)
            {
                await RunAsync(["disconnect", endpoint], cancellationToken);
            }
        }
    }

    public async Task PairWirelessAsync(string input, string code, CancellationToken cancellationToken)
    {
        var endpoint = NormalizeEndpoint(input, requirePort: true);
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw new ArgumentException("请输入手机显示的 6 位数字配对码。");
        var result = await RunAsync(["pair", endpoint, code], cancellationToken, 30);
        if (!result.StartsWith("Successfully paired to ", StringComparison.Ordinal))
            throw new IOException(result);
    }

    public async Task<string> SwitchUsbToWirelessAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        if (device.Connection != "USB" || device.State != "device")
            throw new ArgumentException("请先选择已授权的 USB 真机。");
        var identity = await RunAsync(["-s", device.Serial, "shell", "getprop", "ro.serialno"], cancellationToken);
        if (identity.Length == 0) throw new IOException("手机未提供硬件序列号，无法核对无线连接是否属于同一台手机。");
        var addresses = await RunAsync(["-s", device.Serial, "shell", "ip", "-4", "-o", "addr", "show"], cancellationToken);
        var wifi = Regex.Match(addresses, @"(?m)^\d+:\s+(?:wlan|wifi)\d+\s+inet\s+(\d+(?:\.\d+){3})/");
        if (!wifi.Success) throw new IOException("未找到手机 Wi-Fi 的 IPv4 地址。请先让手机连接与电脑相通的 Wi-Fi，再重试；也可在“直接连接”中手动填写地址。");
        var endpoint = NormalizeEndpoint(wifi.Groups[1].Value);
        var port = await RunAsync(["-s", device.Serial, "shell", "getprop", "service.adb.tcp.port"], cancellationToken);
        if (port != "5555")
        {
            await RunAsync(["-s", device.Serial, "tcpip", "5555"], cancellationToken);
            await Task.Delay(1200, cancellationToken);
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ConnectWirelessAsync(endpoint, cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < 2 && ex is IOException or TimeoutException)
            {
                await Task.Delay(800, cancellationToken);
            }
        }
        var wirelessIdentity = await RunAsync(["-s", endpoint, "shell", "getprop", "ro.serialno"], cancellationToken);
        if (wirelessIdentity != identity)
            throw new IOException("无线设备与 USB 手机的硬件序列号不一致，请检查 IP 地址。");
        return endpoint;
    }
}
