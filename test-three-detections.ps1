param(
    [string]$ApiBase = "http://localhost:5078",
    [int]$MaxImages = 24
)

$ErrorActionPreference = "Stop"

function New-SessionId([string]$prefix) {
    return "$prefix-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
}

function Invoke-DetectionRun {
    param(
        [string]$ModeName,
        [string]$SessionId,
        [string[]]$ImagePaths,
        [hashtable]$PayloadTemplate
    )

    Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/initialize/$SessionId" | Out-Null
    $stats = [System.Collections.Generic.List[object]]::new()
    $sourceCounts = @{}
    $categoryCounts = @{}

    try {
        $frameNo = 0
        foreach ($img in $ImagePaths) {
            $frameNo++
            $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($img))
            $payload = @{
                sessionId = $SessionId
                frameNumber = $frameNo
                timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                imageData = "data:image/png;base64,$b64"
            }
            foreach ($k in $PayloadTemplate.Keys) {
                $payload[$k] = $PayloadTemplate[$k]
            }

            $json = $payload | ConvertTo-Json -Depth 12
            $resp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/process-frame" -ContentType "application/json" -Body $json
            $scene = $resp.sceneAnalysis
            $totalMs = if ($resp.stageTimings -and $null -ne $resp.stageTimings.totalMs) { [double]$resp.stageTimings.totalMs } else { 0.0 }
            $facesDetected = if ($resp.stats -and $null -ne $resp.stats.facesDetected) { [int]$resp.stats.facesDetected } else { 0 }
            $handsDetected = if ($resp.stats -and $null -ne $resp.stats.handsDetected) { [int]$resp.stats.handsDetected } else { 0 }
            $entitiesCount = if ($scene -and $scene.entities) { [int](($scene.entities | Measure-Object).Count) } else { 0 }
            $pipelineName = if ($scene -and $scene.pipeline) { [string]$scene.pipeline } else { "standard" }
            $stats.Add([pscustomobject]@{
                frame = $frameNo
                totalMs = $totalMs
                faces = $facesDetected
                hands = $handsDetected
                entities = $entitiesCount
                pipeline = $pipelineName
            }) | Out-Null

            $sceneEntities = if ($scene -and $scene.entities) { $scene.entities } else { @() }
            foreach ($ent in $sceneEntities) {
                $src = if ($null -ne $ent.source -and "$($ent.source)".Trim().Length -gt 0) { [string]$ent.source } else { "unknown" }
                $cat = if ($null -ne $ent.category -and "$($ent.category)".Trim().Length -gt 0) { [string]$ent.category } else { "unknown" }
                if (-not $sourceCounts.ContainsKey($src)) { $sourceCounts[$src] = 0 }
                if (-not $categoryCounts.ContainsKey($cat)) { $categoryCounts[$cat] = 0 }
                $sourceCounts[$src]++
                $categoryCounts[$cat]++
            }
        }
    }
    finally {
        Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/cleanup/$SessionId" | Out-Null
    }

    $avgMs = ($stats | Measure-Object totalMs -Average).Average
    $avgFps = if ($avgMs -gt 0) { 1000.0 / $avgMs } else { 0 }
    $avgFaces = ($stats | Measure-Object faces -Average).Average
    $avgHands = ($stats | Measure-Object hands -Average).Average
    $avgEntities = ($stats | Measure-Object entities -Average).Average

    return [pscustomobject]@{
        mode = $ModeName
        frames = $stats.Count
        avgMs = [math]::Round($avgMs, 2)
        avgFps = [math]::Round($avgFps, 2)
        avgFaces = [math]::Round($avgFaces, 2)
        avgHands = [math]::Round($avgHands, 2)
        avgEntities = [math]::Round($avgEntities, 2)
        bySource = $sourceCounts
        byCategory = $categoryCounts
    }
}

$health = Invoke-WebRequest -Uri "$ApiBase/api/detection/health" -UseBasicParsing -TimeoutSec 8
if ($health.StatusCode -ne 200) {
    throw "API is not healthy on $ApiBase"
}

$imgDir = "TDM_MULTIMEDIA DOTNET CORE/ImgToText/wwwroot/uploads"
$imagePaths = Get-ChildItem -Path $imgDir -File |
    Where-Object { $_.Extension -match '^\.(png|jpg|jpeg)$' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First $MaxImages |
    ForEach-Object { $_.FullName }

if (-not $imagePaths -or $imagePaths.Count -eq 0) {
    throw "No test images found in $imgDir"
}

$runs = @()

$runs += Invoke-DetectionRun -ModeName "detectionview-standard" -SessionId (New-SessionId "std") -ImagePaths $imagePaths -PayloadTemplate @{
    processingMode = "standard"
}

$runs += Invoke-DetectionRun -ModeName "scene-ssd" -SessionId (New-SessionId "ssd") -ImagePaths $imagePaths -PayloadTemplate @{
    processingMode = "scene"
    sceneOptions = @{
        includeHuman = $true
        includeAnimal = $true
        includeObject = $true
        processAllModels = $true
        enableSsdModel = $true
        enableFaceCascade = $true
        enableFullBodyCascade = $true
        enableCatCascade = $true
        sceneModelBackend = "ssd"
        ssdConfidenceThreshold = 0.35
    }
}

$runs += Invoke-DetectionRun -ModeName "scene-yolo" -SessionId (New-SessionId "yolo") -ImagePaths $imagePaths -PayloadTemplate @{
    processingMode = "scene"
    sceneOptions = @{
        includeHuman = $true
        includeAnimal = $true
        includeObject = $true
        processAllModels = $true
        enableSsdModel = $true
        enableFaceCascade = $true
        enableFullBodyCascade = $true
        enableCatCascade = $true
        sceneModelBackend = "yolo"
        ssdConfidenceThreshold = 0.30
    }
}

$report = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    apiBase = $ApiBase
    imagesUsed = $imagePaths.Count
    modes = $runs
}

$outDir = "TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/frame-logs"
New-Item -Path $outDir -ItemType Directory -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outPath = Join-Path $outDir "three-detection-report-$stamp.json"
$report | ConvertTo-Json -Depth 12 | Set-Content -Path $outPath -Encoding UTF8

Write-Host "Saved report to: $outPath"
$report | ConvertTo-Json -Depth 8
