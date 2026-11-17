using EasyTranslation.Logging;

namespace EasyTranslation.Clipboard;

/// <summary>Runs clipboard work on a throwaway STA thread so the UI thread can never hang on a busy clipboard.</summary>
internal static class StaClipboard
{
    public static T? Run<T>(Func<T> func, int timeoutMs = 2000)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        })
        {
            IsBackground = true,
            Name = "zh2en-clipboard",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            Log.Warn("clipboard operation timed out (another app is holding the clipboard)");
            return default;
        }

        if (error != null)
            throw new InvalidOperationException("clipboard operation failed: " + error.Message, error);

        return result;
    }
}
