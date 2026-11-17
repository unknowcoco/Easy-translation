using System.Text.Json;
using EasyTranslation.Logging;

namespace EasyTranslation.Config;

/// <summary>Persisted settings. The API key is stored DPAPI-protected, never in plain text.</summary>
public sealed class AppSettings
{
    public const string DefaultBaseUrl = "https://api.deepseek.com";
    public const string DefaultModel = "deepseek-chat";

    public string Provider { get; set; } = "deepseek";
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string Model { get; set; } = DefaultModel;
    public string ApiKeyProtected { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "en";
    public string Tone { get; set; } = "default";
    public int MaxChars { get; set; } = 6000;

    public bool ChipEnabled { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+E";
    public bool ReplaceNativeMenu { get; set; }
    public bool RestoreClipboard { get; set; } = true;
    public bool Autostart { get; set; }
    public bool Enabled { get; set; } = true;

    public string LogLevel { get; set; } = "info";

    public string GetApiKey() => SecretProtector.Unprotect(ApiKeyProtected);

    public void SetApiKey(string key) => ApiKeyProtected = SecretProtector.Protect(key ?? string.Empty);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public SettingsStore(string? directory = null)
    {
        if (directory != null)
        {
            Directory = directory;
            return;
        }

        // Prefer %APPDATA%; fall back to a writable location when the profile is locked down
        // (sandboxed sessions, roaming profile problems, portable installs).
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyTranslation"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyTranslation"),
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Path.GetTempPath(), "EasyTranslation"),
        };

        Directory = candidates[0];
        foreach (var candidate in candidates)
        {
            try
            {
                System.IO.Directory.CreateDirectory(candidate);
                Directory = candidate;
                break;
            }
            catch
            {
                // try the next candidate
            }
        }

        MigrateLegacySettings();
    }

    /// <summary>Copies settings from the pre-rename Zh2En folder on first launch.</summary>
    private void MigrateLegacySettings()
    {
        try
        {
            if (File.Exists(FilePath)) return;
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            };
            foreach (var root in roots)
            {
                var legacy = Path.Combine(root, "Zh2En", "settings.json");
                if (!File.Exists(legacy)) continue;
                File.Copy(legacy, FilePath, overwrite: false);
                Log.Info("migrated settings from legacy Zh2En folder");
                return;
            }
        }
        catch
        {
            // migration is best effort; defaults are fine
        }
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, "settings.json");

    public string LogsDirectory => Path.Combine(Directory, "logs");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Normalize(settings);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch
            {
                // ignore backup failures
            }
            Logging.Log.Warn($"settings.json unreadable, using defaults: {ex.GetType().Name}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Provider = (settings.Provider ?? "deepseek").Trim().ToLowerInvariant();
        if (settings.Provider is not ("deepseek" or "openai" or "deepl" or "echo"))
        {
            Logging.Log.Warn($"unknown provider '{settings.Provider}', falling back to deepseek");
            settings.Provider = "deepseek";
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl)) settings.BaseUrl = AppSettings.DefaultBaseUrl;
        if (string.IsNullOrWhiteSpace(settings.Model)) settings.Model = AppSettings.DefaultModel;

        var target = settings.TargetLanguage;
        if (!Languages.LanguageCatalog.TryGet(target, out _))
        {
            Logging.Log.Warn($"unknown target language '{target}', falling back to en");
            settings.TargetLanguage = "en";
        }

        if (settings.SourceLanguage != Languages.LanguageCatalog.Auto &&
            !Languages.LanguageCatalog.TryGet(settings.SourceLanguage, out _))
        {
            Logging.Log.Warn($"unknown source language '{settings.SourceLanguage}', falling back to auto");
            settings.SourceLanguage = Languages.LanguageCatalog.Auto;
        }

        if (settings.SourceLanguage == settings.TargetLanguage) settings.SourceLanguage = Languages.LanguageCatalog.Auto;

        settings.Tone = settings.Tone?.Trim().ToLowerInvariant() switch
        {
            "formal" or "casual" or "technical" => settings.Tone!.Trim().ToLowerInvariant(),
            _ => "default",
        };

        settings.MaxChars = Math.Clamp(settings.MaxChars <= 0 ? 6000 : settings.MaxChars, 200, 6000);
        settings.LogLevel = settings.LogLevel?.Trim().ToLowerInvariant() switch
        {
            "debug" or "warn" or "error" or "off" => settings.LogLevel!.Trim().ToLowerInvariant(),
            _ => "info",
        };
        return settings;
    }
}
