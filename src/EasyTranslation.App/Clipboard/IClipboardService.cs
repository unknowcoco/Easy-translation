namespace EasyTranslation.Clipboard;

public interface IClipboardService
{
    /// <summary>Win32 clipboard sequence number; changes whenever any process writes the clipboard.</summary>
    long Sequence { get; }

    string? GetText();

    bool SetText(string text);

    IClipboardBackup Backup();
}

public interface IClipboardBackup : IDisposable
{
    /// <summary>Remember the sequence number our translated text got, so we only restore over our own write.</summary>
    void MarkWritten(long sequence);

    /// <summary>Keep our written text on the clipboard (do not restore the previous contents).</summary>
    void Keep();

    void Restore();
}
