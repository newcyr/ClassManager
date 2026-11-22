$repo = 'newcyr/newcyr.github.io'
$tmp = Join-Path $env:TEMP 'gh-push4'
$site = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\class-site'
$exe = Join-Path $site 'downloads\ClassManagerSetup.exe'

$inFile = Join-Path $env:TEMP 'cred-in.txt'
$outFile = Join-Path $env:TEMP 'cred-out.txt'
Set-Content -Path $inFile -Value @('protocol=https', 'host=github.com', '') -Encoding ASCII
Start-Process git -ArgumentList 'credential', 'fill' -RedirectStandardInput $inFile -RedirectStandardOutput $outFile -NoNewWindow -Wait
$cred = Get-Content $outFile -Raw
Remove-Item $inFile, $outFile -Force -ErrorAction SilentlyContinue
$token = [regex]::Match($cred, 'password=(.+)').Groups[1].Value.Trim()
$headers = @{ Authorization = "token $token"; 'User-Agent' = 'ClassManager-Deploy'; Accept = 'application/vnd.github+json' }
$ProgressPreference = 'SilentlyContinue'

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
for ($i = 1; $i -le 4; $i++) {
    git clone --depth 1 "https://github.com/$repo.git" $tmp 2>&1 | Write-Host
    if (Test-Path (Join-Path $tmp '.git')) { break }
    Start-Sleep 10
}
if (-not (Test-Path (Join-Path $tmp '.git'))) { Write-Host 'clone failed'; exit 1 }

Copy-Item (Join-Path $site 'index.html') (Join-Path $tmp 'class\index.html') -Force
Copy-Item (Join-Path $site 'update.html') (Join-Path $tmp 'class\update.html') -Force
Copy-Item (Join-Path $site 'gx.json') (Join-Path $tmp 'class\gx.json') -Force
Copy-Item (Join-Path $site 'wb.json') (Join-Path $tmp 'class\wb.json') -Force
Copy-Item (Join-Path $site 'kb.html') (Join-Path $tmp 'class\kb.html') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $site 'kj.html') (Join-Path $tmp 'class\kj.html') -Force
Copy-Item (Join-Path $site 'wb\index.html') (Join-Path $tmp 'class\wb\index.html') -Force
Copy-Item $exe (Join-Path $tmp 'class\downloads\ClassManagerSetup.exe') -Force
git -C $tmp add class 2>&1 | Write-Host
git -C $tmp -c user.name=ClassManager -c user.email='classmanager@users.noreply.github.com' commit -m 'release 1.1.0: auto update, seewo fixes, daily timetables, update notice' 2>&1 | Write-Host

for ($i = 1; $i -le 5; $i++) {
    git -C $tmp push origin main 2>&1 | Write-Host
    if ($LASTEXITCODE -eq 0) { Write-Host 'pushed=True'; break }
    Write-Host ("push retry {0}" -f $i)
    Start-Sleep 10
}

$release = $null
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/v1.1.0" -Headers $headers -TimeoutSec 30
    Write-Host ("release exists id={0}" -f $release.id)
} catch {
    $body = @{
        tag_name = 'v1.1.0'
        name = 'v1.1.0'
        body = '自动更新、希沃账号与登录修复、每日独立课表（提前 2 分钟）、更新通知。'
        draft = $false
        prerelease = $false
    } | ConvertTo-Json -Compress
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases" -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 60
    Write-Host ("release created id={0}" -f $release.id)
}

$assets = @(Invoke-RestMethod "https://api.github.com/repos/$repo/releases/$($release.id)/assets" -Headers $headers -TimeoutSec 60)
foreach ($a in $assets) {
    if ($a.name -eq 'ClassManagerSetup.exe') {
        Invoke-RestMethod "https://api.github.com/repos/$repo/releases/assets/$($a.id)" -Method Delete -Headers $headers -TimeoutSec 60 | Out-Null
        Write-Host ("deleted old asset {0}" -f $a.id)
    }
}
$upHeaders = @{ Authorization = "token $token"; 'User-Agent' = 'ClassManager-Deploy' }
for ($i = 1; $i -le 4; $i++) {
    try {
        $u = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=ClassManagerSetup.exe"
        $resp = Invoke-RestMethod $u -Method Post -Headers $upHeaders -ContentType 'application/octet-stream' -InFile $exe -TimeoutSec 900
        Write-Host ("asset uploaded size={0}" -f $resp.size)
        break
    } catch {
        Write-Host ("upload retry {0}: {1}" -f $i, $_.Exception.Message)
        Start-Sleep 10
    }
}
