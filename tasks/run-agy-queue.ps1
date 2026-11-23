# Chay lan luot cac phieu trong tasks\queue.txt bang agy (moi dong mot ma phieu, vi du T020). KHONG chay hai phieu cung luc (cung sua App/).
# Bo qua phieu da co bao cao "TRANG THAI: XONG". Sau moi phieu chay build + test; do thi DUNG hang doi (khong de loi chong chat).
# Doc lai queue.txt sau moi phieu nen co the them phieu vao cuoi file khi dang chay.
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\kimth\Projects\quan-ly-hang-hoan'
$stop = 'tasks\reports\queue-stopped.txt'
if (Test-Path $stop) { Remove-Item $stop -Force }

function Wait-AgyIdle { while (Get-Process agy -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 20 } }
function Test-Done($id) {
    $r = "tasks\reports\$id-report.md"
    if (-not (Test-Path $r)) { return $false }
    $last = (Get-Content $r -Encoding UTF8 -Tail 3) -join ' '
    return ($last -match 'XONG' -and $last -notmatch 'CH.{1,3}A\s+XONG')
}

Wait-AgyIdle
while ($true) {
    $next = $null
    foreach ($line in (Get-Content 'tasks\queue.txt' -ErrorAction SilentlyContinue)) {
        $id = $line.Trim()
        if ($id -eq '' -or $id.StartsWith('#')) { continue }
        if (-not (Test-Done $id)) { $next = $id; break }
    }
    if (-not $next) { 'Hang doi da het.' | Out-File $stop -Encoding utf8; break }

    $spec = Get-ChildItem "tasks\$next-*.md" | Select-Object -First 1
    if (-not $spec) { "Khong thay phieu $next" | Out-File $stop -Encoding utf8; break }

    Wait-AgyIdle
    $log = "tasks\reports\$next-agy-output.log"
    $prompt = "Doc AGENTS.md roi lam theo tasks/$($spec.Name). Claude co the dang sua v2/src/QuanLyHangHoan.Data/ va Core/ va cac test ngoai UiTests/: KHONG sua cac tep do. Neu thieu API o Data thi ghi vao bao cao."
    agy -p $prompt --dangerously-skip-permissions *> $log

    if (-not (Test-Done $next)) { "Phieu $next chua co bao cao XONG sau khi agy dung. Xem $log" | Out-File $stop -Encoding utf8; break }

    Set-Location 'C:\Users\kimth\Projects\quan-ly-hang-hoan\v2'
    $b = dotnet build QuanLyHangHoan.sln --nologo -v q 2>&1 | Out-String
    $t = dotnet test QuanLyHangHoan.sln --no-build --nologo -v q 2>&1 | Out-String
    Set-Location 'C:\Users\kimth\Projects\quan-ly-hang-hoan'
    if ($b -notmatch '0 Error' -or $t -notmatch 'Failed:\s+0') { "Sau $next build/test DO. Dung hang doi de claude kiem tra.`n$t" | Out-File $stop -Encoding utf8; break }
}
