$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'User-Agent' = 'ClassManager-Probe'; Origin = 'https://developer.amd.com.cn' }
$base = 'https://developer.amd.com.cn/radeon/api/v1'
$bodies = @(
  '{"model":"d3-flash","questions":{"q1":{}}}',
  '{"model":"d3-flash","questions":{"q1":{"question":"Say OK"}}}',
  '{"model":"d3-flash","questions":{"q1":{"text":"Say OK"}}}',
  '{"model":"d3-flash","questions":{"q1":{"prompt":"Say OK"}}}',
  '{"model":"d3-flash","questions":{"q1":{"type":"yesno"}}}'
)
foreach ($b in $bodies) {
    try {
        $r = Invoke-WebRequest "$base/systemone" -Method Post -Headers $headers -Body $b -ContentType 'application/json' -TimeoutSec 90 -UseBasicParsing
        Write-Host "OK $b"
        Write-Host $r.Content.Substring(0, [Math]::Min(600, $r.Content.Length))
        break
    } catch {
        try {
            $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
            $txt = $sr.ReadToEnd()
            Write-Host ("$b => " + $txt.Substring(0, [Math]::Min(500, $txt.Length)))
        } catch { Write-Host ("$b => " + $_.Exception.Message) }
    }
}
