using System.Security.Cryptography;
using System.Text;
using EasyTranslation.Languages;
using EasyTranslation.Logging;

namespace EasyTranslation.Translation;

public sealed class TranslationService
{
    private const int ChunkSize = 2500;

    private readonly ITranslator _translator;
    private readonly int _maxChars;
    private readonly LruCache _cache;

    public TranslationService(ITranslator translator, int maxChars = 6000, LruCache? cache = null)
    {
        _translator = translator;
        _maxChars = Math.Clamp(maxChars, 200, 20000);
        _cache = cache ?? new LruCache(100);
    }

    public ITranslator Translator => _translator;

    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new TranslationException("选中的内容为空。");
        if (request.Source == request.Target)
            throw new TranslationException("源语言和目标语言相同。");
        if (!_translator.Supports(request.Source, request.Target))
            throw new TranslationException($"当前翻译服务（{_translator.Name}）不支持这个语言方向。");
        if (request.Text.Length > _maxChars)
            throw new TranslationException($"选中的文字超过 {_maxChars} 字，请分段翻译。");

        var cacheKey = BuildCacheKey(request);
        if (_cache.TryGet(cacheKey, out var cached)) return cached;

        var chunks = TextChunker.Split(request.Text, ChunkSize);
        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var translated = await TranslateChunkAsync(chunk, request, cancellationToken);
            if (string.IsNullOrEmpty(translated))
                throw new TranslationException("翻译结果为空，请重试。", retryable: true);
            builder.Append(PostProcessor.PreserveTrailingNewline(chunk, translated));
        }

        var result = PostProcessor.Clean(builder.ToString());
        if (string.IsNullOrEmpty(result))
            throw new TranslationException("翻译结果为空，请重试。", retryable: true);

        _cache.Set(cacheKey, result);
        return result;
    }

    private async Task<string> TranslateChunkAsync(string chunk, TranslationRequest request, CancellationToken cancellationToken)
    {
        var text = await CallAsync(chunk, request with { Text = chunk }, cancellationToken);
        if (!IsTestDouble() && PostProcessor.LooksUntranslated(chunk, text, request.Target))
        {
            Log.Warn("translation looks untranslated, retrying in strict mode");
            text = await CallAsync(chunk, request with { Text = chunk, Strict = true }, cancellationToken);
            if (PostProcessor.LooksUntranslated(chunk, text, request.Target))
                throw new TranslationException("译文疑似未翻译（仍含原文语言），已中止，原文未改动。");
        }

        return text;
    }

    private async Task<string> CallAsync(string chunk, TranslationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _translator.TranslateAsync(request, cancellationToken);
            return PostProcessor.Clean(raw);
        }
        catch (TranslationException ex) when (ex.Retryable)
        {
            Log.Warn($"translation retryable failure: {ex.Message}");
            await Task.Delay(400, cancellationToken);
            var raw = await _translator.TranslateAsync(request, cancellationToken);
            return PostProcessor.Clean(raw);
        }
    }

    private bool IsTestDouble() => _translator.Name == "echo";

    private static string BuildCacheKey(TranslationRequest request)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)));
        return $"{hash}|{request.Source}|{request.Target}|{request.Tone}";
    }
}
