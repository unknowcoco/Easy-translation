using System.Drawing;
using EasyTranslation.Clipboard;
using EasyTranslation.Input;
using EasyTranslation.Translation;
using EasyTranslation.Workflow;
using Xunit;

namespace EasyTranslation.Tests;

public class WorkflowTests
{
    private static readonly IntPtr Win = new(0x777);

    private sealed class FakeBackup : IClipboardBackup
    {
        private readonly FakeClipboard _owner;
        private readonly string? _original;
        private readonly long _snapshot;
        private long _written;
        private bool _keep;

        public FakeBackup(FakeClipboard owner, string? original)
        {
            _owner = owner;
            _original = original;
            _snapshot = owner.Sequence;
        }

        public void MarkWritten(long sequence) => _written = sequence;

        public void Keep() => _keep = true;

        public void Restore()
        {
            if (_keep) return;
            if (_written > 0)
            {
                if (_owner.Sequence != _written) return;
            }
            else if (_owner.Sequence == _snapshot)
            {
                return;
            }

            _owner.Text = _original;
            _owner.Sequence++;
        }

        public void Dispose() => Restore();
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; set; }
        public string? SelectionToCopy { get; set; }
        public bool FailSetText { get; set; }
        public long Sequence { get; set; } = 1;

        public string? GetText() => Text;

        public bool SetText(string text)
        {
            if (FailSetText) return false;
            Text = text;
            Sequence++;
            return true;
        }

