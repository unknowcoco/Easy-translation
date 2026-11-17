using System.Text;
using EasyTranslation.Languages;

namespace EasyTranslation.Translation;

public static class PromptBuilder
{
    public static string System(string source, string target, string tone, bool strict = false)
    {
        var sourceName = LanguageCatalog.PromptName(source);
        var targetName = LanguageCatalog.PromptName(target);
        var sb = new StringBuilder();
        sb.Append($"你是专业翻译。把用户提供的{sourceName}文本翻译成{targetName}。");
        sb.Append("要求：忠实原意，保持原有语气；保留原文的换行、列表、数字、URL、代码、占位符和标点结构；");
        sb.Append("只输出译文本身，不要解释、不要加引号、不要用代码块包裹。");
        sb.Append($"如果原文已经是{targetName}，原样输出原文。");

        sb.Append(tone switch
        {
            "formal" => "风格：正式、书面。",
            "casual" => "风格：口语、自然。",
            "technical" => "风格：技术文档，术语准确、简洁。",
            _ => string.Empty,
        });

        if (strict)
            sb.Append("注意：上一次的输出没有被接受（为空、仍是原文语言或夹带解释）。这一次必须只输出纯译文。");
        return sb.ToString();
    }
}
