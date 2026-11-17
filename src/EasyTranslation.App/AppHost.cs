using System.Drawing;
using EasyTranslation.Clipboard;
using EasyTranslation.Config;
using EasyTranslation.Input;
using EasyTranslation.Interop;
using EasyTranslation.Languages;
using EasyTranslation.Logging;
using EasyTranslation.Translation;
using EasyTranslation.Tray;
using EasyTranslation.Ui;
using EasyTranslation.Workflow;

namespace EasyTranslation;

/// <summary>
/// Owns the tray icon, the input hooks, the floating surfaces and the workflow. Runs on the WinForms
/// UI thread; the low level hook callbacks stay on their own thread and only post here.
/// </summary>
public sealed class AppHost : ApplicationContext, IWorkflowUi
{
    private readonly SettingsStore _store;
    private readonly bool _forceEcho;
    private readonly SynchronizationContext _ui;
    private readonly WindowsClipboardService _clipboard = new();
    private readonly WindowsInputSender _input = new();
    private readonly FocusGuard _focus = new();
    private readonly NativeMenuCloser _menu = new();
    private readonly SelectionTracker _tracker = new();
    private readonly ChipForm _chip = new();
    private readonly ToastForm _toast = new();
    private readonly ContextMenuForm _takeoverMenu = new();
    private readonly TrayController _tray;
    private readonly HotkeyWindow _hotkey = new();
    private readonly LowLevelMouseHook _mouseHook;

    private AppSettings _settings;
    private ITranslator _translator;
    private TranslateReplaceWorkflow _workflow;
    private WorkflowContext? _pendingContext;
    private LowLevelKeyboardHook? _escapeHook;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _swallowChipDown;
    private bool _swallowMenuDown;
    private bool _chipHover;
    private int _menuHoverIndex = -1;

