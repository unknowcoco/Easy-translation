using EasyTranslation.Config;

namespace EasyTranslation.Tray;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly Icon _ownedIcon;

    public TrayController(
        AppSettings settings,
        Icon icon,
        Action<bool> onEnabledChanged,
        Action onSettings,
        Action onOpenLogs,
        Action<bool> onAutostartChanged,
        Action onExit)
    {
        _ownedIcon = icon;

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        _enabledItem = new ToolStripMenuItem("启用（右键浮条）") { CheckOnClick = true, Checked = settings.Enabled };
        _enabledItem.CheckedChanged += (_, _) => onEnabledChanged(_enabledItem.Checked);
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => onSettings();
        menu.Items.Add(settingsItem);

        var logsItem = new ToolStripMenuItem("打开日志目录");
        logsItem.Click += (_, _) => onOpenLogs();
        menu.Items.Add(logsItem);

        _autostartItem = new ToolStripMenuItem("开机自动启动") { CheckOnClick = true, Checked = settings.Autostart };
        _autostartItem.CheckedChanged += (_, _) => onAutostartChanged(_autostartItem.Checked);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => onExit();
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "Easy translation：选中文字右键即可翻译",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => onSettings();
    }

    public void Update(AppSettings settings)
    {
        _enabledItem.Checked = settings.Enabled;
        _autostartItem.Checked = settings.Autostart;
    }

    public void ShowBalloon(string title, string text, int timeoutMs = 5000)
    {
        try
        {
            _icon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
        }
        catch
        {
            // balloons are best effort
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon.Dispose();
    }
}
