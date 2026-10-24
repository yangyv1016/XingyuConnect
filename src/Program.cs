namespace XingyuConnect;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppLog.Start();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("未处理的进程异常：" + e.ExceptionObject);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
        {
            AppLog.Write("未处理的界面异常：" + e.Exception);
            MessageBox.Show("程序遇到错误，请重新打开。运行日志可在“使用说明”中查看。", "星语互联", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        };
        try
        {
            // Internal elevated helper for the graphical driver initialization action.
            if (args.Length is 1 or 2 && args[0] == "--initialize-audio")
            {
                try { AudioDriver.Initialize(args.Length == 2 ? args[1] : null); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
                {
                    AppLog.Write("播放驱动初始化失败：" + ex);
                    Environment.ExitCode = 1;
                    MessageBox.Show(ex.Message, "初始化播放驱动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            Application.Run(new MainForm());
        }
        finally { AppLog.Write("程序退出"); }
    }
}
