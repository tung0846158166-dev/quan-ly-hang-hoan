# Nhap API token + Account ID cua TAI KHOAN CLOUDFLARE CHINH (27/11/2026).
# Luu vao cloud\worker\.secrets.main-account.local (khong dua len Git). Khong hien token ra man hinh.
$ErrorActionPreference = 'Stop'
$file = Join-Path (Split-Path -Parent $PSScriptRoot) '.secrets.main-account.local'
Write-Host 'Dan Account ID (32 ky tu, o trang chu tai khoan Cloudflare, cot ben phai):'
$acc = (Read-Host).Trim()
if ($acc -notmatch '^[0-9a-f]{32}$') { Write-Host 'Account ID khong dung dang (32 ky tu 0-9 a-f).'; Read-Host 'Nhan Enter de dong'; exit 1 }
Write-Host 'Dan API token (khong hien ra khi dan):'
$sec = Read-Host -AsSecureString
$tok = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)).Trim()
if ($tok.Length -lt 30) { Write-Host 'Token qua ngan, kiem tra lai.'; Read-Host 'Nhan Enter de dong'; exit 1 }
# kiem tra token con hieu luc (khong in token)
try {
  $r = Invoke-RestMethod -Uri 'https://api.cloudflare.com/client/v4/user/tokens/verify' -Headers @{ Authorization = "Bearer $tok" }
  if (-not $r.success) { throw 'token khong hop le' }
} catch { Write-Host "Token khong dung duoc: $($_.Exception.Message)"; Read-Host 'Nhan Enter de dong'; exit 1 }
"CLOUDFLARE_ACCOUNT_ID=$acc`nCLOUDFLARE_API_TOKEN=$tok" | Set-Content -Path $file -Encoding ascii
Write-Host 'Da luu. Token hop le. Ban co the dong cua so nay va bao Claude.'
Read-Host 'Nhan Enter de dong'
