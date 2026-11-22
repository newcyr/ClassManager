$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/chat/completions'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$png = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build\seewo-login2.png'
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($png))
$prompt = 'This is a screenshot of Seewo whiteboard app (Chinese UI). Answer: 1) which screen is it (login page / home page / other); 2) pixel coordinates (x,y) of the account input, password input, and green login button; 3) whether the privacy checkbox at the bottom is checked. Reply in compact JSON.'

$models = @('MiniCPM-V-4.7-1B', 'MiniCPM-V-4.7-35B-A3B', 'Qwen3.8-27B')

foreach ($m in $models) {
    $body = @{
        model = $m
        max_tokens = 400
        messages = @(@{
            role = 'user'
            content = @(
                @{ type = 'text'; text = $prompt },
                @{ type = 'image_url'; image_url = @{ url = 'data:image/png;base64,' + $b64 } }
            )
        })
    } | ConvertTo-Json -Depth 12 -Compress
    $f = Join-Path $env:TEMP 'chat.json'
    [IO.File]::WriteAllText($f, $body, [Text.Encoding]::ASCII)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = & curl.exe -s -X POST $uri -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$f" 2>&1
    $sw.Stop()
    Write-Host ("=== {0} ({1}ms) ===" -f $m, $sw.ElapsedMilliseconds)
    Write-Host ($out -join "`n")
}
