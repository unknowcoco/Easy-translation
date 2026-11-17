using System.Text.RegularExpressions;
using EasyTranslation.Languages;

namespace EasyTranslation.Translation;

/// <summary>Cleans model output and detects obviously failed translations.</summary>
public static class PostProcessor
{
    private static readonly Regex PrefixRegex = new(
        @"^(译文|翻译|Translation|Output|English)\s*[:：]\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
                if (text.EndsWith("```", StringComparison.Ordinal))
                    text = text[..^3];
                text = text.Trim();
            }
        }

        text = PrefixRegex.Replace(text, string.Empty).Trim();

        if (text.Length >= 4 && IsWrappedIn(text, '「', '」')) text = text[1..^1].Trim();
        if (text.Length >= 4 && IsWrappedIn(text, '『', '』')) text = text[1..^1].Trim();
        return text;
    }

    private static bool IsWrappedIn(string text, char open, char close) =>
        text.Length >= 2 && text[0] == open && text[^1] == close && !text[1..^1].Contains(close);

    /// <summary>True when the model looks like it returned the input instead of a translation.</summary>
    public static bool IsSameText(string input, string output) =>
        string.Equals(Normalize(input), Normalize(output), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ");

    /// <summary>
    /// Returns true when the output still contains a lot of the source script
    /// (e.g. Chinese characters when translating to English), which means the model refused or echoed.
    /// </summary>
    public static bool LooksUntranslated(string input, string output, string target)
    {
        if (string.IsNullOrWhiteSpace(output)) return true;
        if (IsSameText(input, output)) return true;

        var outputCounts = LanguageGuesser.Count(output);
        if (outputCounts.Total == 0) return false;

        if (!LanguageCatalog.TryGet(target, out var targetInfo)) return false;

        // Non-Latin targets must actually contain their own script.
        // Latin targets must not still be dominated by a non-Latin source script.
        if (targetInfo.Script == ScriptKind.Latin)
            return outputCounts.NonLatin > outputCounts.Total * 0.2;

        var ratio = targetInfo.Script switch
        {
            ScriptKind.Han => outputCounts.Han,
            ScriptKind.Kana => outputCounts.Kana + outputCounts.Han,
            ScriptKind.Hangul => outputCounts.Hangul,
            ScriptKind.Cyrillic => outputCounts.Cyrillic,
            ScriptKind.Arabic => outputCounts.Arabic,
            ScriptKind.Thai => outputCounts.Thai,
            _ => 0,
        };
        return ratio < outputCounts.Total * 0.2;
    }

    /// <summary>Preserves a trailing newline the source chunk had (models often trim it).</summary>
    public static string PreserveTrailingNewline(string sourceChunk, string translated)
    {
        var newlines = 0;
        for (var i = sourceChunk.Length - 1; i >= 0 && sourceChunk[i] == '\n'; i--) newlines++;
        if (newlines == 0) return translated;
        var trimmed = translated.TrimEnd('\n');
        return trimmed + new string('\n', newlines);
    }
}
