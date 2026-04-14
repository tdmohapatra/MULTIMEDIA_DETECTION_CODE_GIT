$ErrorActionPreference = "Stop"

param(
    [string]$ApiBase = "http://localhost:5078",
    [string]$SessionId = "bench-session",
    [int]$Frames = 120,
    [string]$InputImage = "TDM_MULTIMEDIA DOTNET CORE/ImgToText/wwwroot/uploads/default.png"
)

if (!(Test-Path $InputImage)) {
    throw "Input image not found: $InputImage"
}

$bytes = [IO.File]::ReadAllBytes($InputImage)
$b64 = [Convert]::ToBase64String($bytes)
$dataUrl = "data:image/png;base64,$b64"

Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/initialize/$SessionId" | Out-Null

$results = @()
for ($i = 1; $i -le $Frames; $i++) {
    $payload = @{
        sessionId = $SessionId
        frameNumber = $i
        timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        processingMode = "scene"
        imageData = $dataUrl
        sceneOptions = @{
            includeHuman = $true
            includeAnimal = $true
            includeObject = $true
            enableSsdModel = $true
            sceneModelBackend = "ssd"
        }
    } | ConvertTo-Json -Depth 8

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $resp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/process-frame" -ContentType "application/json" -Body $payload
    $sw.Stop()
    $results += [pscustomobject]@{
        frame = $i
        clientMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        serverMs = [Math]::Round(($resp.stageTimings.totalMs ?? 0), 2)
        pipeline = ($resp.sceneAnalysis.pipeline ?? "n/a")
    }
}

$avgClient = ($results | Measure-Object clientMs -Average).Average
$avgServer = ($results | Measure-Object serverMs -Average).Average
$fps = if ($avgServer -gt 0) { 1000.0 / $avgServer } else { 0 }

Write-Host "Benchmark Summary"
Write-Host "Frames: $Frames"
Write-Host ("Avg client ms: {0:N2}" -f $avgClient)
Write-Host ("Avg server ms: {0:N2}" -f $avgServer)
Write-Host ("Estimated FPS: {0:N2}" -f $fps)
Write-Host "Target SLO: sustained 20-24 FPS balanced mode"

Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/cleanup/$SessionId" | Out-Null
