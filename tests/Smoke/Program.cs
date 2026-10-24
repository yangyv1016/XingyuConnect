using System.Reflection;
using System.Reflection.PortableExecutable;
using XingyuConnect;

internal static class Smoke
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppLog.Start();
        try
        {
            Check(AppLog.FilePath is not null && AppLog.LastError is null, "session log created");
            var payload = "中文与表情😸 " + new string('x', 20000);
            AppLog.Write(payload);
            Parallel.For(0, 80, index => AppLog.Write("parallel-entry-" + index));
            var text = File.ReadAllText(AppLog.FilePath!);
            Check(text.Contains(payload) && text.Contains("程序启动"), "UTF-8 session data and long records retained");
            Check(text.Split('\n').Count(line => line.Contains("parallel-entry-")) == 80, "concurrent log writes stay complete");
            using (var blocked = new FileStream(AppLog.FilePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                AppLog.Write("controlled write failure");
                Check(AppLog.LastError is not null, "log write failure does not crash the client");
            }
            AppLog.Write("write recovered");
            Check(AppLog.LastError is null && File.ReadAllText(AppLog.FilePath!).Contains("write recovered"), "logging recovers after file access returns");
            using var launcher = new MainForm { ShowInTaskbar = false };
            var tray = (NotifyIcon)typeof(MainForm).GetField("trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(launcher)!;
            tray.Visible = false;
            Check(!Descendants(launcher).OfType<Button>().Any(button => button.Text is "运行记录" or "运行日志" or "收起记录"), "main window has no command or log panel entry");
            var root = launcher.Controls.OfType<TableLayoutPanel>().Single();
            Check(root.RowCount == 3 && root.RowStyles.Count == 3, "removed log panel leaves no reserved row");
            var devices = (ListBox)typeof(MainForm).GetField("deviceList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(launcher)!;
            Check(devices.Enabled, "device list remains enabled");
            using var guide = new ConnectionGuideForm();
            Check(Descendants(guide).OfType<Button>().Any(button => button.Text == "打开日志文件夹"), "connection guide contains graphical log-folder entry");
            Check(!launcher.Visible && !guide.Visible, "checks show no desktop windows and send no input");
            if (args.Length == 1)
            {
                var package = Path.GetFullPath(args[0]);
                using var executable = File.OpenRead(Path.Combine(package, "XingyuConnect.exe"));
                using var pe = new PEReader(executable);
                Check(pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui, "user executable is Windows GUI, without console allocation");
                foreach (var relative in new[] { "使用说明.md", "第三方声明.md", "scrcpy/adb.exe", "scrcpy/scrcpy.exe", "scrcpy/scrcpy-server", "audioshare/audio_capture_xingyu.dll", "audioshare/server", "audioshare/source/audio_capture.cpp", "audioshare/AudioShare-v2.2.2-source.zip", "adbkeyboard/ADBKeyboard.apk", "adbkeyboard/source-v2.5-dev.zip", "vbcable/VBCABLE_Driver_Pack45.zip" })
                    Check(File.Exists(Path.Combine(package, relative)), "package includes " + relative);
                Check(!Directory.GetFiles(package, "*", SearchOption.AllDirectories).Any(path => new[] { ".bat", ".cmd", ".ps1", ".vbs", ".pdb", ".lib", ".psd" }.Contains(Path.GetExtension(path).ToLowerInvariant())), "user package excludes terminal launchers and debug artifacts");
                Check(Directory.GetFiles(package, "*.exe").Length == 1, "only the graphical client is an executable entry at package root");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (AppLog.FilePath is not null) File.Delete(AppLog.FilePath); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
