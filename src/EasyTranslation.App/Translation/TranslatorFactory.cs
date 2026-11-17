using EasyTranslation.Config;

namespace EasyTranslation.Translation;

public static class TranslatorFactory
{
    public static ITranslator Create(AppSettings settings, bool forceEcho = false)
    {
        if (forceEcho || string.Equals(settings.Provider, "echo", StringComparison.OrdinalIgnoreCase))
            return new EchoTranslator();

        var apiKey = settings.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new UnavailableTranslator("还没有填写 API Key。请右键托盘图标 →「设置…」填写后重试。");

        return string.Equals(settings.Provider, "deepl", StringComparison.OrdinalIgnoreCase)
            ? new DeepLTranslator(settings.BaseUrl, apiKey)
            : new OpenAiCompatibleTranslator(settings.BaseUrl, settings.Model, apiKey);
    }

    /// <summary>Used by the settings dialog "test connection" button with in-memory (unsaved) values.</summary>
    public static ITranslator CreateForTest(string provider, string baseUrl, string model, string apiKey)
    {
        if (string.Equals(provider, "echo", StringComparison.OrdinalIgnoreCase))
            return new EchoTranslator();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new UnavailableTranslator("还没有填写 API Key。");
        return string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase)
            ? new DeepLTranslator(baseUrl, apiKey)
            : new OpenAiCompatibleTranslator(baseUrl, model, apiKey);
    }
}
