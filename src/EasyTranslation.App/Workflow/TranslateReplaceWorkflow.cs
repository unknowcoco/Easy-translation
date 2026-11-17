using System.Diagnostics;
using EasyTranslation.Clipboard;
using EasyTranslation.Input;
using EasyTranslation.Languages;
using EasyTranslation.Logging;
using EasyTranslation.Translation;

namespace EasyTranslation.Workflow;

/// <summary>
/// The full "translate the selection in place" flow. All side effects go through interfaces so the
/// orchestration can be unit tested without a desktop.
/// </summary>
public sealed class TranslateReplaceWorkflow
{
    private readonly IClipboardService _clipboard;
    private readonly IInputSender _input;
    private readonly IFocusGuard _focus;
    private readonly IMenuCloser _menu;
    private readonly ITranslator _translator;
    private readonly IWorkflowUi _ui;
    private readonly WorkflowOptions _options;
    private readonly WorkflowTimings _timings;

    public TranslateReplaceWorkflow(
        IClipboardService clipboard,
        IInputSender input,
        IFocusGuard focus,
        IMenuCloser menu,
        ITranslator translator,
        IWorkflowUi ui,
        WorkflowOptions options,
        WorkflowTimings? timings = null)
    {
        _clipboard = clipboard;
        _input = input;
        _focus = focus;
        _menu = menu;
        _translator = translator;
        _ui = ui;
        _options = options;
        _timings = timings ?? WorkflowTimings.Default;
    }

    public async Task<WorkflowResult> RunAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Info($"workflow start {_options.SourceLanguage}->{_options.TargetLanguage} window=0x{context.TargetWindow.ToInt64():X}");
        _ui.ShowBusy(context.Cursor);

        IClipboardBackup? backup = null;
        try
        {
            if (!_menu.Close(context.TargetWindow))
                return Result(WorkflowStatus.MenuNotClosed, "无法关闭右键菜单，本次已取消，请再试一次。", context);

            cancellationToken.ThrowIfCancellationRequested();

            _focus.TryRestore(context.TargetWindow, _focus.Capture(context.TargetWindow));
            if (_timings.MenuSettleMs > 0) await Task.Delay(_timings.MenuSettleMs, cancellationToken);
            var focusSnapshot = _focus.Capture(context.TargetWindow);

            if (_options.RestoreClipboard) backup = _clipboard.Backup();
            var sequence = _clipboard.Sequence;
            _input.Copy();
            var text = await WaitForSelectionAsync(sequence, cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
                return Result(WorkflowStatus.NoSelection, "没有取到选中的文字（该窗口可能不支持复制）。", context);

            if (text.Length > _options.MaxChars)
                return Result(WorkflowStatus.TooLong, $"选中的文字超过 {_options.MaxChars} 字，请分段翻译。", context);

            if (LanguageGuesser.IsLikelyAlreadyInTarget(text, _options.TargetLanguage))
            {
                var targetName = LanguageCatalog.DisplayName(_options.TargetLanguage);
                return Result(WorkflowStatus.AlreadyTarget, $"选中的内容已经是{targetName}。", context);
            }

            if (_options.SourceLanguage != LanguageCatalog.Auto &&
                !LanguageGuesser.MatchesSource(text, _options.SourceLanguage))
            {
                var sourceName = LanguageCatalog.DisplayName(_options.SourceLanguage);
                return Result(WorkflowStatus.SourceMismatch, $"选中的内容不像{sourceName}。", context);
            }

            var service = new TranslationService(_translator, _options.MaxChars);
            var translation = await service.TranslateAsync(
                new TranslationRequest(text, _options.SourceLanguage, _options.TargetLanguage, _options.Tone),
                cancellationToken);

            if (PostProcessor.IsSameText(text, translation))
            {
                var targetName = LanguageCatalog.DisplayName(_options.TargetLanguage);
                return Result(WorkflowStatus.AlreadyTarget, $"选中的内容已经是{targetName}（模型未作改动）。", context);
            }

            if (!_clipboard.SetText(translation))
                return Result(WorkflowStatus.ClipboardBusy, "无法写入剪贴板（可能被其他程序占用），原文未改动。", context);

            backup?.MarkWritten(_clipboard.Sequence);
            backup?.Keep();

            // Notepad & co. can race the OLE clipboard right after a write: give it a moment and
            // verify the text really landed before sending Ctrl+V.
            if (!await WaitForClipboardTextAsync(translation, 400, cancellationToken))
            {
                Log.Warn("translation did not reach the clipboard in time");
                return Result(WorkflowStatus.ClipboardBusy, "剪贴板没有准备好，原文未改动。", context, translation);
            }
            await Task.Delay(40, cancellationToken);

            var beforePaste = _focus.Capture(context.TargetWindow);
            Log.Debug($"paste pre: valid={_focus.IsStillValid(focusSnapshot)} fg=0x{beforePaste.Foreground.ToInt64():X} focus=0x{beforePaste.Focus.ToInt64():X} caret={beforePaste.Caret} has={beforePaste.HasCaret} snapshotFg=0x{focusSnapshot.Foreground.ToInt64():X} snapshotFocus=0x{focusSnapshot.Focus.ToInt64():X}");
            if (!_focus.IsStillValid(focusSnapshot))
                return Result(WorkflowStatus.FocusChanged, "焦点已变化，没有粘贴；译文已复制到剪贴板。", context, translation);

            _input.Paste();
            Log.Debug("paste sent");
            if (_timings.PasteSettleMs > 0) await Task.Delay(_timings.PasteSettleMs, cancellationToken);

            Log.Info($"workflow ok in {stopwatch.ElapsedMilliseconds}ms ({text.Length} chars)");
            return Result(WorkflowStatus.Success, "已替换 ✓　Ctrl+Z 撤销", context, translation);
        }
        catch (OperationCanceledException)
        {
            Log.Info("workflow cancelled");
            return Result(WorkflowStatus.Cancelled, "已取消。", context);
        }
        catch (TranslationException ex)
        {
            Log.Warn($"workflow translation failed: {ex.Message}");
            return Result(WorkflowStatus.TranslationFailed, ex.UserMessage, context);
        }
        catch (Exception ex)
        {
            Log.Error("workflow crashed", ex);
            return Result(WorkflowStatus.UnexpectedError, "发生意外错误，请查看日志。", context);
        }
        finally
        {
            backup?.Dispose();
            _ui.HideBusy();
        }
    }

    private async Task<string?> WaitForSelectionAsync(long initialSequence, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < _timings.SelectionTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clipboard.Sequence != initialSequence)
            {
                await Task.Delay(40, cancellationToken);
                return _clipboard.GetText();
            }

            await Task.Delay(30, cancellationToken);
        }

        return null;
    }

    private async Task<bool> WaitForClipboardTextAsync(string expected, int timeoutMs, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (string.Equals(_clipboard.GetText(), expected, StringComparison.Ordinal)) return true;
            await Task.Delay(30, cancellationToken);
        }

        return false;
    }

    private WorkflowResult Result(WorkflowStatus status, string message, WorkflowContext context, string? translation = null)
    {
        var isError = status != WorkflowStatus.Success;
        _ui.ShowResult(message, isError, context.Cursor);
        return new WorkflowResult(status, message, translation);
    }
}
