using EasyTranslation.Translation;
using Xunit;

namespace EasyTranslation.Tests;

public class TranslationServiceTests
{
    private sealed class FakeTranslator : ITranslator
    {
        public string Name { get; init; } = "fake";
        public bool Supported { get; set; } = true;
        public Func<TranslationRequest, int, string> Handler { get; set; } = (_, _) => "Hello";
        public int Calls { get; private set; }
        public List<string> Inputs { get; } = new();

        public bool Supports(string source, string target) => Supported;

        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Inputs.Add(request.Text);
            return Task.FromResult(Handler(request, Calls));
        }
    }

    private static TranslationRequest Request(string text, string source = "zh", string target = "en") =>
        new(text, source, target, "default");

    [Fact]
    public async Task SecondIdenticalRequest_UsesTheCache()
    {
        var translator = new FakeTranslator();
        var service = new TranslationService(translator);

        var first = await service.TranslateAsync(Request("你好"), CancellationToken.None);
        var second = await service.TranslateAsync(Request("你好"), CancellationToken.None);

        Assert.Equal("Hello", first);
        Assert.Equal("Hello", second);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task LongText_IsTranslatedInChunks()
    {
        var translator = new FakeTranslator();
        var service = new TranslationService(translator);
        var text = string.Join("\n", Enumerable.Repeat("短句。", 800));

        var result = await service.TranslateAsync(Request(text), CancellationToken.None);

        Assert.Equal(2, translator.Calls);
        Assert.All(translator.Inputs, input => Assert.True(input.Length <= 2500));
        Assert.Equal("Hello\nHello", result);
    }

    [Fact]
    public async Task SameSourceAndTarget_IsRejected()
    {
        var service = new TranslationService(new FakeTranslator());
        await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateAsync(Request("你好", "zh", "zh"), CancellationToken.None));
    }

    [Fact]
    public async Task UnsupportedDirection_IsRejected()
    {
        var service = new TranslationService(new FakeTranslator { Supported = false });
        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateAsync(Request("你好", "zh", "ja"), CancellationToken.None));
        Assert.Contains("不支持", ex.UserMessage);
    }

    [Fact]
    public async Task TextOverTheLimit_IsRejected()
    {
        var service = new TranslationService(new FakeTranslator(), maxChars: 200);
        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateAsync(Request(new string('啊', 300)), CancellationToken.None));
        Assert.Contains("超过", ex.UserMessage);
    }

    [Fact]
    public async Task UntranslatedOutput_IsRetriedStrictThenRejected()
    {
        var translator = new FakeTranslator { Handler = (request, _) => request.Text };
        var service = new TranslationService(translator);

        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateAsync(Request("你好，世界"), CancellationToken.None));

        Assert.Contains("疑似未翻译", ex.UserMessage);
        Assert.Equal(2, translator.Calls);
    }

    [Fact]
    public async Task UntranslatedOutput_IsAcceptedAfterAStrictRetry()
    {
        var translator = new FakeTranslator
        {
            Handler = (request, _) => request.Strict ? "Hello, world" : request.Text,
        };
        var service = new TranslationService(translator);

        var result = await service.TranslateAsync(Request("你好，世界"), CancellationToken.None);

        Assert.Equal("Hello, world", result);
        Assert.Equal(2, translator.Calls);
    }

    [Fact]
    public async Task RetryableFailure_IsRetriedOnce()
    {
        var translator = new FakeTranslator
        {
            Handler = (_, call) => call == 1
                ? throw new TranslationException("服务繁忙", retryable: true)
                : "Hello",
        };
        var service = new TranslationService(translator);

        var result = await service.TranslateAsync(Request("你好"), CancellationToken.None);

        Assert.Equal("Hello", result);
        Assert.Equal(2, translator.Calls);
    }

    [Fact]
    public async Task EchoTranslator_IsNotTreatedAsUntranslated()
    {
        var service = new TranslationService(new EchoTranslator());
        var result = await service.TranslateAsync(Request("你好"), CancellationToken.None);
        Assert.Contains("你好", result);
    }
}
