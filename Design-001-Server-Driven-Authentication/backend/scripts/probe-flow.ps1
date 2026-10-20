<#
.SYNOPSIS
  Experiments A/B helper: for each round, uses a fresh client (no cookies), identifies Account A
  through the proxy, then selects an alternative on that flow, and prints which instance handled each call.
#>
param([int]$Rounds = 6, [string]$BaseUrl = 'https://localhost:5100')

foreach ($i in 1..$Rounds) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $a = Invoke-WebRequest "$BaseUrl/api/auth/identify" -Method Post -ContentType application/json -WebSession $session `
        -Body '{"identifier":"account.a@example.test"}' -UseBasicParsing -SkipHttpErrorCheck
    $flow = ($a.Content | ConvertFrom-Json).flowId
    $b = Invoke-WebRequest "$BaseUrl/api/auth/flows/$flow/select" -Method Post -ContentType application/json -WebSession $session `
        -Body '{"authenticationType":"EmailOtp"}' -UseBasicParsing -SkipHttpErrorCheck
    $ia = ($a.Headers['X-Instance-Id'] | Select-Object -First 1)
    $ib = ($b.Headers['X-Instance-Id'] | Select-Object -First 1)
    $same = $ia -eq $ib
    '{0}: identify={1} [{2}] -> select={3} [{4}]  same instance: {5}' -f $i, $a.StatusCode, $ia, $b.StatusCode, $ib, $same
}
