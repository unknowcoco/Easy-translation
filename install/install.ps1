param(
    [switch]$DesktopShortcut,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

# 若正在运行，先结束（否则 exe 被占用，复制会失败）
Get-Process -Name 'EasyTranslation','Zh2En' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ('检测到 {0} 正在运行，先结束进程以便覆盖安装…' -f $_.ProcessName)
    $_ | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'publish'
$exeName = 'EasyTranslation.exe'
$exe = Join-Path $source $exeName
if (-not (Test-Path $exe)) {
    throw "找不到 $exe。请先在仓库根目录运行 build.ps1 -SelfContained（发布自包含文件夹到 publish）。"
}

$target = Join-Path $env:LOCALAPPDATA 'Programs\EasyTranslation'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$installedExe = Join-Path $target $exeName
$shell = New-Object -ComObject WScript.Shell

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
$startLnk = Join-Path $startMenu 'Easy translation.lnk'
$shortcut = $shell.CreateShortcut($startLnk)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Description = 'Easy translation 划词翻译替换（右键浮条）'
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $desktopLnk = Join-Path $desktop 'Easy translation.lnk'
    $shortcut = $shell.CreateShortcut($desktopLnk)
    $shortcut.TargetPath = $installedExe
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = "$installedExe,0"
    $shortcut.Description = 'EasyTranslation 划词翻译替换（右键浮条）'
    $shortcut.Save()
    Write-Host "已创建桌面快捷方式：$desktopLnk"
}

Write-Host "已安装到：$target"
Write-Host "开始菜单：$startLnk"
Write-Host "提示：首次运行请在托盘图标右键 → 设置，填入 DeepSeek API Key（也可先填其他 OpenAI 兼容服务）。"

if (-not $NoStart) {
    Start-Process -FilePath $installedExe
}
