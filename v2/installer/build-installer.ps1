# Dong goi app Windows v2.
#   .\build-installer.ps1 [-Version 2.0.0] [-SkipInstaller]
# Buoc 1: dotnet publish SELF-CONTAINED (khong can cai .NET tren may nguoi dung) vao v2\dist\self-contained.
# Buoc 2: tao ban "portable" (zip) trong v2\dist\release.
# Buoc 3: neu co Inno Setup 6 (ISCC.exe) thi bien dich QuanLyHangHoan.iss thanh bo cai .exe (cai theo nguoi dung, KHONG can quyen admin).
# KHONG dong vao du lieu nguoi dung (%LocalAppData%\QuanLyHangHoan\v2). Dong app dang chay truoc khi chay script nay.
param([string]$Version = '2.0.0', [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot            # ...\v2
$pub  = Join-Path $root 'dist\self-contained'
$rel  = Join-Path $root 'dist\release'
New-Item -ItemType Directory -Force $rel | Out-Null

if (Get-Process QuanLyHangHoan.App -ErrorAction SilentlyContinue) { throw 'App dang chay: dong app truoc khi dong goi.' }
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }

dotnet publish (Join-Path $root 'src\QuanLyHangHoan.App') -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishReadyToRun=false -o $pub --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish loi' }

$zip = Join-Path $rel "QuanLyHangHoan-v2-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip
"Portable: $zip ({0:N0} MB)" -f ((Get-Item $zip).Length / 1MB)

if ($SkipInstaller) { return }
$iscc = @("$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { 'Chua cai Inno Setup 6: bo qua bo cai .exe (cai bang: winget install JRSoftware.InnoSetup). Van dung duoc ban portable.'; return }
& $iscc "/DAppVersion=$Version" "/DSourceDir=$pub" "/DOutDir=$rel" (Join-Path $PSScriptRoot 'QuanLyHangHoan.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup loi' }
Get-ChildItem $rel -Filter 'QuanLyHangHoan-v2-*-setup.exe' | ForEach-Object { "Bo cai: $($_.FullName) ({0:N0} MB)" -f ($_.Length / 1MB) }
