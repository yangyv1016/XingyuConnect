using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Exceptions;
using AdvancedSharpAdbClient.Models;

namespace XingyuConnect;

internal sealed record PhoneFile(string Name, string Path, bool IsDirectory, ulong Size, DateTimeOffset Modified);
internal sealed record PhoneApp(string Name, string PackageName, bool IsSystem);

internal sealed class PhoneContentService(DeviceBridge bridge, string serial)
{
    private readonly DeviceData device = new() { Serial = serial, State = AdvancedSharpAdbClient.Models.DeviceState.Online };
    private readonly FileTransferBudget budget = FileTransferBudget.ForDevice(bridge, serial);

    public static string JoinPath(string directory, string name) => directory.TrimEnd('/') + "/" + name;
    public static string QuoteShell(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    public async Task<List<PhoneFile>> ListFilesAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var features = (await new AdbClient().GetFeatureSetAsync(device, cancellationToken).ConfigureAwait(false)).ToHashSet();
            using var sync = new SyncService(device);
            var directory = await sync.StatAsync(path, cancellationToken).ConfigureAwait(false);
            var type = directory.FileMode & UnixFileStatus.TypeMask;
            if (type is not UnixFileStatus.Directory and not UnixFileStatus.SymbolicLink)
                throw new IOException("目录不存在或无法访问：" + path);
            List<PhoneFile> files;
            if (features.Contains("ls_v2"))
            {
                var entries = await sync.GetDirectoryListingExAsync(path, cancellationToken).ConfigureAwait(false);
                var unreadable = entries.FirstOrDefault(entry => entry.Error != UnixErrorCode.Default);
                if (unreadable is not null) throw new IOException("无法读取目录项：" + unreadable.Path + "（" + unreadable.Error + "）");
                files = entries.Where(entry => entry.Path is not "." and not "..").Select(entry => new PhoneFile(entry.Path,
                    JoinPath(path, entry.Path), (entry.FileMode & UnixFileStatus.TypeMask) == UnixFileStatus.Directory, entry.Size, entry.ModifiedTime)).ToList();
            }
            else
            {
                var entries = await sync.GetDirectoryListingAsync(path, cancellationToken).ConfigureAwait(false);
                files = entries.Where(entry => entry.Path is not "." and not "..").Select(entry => new PhoneFile(entry.Path,
                    JoinPath(path, entry.Path), (entry.FileMode & UnixFileStatus.TypeMask) == UnixFileStatus.Directory, entry.Size, entry.Time)).ToList();
            }
            return files.OrderByDescending(file => file.IsDirectory).ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (AdbException ex) { throw new IOException("无法读取手机目录：" + ex.Message, ex); }
    }

    public async Task DownloadAsync(string remotePath, string localPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temporaryPath = localPath + ".xingyu-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await ReadFileAsync(remotePath, stream, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, localPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    public async Task ReadFileAsync(string remotePath, Stream output, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            var features = (await new AdbClient().GetFeatureSetAsync(device, cancellationToken).ConfigureAwait(false)).ToHashSet();
            using var sync = new SyncService(device);
            using var background = new BackgroundTransferStream(output, budget);
            await sync.PullAsync(remotePath, background, update => progress?.Report(update.ProgressPercentage),
                features.Contains("sendrecv_v2"), cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException ex) { throw new IOException("下载未完成：" + ex.Message, ex); }
        // The library's PullAsync reads StatAsync.Result inside a continuation.
        catch (AggregateException ex) when (ex.GetBaseException() is OperationCanceledException)
        { throw new OperationCanceledException("下载已取消", ex, cancellationToken); }
        catch (AggregateException ex) when (ex.GetBaseException() is AdbException or IOException or SocketException)
        { throw new IOException("下载未完成：" + ex.GetBaseException().Message, ex); }
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var directory = remotePath[..remotePath.LastIndexOf('/')];
        var temporaryPath = JoinPath(directory, ".xingyu-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            var features = (await new AdbClient().GetFeatureSetAsync(device, cancellationToken).ConfigureAwait(false)).ToHashSet();
            using var sync = new SyncService(device);
            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var background = new BackgroundTransferStream(stream, budget);
            await sync.PushAsync(background, temporaryPath, UnixFileStatus.DefaultFileMode, File.GetLastWriteTimeUtc(localPath),
                update => progress?.Report(update.ProgressPercentage), features.Contains("sendrecv_v2"), cancellationToken).ConfigureAwait(false);
            await bridge.RunAsync(["-s", serial, "shell", "mv -f -- " + QuoteShell(temporaryPath) + " " + QuoteShell(remotePath)], cancellationToken);
            await bridge.RunAsync(["-s", serial, "shell", "am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d " + QuoteShell("file://" + remotePath)], cancellationToken);
        }
        catch (Exception ex) when (ex is AdbException or IOException or SocketException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await bridge.RunAsync(["-s", serial, "shell", "rm -f -- " + QuoteShell(temporaryPath)], cleanup.Token, 4);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                throw new IOException("上传未完成，手机临时文件未能清理：" + temporaryPath, new AggregateException(ex, cleanupError));
            }
            if (ex is AdbException) throw new IOException("上传未完成：" + ex.Message, ex);
            throw;
        }
    }

    public async Task<List<PhoneApp>> ListAppsAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(bridge.ScrcpyPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        startInfo.Environment["ADB"] = bridge.AdbPath;
        foreach (var argument in new[] { "-s", serial, "--list-apps" }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output + "\n" + await errors;
            if (process.ExitCode != 0) throw new IOException("无法读取手机应用：" + text.Trim());
            return text.Split('\n').Select(line => Regex.Match(line.Trim(), @"^([*\-])\s+(.+?)\s+([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)\s*$"))
                .Where(match => match.Success).Select(match => new PhoneApp(match.Groups[2].Value.Trim(), match.Groups[3].Value, match.Groups[1].Value == "*"))
                .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("应用列表读取超时，请重新连接后刷新。"); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
