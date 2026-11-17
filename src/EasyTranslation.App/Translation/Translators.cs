using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyTranslation.Languages;
using EasyTranslation.Logging;

namespace EasyTranslation.Translation;

public static class TranslationErrors
{
    public static TranslationException FromHttp(int status, string body)
    {
        var snippet = body.Length > 300 ? body[..300] : body;
        Log.Warn($"translator HTTP {status}: {snippet}");
        return status switch
        {
            400 => new TranslationException("翻译请求被拒绝（400），请检查模型名称或语言设置。"),
            401 => new TranslationException("API Key 无效或已过期，请在设置里更新。"),
            402 => new TranslationException("账户余额不足，请充值后再试。"),
            403 => new TranslationException("没有访问该翻译服务的权限（403）。"),
            404 => new TranslationException("翻译接口地址不存在（404），请检查 Base URL。"),
            429 => new TranslationException("请求过于频繁或额度已用尽，请稍后再试。", retryable: true),
            >= 500 => new TranslationException($"翻译服务暂时不可用（{status}），请稍后再试。", retryable: true),
            _ => new TranslationException($"翻译服务返回错误（{status}）。"),
        };
    }
}

public sealed class OpenAiCompatibleTranslator : ITranslator, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;

    public OpenAiCompatibleTranslator(string baseUrl, string model, string apiKey, HttpClient? http = null)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = http is null;
    }

    public string Name => "openai";

    public bool Supports(string source, string target) => true;

    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["stream"] = false,
            ["messages"] = new object[]
            {
                new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] = PromptBuilder.System(request.Source, request.Target, request.Tone, request.Strict),
                },
                new Dictionary<string, string> { ["role"] = "user", ["content"] = request.Text },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw TranslationErrors.FromHttp((int)response.StatusCode, body);

            try
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                if (string.IsNullOrWhiteSpace(content))
                    throw new TranslationException("翻译服务返回了空结果。", retryable: true);
                return content;
            }
            catch (Exception ex) when (ex is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
            {
                Log.Warn($"unexpected translator response: {Truncate(body, 300)}");
                throw new TranslationException("翻译服务返回格式异常。", retryable: true, ex);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("翻译请求超时，请检查网络。", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("无法连接翻译服务，请检查网络和 Base URL。", retryable: true, ex);
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

public sealed class DeepLTranslator : ITranslator, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public DeepLTranslator(string baseUrl, string apiKey, HttpClient? http = null)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api-free.deepl.com" : baseUrl.Trim().TrimEnd('/');
        _apiKey = apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = http is null;
    }

    public string Name => "deepl";

    /// <summary>True when DeepL can translate this direction (it does not support every catalog language).</summary>
    public static bool CanTranslate(string source, string target)
    {
        if (!LanguageCatalog.TryGet(target, out var targetInfo) || targetInfo.DeepLCode is null) return false;
        return source == LanguageCatalog.Auto || LanguageCatalog.DeepLSourceCode(source) is not null;
    }

    public bool Supports(string source, string target) => CanTranslate(source, target);

    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!LanguageCatalog.TryGet(request.Target, out var target) || target.DeepLCode is null)
            throw new TranslationException("DeepL 不支持目标语言。");

        var form = new List<KeyValuePair<string, string>>
        {
            new("auth_key", _apiKey),
            new("text", request.Text),
            new("target_lang", target.DeepLCode),
        };

        var sourceCode = LanguageCatalog.DeepLSourceCode(request.Source);
        if (request.Source != LanguageCatalog.Auto && sourceCode is not null)
        {
            form.Add(new KeyValuePair<string, string>("source_lang", sourceCode));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v2/translate")
        {
            Content = new FormUrlEncodedContent(form),
        };

        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw TranslationErrors.FromHttp((int)response.StatusCode, body);

            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text))
                throw new TranslationException("DeepL 返回了空结果。", retryable: true);
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("DeepL 请求超时，请检查网络。", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("无法连接 DeepL，请检查网络和 Base URL。", retryable: true, ex);
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

public sealed class EchoTranslator : ITranslator
{
    public string Name => "echo";

    public bool Supports(string source, string target) => true;

    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var label = LanguageCatalog.TryGet(request.Target, out var target) ? target.ShortLabel : request.Target;
        return Task.FromResult($"[{label}] {request.Text}");
    }
}

public sealed class UnavailableTranslator : ITranslator
{
    private readonly string _message;

    public UnavailableTranslator(string message) => _message = message;

    public string Name => "unavailable";

    public bool Supports(string source, string target) => true;

    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) =>
        throw new TranslationException(_message);
}
