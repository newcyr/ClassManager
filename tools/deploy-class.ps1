# 将 class-site/ 发布到 GitHub Pages 仓库的 /class/ 目录
# 用法：把 config.local.json.example 复制为 config.local.json 并填入 token，然后执行本脚本。
# 该脚本通过 GitHub Contents API 上传文件，不需要本机 git，也不依赖 GitHub 页面可达。

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $root "config.local.json"

if (-not (Test-Path $configPath)) {
    throw "缺少 config.local.json，请先复制 config.local.json.example 并填写 githubToken / owner / repo / branch"
}

$cfg = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $cfg.githubToken -or $cfg.githubToken -like "ghp_xxx*") { throw "请填写有效的 githubToken" }
$owner = if ($cfg.owner) { $cfg.owner } else { "newcyr" }
$repo = if ($cfg.repo) { $cfg.repo } else { "newcyr.github.io" }
$branch = if ($cfg.branch) { $cfg.branch } else { "main" }
$remoteDir = if ($cfg.remoteDir) { $cfg.remoteDir } else { "class" }

$headers = @{
    Authorization          = "Bearer $($cfg.githubToken)"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "ClassManager-Deploy"
}

function Get-RemoteSha([string]$path) {
    $url = "https://api.github.com/repos/$owner/$repo/contents/$path?ref=$branch"
    try {
        $resp = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        return $resp.sha
    } catch {
        return $null
    }
}

$siteDir = Join-Path $root "class-site"
$files = Get-ChildItem $siteDir -Recurse -File | Where-Object { $_.Name -notlike ".*" }
$ok = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($siteDir.Length + 1).Replace('\', '/')
    $remotePath = "$remoteDir/$relative"
    $content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file.FullName))
    $sha = Get-RemoteSha $remotePath

    $body = @{
        message = "chore(class): update $relative"
        content = $content
        branch  = $branch
    }
    if ($sha) { $body.sha = $sha }

    $json = $body | ConvertTo-Json -Depth 5
    $url = "https://api.github.com/repos/$owner/$repo/contents/$remotePath"
    try {
        Invoke-RestMethod -Uri $url -Headers $headers -Method Put -Body $json -ContentType "application/json; charset=utf-8" | Out-Null
        Write-Host "OK   $remotePath" -ForegroundColor Green
        $ok++
    } catch {
        Write-Host "FAIL $remotePath -> $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "完成：$ok / $($files.Count) 个文件已上传到 https://github.com/$owner/$repo/tree/$branch/$remoteDir"
Write-Host "页面地址：https://$owner.github.io/$remoteDir/kj.html"
