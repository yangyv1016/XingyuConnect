using System.Text;

namespace XingyuConnect;

internal static class AppLog
{
    private static readonly object gate = new();
    private static readonly Encoding encoding = new UTF8Encoding(false);
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "Logs");
    public static string? FilePath { get; private set; }
    public static string? LastError { get; private set; }

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"XingyuConnect-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            FilePath = path;
            Write($"程序启动 · 版本 {typeof(AppLog).Assembly.GetName().Version} · {Environment.OSVersion} · PID {Environment.ProcessId}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
    }

    public static void Write(string message)
    {
        lock (gate)
        {
            if (FilePath is null) return; // A failed startup is reported by the log-folder entry.
            try
            {
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}", encoding);
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
        }
    }
}
