namespace EasyTranslation.Translation;

public sealed record TranslationRequest(
    string Text,
    string Source,
    string Target,
    string Tone,
    bool Strict = false);

public interface ITranslator
{
    string Name { get; }

    bool Supports(string source, string target);

    Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

public sealed class TranslationException : Exception
{
    public TranslationException(string userMessage, bool retryable = false, Exception? inner = null)
        : base(userMessage, inner)
    {
        UserMessage = userMessage;
        Retryable = retryable;
    }

    /// <summary>Message that is safe and useful to show to the user.</summary>
    public string UserMessage { get; }

    public bool Retryable { get; }
}