    public AppHost(SettingsStore store, bool forceEcho, bool openSettings = false)
    {
        _store = store;
        _forceEcho = forceEcho;
        var firstRun = !File.Exists(store.FilePath);
        _settings = store.Load();
        Log.Init(store.LogsDirectory, Environment.GetCommandLineArgs().Contains("--debug") ? "debug" : _settings.LogLevel);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _translator = TranslatorFactory.Create(_settings, _forceEcho);
        _workflow = BuildWorkflow();

        _tray = new TrayController(
            _settings,
            IconFactory.CreateTrayIcon(),
            OnEnabledChanged,
            OpenSettings,
            OpenLogs,
            OnAutostartChanged,
            ExitApp);
        _hotkey.Pressed += OnHotkey;
        _mouseHook = new LowLevelMouseHook(OnMouse);
        if (!_mouseHook.Start())
            Log.Error("low level mouse hook did not start");

        ApplyHotkey();
        AutostartManager.Apply(_settings.Autostart);

        if (openSettings) Post(OpenSettings);

        if (firstRun && !_forceEcho)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                _tray.ShowBalloon("Easy translation 已启动", "在任意软件中选中文字 → 右键 → 点浮条即可翻译。先右键托盘图标 →「设置…」填写 API Key。");
            };
            timer.Start();
        }
    }

    private TranslationDirection Direction => new(_settings.SourceLanguage, _settings.TargetLanguage);

    private TranslateReplaceWorkflow BuildWorkflow() => new(
        _clipboard,
        _input,
        _focus,
        _menu,
        _translator,
        this,
        new WorkflowOptions(
            _settings.SourceLanguage,
            _settings.TargetLanguage,
            _settings.Tone,
            _settings.MaxChars,
            _settings.RestoreClipboard));

    // ---- low level mouse hook (background thread) -------------------------------------------

    private bool _hookEventLogged;

    private bool OnMouse(MouseHookEvent e)
    {
        try
        {
            if (!_hookEventLogged)
            {
                _hookEventLogged = true;
                Log.Debug($"mouse hook first event: {e.Kind} at {e.Position}");
            }

            if (_settings.Enabled && _settings.ReplaceNativeMenu)
            {
                if (e.Kind == MouseHookKind.RightDown)
                {
                    var hasSelection = _tracker.Latest(DateTime.Now) is not null;
                    var at = e.Position;
                    Post(() => ShowTakeoverMenu(at, hasSelection));
                    return true;
                }

                if (e.Kind == MouseHookKind.RightUp) return true;
            }

            switch (e.Kind)
            {
                case MouseHookKind.Move:
                    _tracker.OnMove(e.Position);
                    UpdateHover(e.Position);
                    return false;

                case MouseHookKind.LeftDown:
                    if (_chip.IsOver(e.Position))
                    {
                        _swallowChipDown = true;
                        return true;
                    }

                    if (_takeoverMenu.Visible && _takeoverMenu.HitTest(e.Position) >= 0)
                    {
                        _swallowMenuDown = true;
                        return true;
                    }

                    if (_chip.Visible) Post(HideChip);
                    if (_takeoverMenu.Visible) Post(HideTakeoverMenu);
                    _tracker.OnLeftDown(e.Position, DateTime.Now);
                    return false;

                case MouseHookKind.LeftUp:
                    if (_swallowChipDown)
                    {
                        _swallowChipDown = false;
                        if (_chip.IsOver(e.Position)) Post(TriggerChip);
                        return true;
                    }

                    if (_swallowMenuDown)
                    {
                        _swallowMenuDown = false;
                        var index = _takeoverMenu.HitTest(e.Position);
                        Post(() => ActivateTakeover(index));
                        return true;
                    }

                    var released = _tracker.OnLeftUp(e.Position, NativeMethods.GetForegroundWindow(), DateTime.Now);
                    Log.Debug($"left up at {e.Position}, fg=0x{NativeMethods.GetForegroundWindow():X}, selection={(released is null ? "none" : released.Value.Bounds.ToString())}");
                    return false;

                case MouseHookKind.RightDown:
                    return HandleRightDown(e.Position);
            }
        }
        catch (Exception ex)
        {
            Log.Error("mouse hook dispatch failed", ex);
        }

        return false;
    }

    private bool HandleRightDown(Point point)
    {
        if (!_settings.Enabled) return false;
        var foreground = NativeMethods.GetForegroundWindow();
        var snapshot = _tracker.ForRightClick(point, foreground, DateTime.Now);
        Log.Debug($"right down at {point}, fg=0x{foreground:X}, selection={(snapshot is null ? "none" : snapshot.Value.Bounds.ToString())}");
        if (snapshot is null)
        {
            if (_chip.Visible) Post(HideChip);
            return false;
        }

        var context = new WorkflowContext(snapshot.Value.Window, point);
        Post(() => ShowChip(context));
        return false;
    }

    private void UpdateHover(Point point)
    {
        if (_chip.Visible)
        {
            var over = _chip.IsOver(point);
            if (over != _chipHover)
            {
                _chipHover = over;
                Post(() => _chip.MarkHover(over));
            }
        }

        if (_takeoverMenu.Visible)
        {
            var index = _takeoverMenu.HitTest(point);
            if (index != _menuHoverIndex)
            {
                _menuHoverIndex = index;
                Post(() => _takeoverMenu.MarkHover(point));
            }
        }
    }

    // ---- UI thread actions ------------------------------------------------------------------

    private void ShowChip(WorkflowContext context)
    {
        Log.Debug($"show chip: busy={_busy}, enabled={_settings.Enabled}, chipEnabled={_settings.ChipEnabled}");
        if (_busy || !_settings.Enabled || !_settings.ChipEnabled) return;
        _pendingContext = context;
        _chip.ShowAt(context.Cursor, Direction.ChipText);
    }

    private void HideChip()
    {
        _chip.HideChip();
        _chipHover = false;
    }

    private void TriggerChip()
    {
        HideChip();
        var context = _pendingContext;
        _pendingContext = null;
        Log.Debug($"chip clicked: busy={_busy}, context={(context is null ? "none" : context.TargetWindow.ToString("X"))}");
        if (_busy || context is null) return;
        if (NativeMethods.GetForegroundWindow() != context.TargetWindow)
        {
            _toast.ShowToast("目标窗口已切换，已取消。", true, Cursor.Position);
            return;
        }

        StartWorkflow(context);
    }

    private void ShowTakeoverMenu(Point at, bool hasSelection)
    {
        if (_busy || !_settings.Enabled) return;
        HideChip();
        var target = LanguageCatalog.DisplayName(_settings.TargetLanguage);
        var entries = new List<MenuEntry>
        {
            new($"转换成{target}", hasSelection, () => BeginFromMenu(at, hasSelection)),
            MenuEntry.Separator(),
            new("复制　Ctrl+C", hasSelection, SendInputHelper.SendCopy),
            new("粘贴　Ctrl+V", true, SendInputHelper.SendPaste),
            new("全选　Ctrl+A", true, SendInputHelper.SendSelectAll),
            new("撤销　Ctrl+Z", true, SendInputHelper.SendUndo),
        };
        _takeoverMenu.ShowMenu(at, entries);
    }

    private void HideTakeoverMenu()
    {
        _takeoverMenu.HideMenu();
        _menuHoverIndex = -1;
    }

    private void BeginFromMenu(Point at, bool hasSelection)
    {
        HideTakeoverMenu();
        if (!hasSelection)
        {
            _toast.ShowToast("请先选中要翻译的文字。", true, at);
            return;
        }

        StartWorkflow(new WorkflowContext(NativeMethods.GetForegroundWindow(), at));
    }

    private void ActivateTakeover(int index)
    {
        var action = _takeoverMenu.ActionAt(index);
        HideTakeoverMenu();
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("menu action failed", ex);
        }
    }

    private void StartWorkflow(WorkflowContext context)
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        _escapeHook = new LowLevelKeyboardHook((vk, injected) =>
        {
            // Ignore the Esc that NativeMenuCloser injects to close the context menu.
            if (injected || vk != NativeMethods.VK_ESCAPE) return false;
            Post(CancelWorkflow);
            return true;
        });
        _escapeHook.Start();

        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            WorkflowResult result;
            try
            {
                result = await _workflow.RunAsync(context, token);
            }
            catch (Exception ex)
            {
                Log.Error("workflow failed outside its own handler", ex);
                result = new WorkflowResult(WorkflowStatus.UnexpectedError, "发生意外错误，请查看日志。");
            }

            Log.Info($"workflow done: {result.Status} ({result.Message})");
            Post(EndWorkflow);
        });
    }

    private void CancelWorkflow() => _cts?.Cancel();

    private void EndWorkflow()
    {
        _busy = false;
        _cts?.Dispose();
        _cts = null;
        _escapeHook?.Dispose();
        _escapeHook = null;
    }

    private void OnHotkey()
    {
        if (!_settings.Enabled || _busy) return;
        StartWorkflow(new WorkflowContext(NativeMethods.GetForegroundWindow(), Cursor.Position));
    }

    // ---- workflow UI --------------------------------------------------------------------

    void IWorkflowUi.ShowBusy(Point at) => Post(() => _toast.ShowToast("翻译中…", false, at, busy: true));

    void IWorkflowUi.HideBusy() => Post(() => _toast.HideToast());

    void IWorkflowUi.ShowResult(string message, bool isError, Point at) =>
        Post(() => _toast.ShowToast(message, isError, at));

    private void Post(Action action)
    {
        _ui.Post(_ => action(), null);
    }

    // ---- settings / tray ------------------------------------------------------------------

    private void OnEnabledChanged(bool enabled)
    {
        _settings.Enabled = enabled;
        SaveSettings();
        if (!enabled)
        {
            HideChip();
            HideTakeoverMenu();
            _hotkey.Unregister();
        }
        else
        {
            ApplyHotkey();
        }
    }

    private void OnAutostartChanged(bool enabled)
    {
        _settings.Autostart = enabled;
        AutostartManager.Apply(enabled);
        SaveSettings();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings.Clone(), _store, _forceEcho);
        if (form.ShowDialog() != DialogResult.OK || form.Result is null) return;
        _settings = form.Result;
        SaveSettings();
        ApplySettings();
    }

    private void ApplySettings()
    {
        Log.SetLevel(_settings.LogLevel);
        _translator = TranslatorFactory.Create(_settings, _forceEcho);
        _workflow = BuildWorkflow();
        _tray.Update(_settings);
        ApplyHotkey(showError: true);
        AutostartManager.Apply(_settings.Autostart);
        if (!_settings.Enabled)
        {
            HideChip();
            HideTakeoverMenu();
        }
    }

    private void ApplyHotkey(bool showError = false)
    {
        if (!_settings.Enabled || !_settings.HotkeyEnabled)
        {
            _hotkey.Unregister();
            return;
        }

        if (!_hotkey.Register(_settings.Hotkey, out var error) && showError)
            _toast.ShowToast(error, true, Cursor.Position);
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("settings save failed", ex);
            _toast.ShowToast("设置保存失败：" + ex.Message, true, Cursor.Position);
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_store.LogsDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "explorer.exe", $"\"{_store.LogsDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"open logs failed: {ex.Message}");
        }
    }

    private void ExitApp()
    {
        SaveSettings();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mouseHook.Dispose();
            _escapeHook?.Dispose();
            _hotkey.Dispose();
            _tray.Dispose();
            _toast.Dispose();
            _takeoverMenu.Dispose();
            _chip.Dispose();
            _cts?.Dispose();
        }

        base.Dispose(disposing);
    }
}
