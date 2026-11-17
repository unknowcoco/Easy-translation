using EasyTranslation.Interop;
using EasyTranslation.Logging;

namespace EasyTranslation.Input;

/// <summary>Hidden window that owns the global hotkey registration (WM_HOTKEY).</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x5A32;
    private bool _registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "EasyTranslation.Hotkey" });
    }

    public event Action? Pressed;

    public bool Register(string spec, out string error)
    {
        Unregister();
        if (!TryParse(spec, out var modifiers, out var virtualKey))
        {
            error = $"无法识别的快捷键「{spec}」";
            return false;
        }

        if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            error = $"快捷键 {spec} 已被其他程序占用";
            return false;
        }

        _registered = true;
        error = string.Empty;
        return true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            try
            {
                Pressed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("hotkey handler failed", ex);
            }

            return;
        }

        base.WndProc(ref m);
    }

    private static bool TryParse(string spec, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win": modifiers |= 0x0008; break;
                default: return false;
            }
        }

        if (!Enum.TryParse<Keys>(parts[^1], ignoreCase: true, out var key) || key == Keys.None) return false;
        virtualKey = (uint)key;
        return true;
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
