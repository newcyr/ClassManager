$ErrorActionPreference = 'Continue'
$uri = 'https://developer.amd.com.cn/radeon/api/v1/systemone'
$key = 'rc-eb048c64bc6fd91baaea6707f83d62d7f98c9f57ec13dc88'
$headers = @{ Authorization = "Bearer $key"; 'Content-Type' = 'application/json' }

$variants = @(
    @{ name = 'state-empty'; state = @{} },
    @{ name = 'state-screen-string'; state = @{ screen = '' } },
    @{ name = 'state-screens-array'; state = @{ screens = @() } },
    @{ name = 'state-image'; state = @{ image = '' } },
    @{ name = 'state-page'; state = @{ page = @{} } },
    @{ name = 'state-url'; state = @{ url = 'about:blank' } }
)

foreach ($v in $variants) {
    $body = @{
        model = 'd3-flash'
        state = $v.state
        questions = @{ q1 = @{ type = 'yesno' } }
    } | ConvertTo-Json -Depth 10 -Compress
    $label = $v.name
    try {
        $r = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -Body $body -TimeoutSec 90
        Write-Host ("[$label] {0} => {1}" -f $r.StatusCode, $r.Content)
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object IO.StreamReader($resp.GetResponseStream())
            $txt = $sr.ReadToEnd()
            Write-Host ("[$label] {0} => {1}" -f [int]$resp.StatusCode, $txt)
        } else {
            Write-Host ("[$label] ERR => {0}" -f $_.Exception.Message)
        }
    }
}
