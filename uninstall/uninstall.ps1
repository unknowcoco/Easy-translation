param(
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'

Get-Process -Name 'EasyTranslation','Zh2En' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$desktop = [Environment]::GetFolderPath('Desktop')
foreach ($name in @('Easy translation.lnk', 'EasyTranslation.lnk', 'Zh2En.lnk')) {
    Remove-Item -Path (Join-Path $startMenu $name) -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $desktop $name) -Force -ErrorAction SilentlyContinue
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($name in @('EasyTranslation', 'Zh2En')) {
    if (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $name -Force
    }
}

foreach ($target in @(
    (Join-Path $env:LOCALAPPDATA 'Programs\EasyTranslation'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Zh2En')
)) {
    if (Test-Path $target) {
        Remove-Item -Path $target -Recurse -Force
        Write-Host "已删除程序目录：$target"
    }
}

foreach ($dir in @(
    (Join-Path $env:APPDATA 'EasyTranslation'),
    (Join-Path $env:LOCALAPPDATA 'EasyTranslation'),
    (Join-Path $env:APPDATA 'Zh2En'),
    (Join-Path $env:LOCALAPPDATA 'Zh2En')
)) {
    if (Test-Path $dir) {
        if ($KeepData) {
            Write-Host "保留配置与日志：$dir"
        }
        else {
            Remove-Item -Path $dir -Recurse -Force
            Write-Host "已删除配置与日志：$dir"
        }
    }
}

Write-Host 'Easy translation 已卸载。'
