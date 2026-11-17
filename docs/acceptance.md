# 验收记录

日期：2026-11-17　环境：Windows 10 22H2 x64 (19045.6466)、1920×1080 @96DPI、.NET 8.0.8 Desktop Runtime。
应用版本：**0.2.0**（Easy translation；`--echo` 假翻译用于链路验收，真实翻译需在设置中填 Key）。

## 自动化结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 单元测试 | `dotnet test tests/EasyTranslation.Tests/EasyTranslation.Tests.csproj` | **86/86 通过**（语言目录、简繁识别、拉丁语系猜词、DeepL 能力矩阵、全目标脚本校验、配置、选区、工作流） |
| 记事本 E2E | `powershell -File tests/e2e/e2e.ps1` | 0.1.0 多轮 **9/9**（debug 5、发布版 3、安装副本 1）；0.2.0 自包含发布版 **1/1**：edit text / 剪贴板均为 `[En] 你好，世界，这是一个测试。` |
| 日语目标 E2E | 将 `TargetLanguage` 改为 `ja` 后 `powershell -File tests/e2e/e2e.ps1` | **1/1 通过**：浮条/翻译/替换按设置使用日文，edit text 与剪贴板均为 `[日] 你好，世界，这是一个测试。` |
| Chrome E2E | `powershell -File tests/e2e/chrome-e2e.ps1` | 0.1.0 **3/3**；0.2.0 **1/1**：网页 textarea 整行拖选→右键→浮条→点击→内容变为 `[En] 你好，世界，这是一个测试。` |
| 安装 / 卸载 | 0.1.0：`install.ps1`、`uninstall.ps1 -KeepData`、再 `install.ps1`；0.2.0：`install.ps1 -NoStart` → 检查目录/快捷方式 → `uninstall.ps1 -KeepData` | 通过：0.2.0 安装到 `%LOCALAPPDATA%\Programs\EasyTranslation` 并创建「Easy translation.lnk」；卸载清理新旧程序目录与快捷方式，`-KeepData` 保留 `%APPDATA%\Zh2En` 旧配置 |
| 设置窗渲染 | `EasyTranslation.exe --echo --settings` + 截图 | 通过：服务商/语言/行为/高级/保存取消均可见；标题「Easy translation 设置」 |
| 发布（框架依赖） | `build.ps1` | 单文件 `publish\EasyTranslation.exe` |
| 发布（自包含） | `build.ps1 -SelfContained` | 单文件 `publish\EasyTranslation.exe`，约 **63 MB**，签名 **Valid** |
| 签名 | `sign.ps1` + `Get-AuthenticodeSignature` | 通过：`CN=Easy translation Local Signing`（Thumbprint `F44E1773A7F37D0360C69F9E3F9ECE8FF1FB7E12`），仅本机 `TrustedPeople` 信任 |

E2E 脚本要点：必须 UTF-8 with BOM；拖选使用 `mouse_event(MOUSEEVENTF_MOVE)` 相对移动，因为 `SetCursorPos` 不会触发 `WH_MOUSE_LL`；Chrome 的 `--user-data-dir` 不能含空格，测试页 URL 要用绝对 `file://` URI 且不加引号；运行 Chrome E2E 前脚本会结束已有 Chrome，并按窗口标题 `EasyTranslation E2E` 定位测试页。

## 修复记录：粘贴偶发不生效（剪贴板就绪竞态）

现象：workflow 返回 Success，但记事本/Chrome 的目标控件偶发未变化；workflow 结束 2.5 s 后剪贴板却已是译文。

定位：独立探针用同样的扫描码 `SendInput` 可稳定粘贴，排除按键注入；失败时 `FocusGuard` 校验通过、`paste sent` 已打印。原因是 `Clipboard.SetText`（临时 STA 线程写入并 flush）刚返回时，目标程序立即处理 `Ctrl+V` 可能还读不到剪贴板。

修复：`TranslateReplaceWorkflow` 在写剪贴板后轮询 `GetText()` 与译文比对（30 ms × 最多 400 ms），未就绪则提示「剪贴板没有准备好，原文未改动。」并且不粘贴；就绪后再等 40 ms 才发送 `Ctrl+V`。修复后记事本、Chrome E2E 稳定通过。

## 重命名与迁移（0.2.0）

