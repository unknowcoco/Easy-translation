using System.Drawing;
using EasyTranslation.Languages;
using EasyTranslation.Translation;
using Xunit;

namespace EasyTranslation.Tests;

public class LanguageTests
{
    [Fact]
    public void Catalog_ExposesChineseAndEnglish()
    {
        Assert.True(LanguageCatalog.TryGet("en", out var en));
        Assert.Equal("英文", en.ChineseName);
        Assert.Equal(18, LanguageCatalog.Targets.Count);
        Assert.True(LanguageCatalog.TryGet("zh-Hant", out var traditional));
        Assert.Equal("繁体中文", traditional.ChineseName);
        Assert.True(LanguageCatalog.TryGet("th", out _));
        Assert.Equal("自动识别", LanguageCatalog.DisplayName(LanguageCatalog.Auto));
        Assert.False(LanguageCatalog.TryGet("xx", out _));
    }

    [Fact]
    public void ChipText_ShowsTheDirection()
    {
        var zhToEn = new TranslationDirection("zh", "en");
        Assert.Contains("中", zhToEn.ChipText);
        Assert.Contains("En", zhToEn.ChipText);
        Assert.Contains("转换成英文", zhToEn.ChipText);

        var autoToJa = new TranslationDirection(LanguageCatalog.Auto, "ja");
        Assert.Contains("转换成日文", autoToJa.ChipText);
    }

    [Fact]
    public void Guesser_CountsScripts()
    {
        var counts = LanguageGuesser.Count("你好，世界 Hello");
        Assert.Equal(4, counts.Han);
        Assert.Equal(5, counts.Latin);
    }

    [Theory]
    [InlineData("你好，世界", "en", false)]
    [InlineData("Hello world", "en", true)]
    [InlineData("你好，世界", "zh", true)]
    [InlineData("こんにちは", "ja", true)]
    [InlineData("Привет", "ru", true)]
    [InlineData("Bonjour tout le monde", "fr", true)]
    [InlineData("Hello world", "fr", false)]
    [InlineData("Hola mundo", "es", true)]
    [InlineData("Hola mundo", "en", false)]
    [InlineData("Merhaba dünya", "tr", true)]
    [InlineData("สวัสดี", "th", true)]
    [InlineData("مرحبا", "ar", true)]
    [InlineData("这个国家", "zh-Hant", false)]
    [InlineData("這個國家", "zh-Hant", true)]
    public void IsLikelyAlreadyInTarget_MatchesExpectations(string text, string target, bool expected)
    {
        Assert.Equal(expected, LanguageGuesser.IsLikelyAlreadyInTarget(text, target));
    }

    [Theory]
    [InlineData("你好，世界", "zh", true)]
    [InlineData("Hello world", "zh", false)]
    [InlineData("Hello world", "en", true)]
    [InlineData("Bonjour", "fr", true)]
    [InlineData("Hello world", "fr", false)]
    [InlineData("你好，世界", LanguageCatalog.Auto, true)]
    public void MatchesSource_MatchesExpectations(string text, string source, bool expected)
    {
        Assert.Equal(expected, LanguageGuesser.MatchesSource(text, source));
    }
}

public class PromptTests
{
    [Fact]
    public void Prompt_NamesBothSidesAndTone()
    {
        var prompt = PromptBuilder.System("zh", "en", "formal");
        Assert.Contains("中文", prompt);
        Assert.Contains("英文", prompt);
        Assert.Contains("正式", prompt);
        Assert.Contains("只输出译文", prompt);
    }

    [Fact]
    public void Prompt_StrictAddsRetryInstruction()
    {
        Assert.Contains("上一次", PromptBuilder.System("zh", "en", "default", strict: true));
        Assert.DoesNotContain("上一次", PromptBuilder.System("zh", "en", "default"));
    }
}

public class PostProcessorTests
{
    [Theory]
    [InlineData("```\nHello\n```", "Hello")]
    [InlineData("译文：Hello", "Hello")]
    [InlineData("Translation: Hello", "Hello")]
    [InlineData("「你好」", "你好")]
    [InlineData("  Hello  ", "Hello")]
    public void Clean_StripsModelNoise(string raw, string expected)
    {
        Assert.Equal(expected, PostProcessor.Clean(raw));
    }

