$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/systemone'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'Content-Type' = 'application/json' }

function Invoke-Probe($label, $bodyObj) {
    $body = $bodyObj | ConvertTo-Json -Depth 12 -Compress
    try {
        $r = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -Body $body -TimeoutSec 120
        Write-Host ("[$label] {0} => {1}" -f $r.StatusCode, $r.Content)
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object IO.StreamReader($resp.GetResponseStream())
            Write-Host ("[$label] {0} => {1}" -f [int]$resp.StatusCode, $sr.ReadToEnd())
        } else {
            Write-Host ("[$label] ERR => {0}" -f $_.Exception.Message)
        }
    }
}

Invoke-Probe 'choice-min' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'choice' } }
}

Invoke-Probe 'choice-options' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'choice'; options = @('A','B') } }
}

Invoke-Probe 'choice-question-options' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'choice'; question = 'Pick one'; options = @('A','B') } }
}

Invoke-Probe 'score-min' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'score' } }
}

Invoke-Probe 'noul-min' @{
    model = 'd3-flash'
    state = @{}
    questions = @{ q1 = @{ type = 'noul' } }
}
