using System.Diagnostics;
using System.Text;
using EasyTranslation.Interop;

namespace EasyTranslation.Input;

public interface IMenuCloser
{
    bool Close(IntPtr targetWindow);
}

/// <summary>
/// Closes the native context menu that the right click opened. Win32 menus are cancelled with
/// WM_CANCELMODE; Chromium-based menus listen to Esc. We then wait until the foreground window is
/// the target again, otherwise the Ctrl+C would be swallowed by the menu.
/// </summary>
public sealed class NativeMenuCloser : IMenuCloser
{
    public bool Close(IntPtr targetWindow) => Close(targetWindow, 600);

    public bool Close(IntPtr targetWindow, int timeoutMs)
    {
        if (targetWindow == IntPtr.Zero) return false;

        var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
        var menu = FindMenu(threadId);
        if (menu != IntPtr.Zero)
            NativeMethods.PostMessage(menu, NativeMethods.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);

        SendInputHelper.SendEscape();
        Thread.Sleep(120);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (NativeMethods.GetForegroundWindow() == targetWindow && FindMenu(threadId) == IntPtr.Zero)
                return true;
            Thread.Sleep(20);
        }

        return NativeMethods.GetForegroundWindow() == targetWindow;
    }

    private static IntPtr FindMenu(uint threadId)
    {
        var found = IntPtr.Zero;
        NativeMethods.EnumThreadWindows(threadId, (hWnd, _) =>
        {
            var sb = new StringBuilder(32);
            NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
            if (sb.ToString() == "#32768")
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }
}