    [Fact]
    public void IsSameText_IgnoresWhitespaceAndCase()
    {
        Assert.True(PostProcessor.IsSameText("Hello  world", "hello world"));
        Assert.False(PostProcessor.IsSameText("Hello", "你好"));
    }

    [Theory]
    [InlineData("你好，世界", "こんにちは世界", "ja", false)]
    [InlineData("你好，世界", "你好，世界", "ja", true)]
    [InlineData("Hello", "안녕하세요", "ko", false)]
    [InlineData("Hello", "Привет", "ru", false)]
    [InlineData("Hello", "مرحبا", "ar", false)]
    [InlineData("Hello", "สวัสดี", "th", false)]
    [InlineData("你好", "Hello", "zh", true)]
    [InlineData("Hello", "Bonjour", "fr", false)]
    [InlineData("Hello", "Hola", "es", false)]
    [InlineData("你好", "Hello 世界", "fr", true)]
    public void LooksUntranslated_HandlesEveryTargetScript(string input, string output, string target, bool expected)
    {
        Assert.Equal(expected, PostProcessor.LooksUntranslated(input, output, target));
    }

    [Fact]
    public void LooksUntranslated_DetectsChineseLeakingIntoEnglish()
    {
        Assert.True(PostProcessor.LooksUntranslated("你好，世界", "你好，世界", "en"));
        Assert.True(PostProcessor.LooksUntranslated("你好，世界", "Hello 世界", "en"));
        Assert.False(PostProcessor.LooksUntranslated("你好，世界", "Hello, world", "en"));
    }

    [Fact]
    public void PreserveTrailingNewline_KeepsThem()
    {
        Assert.Equal("Hello\n\n", PostProcessor.PreserveTrailingNewline("你好\n\n", "Hello"));
        Assert.Equal("Hello", PostProcessor.PreserveTrailingNewline("你好", "Hello"));
    }
}

public class DeepLTests
{
    [Fact]
    public void Supports_OnlyLanguagesDeepLKnows()
    {
        Assert.True(DeepLTranslator.CanTranslate(LanguageCatalog.Auto, "ja"));
        Assert.True(DeepLTranslator.CanTranslate("zh", "zh-Hant"));
        Assert.True(DeepLTranslator.CanTranslate("en", "ja"));
        Assert.False(DeepLTranslator.CanTranslate(LanguageCatalog.Auto, "th"));
        Assert.False(DeepLTranslator.CanTranslate(LanguageCatalog.Auto, "vi"));
        Assert.False(DeepLTranslator.CanTranslate("zh-Hant", "en"));
    }

    [Fact]
    public void SourceAndTargetCodes_DifferForVariants()
    {
        Assert.Equal("EN-US", LanguageCatalog.DeepLTargetCode("en"));
        Assert.Equal("EN", LanguageCatalog.DeepLSourceCode("en"));
        Assert.Equal("PT-PT", LanguageCatalog.DeepLTargetCode("pt"));
        Assert.Equal("PT", LanguageCatalog.DeepLSourceCode("pt"));
        Assert.Equal("ZH-HANT", LanguageCatalog.DeepLTargetCode("zh-Hant"));
        Assert.Null(LanguageCatalog.DeepLSourceCode("zh-Hant"));
    }
}

public class TextChunkerTests
{
    [Fact]
    public void ShortText_IsOneChunk()
    {
        Assert.Equal(new[] { "你好" }, TextChunker.Split("你好", 100));
    }

    [Fact]
    public void LongText_IsChunkedButConcatenatesBack()
    {
        var text = string.Join("\n", Enumerable.Repeat("这是一句中文。", 400));
        var chunks = TextChunker.Split(text, 300);

        Assert.True(chunks.Count > 1);
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 300, $"chunk too long: {chunk.Length}"));
    }

    [Fact]
    public void LongSentence_IsHardSplit()
    {
        var text = new string('啊', 1000);
        var chunks = TextChunker.Split(text, 250);
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 250));
    }
}
