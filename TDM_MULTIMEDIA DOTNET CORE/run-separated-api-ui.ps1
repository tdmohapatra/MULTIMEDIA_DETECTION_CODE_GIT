param(
    [string]$ApiUrl = "http://localhost:5078",
    [string]$UiUrl = "http://localhost:5080"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "ImgToText"
$uiProject = Join-Path $root "ImgToText_UI"

if (-not (Test-Path $apiProject) -or -not (Test-Path $uiProject)) {
    throw "Expected both ImgToText (API) and ImgToText_UI (UI) folders under: $root"
}

Write-Host "Starting API at $ApiUrl"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd `"$apiProject`"; dotnet run --urls $ApiUrl"

Start-Sleep -Seconds 2

Write-Host "Starting UI at $UiUrl"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd `"$uiProject`"; dotnet run --urls $UiUrl"

Write-Host "Launched both services."
Write-Host "API: $ApiUrl"
Write-Host "UI : $UiUrl"
