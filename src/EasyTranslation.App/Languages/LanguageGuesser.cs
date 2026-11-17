using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyTranslation.Languages;

/// <summary>
/// Cheap Unicode-script heuristics plus a small function-word classifier for Latin languages.
/// Used to avoid an API round-trip when the selected text is already in the target language,
/// and to warn when the source language obviously mismatches.
/// When in doubt it answers "no" and lets the model decide.
/// </summary>
public static class LanguageGuesser
{
    public readonly record struct Counts(int Han, int Latin, int Kana, int Hangul, int Cyrillic, int Arabic, int Thai)
    {
        public int Total => Han + Latin + Kana + Hangul + Cyrillic + Arabic + Thai;

        public int NonLatin => Han + Kana + Hangul + Cyrillic + Arabic + Thai;
    }

    private static readonly Regex WordRegex = new(@"[\p{L}']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string[]> LatinHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new[]
        {
            "the", "and", "is", "are", "was", "were", "of", "to", "in", "it", "for", "with", "you", "your",
            "this", "that", "not", "have", "has", "from", "be", "on", "as", "at", "but", "they", "we", "will",
            "would", "can", "hello", "world", "please", "thanks", "thank", "good", "morning", "evening",
            "bye", "yes", "do", "does", "did", "my", "me", "our", "their", "there", "here", "what", "when",
            "where", "who", "how", "about", "just", "like", "want", "need", "make", "see", "know",
        },
        ["fr"] = new[]
        {
            "le", "la", "les", "un", "une", "des", "du", "de", "et", "est", "sont", "que", "qui", "dans",
            "pour", "avec", "vous", "nous", "je", "tu", "il", "elle", "ils", "elles", "bonjour", "merci",
            "monde", "tout", "tous", "plus", "sur", "pas", "au", "aux", "ce", "cette", "ces", "etre",
            "avoir", "faire", "oui", "non", "mon", "ma", "mes", "votre", "notre", "leur", "quand",
            "comment", "pourquoi", "salut", "bien", "tres", "aussi", "mais", "ou", "jour", "soir",
        },
        ["de"] = new[]
        {
            "der", "die", "das", "den", "dem", "des", "und", "ist", "sind", "nicht", "ein", "eine", "einen",
            "einem", "einer", "mit", "fur", "auf", "von", "zu", "zum", "zur", "ich", "du", "wir", "sie",
            "es", "auch", "hallo", "danke", "welt", "haben", "werden", "oder", "aber", "wenn", "wie",
            "was", "wo", "war", "bitte", "guten", "tag", "morgen", "tschuss", "ja", "nein", "mein",
            "meine", "dein", "ihr", "unser", "sehr", "gut", "danke",
        },
        ["es"] = new[]
        {
            "el", "la", "los", "las", "un", "una", "unos", "unas", "de", "del", "y", "es", "son", "que",
            "quien", "en", "para", "con", "por", "no", "si", "hola", "mundo", "gracias", "buenos", "dias",
            "noches", "adios", "como", "cuando", "donde", "porque", "pero", "mas", "muy", "bien", "mal",
            "yo", "tu", "ella", "nosotros", "ellos", "mi", "mis", "tus", "su", "sus", "este", "esta",
            "estos", "estas", "ser", "estar", "tener", "hacer", "gusta", "favor",
        },
        ["pt"] = new[]
        {
            "os", "as", "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das", "sao", "que", "quem",
            "em", "no", "na", "para", "com", "por", "nao", "sim", "ola", "mundo", "obrigado", "obrigada",
            "bom", "dia", "boa", "noite", "tchau", "como", "quando", "onde", "porque", "mas", "muito",
            "bem", "mal", "eu", "voce", "ele", "ela", "nos", "eles", "meu", "minha", "seu", "sua", "ser",
            "estar", "ter", "fazer", "favor",
        },
        ["it"] = new[]
        {
            "il", "lo", "la", "gli", "le", "un", "uno", "una", "di", "del", "della", "sono", "che", "chi",
            "in", "per", "con", "su", "non", "si", "ciao", "mondo", "grazie", "buongiorno", "buonasera",
            "arrivederci", "come", "quando", "dove", "perche", "ma", "molto", "bene", "male", "io", "tu",
            "lui", "lei", "noi", "voi", "loro", "mio", "mia", "tuo", "tua", "suo", "sua", "essere",
            "avere", "fare", "questo", "questa", "questi", "queste",
        },
        ["nl"] = new[]
        {
            "de", "het", "een", "en", "is", "zijn", "niet", "van", "voor", "met", "op", "te", "dat", "die",
            "dit", "deze", "ik", "jij", "je", "hij", "zij", "ze", "wij", "we", "jullie", "hallo", "dank",
            "bedankt", "wereld", "goed", "morgen", "dag", "tot", "ziens", "als", "maar", "ook", "waar",
            "wanneer", "hoe", "waarom", "mijn", "jouw", "haar", "ons", "hun", "heb", "hebt", "heeft",
            "hebben", "kan", "kunnen", "zal", "zullen",
        },
        ["pl"] = new[]
        {
            "w", "na", "nie", "jest", "sa", "ze", "z", "do", "od", "dla", "za", "pod", "nad", "przez",
            "tak", "jak", "gdzie", "kiedy", "dlaczego", "ale", "bardzo", "dobrze", "zle", "czesc",
            "dziekuje", "dzien", "dobry", "swiat", "ja", "ty", "on", "ona", "my", "wy", "oni", "moj",
            "moja", "twoj", "twoja", "jego", "jej", "nasz", "wasz", "ich", "byc", "miec", "robic", "to",
            "ten", "ta", "te",
        },
        ["tr"] = new[]
        {
            "ve", "bir", "bu", "su", "da", "de", "icin", "ile", "ne", "nasil", "neden", "ama", "cok",
            "iyi", "kotu", "merhaba", "tesekkur", "gunaydin", "aksamlar", "dunya", "ben", "sen", "biz",
            "siz", "onlar", "benim", "senin", "onun", "bizim", "sizin", "onlarin", "var", "yok", "olmak",
            "yapmak", "gitmek", "gelmek", "evet", "hayir",
        },
        ["id"] = new[]
        {
            "dan", "yang", "di", "ke", "dari", "ini", "itu", "dengan", "untuk", "pada", "dalam", "tidak",
            "adalah", "saya", "kamu", "anda", "dia", "kami", "kita", "mereka", "apa", "siapa", "kapan",
            "dimana", "mengapa", "bagaimana", "tetapi", "sangat", "baik", "buruk", "halo", "terima",
            "kasih", "selamat", "pagi", "malam", "dunia", "ya", "bisa", "akan", "sudah", "belum",
        },
        ["vi"] = new[]
        {
            "va", "la", "cua", "khong", "duoc", "trong", "cho", "voi", "mot", "nhung", "nguoi", "toi",
            "ban", "anh", "chi", "em", "chung", "ho", "nay", "do", "gi", "sao", "nao", "khi", "thi",
            "ma", "rat", "tot", "xau", "xin", "chao", "cam", "on", "the", "gioi", "co", "se", "da",
            "dang",
        },
    };

