using System.Runtime.InteropServices;
using EasyTranslation.Interop;
using EasyTranslation.Logging;

namespace EasyTranslation.Input;

public readonly record struct FocusSnapshot(IntPtr Foreground, IntPtr Focus, Point Caret, bool HasCaret);

public interface IFocusGuard
{
    FocusSnapshot Capture(IntPtr targetWindow);

    bool IsStillValid(FocusSnapshot snapshot);

    bool TryRestore(IntPtr targetWindow, FocusSnapshot snapshot);
}

/// <summary>Guards the "paste back" step: never paste into a different window or caret position.</summary>
public sealed class FocusGuard : IFocusGuard
{
    private const int CaretTolerancePx = 4;

    public FocusSnapshot Capture(IntPtr targetWindow)
    {
        var info = GetThreadInfo(targetWindow);
        var hasCaret = info.hwndCaret != IntPtr.Zero || info.rcCaret.Width > 0 || info.rcCaret.Height > 0;
        return new FocusSnapshot(
            NativeMethods.GetForegroundWindow(),
            info.hwndFocus,
            new Point(info.rcCaret.Left, info.rcCaret.Top),
            hasCaret);
    }

    public bool IsStillValid(FocusSnapshot snapshot)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (snapshot.Foreground != IntPtr.Zero && foreground != snapshot.Foreground) return false;

        var info = GetThreadInfo(foreground);
        if (snapshot.Focus != IntPtr.Zero && info.hwndFocus != snapshot.Focus) return false;

        if (snapshot.HasCaret && info.hwndCaret != IntPtr.Zero)
        {
            if (Math.Abs(info.rcCaret.Left - snapshot.Caret.X) > CaretTolerancePx) return false;
            if (Math.Abs(info.rcCaret.Top - snapshot.Caret.Y) > CaretTolerancePx) return false;
        }

        return true;
    }

    public bool TryRestore(IntPtr targetWindow, FocusSnapshot snapshot)
    {
        if (targetWindow == IntPtr.Zero) return false;
        try
        {
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                NativeMethods.SetForegroundWindow(targetWindow);
                Thread.Sleep(30);
            }

            var targetThread = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();
            var attached = false;
            if (targetThread != 0 && targetThread != currentThread)
                attached = NativeMethods.AttachThreadInput(currentThread, targetThread, true);

            try
            {
                if (snapshot.Focus != IntPtr.Zero)
                    NativeMethods.SetFocus(snapshot.Focus);
            }
            finally
            {
                if (attached)
                    NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"focus restore failed: {ex.Message}");
        }

        Thread.Sleep(20);
        return NativeMethods.GetForegroundWindow() == targetWindow;
    }

    private static NativeMethods.GUITHREADINFO GetThreadInfo(IntPtr window)
    {
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
        };

        if (window == IntPtr.Zero) return info;
        var threadId = NativeMethods.GetWindowThreadProcessId(window, out _);
        if (threadId == 0) return info;
        NativeMethods.GetGUIThreadInfo(threadId, ref info);
        return info;
    }
}
