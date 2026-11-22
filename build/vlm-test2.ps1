$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'User-Agent' = 'ClassManager-Probe'; Origin = 'https://developer.amd.com.cn'; Referer = 'https://developer.amd.com.cn/' }
$base = 'https://developer.amd.com.cn/radeon/api/v1'

function Show($label, $url, $body) {
    try {
        if ($body) {
            $r = Invoke-WebRequest $url -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 90 -UseBasicParsing
        } else {
            $r = Invoke-WebRequest $url -Headers $headers -TimeoutSec 60 -UseBasicParsing
        }
        Write-Host ("{0} OK {1}" -f $label, $r.StatusCode)
        Write-Host $r.Content.Substring(0, [Math]::Min(600, $r.Content.Length))
    } catch {
        Write-Host ("{0} FAIL {1}" -f $label, $_.Exception.Message)
        try {
            $resp = $_.Exception.Response
            if ($resp) {
                $sr = New-Object IO.StreamReader($resp.GetResponseStream())
                $txt = $sr.ReadToEnd()
                Write-Host $txt.Substring(0, [Math]::Min(600, $txt.Length))
            }
        } catch { }
    }
}

Show 'models' "$base/models" $null
Show 'chat-text' "$base/chat/completions" '{"model":"d3-flash","messages":[{"role":"user","content":"Say OK"}],"max_tokens":20}'
Show 'messages' "$base/messages" '{"model":"d3-flash","max_tokens":20,"messages":[{"role":"user","content":"Say OK"}]}'