        public IClipboardBackup Backup() => new FakeBackup(this, Text);
    }

    private sealed class FakeInput : IInputSender
    {
        private readonly FakeClipboard _clipboard;

        public FakeInput(FakeClipboard clipboard) => _clipboard = clipboard;

        public int CopyCalls { get; private set; }
        public int PasteCalls { get; private set; }
        public int EscapeCalls { get; private set; }

        public void Copy()
        {
            CopyCalls++;
            if (_clipboard.SelectionToCopy is { } selection)
            {
                _clipboard.Text = selection;
                _clipboard.Sequence++;
            }
        }

        public void Paste() => PasteCalls++;

        public void Escape() => EscapeCalls++;
    }

    private sealed class FakeFocus : IFocusGuard
    {
        public bool Valid { get; set; } = true;

        public FocusSnapshot Capture(IntPtr targetWindow) => new(targetWindow, targetWindow, Point.Empty, false);

        public bool IsStillValid(FocusSnapshot snapshot) => Valid;

        public bool TryRestore(IntPtr targetWindow, FocusSnapshot snapshot) => true;
    }

    private sealed class FakeMenu : IMenuCloser
    {
        public bool Ok { get; set; } = true;

        public bool Close(IntPtr targetWindow) => Ok;
    }

    private sealed class FakeTranslator : ITranslator
    {
        public string Name => "fake";
        public Func<TranslationRequest, string> Handler { get; set; } = _ => "Hello, world";
        public bool Supported { get; set; } = true;

        public bool Supports(string source, string target) => Supported;

        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Handler(request));
    }

    private sealed class FakeUi : IWorkflowUi
    {
        public List<string> Messages { get; } = new();
        public int BusyShown { get; private set; }
        public int BusyHidden { get; private set; }

        public void ShowBusy(Point at) => BusyShown++;

        public void HideBusy() => BusyHidden++;

        public void ShowResult(string message, bool isError, Point at) => Messages.Add(message);
    }

    private static WorkflowContext Context() => new(Win, new Point(100, 100));

    private static (TranslateReplaceWorkflow Workflow, FakeClipboard Clipboard, FakeInput Input, FakeMenu Menu, FakeTranslator Translator) Create(
        string? selection = "你好，世界",
        bool focusValid = true,
        bool menuOk = true,
        bool failSetText = false,
        WorkflowOptions? options = null)
    {
        var clipboard = new FakeClipboard
        {
            Text = "原始剪贴板",
            SelectionToCopy = selection,
            FailSetText = failSetText,
        };
        var input = new FakeInput(clipboard);
        var menu = new FakeMenu { Ok = menuOk };
        var translator = new FakeTranslator();
        var workflow = new TranslateReplaceWorkflow(
            clipboard,
            input,
            new FakeFocus { Valid = focusValid },
            menu,
            translator,
            new FakeUi(),
            options ?? new WorkflowOptions("zh", "en", "default", 6000, true),
            new WorkflowTimings(SelectionTimeoutMs: 400, PasteSettleMs: 0, MenuSettleMs: 0));
        return (workflow, clipboard, input, menu, translator);
    }

    [Fact]
    public async Task Success_ReplacesTheSelectionAndKeepsTheTranslation()
    {
        var (workflow, clipboard, input, _, _) = Create();

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Success, result.Status);
        Assert.True(result.Ok);
        Assert.Equal("Hello, world", result.Translation);
        Assert.Equal(1, input.PasteCalls);
        Assert.Equal("Hello, world", clipboard.Text);
    }

    [Fact]
    public async Task NoSelection_ReportsAndLeavesTheClipboardAlone()
    {
        var (workflow, clipboard, input, _, _) = Create(selection: null);

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.NoSelection, result.Status);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("原始剪贴板", clipboard.Text);
    }

    [Fact]
    public async Task AlreadyEnglish_DoesNotPaste()
    {
        var (workflow, clipboard, input, _, _) = Create(selection: "Hello, world");

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.AlreadyTarget, result.Status);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("原始剪贴板", clipboard.Text);
    }

    [Fact]
    public async Task TooLong_IsRejectedBeforeTranslating()
    {
        var (workflow, clipboard, input, _, _) = Create(
            selection: "你好世界",
            options: new WorkflowOptions("zh", "en", "default", 3, true));

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.TooLong, result.Status);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("原始剪贴板", clipboard.Text);
    }

    [Fact]
    public async Task WrongSourceLanguage_IsRejected()
    {
        var (workflow, _, input, _, _) = Create(
            selection: "Hello, world",
            options: new WorkflowOptions("zh", "ja", "default", 6000, true));

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.SourceMismatch, result.Status);
        Assert.Equal(0, input.PasteCalls);
    }

    [Fact]
    public async Task TranslationFailure_LeavesTheSelectionUntouched()
    {
        var (workflow, clipboard, input, _, translator) = Create();
        translator.Handler = _ => throw new TranslationException("API Key 无效，请检查设置。");

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.TranslationFailed, result.Status);
        Assert.Equal("API Key 无效，请检查设置。", result.Message);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("原始剪贴板", clipboard.Text);
    }

    [Fact]
    public async Task FocusChanged_DoesNotPasteAndKeepsTheTranslation()
    {
        var (workflow, clipboard, input, _, _) = Create(focusValid: false);

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.FocusChanged, result.Status);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("Hello, world", clipboard.Text);
    }

    [Fact]
    public async Task ClipboardWriteFailure_LeavesTheSelectionUntouched()
    {
        var (workflow, clipboard, input, _, _) = Create(failSetText: true);

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.ClipboardBusy, result.Status);
        Assert.Equal(0, input.PasteCalls);
        Assert.Equal("原始剪贴板", clipboard.Text);
    }

    [Fact]
    public async Task MenuNotClosed_AbortsBeforeCopying()
    {
        var (workflow, _, input, _, _) = Create(menuOk: false);

        var result = await workflow.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.MenuNotClosed, result.Status);
        Assert.Equal(0, input.CopyCalls);
        Assert.Equal(0, input.PasteCalls);
    }

    [Fact]
    public async Task Cancellation_ReportsCancelled()
    {
        var (workflow, _, input, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await workflow.RunAsync(Context(), cts.Token);

        Assert.Equal(WorkflowStatus.Cancelled, result.Status);
        Assert.Equal(0, input.PasteCalls);
    }
}
