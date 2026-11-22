$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/systemone'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'Content-Type' = 'application/json' }

function Invoke-Probe($label, $bodyObj) {
    $body = $bodyObj | ConvertTo-Json -Depth 12 -Compress
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -Body $body -TimeoutSec 120
        $sw.Stop()
        Write-Host ("[$label] {0} {1}ms => {2}" -f $r.StatusCode, $sw.ElapsedMilliseconds, $r.Content)
    } catch {
        $sw.Stop()
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object IO.StreamReader($resp.GetResponseStream())
            Write-Host ("[$label] {0} {1}ms => {2}" -f [int]$resp.StatusCode, $sw.ElapsedMilliseconds, $sr.ReadToEnd())
        } else {
            Write-Host ("[$label] ERR {0}ms => {1}" -f $sw.ElapsedMilliseconds, $_.Exception.Message)
        }
    }
}

$crit = [ordered]@{ 'yes' = 'the screen shows a login form'; 'no' = 'the screen does not show a login form' }

Invoke-Probe 'choice-criteria' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'choice'; criteria = $crit } }
}

Invoke-Probe 'score-levels' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'score'; criteria = @('low','medium','high') } }
}

Invoke-Probe 'choice-state-text' @{
    model = 'd3-flash'
    state = @{ text = 'This is a blank white page with a green button in the middle.' }
    questions = @{ q1 = @{ type = 'choice'; criteria = $crit } }
}
