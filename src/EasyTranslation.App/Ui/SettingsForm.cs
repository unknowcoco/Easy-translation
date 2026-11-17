using System.Diagnostics;
using System.Drawing;
using EasyTranslation.Config;
using EasyTranslation.Languages;
using EasyTranslation.Translation;

namespace EasyTranslation.Ui;

/// <summary>All settings in one dialog. Values are written back to AppSettings only on 保存.</summary>
public sealed class SettingsForm : Form
{
    private readonly SettingsStore _store;
    private readonly bool _forceEcho;
    private bool _loading = true;

    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _model = new();
    private readonly TextBox _apiKey = new() { UseSystemPasswordChar = true };
    private readonly Button _testButton = new() { Text = "测试连接", AutoSize = true };
    private readonly Label _testResult = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) };

    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _tone = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _maxChars = new() { Minimum = 200, Maximum = 6000, Increment = 500 };

    private readonly CheckBox _chip = new() { Text = "显示浮条（选中文字后右键出现）", AutoSize = true };
    private readonly CheckBox _hotkey = new() { Text = "启用快捷键 Ctrl+Alt+E", AutoSize = true };
    private readonly CheckBox _restore = new() { Text = "翻译后恢复原来的剪贴板内容", AutoSize = true };
    private readonly CheckBox _replaceMenu = new() { Text = "接管右键菜单（实验：自绘菜单替代系统菜单）", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "开机自动启动", AutoSize = true };

    private readonly ComboBox _logLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _openDir = new() { Text = "打开配置目录", AutoSize = true };
    private readonly Button _reset = new() { Text = "恢复默认", AutoSize = true };

    public SettingsForm(AppSettings settings, SettingsStore store, bool forceEcho)
    {
        _store = store;
        _forceEcho = forceEcho;
        Text = "Easy translation 设置";
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 820);
        AutoScroll = true;
        BackColor = Color.FromArgb(0xF7, 0xF8, 0xFA);

        BuildLayout();
        Populate(settings);
        _loading = false;
        _provider.SelectedIndexChanged += (_, _) => ApplyProviderDefaults();
    }

    public AppSettings? Result { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
            AutoScroll = false,
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var service = MakeGroup("翻译服务");
        var serviceTable = MakeTable(2);
        _provider.Items.AddRange(new object[]
        {
            new ComboItem("deepseek", "DeepSeek（推荐）"),
            new ComboItem("openai", "OpenAI 兼容（任意厂商）"),
            new ComboItem("deepl", "DeepL（仅翻译）"),
            new ComboItem("echo", "本地测试 / 离线（echo）"),
        });
        _provider.SelectedIndex = 0;
        AddRow(serviceTable, 0, "服务商", _provider);
        AddRow(serviceTable, 1, "接口地址", _baseUrl);
        AddRow(serviceTable, 2, "模型", _model);
        AddRow(serviceTable, 3, "API Key", _apiKey);
        var testRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        testRow.Controls.Add(_testButton);
        testRow.Controls.Add(_testResult);
        serviceTable.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 4);
        serviceTable.Controls.Add(testRow, 1, 4);
        _testButton.Click += OnTestConnection;
        service.Controls.Add(serviceTable);
        root.Controls.Add(service, 0, 0);

        var languages = MakeGroup("语言（全部可选；DeepL 仅支持部分）");
        var languageTable = MakeTable(2);
        _source.Items.Add(new ComboItem(LanguageCatalog.Auto, "自动识别"));
        foreach (var language in LanguageCatalog.All)
            _source.Items.Add(new ComboItem(language.Code, language.ChineseName));
        foreach (var language in LanguageCatalog.Targets)
            _target.Items.Add(new ComboItem(language.Code, language.ChineseName));
        _tone.Items.AddRange(new object[]
        {
            new ComboItem("default", "默认"),
            new ComboItem("formal", "正式"),
            new ComboItem("casual", "口语"),
            new ComboItem("technical", "技术"),
        });
        AddRow(languageTable, 0, "源语言", _source);
        AddRow(languageTable, 1, "目标语言", _target);
        AddRow(languageTable, 2, "语气", _tone);
        AddRow(languageTable, 3, "字数上限", _maxChars);
        languages.Controls.Add(languageTable);
        root.Controls.Add(languages, 0, 1);

        var behavior = MakeGroup("行为");
        var behaviorFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4),
        };
        behaviorFlow.Controls.AddRange(new Control[] { _chip, _hotkey, _restore, _replaceMenu, _autostart });
        behavior.Controls.Add(behaviorFlow);
        root.Controls.Add(behavior, 0, 2);

        var advanced = MakeGroup("高级");
        var advancedTable = MakeTable(2);
        _logLevel.Items.AddRange(new object[]
        {
            new ComboItem("info", "信息"),
            new ComboItem("warn", "警告"),
            new ComboItem("error", "错误"),
            new ComboItem("debug", "调试"),
            new ComboItem("off", "关闭"),
        });
        AddRow(advancedTable, 0, "日志级别", _logLevel);
        var advancedButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        advancedButtons.Controls.Add(_openDir);
        advancedButtons.Controls.Add(_reset);
        advancedTable.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 1);
        advancedTable.Controls.Add(advancedButtons, 1, 1);
        _openDir.Click += (_, _) => OpenConfigDirectory();
        _reset.Click += (_, _) => ResetToDefaults();
        advanced.Controls.Add(advancedTable);
        root.Controls.Add(advanced, 0, 3);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        var save = new Button { Text = "保存", AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(16, 4, 16, 4) };
        save.Click += OnSave;
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);
        root.Controls.Add(bottom, 0, 4);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static GroupBox MakeGroup(string title) => new()
    {
        Text = title,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        Padding = new Padding(8),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static TableLayoutPanel MakeTable(int rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = rows,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        var text = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 6),
        };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(text, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void Populate(AppSettings settings)
    {
        SelectCode(_provider, settings.Provider);
        _baseUrl.Text = settings.BaseUrl;
        _model.Text = settings.Model;
        _apiKey.Text = _forceEcho ? string.Empty : settings.GetApiKey();
        SelectCode(_source, settings.SourceLanguage);
        SelectCode(_target, settings.TargetLanguage);
        SelectCode(_tone, settings.Tone);
        _maxChars.Value = Math.Clamp(settings.MaxChars, 200, 6000);
        _chip.Checked = settings.ChipEnabled;
        _hotkey.Checked = settings.HotkeyEnabled;
        _restore.Checked = settings.RestoreClipboard;
        _replaceMenu.Checked = settings.ReplaceNativeMenu;
        _autostart.Checked = settings.Autostart;
        SelectCode(_logLevel, settings.LogLevel);
        UpdateEnabledState();
    }

    private static void SelectCode(ComboBox combo, string code)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboItem item && string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void ApplyProviderDefaults()
    {
        if (_loading) return;
        var provider = SelectedProvider();
        switch (provider)
        {
            case "deepl":
                _baseUrl.Text = "https://api-free.deepl.com";
                _model.Text = string.Empty;
                break;
            case "openai":
                _baseUrl.Text = "https://api.openai.com/v1";
                _model.Text = "gpt-4o-mini";
                break;
            case "deepseek":
                _baseUrl.Text = AppSettings.DefaultBaseUrl;
                _model.Text = AppSettings.DefaultModel;
                break;
            case "echo":
                break;
        }

        UpdateEnabledState();
    }

    private void UpdateEnabledState()
    {
        var provider = SelectedProvider();
        _model.Enabled = provider is "deepseek" or "openai";
        _apiKey.Enabled = provider != "echo";
        _testButton.Enabled = provider == "echo" || !string.IsNullOrWhiteSpace(_apiKey.Text);
    }

    private string SelectedProvider() =>
        _provider.SelectedItem is ComboItem item ? item.Code : "deepseek";

    private async void OnTestConnection(object? sender, EventArgs e)
    {
        _testButton.Enabled = false;
        _testResult.Text = "测试中…";
        _testResult.ForeColor = Color.DimGray;
        try
        {
            var translator = BuildTranslator();
            var reply = await translator.TranslateAsync(
                new TranslationRequest("你好，这是一次连接测试。", LanguageCatalog.Auto, "en", "default"),
                CancellationToken.None);
            _testResult.Text = "连接成功：" + (reply.Length > 40 ? reply[..40] + "…" : reply);
            _testResult.ForeColor = Color.SeaGreen;
        }
        catch (TranslationException ex)
        {
            _testResult.Text = "失败：" + ex.UserMessage;
            _testResult.ForeColor = Color.Firebrick;
        }
        catch (Exception ex)
        {
            _testResult.Text = "失败：" + ex.Message;
            _testResult.ForeColor = Color.Firebrick;
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private ITranslator BuildTranslator() => TranslatorFactory.CreateForTest(
        SelectedProvider(),
        _baseUrl.Text.Trim(),
        _model.Text.Trim(),
        _apiKey.Text.Trim());

    private void OnSave(object? sender, EventArgs e)
    {
        var provider = SelectedProvider();
        if (provider != "echo" && string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            MessageBox.Show(this, "请填写 API Key（或选择 echo 测试模式）。", "Easy translation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Uri.TryCreate(_baseUrl.Text.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MessageBox.Show(this, "接口地址不是有效的 http(s) 地址。", "Easy translation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var source = SelectedCode(_source, LanguageCatalog.Auto);
        var target = SelectedCode(_target, "en");
        if (provider == "deepl" && !DeepLTranslator.CanTranslate(source, target))
        {
            MessageBox.Show(
                this,
                $"DeepL 不支持这个语言方向（{LanguageCatalog.DisplayName(source)} → {LanguageCatalog.DisplayName(target)}）。\n请选择其他目标语言，或改用 DeepSeek / OpenAI 兼容服务。",
                "Easy translation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var settings = new AppSettings
        {
            Provider = provider,
            BaseUrl = _baseUrl.Text.Trim(),
            Model = _model.Text.Trim(),
            SourceLanguage = source,
            TargetLanguage = target,
            Tone = SelectedCode(_tone, "default"),
            MaxChars = (int)_maxChars.Value,
            ChipEnabled = _chip.Checked,
            HotkeyEnabled = _hotkey.Checked,
            RestoreClipboard = _restore.Checked,
            ReplaceNativeMenu = _replaceMenu.Checked,
            Autostart = _autostart.Checked,
            LogLevel = SelectedCode(_logLevel, "info"),
        };
        settings.SetApiKey(_apiKey.Text.Trim());
        Result = settings;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string SelectedCode(ComboBox combo, string fallback) =>
        combo.SelectedItem is ComboItem item ? item.Code : fallback;

    private void OpenConfigDirectory()
    {
        try
        {
            Directory.CreateDirectory(_store.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Easy translation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetToDefaults()
    {
        if (MessageBox.Show(this, "恢复所有设置为默认值？（保存后生效）", "Easy translation",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Populate(new AppSettings());
    }

    private sealed record ComboItem(string Code, string Display)
    {
        public override string ToString() => Display;
    }
}
