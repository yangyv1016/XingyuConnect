using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace XingyuConnect;

// Reuses AudioShare v2.2.2 with the endpoint-selection extension in native/audioshare.
// PCM conversion, transport framing and Android playback remain upstream.
internal sealed class AudioShareSession : IAsyncDisposable
{
    private readonly DeviceBridge bridge;
    private readonly string serial;
    private readonly string? endpointId;
    private readonly string socketName = "xingyu_audio_" + Guid.NewGuid().ToString("N");
    private readonly string connectCode = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource stopped = new();
    private nint library;
    private NativeCleanup? cleanup;
    private Process? server;
    private Task<string>? serverError;
    private bool reverseCreated;
    private int disposed;

    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsStreaming { get; private set; }
    public string SocketName => socketName;

    public AudioShareSession(DeviceBridge bridge, string serial, string? endpointId = null)
    {
        this.bridge = bridge;
        this.serial = serial;
        this.endpointId = endpointId;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopped.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "audioshare");
            library = NativeLibrary.Load(Path.Combine(directory, "audio_capture_xingyu.dll"), typeof(AudioShareSession).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
            var initialize = Marshal.GetDelegateForFunctionPointer<NativeInitializeDevice>(NativeLibrary.GetExport(library, "AudioCapture_InitializeDevice"));
            var listen = Marshal.GetDelegateForFunctionPointer<NativeListen>(NativeLibrary.GetExport(library, "AudioCapture_Listen"));
            var start = Marshal.GetDelegateForFunctionPointer<NativeInitialize>(NativeLibrary.GetExport(library, "AudioCapture_Start"));
            cleanup = Marshal.GetDelegateForFunctionPointer<NativeCleanup>(NativeLibrary.GetExport(library, "AudioCapture_Cleanup"));
            var connected = Marshal.GetDelegateForFunctionPointer<NativeConnected>(NativeLibrary.GetExport(library, "AudioCapture_IsConnected"));
            if (initialize(endpointId) == 0)
                throw new IOException("AudioShare 无法打开所选 Windows 音频设备，请确认设备可用后重新开启。");

            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            if (listen(port, 0) == 0)
                throw new IOException("AudioShare 无法建立音频通道，请重试。");

            await bridge.RunAsync(["-s", serial, "push", Path.Combine(directory, "server"), "/data/local/tmp/xingyu-audioshare"], timeout.Token);
            await bridge.RunAsync(["-s", serial, "reverse", "localabstract:" + socketName, "tcp:" + port], timeout.Token);
            reverseCreated = true;
            timeout.Token.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo(bridge.AdbPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { "-s", serial, "shell", "app_process",
                "-Djava.class.path=/data/local/tmp/xingyu-audioshare", "/data/local/tmp", "com.ysbing.audioshare.Main",
                "socketName=" + socketName, "connectCode=" + connectCode })
                startInfo.ArgumentList.Add(argument);
            var process = new Process { StartInfo = startInfo };
            process.Start();
            server = process;
            serverError = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            var identity = new StringBuilder(256);
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (connected(identity, identity.Capacity) != 0) break;
                if (process.HasExited)
                    throw new IOException("手机音频服务启动失败：" + (await serverError).Trim());
                await Task.Delay(80, timeout.Token);
            }
            if (identity.ToString() != connectCode)
                throw new IOException("音频通道的设备身份不匹配，请重新连接。");
            if (start() == 0)
                throw new IOException("AudioShare 无法开始采集电脑声音，请检查 Windows 默认音频输出。");
            IsStreaming = true;
            Completion = MonitorAsync(process);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !stopped.IsCancellationRequested)
        {
            throw new TimeoutException("电脑音频连接超时，请检查手机的 ADB 调试连接。");
        }
    }

    private async Task MonitorAsync(Process process)
    {
        await process.WaitForExitAsync(stopped.Token);
        if (!stopped.IsCancellationRequested)
            throw new IOException("电脑音频连接已断开：" + (await serverError!).Trim());
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        IsStreaming = false;
        stopped.Cancel();
        if (server is not null)
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
        }
        // Finish native cleanup before a new session can initialize the DLL's global state.
        if (library != 0)
        {
            cleanup?.Invoke();
            NativeLibrary.Free(library);
            library = 0;
        }
        if (server is not null)
        {
            await server.WaitForExitAsync();
            server.Dispose();
        }
        try
        {
            if (reverseCreated)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await bridge.RunAsync(["-s", serial, "reverse", "--remove", "localabstract:" + socketName], timeout.Token, 4);
            }
        }
        finally { stopped.Dispose(); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeInitialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int NativeInitializeDevice([MarshalAs(UnmanagedType.LPWStr)] string? endpointId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeListen(int port, nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCleanup();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NativeConnected([Out] StringBuilder buffer, int capacity);
}
