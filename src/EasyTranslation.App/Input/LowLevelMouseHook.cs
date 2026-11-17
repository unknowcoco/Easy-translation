using System.Runtime.InteropServices;
using EasyTranslation.Interop;
using EasyTranslation.Logging;

namespace EasyTranslation.Input;

public enum MouseHookKind
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
}

public readonly record struct MouseHookEvent(MouseHookKind Kind, Point Position);

/// <summary>
/// WH_MOUSE_LL lives on its own thread with its own message loop. The callback must stay fast
/// (Windows silently unhooks a hook that is too slow); expensive work is posted to the UI thread.
/// </summary>
public sealed class LowLevelMouseHook : IDisposable
{
    private readonly Func<MouseHookEvent, bool> _handler;
    private readonly NativeMethods.HookProc _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _disposed;

    public LowLevelMouseHook(Func<MouseHookEvent, bool> handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    public bool Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "zh2en-mouse-hook" };
        _thread.Start();
        return _ready.Wait(3000) && _hook != IntPtr.Zero;
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Error($"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
        _ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var kind = (int)wParam switch
            {
                NativeMethods.WM_MOUSEMOVE => MouseHookKind.Move,
                NativeMethods.WM_LBUTTONDOWN => MouseHookKind.LeftDown,
                NativeMethods.WM_LBUTTONUP => MouseHookKind.LeftUp,
                NativeMethods.WM_RBUTTONDOWN => MouseHookKind.RightDown,
                NativeMethods.WM_RBUTTONUP => MouseHookKind.RightUp,
                NativeMethods.WM_MBUTTONDOWN => MouseHookKind.MiddleDown,
                NativeMethods.WM_MBUTTONUP => MouseHookKind.MiddleUp,
                _ => (MouseHookKind?)null,
            };

            if (kind is { } k)
            {
                try
                {
                    if (_handler(new MouseHookEvent(k, new Point(data.pt.X, data.pt.Y))))
                        return (IntPtr)1;
                }
                catch (Exception ex)
                {
                    Log.Error("mouse hook handler failed", ex);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
        _ready.Dispose();
    }
}