- exe / 安装目录 / 快捷方式 / 自启动项 / 互斥体 / 配置目录均改为 `Easy translation`。
- 首次启动时若新配置不存在，会从 `%APPDATA%\Zh2En\settings.json`（或 `%LOCALAPPDATA%\Zh2En`）复制旧配置。
- 卸载脚本同时清理 `EasyTranslation` 与历史 `Zh2En` 的目录、快捷方式和 Run 键。
- 图标改为蓝底白 `L`，由 `tools/make_icon.py` 生成多尺寸 ICO。

## 人工验收矩阵（发布前逐项勾选）

| # | 场景 | 步骤 | 期望 | 状态 |
| --- | --- | --- | --- | --- |
| 1 | 记事本 | 输入中文，拖选，右键，点浮条 | 原地变英文；Ctrl+Z 可撤销 | ✅ 自动化 |
| 2 | Chrome 网页 textarea | 在网页输入框输入中文，拖选，右键，点浮条 | 原地变英文；系统菜单自动关闭 | ✅ 自动化 |
| 3 | Chrome 地址栏 | 地址栏输入中文，拖选，右键，点浮条 | 原地变英文 | ☐ |
| 4 | Edge 网页 | 同 2 | 同上 | ☐ |
| 5 | 微信 / QQ 输入框 | 输入中文，拖选，右键，点浮条 | 同上 | ☐ |
| 6 | WPS 文字 | 输入中文，拖选，右键，点浮条 | 同上 | ☐ |
| 7 | 资源管理器重命名框 | 重命名文件，输入中文，拖选，右键，点浮条 | 同上 | ☐ |
| 8 | 只读网页正文 | 拖选网页正文，右键，点浮条 | 不破坏页面；译文留在剪贴板并提示 | ☐ |
| 9 | 无选区 | 桌面/文件图标上右键 | 不出现浮条 | ☐ |
| 10 | 右键点在选区外 | 拖选后把右键点移到选区外 | 不出现浮条 | ☐ |
| 11 | 切换窗口 | 拖选后切到别的窗口再右键 | 不出现浮条 | ☐ |
| 12 | Esc 取消 | 点浮条后立即按 Esc | 提示「已取消」，原文不变 | ☐ |
| 13 | 热键 | 拖选后按 Ctrl+Alt+E | 直接替换 | ☐ |
| 14 | 错误 Key | 设置里填错 Key，测试连接 | 提示 401/Key 无效；翻译时原文不变 | ☐ |
| 15 | 断网 | 拔网/禁用网卡后翻译 | 超时提示；原文不变；剪贴板恢复 | ☐ |
| 16 | 剪贴板恢复 | 先在别处复制一段文本，再翻译成功/失败 | 成功后剪贴板是译文（可撤销）；失败后恢复原剪贴板 | ☐ |
| 17 | 单实例 | 再次双击 exe | 提示已在运行，不出现第二个托盘图标 | ☐ |
| 18 | 托盘 | 停用/启用、打开日志、开机启动、退出 | 行为正确；退出后钩子释放 | ☐ |
| 19 | 设置持久化 | 改目标语言为日文并保存，重启 | 下拉保持日文；浮条显示日文方向 | ✅ 读配置/翻译贯通已自动化（日文 E2E）；UI 保存未自动化 |
| 20 | 空闲性能 | 空闲 5 分钟观察任务管理器 | CPU ≈0%；内存 < 120 MB | ☐ |
| 21 | 高 DPI | 125%/150% 缩放 | 浮条/设置窗不模糊、不越界 | ☐ |
| 22 | 管理员窗口 | 以管理员运行记事本，普通权限运行 Easy translation | 预期收不到事件；文档已说明 | ☐ |

## 已知问题 / 后续

- 只读区域（网页正文、PDF）无法检测「粘贴无效」，可能提示成功但页面未变；译文在剪贴板中。后续可在粘贴后重新 `Ctrl+C` 对比文本，或对只读场景只做「复制译文」。
- 部分应用（UWP、游戏）对模拟按键支持有限。
- 本机 `publish\EasyTranslation.exe` 用自签名证书签名（仅本机 TrustedPeople 信任）；GitHub Release 由 CI 构建、未签名，首次运行仍会触发 SmartScreen。正式分发需要 OV/EV 代码签名证书。
- 某些软件（如新版记事本）的右键菜单不会因 `Esc`/`WM_CANCELMODE` 关闭，浮条截图里仍可见系统菜单；不影响点击浮条本身。
