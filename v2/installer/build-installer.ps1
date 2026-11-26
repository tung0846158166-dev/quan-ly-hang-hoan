# Dong goi app Windows v2: HAI bo cai (chu du an 26/11/2026)
#   - May CHINH: day du (tao ma ghep, quan ly thiet bi da ghep).
#   - May PHU  : bo "Tao ma ghep" + "Thiet bi da ghep" trong Cai dat (dotnet publish -p:Edition=Secondary).
#   .\build-installer.ps1 [-Version 2.1.0] [-Edition Both|Main|Secondary] [-SkipInstaller]
# Buoc 1: dotnet publish SELF-CONTAINED (khong can cai .NET tren may nguoi dung) vao v2\dist\self-contained-<loai>.
# Buoc 2: ban "portable" (zip) trong v2\dist\release.
# Buoc 3: neu co Inno Setup 6 (ISCC.exe) thi bien dich QuanLyHangHoan.iss thanh bo cai .exe (cai theo nguoi dung, KHONG can quyen admin).
# KHONG dong vao du lieu nguoi dung (%LocalAppData%\QuanLyHangHoan\v2). Dong app dang chay truoc khi chay script nay.
param([string]$Version = '2.1.0', [ValidateSet('Both', 'Main', 'Secondary')][string]$Edition = 'Both', [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot            # ...\v2
$rel  = Join-Path $root 'dist\release'
New-Item -ItemType Directory -Force $rel | Out-Null

if (Get-Process QuanLyHangHoan.App -ErrorAction SilentlyContinue) { throw 'App dang chay: dong app truoc khi dong goi.' }
$iscc = @("$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1

$editions = if ($Edition -eq 'Both') { @('Main', 'Secondary') } else { @($Edition) }
foreach ($ed in $editions) {
    $tag = if ($ed -eq 'Secondary') { 'may-phu' } else { 'may-chinh' }
    $pub = Join-Path $root "dist\self-contained-$tag"
    if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
    # obj dung chung giua hai lan publish: xoa de hang so bien dich SECONDARY khong bi dinh tu lan truoc
    Remove-Item (Join-Path $root 'src\QuanLyHangHoan.App\obj\Release') -Recurse -Force -ErrorAction SilentlyContinue

    dotnet publish (Join-Path $root 'src\QuanLyHangHoan.App') -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:Edition=$ed -p:PublishReadyToRun=false -o $pub --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish loi ($ed)" }

    $zip = Join-Path $rel "QuanLyHangHoan-v2-$Version-$tag-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip
    "Portable ($tag): $zip ({0:N0} MB)" -f ((Get-Item $zip).Length / 1MB)

    if ($SkipInstaller) { continue }
    if (-not $iscc) { 'Chua cai Inno Setup 6: bo qua bo cai .exe (cai bang: winget install JRSoftware.InnoSetup). Van dung duoc ban portable.'; continue }
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$pub" "/DOutDir=$rel" "/DEditionTag=$tag" (Join-Path $PSScriptRoot 'QuanLyHangHoan.iss') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup loi ($ed)" }
    Get-ChildItem $rel -Filter "QuanLyHangHoan-v2-$Version-$tag-setup.exe" | ForEach-Object { "Bo cai ($tag): $($_.FullName) ({0:N0} MB)" -f ($_.Length / 1MB) }
}
