using EasyTranslation.Interop;
using EasyTranslation.Logging;
using WinFormsClipboard = System.Windows.Forms.Clipboard;

namespace EasyTranslation.Clipboard;

/// <summary>
/// Snapshot of the clipboard taken before we overwrite it. Restore only happens if the clipboard
/// still contains exactly the text we wrote (sequence number unchanged).
/// </summary>
internal sealed class ClipboardBackup : IClipboardBackup
{
    private readonly IDataObject? _data;
    private readonly string? _text;
    private readonly uint _snapshotSequence;
    private long _writtenSequence = -1;
    private bool _restored;
    private bool _keep;

    private ClipboardBackup(IDataObject? data, string? text, uint snapshotSequence)
    {
        _data = data;
        _text = text;
        _snapshotSequence = snapshotSequence;
    }

    public static ClipboardBackup Empty { get; } = new(null, null, 0);

    public static ClipboardBackup Capture()
    {
        try
        {
            var sequence = NativeMethods.GetClipboardSequenceNumber();
            var data = WinFormsClipboard.GetDataObject();
            var text = WinFormsClipboard.ContainsText(TextDataFormat.UnicodeText)
                ? WinFormsClipboard.GetText(TextDataFormat.UnicodeText)
                : null;
            return new ClipboardBackup(data, text, sequence);
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard capture failed: {ex.Message}");
            return Empty;
        }
    }

    public void MarkWritten(long sequence) => _writtenSequence = sequence;

    public void Keep() => _keep = true;

    public void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (_keep) return;

        try
        {
            StaClipboard.Run(() =>
            {
                var current = NativeMethods.GetClipboardSequenceNumber();
                // We only overwrite our own translated text; when Ctrl+C did not change the clipboard
                // at all there is nothing to restore either.
                if (_writtenSequence > 0)
                {
                    if (current != (uint)_writtenSequence)
                    {
                        Log.Debug("clipboard changed after our write, not restoring");
                        return true;
                    }
                }
                else if (current == _snapshotSequence)
                {
                    return true;
                }

                if (_data != null)
                {
                    try
                    {
                        WinFormsClipboard.SetDataObject(_data, copy: true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"clipboard data restore failed ({ex.Message}), falling back to text");
                    }
                }

                if (_text != null) WinFormsClipboard.SetText(_text);
                return true;
            }, 1500);
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard restore failed: {ex.Message}");
        }
    }

    public void Dispose() => Restore();
}

public sealed class WindowsClipboardService : IClipboardService
{
    public long Sequence => NativeMethods.GetClipboardSequenceNumber();

    public string? GetText()
    {
        try
        {
            return StaClipboard.Run(() =>
            {
                try
                {
                    return WinFormsClipboard.ContainsText(TextDataFormat.UnicodeText)
                        ? WinFormsClipboard.GetText(TextDataFormat.UnicodeText)
                        : null;
                }
                catch (Exception ex)
                {
                    Log.Warn($"clipboard read failed: {ex.Message}");
                    return null;
                }
            }, 1500);
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard read failed: {ex.Message}");
            return null;
        }
    }

    public bool SetText(string text)
    {
        try
        {
            return StaClipboard.Run(() =>
            {
                try
                {
                    WinFormsClipboard.SetText(text);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"clipboard write failed: {ex.Message}");
                    return false;
                }
            }, 1500);
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard write failed: {ex.Message}");
            return false;
        }
    }

    public IClipboardBackup Backup()
    {
        try
        {
            return StaClipboard.Run(ClipboardBackup.Capture, 1500) ?? ClipboardBackup.Empty;
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard backup failed: {ex.Message}");
            return ClipboardBackup.Empty;
        }
    }
}
