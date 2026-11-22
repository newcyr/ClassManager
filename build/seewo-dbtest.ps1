Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-State {
    $ps = Get-Process EasiNote -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
    if (-not $ps) { return 'no-window' }
    foreach ($p in $ps) {
        $w = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
        if (-not $w) { continue }
        $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'AccountProfileButton')
        $b = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
        $ec = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
        $edits = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ec)
        if ($b) { return 'logged-in' }
        if ($edits.Count -ge 2) { return 'login-page' }
    }
    return 'unknown'
}

$db = Join-Path $env:APPDATA 'Seewo\Users\User.db'
$bak = Join-Path $env:TEMP 'User.db.bak'
$exe = 'C:\Program Files (x86)\Seewo\EasiNote5\EasiNote5_5.2.4.11610\Main\EasiNote.exe'

Write-Host ("before=" + (Get-State))
Get-Process EasiNote* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
Copy-Item $db $bak -Force
Write-Host ("backup=" + (Get-Item $bak).Length)

Start-Process $exe
Start-Sleep 20
Write-Host ("with-db=" + (Get-State))

Get-Process EasiNote* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
Remove-Item $db -Force -ErrorAction SilentlyContinue
Start-Process $exe
Start-Sleep 20
Write-Host ("no-db=" + (Get-State))
