using EasyTranslation.Config;
using EasyTranslation.Logging;

namespace EasyTranslation;

internal static class Program
{
    private static void RunDiagnose()
    {
        var store = new SettingsStore();
        var lines = new List<string>
        {
            "base=" + AppContext.BaseDirectory,
            "user=" + Environment.UserName + " domain=" + Environment.UserDomainName,
            "appdata=" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "store=" + store.Directory,
            "logs=" + store.LogsDirectory,
        };

        try
        {
            Directory.CreateDirectory(store.LogsDirectory);
            lines.Add("create=ok");
        }
        catch (Exception ex)
        {
            lines.Add("create=fail " + ex.GetType().Name + ": " + ex.Message);
        }

        Log.Init(store.LogsDirectory, "debug");
        lines.Add("logfile=" + Log.LogFile);
        Log.Info("diagnose run");
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnose.txt"), string.Join(Environment.NewLine, lines));
        }
        catch
        {
            // diagnostics must not crash
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var forceEcho = args.Any(a => string.Equals(a, "--echo", StringComparison.OrdinalIgnoreCase));
        var openSettings = args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => string.Equals(a, "--diagnose", StringComparison.OrdinalIgnoreCase)))
        {
            RunDiagnose();
            return;
        }

        using var instance = new Mutex(initiallyOwned: true, @"Local\EasyTranslation.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Easy translation 已经在运行（请查看系统托盘）。", "Easy translation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var store = new SettingsStore();
        Log.Init(store.LogsDirectory, "info");
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("unhandled exception", e.ExceptionObject as Exception);

        try
        {
            using var host = new AppHost(store, forceEcho, openSettings);
            Application.Run(host);
        }
        catch (Exception ex)
        {
            Log.Error("fatal error", ex);
            MessageBox.Show("Easy translation 启动失败：" + ex.Message + "\n\n日志：" + store.LogsDirectory,
                "Easy translation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
