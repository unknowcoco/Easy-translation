namespace EasyTranslation.Workflow;

public enum WorkflowStatus
{
    Success,
    MenuNotClosed,
    NoSelection,
    AlreadyTarget,
    SourceMismatch,
    TooLong,
    ClipboardBusy,
    FocusChanged,
    TranslationFailed,
    Cancelled,
    UnexpectedError,
}

public sealed record WorkflowOptions(
    string SourceLanguage,
    string TargetLanguage,
    string Tone,
    int MaxChars,
    bool RestoreClipboard);

public sealed record WorkflowContext(IntPtr TargetWindow, Point Cursor);

public sealed record WorkflowResult(WorkflowStatus Status, string Message, string? Translation = null)
{
    public bool Ok => Status == WorkflowStatus.Success;
}

public sealed record WorkflowTimings(int SelectionTimeoutMs = 900, int PasteSettleMs = 700, int MenuSettleMs = 40)
{
    public static WorkflowTimings Default { get; } = new();
}

public interface IWorkflowUi
{
    void ShowBusy(Point at);

    void HideBusy();

    void ShowResult(string message, bool isError, Point at);
}
