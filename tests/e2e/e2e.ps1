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

$sample = Join-Path $PSScriptRoot '..\..\e2e-sample.txt'
[IO.File]::WriteAllText($sample, '你好，世界，这是一个测试。', (New-Object System.Text.UTF8Encoding($true)))

$np = Start-Process notepad.exe -ArgumentList ('"' + $sample + '"') -PassThru
Start-Sleep -Milliseconds 900
$np.Refresh()
$npw = [U]::GetForegroundWindow()
if ($npw -eq [IntPtr]::Zero -or $np.MainWindowHandle -eq [IntPtr]::Zero) {
    $npw = ($np | Select-Object -ExpandProperty MainWindowHandle)
}
if ($npw -eq [IntPtr]::Zero) {
    $candidate = Get-WindowsOf ([uint32]$np.Id) | Where-Object { $_.Visible -and $_.Title -like '*e2e-sample*' } | Select-Object -First 1
    if ($candidate) { $npw = $candidate.Handle }
}
if ($npw -eq [IntPtr]::Zero) { throw 'Notepad window not found' }

[void][U]::ShowWindow($npw, 9)
[void][U]::MoveWindow($npw, 120, 120, 900, 520, $true)
[void][U]::SetForegroundWindow($npw)
Start-Sleep -Milliseconds 400
$cls = New-Object System.Text.StringBuilder 256
[void][U]::GetClassName($npw, $cls, 256)
Write-Output ("npw=0x{0:X} class={1} fg=0x{2:X}" -f $npw.ToInt64(), $cls.ToString(), [U]::GetForegroundWindow().ToInt64())

$edit = [U]::FindWindowEx($npw, [IntPtr]::Zero, 'Edit', $null)
if ($edit -eq [IntPtr]::Zero) {
    $edit = Get-WindowsOf ([uint32]$np.Id) | Where-Object { $_.Class -eq 'Edit' } | Select-Object -First 1 -ExpandProperty Handle
}
if ($edit -eq [IntPtr]::Zero) { throw 'Notepad edit control not found' }
Write-Output ("edit=0x{0:X}" -f $edit.ToInt64())

$r = New-Object U+RECT
[void][U]::GetWindowRect($edit, [ref]$r)
Write-Output ("edit rect: {0},{1} - {2},{3}" -f $r.Left, $r.Top, $r.Right, $r.Bottom)

$sx = $r.Left + 6
$sy = $r.Top + 8
[void][U]::SetCursorPos($sx, $sy)
Start-Sleep -Milliseconds 80
[U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # left down
Start-Sleep -Milliseconds 60
for ($i = 1; $i -le 12; $i++) {
    [U]::mouse_event(0x0001, 22, 0, 0, [IntPtr]::Zero)   # relative move (visible to WH_MOUSE_LL)
    Start-Sleep -Milliseconds 25
}
[U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # left up
Start-Sleep -Milliseconds 200

$rx = $sx + 120
$ry = $sy + 6
[void][U]::SetCursorPos($rx, $ry)
[U]::mouse_event(0x0008, 0, 0, 0, [IntPtr]::Zero)   # right down
Start-Sleep -Milliseconds 40
[U]::mouse_event(0x0010, 0, 0, 0, [IntPtr]::Zero)   # right up
Start-Sleep -Milliseconds 600

$appPid = (Get-Process EasyTranslation -ErrorAction Stop | Select-Object -First 1).Id
$chip = Get-WindowsOf ([uint32]$appPid) | Where-Object {
    $_.Visible -and $_.Top -lt $ry -and $_.Bottom -gt ($ry - 80) -and $_.Right -gt ($rx - 200) -and $_.Left -lt ($rx + 200) -and $_.Bottom -gt $_.Top
} | Sort-Object { [Math]::Abs($_.Left - $rx) } | Select-Object -First 1

if (-not $chip) {
    Write-Output 'chip windows of app:'
    Get-WindowsOf ([uint32]$appPid) | Format-Table -AutoSize | Out-String | Write-Output
    throw 'EasyTranslation chip not found'
}

Write-Output ("chip: {0},{1} - {2},{3} visible={4}" -f $chip.Left, $chip.Top, $chip.Right, $chip.Bottom, $chip.Visible)
$cx = [int](($chip.Left + $chip.Right) / 2)
$cy = [int](($chip.Top + $chip.Bottom) / 2)
[void][U]::SetCursorPos($cx, $cy)
Start-Sleep -Milliseconds 80
[U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 40
[U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 2500

$clipAfter = Get-Clipboard -Raw
Write-Output '--- clipboard right after workflow ---'
Write-Output $clipAfter

[void][U]::SetForegroundWindow($npw)
Start-Sleep -Milliseconds 200
Send-Ctrl 0x41   # Ctrl+A
Start-Sleep -Milliseconds 100
Send-Ctrl 0x43   # Ctrl+C
Start-Sleep -Milliseconds 250

$replaced = Get-Clipboard -Raw
Write-Output '--- clipboard after replacement ---'
Write-Output $replaced

$buf = New-Object System.Text.StringBuilder 2048
[void][U]::SendMessage($edit, 0x000D, [IntPtr]2048, $buf)
Write-Output '--- edit text after replacement ---'
Write-Output $buf.ToString()

$np.Kill()
$result = @{
    chip = "$($chip.Left),$($chip.Top)-$($chip.Right),$($chip.Bottom)"
    replaced = $replaced
}
$result | ConvertTo-Json -Compress | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot '..\..\e2e-result.json')
