# 架构说明

## 目标与约束

在**任意** Windows 应用的文本控件中，把用户选中的文字原地替换成目标语言（默认中 → 英，内置 18 种目标语言）。约束：

- 不注入 DLL、不要求目标程序配合、不弹 UAC；
- 不破坏用户剪贴板、不改动原文除非替换成功；
- 只依赖 Win32 公共 API 与 .NET 8，便于长期维护；
- 语言对可扩展（内置 18 种目标语言，默认中 → 英）。

原生右键菜单无法扩展：`ITfMenu` 只作用于输入法语言栏；资源管理器右键菜单只对文件对象生效；DLL 注入被否决。因此默认方案是**伴侣浮条**：保留系统右键菜单，在旁边显示一个小浮条；另提供 opt-in 的自绘菜单接管模式。

## 交互链路

```
用户拖选 / 双击 / 三击
        │  WH_MOUSE_LL（独立线程）
        ▼
SelectionTracker ── 记录 bbox、窗口、时间（拖选阈值 4px，水平拖选自动补足 20px 高度带）
        │
用户右键（落在 bbox 内、同一前台窗口）
        ▼
浮条 ChipForm（WS_EX_NOACTIVATE + TOPMOST，ShowWithoutActivation）
        │  点击由鼠标钩子识别：吞掉 LButtonDown/Up，不让目标窗口失焦
        ▼
TranslateReplaceWorkflow
  1. Esc / WM_CANCELMODE 关闭原生右键菜单（最多等 600 ms）
  2. 尝试恢复目标窗口焦点；抓取前台窗口 + 焦点控件 + 光标位置快照
  3. 备份剪贴板（IDataObject + 文本 + 序列号）
  4. 发 Ctrl+C，轮询剪贴板序列号（≤900 ms），读选区
  5. 校验：非空、≤ maxChars、不是目标语言、匹配源语言
  6. 分块（每块 ≤2500 字）→ 翻译服务 → 后处理 → 拼回
  7. 写入剪贴板；回读确认译文已就绪（30 ms × 最多 400 ms，未就绪则报「剪贴板没有准备好」），再等 40 ms
  8. 校验前台/焦点/光标未变（焦点守卫）；发 Ctrl+V；等 700 ms
  9. 成功保留译文；失败时绝不粘贴，原文保持不变；错误以 Toast 提示
```

## 组件

| 目录 | 职责 |
| --- | --- |
| `Interop/NativeMethods.cs` | 全部 P/Invoke 与结构体（钩子、SendInput、前台/焦点、剪贴板序列号、热键、DPI） |
| `Interop/Dpapi.cs` | DPAPI 加密，避免引入 NuGet |
| `Input/LowLevelMouseHook.cs` | WH_MOUSE_LL 专用线程；回调可吞事件 |
| `Input/LowLevelKeyboardHook.cs` | 翻译期间的 Esc 取消；忽略发送注入的 Esc（否则会取消自己关闭菜单的动作） |
| `Input/SelectionTracker.cs` | 选区状态机（拖选/双击/三击、窗口与时间校验），纯逻辑可单测 |
| `Input/SendInputHelper.cs` | 扫描码方式发 Ctrl+C / Ctrl+V / Esc / Ctrl+A / Ctrl+Z |
| `Input/NativeMenuCloser.cs` | 找当前线程的 `#32768` 菜单并 `WM_CANCELMODE`，同时发 Esc，轮询前台恢复 |
| `Input/FocusGuard.cs` | 抓取并校验前台窗口/焦点控件/光标（±4px），失焦时拒绝粘贴 |
| `Clipboard/*` | 临时 STA 线程访问 OLE 剪贴板；快照（IDataObject + 文本 + 序列号）/恢复；写入后回读确认，避免目标程序在剪贴板就绪前处理 Ctrl+V |
| `Workflow/*` | 状态机与可注入依赖（剪贴板、输入、焦点、菜单、翻译器、UI），便于测试 |
| `Languages/*` | 语言目录（18 种：zh/zh-Hant/en/ja/ko/fr/de/es/pt/it/ru/ar/th/vi/tr/id/nl/pl）+ 脚本识别 + 拉丁语系常用词猜语言 |
| `Translation/*` | 提示词、分块、后处理、LRU 缓存、DeepSeek/OpenAI 兼容/DeepL/echo |
| `Ui/*` | 浮条、Toast、自绘菜单、设置窗、主题、运行时图标 |
| `Tray/*` | 托盘菜单与状态 |
| `Config/*` | 设置模型、原子读写、损坏回退、DPAPI Key、开机启动 |
| `Logging/Log.cs` | 1 MB × 3 轮转文件日志，不记录原文 |

## 关键决策

- **不抢焦点**：浮条和自绘菜单都带 `WS_EX_NOACTIVATE`，点击由钩子吞掉，避免目标窗口失焦导致选区消失。
- **剪贴板是唯一可靠通道**：不同框架（Win32/Chromium/Electron/WPS/Qt）复制粘贴实现差异很大，但 `Ctrl+C` / `Ctrl+V` 是共同支持面。
- **失败不粘贴**：写入剪贴板后先回读确认（30 ms × 最多 400 ms）并等 40 ms，再做焦点守卫，最后粘贴；未就绪时提示「剪贴板没有准备好，原文未改动。」，避免「看起来成功但文字没变」。
- **剪贴板快照按序列号还原**：保留 `IDataObject` 并附带纯文本回退；成功路径调用 `Keep()` 不还原；失败路径只在当前序列号仍等于本次 Ctrl+C 写入值时还原，不覆盖用户在此期间的新复制。
- **水平拖选**：真实单行选区常常只有 1–2 px 高，因此接受「任一方向 >4px」的拖选，并给 bbox 补 20px 高度带，否则右键点不中。
- **Esc 注入识别**：`NativeMenuCloser` 发送的 Esc 带 `LLKHF_INJECTED`，取消钩子必须忽略它，只响应用户真实按键。
- **语言可扩展**：翻译器只接收 `(source, target)` 代码；`LanguageCatalog` 增加条目 + 目标语言下拉即可支持新语言对，提示词与后处理按目标脚本工作。

## 失败模式

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 浮条不出现 | 没有拖选/双击选区；右键不在 bbox；窗口已切换；浮条被关闭 | 托盘确认已启用；`--debug` 看 `right down ... selection=...` |
| 点浮条没反应 | 钩子未启动 / 浮条被其它置顶窗口遮挡 | 日志中应有 `chip clicked`；可改用 `Ctrl+Alt+E` |
| 翻译成功但文字没变 | 目标控件只读；或 Ctrl+V 被应用拦截；或剪贴板未就绪 | 写入后回读 + 40 ms 等待；译文留在剪贴板；焦点/粘贴守卫会尽量阻止误报 |
| 右键菜单没关掉 | 应用使用非 `#32768` 且不响应 Esc | 最多等 600 ms 后放弃；可用热键 |
| Key 无效/余额不足/限流 | 服务端错误 | 401/402/429 分别提示，原文不动 |
| 启动即退出 | 单实例互斥体已有进程 | 查看托盘；或结束旧进程 |

## 命名与数据目录

0.2.0 起项目名为 **Easy translation**（原 Zh2En）。默认配置目录 `%APPDATA%\EasyTranslation`，依次回退到 `%LOCALAPPDATA%\EasyTranslation`、程序目录 `data`、`%TEMP%\EasyTranslation`。首次启动若新配置不存在，会从旧版 `%APPDATA%\Zh2En\settings.json` 复制；卸载脚本同时清理新旧两套安装痕迹。
