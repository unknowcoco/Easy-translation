using EasyTranslation.Config;
using Xunit;

namespace EasyTranslation.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var settings = new SettingsStore(_dir).Load();

        Assert.Equal("deepseek", settings.Provider);
        Assert.Equal("auto", settings.SourceLanguage);
        Assert.Equal("en", settings.TargetLanguage);
        Assert.Equal(6000, settings.MaxChars);
        Assert.True(settings.ChipEnabled);
        Assert.True(settings.RestoreClipboard);
        Assert.Equal(string.Empty, settings.ApiKeyProtected);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndDefaultsReturned()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json");

        var settings = new SettingsStore(_dir).Load();

        Assert.Equal("en", settings.TargetLanguage);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.bad")));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_dir);
        var original = new AppSettings
        {
            SourceLanguage = "zh",
            TargetLanguage = "ja",
            Tone = "formal",
            MaxChars = 1234,
            ChipEnabled = false,
            HotkeyEnabled = false,
            Hotkey = "Ctrl+Shift+F",
            ReplaceNativeMenu = true,
            RestoreClipboard = false,
            Autostart = true,
            LogLevel = "debug",
        };
        original.SetApiKey("sk-test-123");

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("zh", loaded.SourceLanguage);
        Assert.Equal("ja", loaded.TargetLanguage);
        Assert.Equal("formal", loaded.Tone);
        Assert.Equal(1234, loaded.MaxChars);
        Assert.False(loaded.ChipEnabled);
        Assert.False(loaded.HotkeyEnabled);
        Assert.Equal("Ctrl+Shift+F", loaded.Hotkey);
        Assert.True(loaded.ReplaceNativeMenu);
        Assert.False(loaded.RestoreClipboard);
        Assert.True(loaded.Autostart);
        Assert.Equal("debug", loaded.LogLevel);
        Assert.Equal("sk-test-123", loaded.GetApiKey());
    }

    [Fact]
    public void ApiKey_IsNotStoredInPlainText()
    {
        var settings = new AppSettings();
        settings.SetApiKey("sk-super-secret");

        Assert.DoesNotContain("sk-super-secret", settings.ApiKeyProtected);
        Assert.Equal("sk-super-secret", settings.GetApiKey());
    }

    [Fact]
    public void Normalize_FallsBackOnBadValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """
            {
              "Provider": "mystery",
              "TargetLanguage": "xx",
              "SourceLanguage": "yy",
              "Tone": "rude",
              "MaxChars": 99999,
              "LogLevel": "chatty"
            }
            """);

        var settings = new SettingsStore(_dir).Load();

        Assert.Equal("deepseek", settings.Provider);
        Assert.Equal("en", settings.TargetLanguage);
        Assert.Equal("auto", settings.SourceLanguage);
        Assert.Equal("default", settings.Tone);
        Assert.Equal(6000, settings.MaxChars);
        Assert.Equal("info", settings.LogLevel);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("th")]
    [InlineData("zh-Hant")]
    [InlineData("pt")]
    public void Normalize_AcceptsEveryCatalogTarget(string target)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), $$"""{ "TargetLanguage": "{{target}}" }""");

        Assert.Equal(target, new SettingsStore(_dir).Load().TargetLanguage);
    }

    [Fact]
    public void Normalize_ResetsSourceWhenItEqualsTarget()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "SourceLanguage": "ja", "TargetLanguage": "ja" }""");

        Assert.Equal("auto", new SettingsStore(_dir).Load().SourceLanguage);
    }

    [Fact]
    public void Normalize_ClampsMaxChars()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "MaxChars": 5 }""");

        Assert.Equal(200, new SettingsStore(_dir).Load().MaxChars);
    }
}