    private static readonly HashSet<char> SimplifiedOnly = new(
        "们个这说国学对时会为来后里发开关门问间样么产业实现点电话车进过还边读写听讲买卖钱长马鸟龙龟汉语华东飞爱乐书见觉认识让给从两头块张纸笔录页题标准备战争经济团队员师医院场厂农村镇县市区号线网机构权利义务责任选举议员长官政府");

    private static readonly HashSet<char> TraditionalOnly = new(
        "們個這說國學對時會為來後裡發開關門問間樣麼產業實現點電話車進過還邊讀寫聽講買賣錢長馬鳥龍龜漢語華東飛愛樂書見覺認識讓給從兩頭塊張紙筆錄頁題標準備戰爭經濟團隊員師醫院場廠農村鎮縣市區號線網機構權利義務責任選舉議員長官政府");

    public static Counts Count(string text)
    {
        int han = 0, latin = 0, kana = 0, hangul = 0, cyrillic = 0, arabic = 0, thai = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            switch (v)
            {
                case >= 0x4E00 and <= 0x9FFF:
                case >= 0x3400 and <= 0x4DBF:
                case >= 0xF900 and <= 0xFAFF:
                case >= 0x20000 and <= 0x2FA1F:
                    han++;
                    break;
                case >= 0x3040 and <= 0x30FF:
                case >= 0x31F0 and <= 0x31FF:
                    kana++;
                    break;
                case >= 0xAC00 and <= 0xD7AF:
                case >= 0x1100 and <= 0x11FF:
                    hangul++;
                    break;
                case >= 0x0400 and <= 0x04FF:
                    cyrillic++;
                    break;
                case >= 0x0600 and <= 0x06FF:
                case >= 0x0750 and <= 0x077F:
                    arabic++;
                    break;
                case >= 0x0E00 and <= 0x0E7F:
                    thai++;
                    break;
                case >= 'A' and <= 'Z':
                case >= 'a' and <= 'z':
                case >= 0x00C0 and <= 0x024F:
                    latin++;
                    break;
            }
        }

