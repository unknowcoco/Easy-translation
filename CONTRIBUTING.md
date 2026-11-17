# 参与开发

感谢你愿意改进 Easy translation。这个项目是一个 Windows 托盘小工具，代码量不大，欢迎 issue、语言对建议和 PR。

## 环境

- Windows 10 1809+ / Windows 11 x64（开发与验收在 Windows 10 22H2 上完成）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows PowerShell 5.1（项目脚本使用它；PowerShell 7 也可以运行大部分命令）
- 端到端脚本需要**交互式桌面会话**（会移动鼠标、模拟按键，不能在没有桌面的 CI 会话里跑 UI 部分）

## 常用命令

```powershell
dotnet build EasyTranslation.sln
dotnet test tests/EasyTranslation.Tests/EasyTranslation.Tests.csproj
.\build.ps1                    # 测试 + 框架依赖发布
.\build.ps1 -SelfContained     # 测试 + 自包含单文件发布（约 63 MB）
```

UI 冒烟：

```powershell
.\publish\EasyTranslation.exe --echo --debug
powershell -ExecutionPolicy Bypass -File .\tests\e2e\e2e.ps1
powershell -ExecutionPolicy Bypass -File .\tests\e2e\chrome-e2e.ps1
```

## 约定

- **所有 `.ps1` 必须保存为 UTF-8 with BOM。** Windows PowerShell 5.1 会把无 BOM 文件里的中文按 ANSI 解析，导致 `Missing closing '}'` 之类的解析错误。编辑后请重新保存 BOM，或用 `[ScriptBlock]::Create((Get-Content -Raw .\script.ps1))` 校验。
- 不要在日志里记录选中的原文、API Key 或其他用户内容。
- 不引入新的运行时依赖；翻译后端用 `HttpClient` 直接调用。
- 公共逻辑（选区状态机、分块、后处理、配置归一化、工作流分支）应有单元测试；纯 UI 变化请在 PR 里附截图。
- 修改用户可见行为时，同步更新 `README.md`、`CHANGELOG.md`，必要时更新 `docs/acceptance.md`。

## 提交 PR

1. Fork 并新建分支。
2. 保持改动聚焦；如果是新语言，请同时更新 `LanguageCatalog`、`LanguageGuesser` 和测试。
3. 跑 `dotnet test`，确认 86 个测试全绿。
4. 在 PR 描述里说明：改了什么、为什么、怎么验证（命令/截图）。

## 许可

提交的代码默认以 [MIT](LICENSE) 许可发布。
