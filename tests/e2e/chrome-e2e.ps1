# End-to-end smoke test for EasyTranslation (run with the app already started in --echo mode).
# Opens Notepad with Chinese text, drag-selects it with the mouse, right-clicks,
# clicks the EasyTranslation chip and verifies the text was replaced.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class U {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string cls, string win);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, System.Text.StringBuilder l);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
}
"@

function Send-Ctrl([byte]$vk) {
    [U]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    [U]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [U]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
    [U]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
}

function Get-WindowsOf([uint32]$targetPid) {
    $rows = New-Object System.Collections.ArrayList
    $cb = [U+EnumProc]{
        param($h, $l)
        $p = [uint32]0
        [void][U]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -eq $targetPid) {
            $title = New-Object System.Text.StringBuilder 256
            [void][U]::GetWindowText($h, $title, 256)
            $cls = New-Object System.Text.StringBuilder 256
            [void][U]::GetClassName($h, $cls, 256)
            $r = New-Object U+RECT
            [void][U]::GetWindowRect($h, [ref]$r)
            [void]$rows.Add([pscustomobject]@{
                Handle = $h; Title = $title.ToString(); Class = $cls.ToString();
                Visible = [U]::IsWindowVisible($h); Left = $r.Left; Top = $r.Top;
                Right = $r.Right; Bottom = $r.Bottom
            })
        }
        return $true
    }
    [void][U]::EnumWindows($cb, [IntPtr]::Zero)
    return $rows
}


$html = ([Uri](Join-Path $PSScriptRoot 'chrome-e2e.html')).AbsoluteUri
Get-Process chrome -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$profile = Join-Path $env:TEMP 'zh2en-chrome-e2e'
if (Test-Path $profile) { Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue }
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$cp = Start-Process $chrome -ArgumentList @('--user-data-dir=' + $profile, '--no-first-run', '--no-default-browser-check', '--disable-sync', '--window-position=100,100', '--window-size=1000,700', $html) -PassThru
Start-Sleep -Milliseconds 2500
$cw = [IntPtr]::Zero
for ($i=0; $i -lt 40; $i++) {
    $w = Get-Process chrome -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like '*EasyTranslation*' } | Select-Object -First 1
    if (-not $w) { $w = Get-Process chrome -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
    if ($w) { $cw = $w.MainWindowHandle; break }
    Start-Sleep -Milliseconds 250
}
if ($cw -eq [IntPtr]::Zero) { throw 'chrome window not found' }
[void][U]::ShowWindow($cw, 9)
[void][U]::MoveWindow($cw, 100, 100, 1000, 700, $true)
[void][U]::SetForegroundWindow($cw)
Start-Sleep -Milliseconds 600

$sx = 132; $sy = 250
[void][U]::SetCursorPos($sx, $sy); Start-Sleep -Milliseconds 120
[U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
for ($i = 1; $i -le 18; $i++) { [U]::mouse_event(0x0001, 30, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 25 }
[U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 300

$rx = 300; $ry = 250
[void][U]::SetCursorPos($rx, $ry); Start-Sleep -Milliseconds 60
[U]::mouse_event(0x0008, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40
[U]::mouse_event(0x0010, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 700

$appPid = (Get-Process EasyTranslation -ErrorAction Stop | Select-Object -First 1).Id
$chip = Get-WindowsOf ([uint32]$appPid) | Where-Object {
    $_.Visible -and $_.Top -lt $ry -and $_.Bottom -gt ($ry - 90) -and $_.Right -gt ($rx - 220) -and $_.Left -lt ($rx + 220) -and $_.Bottom -gt $_.Top
} | Sort-Object { [Math]::Abs($_.Left - $rx) } | Select-Object -First 1
if (-not $chip) { Write-Output 'chip windows:'; Get-WindowsOf ([uint32]$appPid) | Format-Table -AutoSize | Out-String | Write-Output; throw 'chip not found (chrome)' }
Write-Output ("chip: {0},{1} - {2},{3}" -f $chip.Left, $chip.Top, $chip.Right, $chip.Bottom)

$cx = [int](($chip.Left + $chip.Right) / 2); $cy = [int](($chip.Top + $chip.Bottom) / 2)
[void][U]::SetCursorPos($cx, $cy); Start-Sleep -Milliseconds 100
[U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40
[U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 2800

Write-Output '--- chrome clipboard right after workflow ---'
Write-Output (Get-Clipboard -Raw)

[void][U]::SetForegroundWindow($cw); Start-Sleep -Milliseconds 300
Send-Ctrl 0x41; Start-Sleep -Milliseconds 120
Send-Ctrl 0x43; Start-Sleep -Milliseconds 300
Write-Output '--- chrome clipboard after replacement ---'
Write-Output (Get-Clipboard -Raw)

try { $cp.Kill() } catch {}
Stop-Process -Name chrome -ErrorAction SilentlyContinue
