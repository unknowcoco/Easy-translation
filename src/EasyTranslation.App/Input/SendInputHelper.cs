using System.Runtime.InteropServices;
using EasyTranslation.Interop;
using EasyTranslation.Logging;

namespace EasyTranslation.Input;

public interface IInputSender
{
    void Copy();
    void Paste();
    void Escape();
}

/// <summary>Sends synthetic Ctrl+C / Ctrl+V / Esc using scan codes (layout independent).</summary>
public static class SendInputHelper
{
    public static void SendCopy()
    {
        SendCombo(NativeMethods.VK_C);
    }

    public static void SendPaste()
    {
        SendCombo(NativeMethods.VK_V);
    }

    public static void SendSelectAll()
    {
        SendCombo(NativeMethods.VK_A);
    }

    public static void SendUndo()
    {
        SendCombo(NativeMethods.VK_Z);
    }

    public static void SendEscape()
    {
        var scan = ScanCode(NativeMethods.VK_ESCAPE);
        Send(new[] { Key(scan, down: true), Key(scan, down: false) });
    }

    private static void SendCombo(int virtualKey)
    {
        var ctrl = ScanCode(NativeMethods.VK_LCONTROL);
        var key = ScanCode(virtualKey);
        var inputs = new[]
        {
            Key(ctrl, down: true),
            Key(key, down: true),
            Key(key, down: false),
            Key(ctrl, down: false),
        };
        Send(inputs);
    }

    private static NativeMethods.INPUT Key(ushort scan, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (down ? 0 : NativeMethods.KEYEVENTF_KEYUP),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static ushort ScanCode(int virtualKey)
    {
        var scan = (ushort)NativeMethods.MapVirtualKey((uint)virtualKey, NativeMethods.MAPVK_VK_TO_VSC);
        if (scan == 0) scan = (ushort)virtualKey;
        return scan;
    }

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
            Log.Warn($"SendInput injected {sent}/{inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
    }
}

public sealed class WindowsInputSender : IInputSender
{
    public void Copy() => SendInputHelper.SendCopy();
    public void Paste() => SendInputHelper.SendPaste();
    public void Escape() => SendInputHelper.SendEscape();
}
