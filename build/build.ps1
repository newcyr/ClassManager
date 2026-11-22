# 一键构建：生成资源 → 发布单文件 → 打包安装程序
# 用法：powershell -ExecutionPolicy Bypass -File build\build.ps1 [-SkipInstaller]

param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "[1/4] 生成图标与内置铃声..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "make-assets.ps1")

Write-Host "[2/4] 发布 self-contained 单文件..." -ForegroundColor Cyan
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
& $dotnet publish "src\ClassManager.App\ClassManager.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o "build\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

Write-Host "[3/4] 补齐静态资源..." -ForegroundColor Cyan
Copy-Item "assets\icon.ico" "build\publish\assets\icon.ico" -Force
Copy-Item "assets\ringtone.wav" "build\publish\assets\ringtone.wav" -Force

if (-not $SkipInstaller) {
    Write-Host "[4/4] 编译安装包 (Inno Setup 6)..." -ForegroundColor Cyan
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "未找到 ISCC.exe，请先安装 Inno Setup 6" }
    & $iscc "installer\setup.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败" }
}
else {
    Write-Host "[4/4] 已跳过安装包编译" -ForegroundColor Yellow
}

Write-Host "完成：build\publish\ClassManager.exe" -ForegroundColor Green
if (-not $SkipInstaller) { Write-Host "安装包：release\ClassManagerSetup.exe" -ForegroundColor Green }