        return new Counts(han, latin, kana, hangul, cyrillic, arabic, thai);
    }

    /// <summary>Best-guess Latin-script language from common function words (null when unsure).</summary>
    public static string? GuessLatinLanguage(string text)
    {
        var counts = Count(text);
        if (counts.Latin == 0 || counts.Latin < counts.Total * 0.5) return null;

        var words = WordRegex.Matches(text.ToLowerInvariant()).Select(m => Fold(m.Value)).ToArray();
        if (words.Length == 0) return null;

        string? best = null;
        var bestScore = 0;
        var ties = 0;
        foreach (var (code, hints) in LatinHints)
        {
            var set = new HashSet<string>(hints, StringComparer.Ordinal);
            var score = words.Count(set.Contains);
            if (score > bestScore)
            {
                best = code;
                bestScore = score;
                ties = 1;
            }
            else if (score == bestScore && score > 0)
            {
                ties++;
            }
        }

        var minimum = words.Length <= 2 ? 1 : 2;
        return bestScore >= minimum && ties == 1 ? best : null;
    }

    /// <summary>True when the text is almost certainly already written in <paramref name="targetCode"/>.</summary>
    public static bool IsLikelyAlreadyInTarget(string text, string targetCode)
    {
        var c = Count(text);
        if (c.Total == 0) return false;

        switch (targetCode.ToLowerInvariant())
        {
            case "zh":
                return c.Han >= c.Total * 0.5 && !LooksTraditional(text);
            case "zh-hant":
                return c.Han >= c.Total * 0.5 && !LooksSimplified(text);
            case "ja":
                return c.Kana > 0 && c.Kana + c.Han >= c.Total * 0.4;
            case "ko":
                return c.Hangul >= c.Total * 0.4;
            case "ru":
                return c.Cyrillic >= c.Total * 0.5;
            case "ar":
                return c.Arabic >= c.Total * 0.5;
            case "th":
                return c.Thai >= c.Total * 0.5;
            default:
                return string.Equals(GuessLatinLanguage(text), targetCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>True when the text plausibly contains <paramref name="sourceCode"/> (auto always matches).</summary>
    public static bool MatchesSource(string text, string sourceCode)
    {
        if (sourceCode == LanguageCatalog.Auto) return true;
        var c = Count(text);
        if (c.Total == 0) return false;

        switch (sourceCode.ToLowerInvariant())
        {
            case "zh":
            case "zh-hant":
                return c.Han >= c.Total * 0.15;
            case "ja":
                return c.Kana + c.Han >= c.Total * 0.3;
            case "ko":
                return c.Hangul >= c.Total * 0.2;
            case "ru":
                return c.Cyrillic >= c.Total * 0.3;
            case "ar":
                return c.Arabic >= c.Total * 0.3;
            case "th":
                return c.Thai >= c.Total * 0.3;
            default:
                if (c.Latin < c.Total * 0.3) return false;
                var guess = GuessLatinLanguage(text);
                return guess is null || string.Equals(guess, sourceCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool LooksSimplified(string text)
    {
        int simplified = 0, traditional = 0;
        foreach (var ch in text)
        {
            if (SimplifiedOnly.Contains(ch)) simplified++;
            else if (TraditionalOnly.Contains(ch)) traditional++;
        }

        return simplified > traditional;
    }

    private static bool LooksTraditional(string text)
    {
        int simplified = 0, traditional = 0;
        foreach (var ch in text)
        {
            if (SimplifiedOnly.Contains(ch)) simplified++;
            else if (TraditionalOnly.Contains(ch)) traditional++;
        }

        return traditional > simplified;
    }

    private static string Fold(string word)
    {
        if (word.Length == 0) return word;
        var normalized = word.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }
}
