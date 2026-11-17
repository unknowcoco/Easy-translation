# 处理 SmartScreen / “已保护你的电脑”提示：
#   1) 解除从浏览器/聊天下载留下的“锁定”标记（Zone.Identifier）
#   2) 检查并显示 Authenticode 签名状态
#   3) 可选：给 Windows Defender 添加程序目录排除项（仅当杀软误报时使用）
param(
    [switch]$AddDefenderExclusion
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$installed = Join-Path $env:LOCALAPPDATA 'Programs\EasyTranslation'

Write-Host '== 1. 解除下载锁定标记 =='
$roots = @($repo, $installed, (Join-Path $env:USERPROFILE 'Downloads'))
foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -Recurse -File -Include 'EasyTranslation.exe', '*.ps1' -ErrorAction SilentlyContinue | ForEach-Object {
        $hasMotw = $null -ne (Get-Item $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue)
        if ($hasMotw) {
            Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
            Write-Host ("  已解除：{0}" -f $_.FullName)
        }
    }
}
Write-Host '  完成（没有锁定标记的文件会自动跳过）。'

Write-Host ''
Write-Host '== 2. 签名状态 =='
$exe = Join-Path $installed 'EasyTranslation.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $repo 'publish\EasyTranslation.exe' }
if (Test-Path $exe) {
    $sig = Get-AuthenticodeSignature $exe
    Write-Host ("  文件：{0}" -f $exe)
    Write-Host ("  状态：{0}" -f $sig.Status)
    if ($sig.SignerCertificate) { Write-Host ("  发布者：{0}" -f $sig.SignerCertificate.Subject) }
}
else {
    Write-Host '  未找到 EasyTranslation.exe。'
}

if ($AddDefenderExclusion) {
    Write-Host ''
    Write-Host '== 3. Windows Defender 排除项 =='
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning '需要管理员权限才能添加 Defender 排除项。'
    }
    else {
        if (Test-Path $installed) { Add-MpPreference -ExclusionPath $installed; Write-Host ("  已排除：{0}" -f $installed) }
        Add-MpPreference -ExclusionPath (Join-Path $repo 'publish'); Write-Host ("  已排除：{0}" -f (Join-Path $repo 'publish'))
    }
}

Write-Host ''
Write-Host '如果仍然出现 SmartScreen：'
Write-Host '  · 点“更多信息” → “仍要运行”（对未广泛传播的未签名程序，这是一次性的预期提示）。'
Write-Host '  · 想在本机彻底消除发布者未知提示：以管理员运行 sign.ps1，再运行 install\install.ps1 重新安装。'
Write-Host '  · 分发给其他人：自签名证书在别人机器上不受信任，需要正式的 OV/EV 代码签名证书。'
