$img = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build\seewo-login2.png'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($img))
$prompt = 'This is a screenshot of a Windows app login page. Answer in JSON: {"state":"login or home or other","account_box":{"x":0.5,"y":0.5},"password_box":{"x":0.5,"y":0.5},"agree_checkbox":{"x":0.5,"y":0.5},"login_button":{"x":0.5,"y":0.5}} Coordinates are relative 0-1 to image width/height. No extra text.'
$body = @{
    model = 'd3-flash'
    messages = @(@{
        role = 'user'
        content = @(
            @{ type = 'text'; text = $prompt },
            @{ type = 'image_url'; image_url = @{ url = "data:image/png;base64,$b64" } }
        )
    })
    max_tokens = 300
} | ConvertTo-Json -Depth 10 -Compress

$urls = @(
    'https://developer.amd.com.cn/radeon/api/v1/chat/completions',
    'https://developer.amd.com.cn/radeon/api/v1/v1/chat/completions',
    'https://developer.amd.com.cn/radeon/api/chat/completions'
)
$headers = @{ Authorization = "Bearer $key"; 'User-Agent' = 'ClassManager-Probe' }
foreach ($u in $urls) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-RestMethod $u -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 120
        $sw.Stop()
        Write-Host ("OK {0} {1}ms" -f $u, $sw.ElapsedMilliseconds)
        $r | ConvertTo-Json -Depth 8
        break
    } catch {
        $sw.Stop()
        Write-Host ("FAIL {0} {1}ms : {2}" -f $u, $sw.ElapsedMilliseconds, $_.Exception.Message)
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message.Substring(0, [Math]::Min(300, $_.ErrorDetails.Message.Length)) }
    }
}
