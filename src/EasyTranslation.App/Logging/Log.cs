namespace EasyTranslation.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Off = 4,
}

/// <summary>Small rotating file logger. Never log the translated text itself.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _logFile;
    private static LogLevel _min = LogLevel.Info;
    private static bool _initialised;

    public static string? LogFile => _logFile;

    public static void Init(string logsDirectory, string level)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(logsDirectory);
                _min = ParseLevel(level);
                _logFile = Path.Combine(logsDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                _initialised = true;
            }
            catch
            {
                _initialised = false;
            }
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, "DBG", message, null);
    public static void Info(string message) => Write(LogLevel.Info, "INF", message, null);
    public static void Warn(string message) => Write(LogLevel.Warn, "WRN", message, null);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, "ERR", message, ex);
    public static void Error(string message, string detail) => Write(LogLevel.Error, "ERR", message, new Exception(detail));

    private static void Write(LogLevel level, string tag, string message, Exception? ex)
    {
        if (!_initialised || level < _min || _min == LogLevel.Off) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}";
        if (ex != null) line += Environment.NewLine + ex;
        lock (Gate)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logFile!, line + Environment.NewLine);
            }
            catch
            {
                // logging must never take the app down
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (_logFile == null || !File.Exists(_logFile)) return;
        var info = new FileInfo(_logFile);
        if (info.Length < 1024 * 1024) return;
        var baseName = Path.Combine(Path.GetDirectoryName(_logFile)!, Path.GetFileNameWithoutExtension(_logFile));
        for (var i = 2; i >= 1; i--)
        {
            var from = $"{baseName}.{i}.log";
            var to = $"{baseName}.{i + 1}.log";
            if (File.Exists(from)) File.Move(from, to, true);
        }
        File.Move(_logFile, $"{baseName}.1.log", true);
        File.WriteAllText(_logFile, string.Empty);
    }

    public static void SetLevel(string level)
    {
        lock (Gate) _min = ParseLevel(level);
    }

    public static LogLevel ParseLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "debug" => LogLevel.Debug,
        "warn" or "warning" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "off" => LogLevel.Off,
        _ => LogLevel.Info,
    };
}
