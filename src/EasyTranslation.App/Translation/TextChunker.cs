using System.Text;

namespace EasyTranslation.Translation;

/// <summary>Splits long text into chunks without losing the original separators.</summary>
public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        if (maxChars <= 0) maxChars = 2500;
        if (text.Length <= maxChars) return new[] { text };

        var pieces = new List<string>();
        foreach (var atom in SplitAtoms(text, maxChars)) pieces.AddRange(SplitLongAtom(atom, maxChars));

        var chunks = new List<string>();
        var sb = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (sb.Length > 0 && sb.Length + piece.Length > maxChars)
            {
                chunks.Add(sb.ToString());
                sb.Clear();
            }

            if (piece.Length > maxChars)
            {
                for (var offset = 0; offset < piece.Length; offset += maxChars)
                    chunks.Add(piece.Substring(offset, Math.Min(maxChars, piece.Length - offset)));
                continue;
            }

            sb.Append(piece);
        }

        if (sb.Length > 0) chunks.Add(sb.ToString());
        return chunks;
    }

    private static IEnumerable<string> SplitAtoms(string text, int maxChars)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < text.Length) yield return text[start..];
    }

    private static IEnumerable<string> SplitLongAtom(string atom, int maxChars)
    {
        if (atom.Length <= maxChars)
        {
            yield return atom;
            yield break;
        }

        var start = 0;
        for (var i = 0; i < atom.Length; i++)
        {
            if (IsSentenceEnd(atom[i]))
            {
                // keep the terminator with the sentence; the packer merges short sentences back together
                yield return atom[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < atom.Length) yield return atom[start..];
    }

    private static bool IsSentenceEnd(char c) =>
        c is '。' or '！' or '？' or '；' or '!' or '?' or ';' or '\u2026';
}
