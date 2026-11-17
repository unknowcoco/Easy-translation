# 为本机的 EasyTranslation.exe 生成/使用自签名代码签名证书并签名。
# 说明：自签名证书只在本机（已加入 TrustedPeople）受信任；分发给他人请使用正式代码签名证书。
param(
    [string]$Exe = "",
    [switch]$KeepCertificate
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $Exe) { $Exe = Join-Path $PSScriptRoot 'publish\EasyTranslation.exe' }
if (-not (Test-Path $Exe)) { throw "找不到 $Exe，请先运行 build.ps1。" }

$subject = 'CN=Easy translation Local Signing'
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
    Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -KeyUsage DigitalSignature `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "已生成本机签名证书：$($cert.Thumbprint)"
}
else {
    Write-Host "使用已有签名证书：$($cert.Thumbprint)"
}

$cer = Join-Path $PSScriptRoot 'install\EasyTranslation-Local.cer'
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
Write-Host "公钥证书：$cer"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host '已把公钥证书加入本机 TrustedPeople（只信任这一张证书，不加入根证书存储）。'
}
else {
    Write-Warning '当前不是管理员：证书未加入 TrustedPeople，签名会显示“不受信任”。请以管理员身份重跑本脚本。'
}

$sig = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256
Write-Host "签名结果：$($sig.Status)  $($sig.StatusMessage)"
Write-Host "文件：$Exe"
Write-Host '提示：安装目录里的副本请在安装后重新运行本脚本并指定 -Exe，或重新运行 install.ps1 复制已签名文件。'
