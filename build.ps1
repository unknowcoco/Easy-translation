# 构建、测试并发布 Easy translation
param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

Write-Host '== 单元测试 =='
dotnet test tests/EasyTranslation.Tests/EasyTranslation.Tests.csproj -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw '单元测试失败。' }

if ($SelfContained) {
    Write-Host '== 发布（自包含单文件，x64，约 150 MB）=='
    dotnet publish src/EasyTranslation.App/EasyTranslation.App.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false -o publish -v q -nologo
}
else {
    Write-Host '== 发布（框架依赖单文件，x64，需 .NET 8 Desktop Runtime）=='
    dotnet publish src/EasyTranslation.App/EasyTranslation.App.csproj -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish -v q -nologo
}
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }

# 如果本机已有 sign.ps1 生成的自签名证书，自动给产物签名
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=Easy translation Local Signing' -and $_.HasPrivateKey } |
    Select-Object -First 1
if ($cert) {
    $sig = Set-AuthenticodeSignature -FilePath (Join-Path $PSScriptRoot 'publish\EasyTranslation.exe') -Certificate $cert -HashAlgorithm SHA256
    Write-Host ("签名：" + $sig.Status)
}
else {
    Write-Host '未发现本机签名证书，跳过签名（可运行 sign.ps1 生成）。'
}

Write-Host '== 完成 =='
Write-Host '可执行文件：publish\EasyTranslation.exe'
Write-Host '安装：powershell -ExecutionPolicy Bypass -File install\install.ps1'
