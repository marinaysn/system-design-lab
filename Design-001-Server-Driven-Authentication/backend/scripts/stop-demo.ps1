<#
.SYNOPSIS
  Stops the demo processes recorded by start-demo.ps1. Nothing else is touched.
.PARAMETER Instance
  Optional: stop only one backend instance (1, 2 or 3), to simulate failure.
#>
param([ValidateRange(1, 3)][int]$Instance)

$pidFile = Join-Path $PSScriptRoot '.demo-pids.json'
if (-not (Test-Path $pidFile)) { Write-Host 'No recorded demo processes.'; return }

$entries = @(Get-Content $pidFile -Raw | ConvertFrom-Json)
$targets = if ($Instance) { $entries | Where-Object name -eq "instance-$Instance" } else { $entries }

foreach ($e in $targets) {
    $p = Get-Process -Id $e.pid -ErrorAction SilentlyContinue
    # Guard against PID reuse: only stop it if it is the same process we started.
    $sameProcess = $p -and $p.ProcessName -eq 'dotnet' -and
        [math]::Abs(($p.StartTime.ToUniversalTime() - ([datetime]$e.start).ToUniversalTime()).TotalSeconds) -lt 2
    if ($sameProcess) {
        Stop-Process -Id $e.pid
        Write-Host "Stopped $($e.name) (PID $($e.pid))"
    } else {
        Write-Host "$($e.name) is not running (or PID was reused); skipped."
    }
}

if ($Instance) {
    @($entries | Where-Object name -ne "instance-$Instance") | ConvertTo-Json | Set-Content $pidFile
} else {
    Remove-Item $pidFile
}
