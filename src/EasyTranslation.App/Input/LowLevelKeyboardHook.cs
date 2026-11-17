using System.Runtime.InteropServices;
using EasyTranslation.Interop;
using EasyTranslation.Logging;

namespace EasyTranslation.Input;

/// <summary>Temporary WH_KEYBOARD_LL used to cancel a running translation with Esc.</summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly Func<int, bool, bool> _onKeyDown;
    private readonly NativeMethods.HookProc _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _disposed;

    public LowLevelKeyboardHook(Func<int, bool, bool> onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _proc = Callback;
    }

    public bool Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "zh2en-keyboard-hook" };
        _thread.Start();
        return _ready.Wait(2000) && _hook != IntPtr.Zero;
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Warn($"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");
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
        if (nCode >= 0 && !_disposed && (int)wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            try
            {
                var injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
                if (_onKeyDown((int)data.vkCode, injected)) return (IntPtr)1;
            }
            catch (Exception ex)
            {
                Log.Error("keyboard hook handler failed", ex);
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
