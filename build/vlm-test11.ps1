$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/chat/completions'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$build = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build'

Add-Type -AssemblyName System.Drawing
foreach ($n in @('seewo-login2.png','seewo-menu.png','seewo-home.png','seewo-filled.png')) {
    $p = Join-Path $build $n
    if (Test-Path $p) {
        $img = [System.Drawing.Image]::FromFile($p)
        Write-Host ("{0}: {1}x{2}" -f $n, $img.Width, $img.Height)
        $img.Dispose()
    }
}

function Ask($imgName, $prompt, $tag) {
    $png = Join-Path $build $imgName
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($png))
    $body = @{
        model = 'MiniCPM-V-4.7-35B-A3B'
        max_tokens = 300
        messages = @(@{
            role = 'user'
            content = @(
                @{ type = 'text'; text = $prompt },
                @{ type = 'image_url'; image_url = @{ url = 'data:image/png;base64,' + $b64 } }
            )
        })
    } | ConvertTo-Json -Depth 12 -Compress
    $f = Join-Path $env:TEMP 'chat2.json'
    [IO.File]::WriteAllText($f, $body, [Text.Encoding]::ASCII)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = & curl.exe -s -X POST $uri -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$f" 2>&1
    $sw.Stop()
    try {
        $j = ($out -join '') | ConvertFrom-Json
        $c = $j.choices[0].message.content
        $cost = $j.usage.cost
        Write-Host ("[{0}] {1}ms cost={2}" -f $tag, $sw.ElapsedMilliseconds, $cost)
        Write-Host $c
    } catch {
        Write-Host ("[{0}] {1}ms RAW={2}" -f $tag, $sw.ElapsedMilliseconds, ($out -join ''))
    }
    Write-Host ''
}

$loginPrompt = 'Screenshot of Seewo whiteboard (Chinese). Reply ONLY compact JSON: {"screen":"login|home|other","account":[x,y],"password":[x,y],"login_button":[x,y],"privacy_checked":true|false}. Coordinates normalized to 0-1000 scale (x/width*1000, y/height*1000).'
$menuPrompt = 'Screenshot of Seewo whiteboard (Chinese). Reply ONLY compact JSON: {"screen":"login|home|other","logout_item":[x,y],"account_avatar":[x,y]}. Coordinates normalized to 0-1000 scale. Use null if not visible.'

Ask 'seewo-login2.png' $loginPrompt 'login-run1'
Ask 'seewo-login2.png' $loginPrompt 'login-run2'
Ask 'seewo-login2.png' $loginPrompt 'login-run3'
Ask 'seewo-filled.png' $loginPrompt 'filled'
Ask 'seewo-menu.png' $menuPrompt 'menu'
Ask 'seewo-home.png' $menuPrompt 'home-with-dialog'
