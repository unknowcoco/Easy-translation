namespace EasyTranslation.Languages;

/// <summary>
/// The single place that knows which languages exist. Adding a language = adding one entry here
/// (plus an optional DeepL code); no other component needs to change.
/// </summary>
public static class LanguageCatalog
{
    public const string Auto = "auto";

    public static readonly LanguageInfo Chinese = new("zh", "中文", "中", "中文", ScriptKind.Han, "ZH");
    public static readonly LanguageInfo TraditionalChinese = new("zh-Hant", "繁体中文", "繁", "繁体中文", ScriptKind.Han, "ZH-HANT");
    public static readonly LanguageInfo English = new("en", "英文", "En", "英文", ScriptKind.Latin, "EN-US");
    public static readonly LanguageInfo Japanese = new("ja", "日文", "日", "日文", ScriptKind.Kana, "JA");
    public static readonly LanguageInfo Korean = new("ko", "韩文", "韩", "韩文", ScriptKind.Hangul, "KO");
    public static readonly LanguageInfo French = new("fr", "法文", "Fr", "法文", ScriptKind.Latin, "FR");
    public static readonly LanguageInfo German = new("de", "德文", "De", "德文", ScriptKind.Latin, "DE");
    public static readonly LanguageInfo Spanish = new("es", "西班牙文", "Es", "西班牙文", ScriptKind.Latin, "ES");
    public static readonly LanguageInfo Portuguese = new("pt", "葡萄牙文", "Pt", "葡萄牙文", ScriptKind.Latin, "PT-PT");
    public static readonly LanguageInfo Italian = new("it", "意大利文", "It", "意大利文", ScriptKind.Latin, "IT");
    public static readonly LanguageInfo Russian = new("ru", "俄文", "Ru", "俄文", ScriptKind.Cyrillic, "RU");
    public static readonly LanguageInfo Arabic = new("ar", "阿拉伯文", "Ar", "阿拉伯文", ScriptKind.Arabic, "AR");
    public static readonly LanguageInfo Thai = new("th", "泰文", "Th", "泰文", ScriptKind.Thai, null);
    public static readonly LanguageInfo Vietnamese = new("vi", "越南文", "Vi", "越南文", ScriptKind.Latin, null);
    public static readonly LanguageInfo Turkish = new("tr", "土耳其文", "Tr", "土耳其文", ScriptKind.Latin, "TR");
    public static readonly LanguageInfo Indonesian = new("id", "印尼文", "Id", "印尼文", ScriptKind.Latin, "ID");
    public static readonly LanguageInfo Dutch = new("nl", "荷兰文", "Nl", "荷兰文", ScriptKind.Latin, "NL");
    public static readonly LanguageInfo Polish = new("pl", "波兰文", "Pl", "波兰文", ScriptKind.Latin, "PL");

    public static IReadOnlyList<LanguageInfo> All { get; } = new[]
    {
        Chinese, TraditionalChinese, English, Japanese, Korean, French, German, Spanish,
        Portuguese, Italian, Russian, Arabic, Thai, Vietnamese, Turkish, Indonesian, Dutch, Polish,
    };

    /// <summary>Languages offered as a translation target: every language in the catalog.</summary>
    public static IReadOnlyList<LanguageInfo> Targets => All;

    public static bool TryGet(string? code, out LanguageInfo info)
    {
        foreach (var language in All)
        {
            if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                info = language;
                return true;
            }
        }

        info = English;
        return false;
    }

    public static LanguageInfo Get(string code) =>
        TryGet(code, out var info) ? info : throw new ArgumentException($"Unknown language code '{code}'.", nameof(code));

    public static string DisplayName(string code) =>
        code == Auto ? "自动识别" : TryGet(code, out var info) ? info.ChineseName : code;

    public static string PromptName(string code) =>
        code == Auto ? "源语言（自动识别）" : TryGet(code, out var info) ? info.PromptName : code;

    /// <summary>DeepL target code (null when DeepL cannot produce that language).</summary>
    public static string? DeepLTargetCode(string code) =>
        TryGet(code, out var info) ? info.DeepLCode : null;

    /// <summary>
    /// DeepL source code. DeepL uses a different code for some variants
    /// (EN-US/PT-PT are target-only; ZH-HANT is target-only), hence this mapping.
    /// </summary>
    public static string? DeepLSourceCode(string code)
    {
        if (!TryGet(code, out var info)) return null;
        return info.Code.ToLowerInvariant() switch
        {
            "en" => "EN",
            "pt" => "PT",
            "zh-hant" => null,
            _ => info.DeepLCode,
        };
    }
}
