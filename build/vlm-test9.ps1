$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/systemone'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$png = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build\seewo-login2.png'
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($png))
$dataUrl = 'data:image/png;base64,' + $b64
Write-Host ("png bytes={0} b64len={1}" -f (Get-Item $png).Length, $b64.Length)

$crit = [ordered]@{
    'login' = 'the screen shows an account/password login form with a green login button'
    'home'  = 'the screen shows the main app home page after login'
}

function Invoke-Probe($label, $stateObj) {
    $bodyObj = @{ model = 'd3-flash'; state = $stateObj; questions = @{ q1 = @{ type = 'choice'; criteria = $crit } } }
    $body = $bodyObj | ConvertTo-Json -Depth 12 -Compress
    $f = Join-Path $env:TEMP ("one-{0}.json" -f $label)
    [IO.File]::WriteAllText($f, $body, [Text.Encoding]::ASCII)
    $out = & curl.exe -s -X POST $uri -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$f" 2>&1
    Write-Host ("[{0}] {1}" -f $label, $out)
}

Invoke-Probe 'image' @{ image = $dataUrl }
Invoke-Probe 'screenshot' @{ screenshot = $dataUrl }
Invoke-Probe 'images-array' @{ images = @($dataUrl) }
Invoke-Probe 'observation' @{ observation = $dataUrl }
