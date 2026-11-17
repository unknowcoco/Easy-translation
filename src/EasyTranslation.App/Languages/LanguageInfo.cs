namespace EasyTranslation.Languages;

public enum ScriptKind
{
    Han,
    Latin,
    Kana,
    Hangul,
    Cyrillic,
    Arabic,
    Thai,
    Unknown,
}

/// <summary>A language the tool can translate from or to.</summary>
public sealed record LanguageInfo(
    string Code,
    string ChineseName,
    string ShortLabel,
    string PromptName,
    ScriptKind Script,
    string? DeepLCode);

public sealed record TranslationDirection(string Source, string Target)
{
    public LanguageInfo TargetInfo => LanguageCatalog.Get(Target);

    public LanguageInfo? SourceInfo =>
        Source == LanguageCatalog.Auto ? null : LanguageCatalog.Get(Source);

    /// <summary>Single-line chip label, e.g. "中 → En　转换成英文".</summary>
    public string ChipText
    {
        get
        {
            var target = TargetInfo;
            return SourceInfo is { } source
                ? $"{source.ShortLabel} → {target.ShortLabel}　转换成{target.ChineseName}"
                : $"→ {target.ShortLabel}　转换成{target.ChineseName}";
        }
    }
}
