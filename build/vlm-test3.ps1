$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'User-Agent' = 'ClassManager-Probe'; Origin = 'https://developer.amd.com.cn' }
$base = 'https://developer.amd.com.cn/radeon/api/v1'

function Post($label, $url, $body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-WebRequest $url -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 120 -UseBasicParsing
        $sw.Stop()
        Write-Host ("{0} OK {1} {2}ms" -f $label, $r.StatusCode, $sw.ElapsedMilliseconds)
        Write-Host $r.Content.Substring(0, [Math]::Min(800, $r.Content.Length))
    } catch {
        $sw.Stop()
        Write-Host ("{0} FAIL {1} {2}ms" -f $label, $_.Exception.Message, $sw.ElapsedMilliseconds)
        try {
            $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
            $txt = $sr.ReadToEnd()
            Write-Host $txt.Substring(0, [Math]::Min(800, $txt.Length))
        } catch { }
    }
}

Post 'systemone-basic' "$base/systemone" '{"model":"d3-flash","messages":[{"role":"user","content":"Say OK"}]}'
Post 'systemone-input' "$base/systemone" '{"model":"d3-flash","input":[{"role":"user","content":"Say OK"}]}'
