<#
.SYNOPSIS
  Starts 3 backend instances (5101-5103) and the YARP proxy (5100) for the Phase 4 demo.
.PARAMETER Affinity
  On (default) or Off. Sets ReverseProxy:Clusters:authcluster:SessionAffinity:Enabled for the proxy.
.NOTES
  Requires the ASP.NET dev certificate to be trusted: dotnet dev-certs https --trust
  Only the processes started here are recorded (scripts\.demo-pids.json) and later stopped by stop-demo.ps1.
#>
param([ValidateSet('On', 'Off')][string]$Affinity = 'On')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $PSScriptRoot '.demo-pids.json'
$logDir = Join-Path $PSScriptRoot '.logs'

if (Test-Path $pidFile) { throw "Demo already recorded as running ($pidFile). Run stop-demo.ps1 first." }
foreach ($p in 5100..5103) {
    if (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) { throw "Port $p is already in use." }
}

dotnet build (Join-Path $root 'AuthPolicyEngine.sln') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Force $logDir | Out-Null

$api = Join-Path $root 'src\AuthPolicyEngine.Api\bin\Debug\net10.0\AuthPolicyEngine.Api.dll'
$proxy = Join-Path $root 'src\AuthPolicyEngine.Proxy\bin\Debug\net10.0\AuthPolicyEngine.Proxy.dll'
$started = @()

function Start-One($name, $dll, $url, $workDir) {
    $proc = Start-Process dotnet -ArgumentList "`"$dll`" --urls $url" -WorkingDirectory $workDir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir "$name.out.log") -RedirectStandardError (Join-Path $logDir "$name.err.log")
    $script:started += [pscustomobject]@{ name = $name; pid = $proc.Id; start = $proc.StartTime.ToString('o'); port = ([uri]$url).Port }
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
foreach ($i in 1..3) {
    $env:Instance__Id = "instance-$i"
    Start-One "instance-$i" $api "https://localhost:510$i" (Join-Path $root 'src\AuthPolicyEngine.Api')
}
Remove-Item Env:\Instance__Id

$env:ReverseProxy__Clusters__authcluster__SessionAffinity__Enabled = ($Affinity -eq 'On').ToString().ToLower()
Start-One 'proxy' $proxy 'https://localhost:5100' (Join-Path $root 'src\AuthPolicyEngine.Proxy')
Remove-Item Env:\ReverseProxy__Clusters__authcluster__SessionAffinity__Enabled

$started | ConvertTo-Json | Set-Content $pidFile

# Wait until the proxy answers through a healthy backend.
$deadline = (Get-Date).AddSeconds(40)
$ready = $false
do {
    Start-Sleep -Milliseconds 800
    try {
        $r = Invoke-WebRequest https://localhost:5100/api/auth/identify -Method Post -ContentType application/json `
            -Body '{"identifier":"account.a@example.test"}' -UseBasicParsing -SkipHttpErrorCheck
        if ($r.StatusCode -eq 200) { $ready = $true }
    } catch { }
} until ($ready -or (Get-Date) -gt $deadline)

if ($ready) { Write-Host "Demo is up (affinity $Affinity). Proxy: https://localhost:5100  Instances: 5101-5103" }
else { Write-Warning "Proxy did not answer in time. Check $logDir" }
