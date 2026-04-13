param(
    [string]$ApiUrl = "http://localhost:5078",
    [string]$UiUrl = "http://localhost:5080",
    [string]$OpenBrowser = "true"
)

$ErrorActionPreference = "Stop"

function Get-PortFromUrl {
    param([string]$Url)
    return ([System.Uri]$Url).Port
}

function Stop-ProcessOnPort {
    param([int]$Port)

    $lines = netstat -ano | Select-String ":$Port\s+.*LISTENING"
    foreach ($line in $lines) {
        $parts = ($line -split "\s+") | Where-Object { $_ -ne "" }
        if ($parts.Length -ge 5) {
            $processId = [int]$parts[-1]
            if ($processId -gt 0) {
                try {
                    Stop-Process -Id $processId -Force -ErrorAction Stop
                    Write-Host "Stopped existing listener on port $Port (PID $processId)"
                }
                catch {
                    Write-Warning "Could not stop PID $processId on port ${Port}: $($_.Exception.Message)"
                }
            }
        }
    }
}

function Wait-HttpReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }
    return $false
}

function Convert-ToBool {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $true }

    switch ($Value.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "y" { return $true }
        "on" { return $true }
        "0" { return $false }
        "false" { return $false }
        "no" { return $false }
        "n" { return $false }
        "off" { return $false }
        default { return $true }
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "ImgToText"
$uiProject = Join-Path $root "ImgToText_UI"

if (-not (Test-Path $apiProject) -or -not (Test-Path $uiProject)) {
    throw "Expected both ImgToText (API) and ImgToText_UI (UI) folders under: $root"
}

$apiPort = Get-PortFromUrl -Url $ApiUrl
$uiPort = Get-PortFromUrl -Url $UiUrl

Stop-ProcessOnPort -Port $apiPort
Stop-ProcessOnPort -Port $uiPort

Write-Host "Starting API at $ApiUrl"
$apiProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--urls", $ApiUrl) -WorkingDirectory $apiProject -PassThru
Write-Host "API process started (PID $($apiProc.Id))"

Write-Host "Waiting for API readiness..."
$apiReady = Wait-HttpReady -Url "$ApiUrl/api/detection/health"
if (-not $apiReady) {
    throw "API did not become ready in time at $ApiUrl"
}

Write-Host "Starting UI at $UiUrl"
$uiProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--urls", $UiUrl) -WorkingDirectory $uiProject -PassThru
Write-Host "UI process started (PID $($uiProc.Id))"

Write-Host "Waiting for UI readiness..."
$uiReady = Wait-HttpReady -Url "$UiUrl/"
if (-not $uiReady) {
    throw "UI did not become ready in time at $UiUrl"
}

Write-Host "Launched both services."
Write-Host "API: $ApiUrl"
Write-Host "UI : $UiUrl/ (main menu) · Live workspace: $UiUrl/DetectionView"

$openBrowserFlag = Convert-ToBool -Value $OpenBrowser
if ($openBrowserFlag) {
    Start-Process "$UiUrl/"
}
