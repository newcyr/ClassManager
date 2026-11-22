$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
$tmp = Join-Path $env:TEMP 'red64.png'
$bmp = New-Object System.Drawing.Bitmap 64, 64
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Red)
$bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($tmp))
Write-Host ("red64 bytes={0}" -f (Get-Item $tmp).Length)

$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/systemone'

function Probe($label, $stateJson) {
    $body = '{"model":"d3-flash","state":' + $stateJson + ',"questions":{"q1":{"type":"choice","criteria":{"red":"the image is mostly red","blue":"the image is mostly blue"}}}}'
    $f = Join-Path $env:TEMP 'd3img.json'
    [IO.File]::WriteAllText($f, $body, [Text.Encoding]::ASCII)
    $out = & curl.exe -s -X POST $uri -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$f" 2>&1
    Write-Host ("[{0}] {1}" -f $label, ($out -join ''))
}

Probe 'openai-part' ('{"image":{"type":"image_url","image_url":{"url":"data:image/png;base64,' + $b64 + '"}}}')
Probe 'content-array' ('{"image":[{"type":"image_url","image_url":{"url":"data:image/png;base64,' + $b64 + '"}}]}')
Probe 'image-array' ('{"images":[{"type":"image_url","image_url":{"url":"data:image/png;base64,' + $b64 + '"}}]}')
