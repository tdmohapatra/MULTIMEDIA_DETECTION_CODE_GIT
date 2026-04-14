param(
    [string]$ApiBase = "http://localhost:5078",
    [int]$MaxImages = 30,
    [double]$Nms = 0.45,
    [int]$InputSize = 640
)

$ErrorActionPreference = "Stop"

function New-SessionId([string]$prefix) {
    return "$prefix-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
}

function Get-ProxyScore {
    param(
        [int]$Frames,
        [int]$Hits,
        [double]$AvgConfidence,
        [double]$HighConfidenceRatio
    )

    if ($Frames -le 0) { return 0.0 }
    $coverage = [double]$Hits / [double]$Frames
    return (0.45 * $coverage) + (0.45 * $AvgConfidence) + (0.10 * $HighConfidenceRatio)
}

function Invoke-YoloSweepRun {
    param(
        [string]$SessionId,
        [string[]]$ImagePaths,
        [double]$Threshold,
        [double]$YoloNmsThreshold,
        [int]$YoloInputSize
    )

    $targetClasses = @("person", "car", "bicycle", "dog", "cat")
    $classStats = @{}
    foreach ($c in $targetClasses) {
        $classStats[$c] = [pscustomobject]@{
            detections = 0
            frameHits = 0
            confidenceSum = 0.0
            highConfidenceDetections = 0
            hitFrames = New-Object 'System.Collections.Generic.HashSet[int]'
        }
    }

    $totalMs = 0.0
    $frameCount = 0
    $allYoloDetections = 0

    Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/initialize/$SessionId" | Out-Null
    try {
        foreach ($img in $ImagePaths) {
            $frameCount++
            $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($img))
            $payload = @{
                sessionId = $SessionId
                frameNumber = $frameCount
                timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                imageData = "data:image/png;base64,$b64"
                processingMode = "scene"
                sceneOptions = @{
                    includeHuman = $true
                    includeAnimal = $true
                    includeObject = $true
                    processAllModels = $false
                    enableSsdModel = $true
                    enableFaceCascade = $false
                    enableFullBodyCascade = $false
                    enableCatCascade = $false
                    sceneModelBackend = "yolo"
                    ssdConfidenceThreshold = $Threshold
                    yoloNmsThreshold = $YoloNmsThreshold
                    yoloInputSize = $YoloInputSize
                }
            }

            $resp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/process-frame" -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 12)
            if ($resp.stageTimings -and $null -ne $resp.stageTimings.totalMs) {
                $totalMs += [double]$resp.stageTimings.totalMs
            }

            $entities = @()
            if ($resp.sceneAnalysis -and $resp.sceneAnalysis.entities) {
                $entities = @($resp.sceneAnalysis.entities)
            }

            $yoloEntities = @($entities | Where-Object { "$($_.source)".ToLowerInvariant() -eq "yolo" })
            $allYoloDetections += $yoloEntities.Count

            foreach ($ent in $yoloEntities) {
                $label = "$($ent.label)".ToLowerInvariant()
                if (-not $classStats.ContainsKey($label)) { continue }
                $bucket = $classStats[$label]
                $bucket.detections++
                $conf = if ($null -ne $ent.confidence) { [double]$ent.confidence } else { 0.0 }
                $bucket.confidenceSum += $conf
                if ($conf -ge [math]::Min(0.95, $Threshold + 0.15)) {
                    $bucket.highConfidenceDetections++
                }
                [void]$bucket.hitFrames.Add($frameCount)
            }
        }
    }
    finally {
        try {
            Invoke-RestMethod -Method Post -Uri "$ApiBase/api/detection/cleanup/$SessionId" | Out-Null
        } catch {
            # best effort cleanup
        }
    }

    $classReport = @{}
    $proxyValues = New-Object System.Collections.Generic.List[double]
    foreach ($c in $targetClasses) {
        $bucket = $classStats[$c]
        $frameHits = $bucket.hitFrames.Count
        $avgConf = if ($bucket.detections -gt 0) { $bucket.confidenceSum / $bucket.detections } else { 0.0 }
        $highRatio = if ($bucket.detections -gt 0) { [double]$bucket.highConfidenceDetections / [double]$bucket.detections } else { 0.0 }
        $proxy = Get-ProxyScore -Frames $frameCount -Hits $frameHits -AvgConfidence $avgConf -HighConfidenceRatio $highRatio
        $proxyValues.Add($proxy) | Out-Null
        $classReport[$c] = [pscustomobject]@{
            detections = $bucket.detections
            frameHits = $frameHits
            avgConfidence = [math]::Round($avgConf, 4)
            highConfidenceRatio = [math]::Round($highRatio, 4)
            precisionProxy = [math]::Round($proxy, 4)
        }
    }

    $avgProxy = if ($proxyValues.Count -gt 0) { ($proxyValues | Measure-Object -Average).Average } else { 0.0 }
    $avgMs = if ($frameCount -gt 0) { $totalMs / $frameCount } else { 0.0 }
    $fps = if ($avgMs -gt 0) { 1000.0 / $avgMs } else { 0.0 }

    return [pscustomobject]@{
        threshold = [math]::Round($Threshold, 2)
        frames = $frameCount
        avgMs = [math]::Round($avgMs, 2)
        avgFps = [math]::Round($fps, 2)
        yoloDetections = $allYoloDetections
        classPrecisionProxy = $classReport
        avgPrecisionProxy = [math]::Round($avgProxy, 4)
        compositeScore = [math]::Round((0.85 * $avgProxy) + (0.15 * [math]::Min(1.0, $fps / 20.0)), 4)
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

$thresholds = @(0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65)
$runs = New-Object System.Collections.Generic.List[object]

foreach ($t in $thresholds) {
    $sid = New-SessionId "yolo-sweep"
    Write-Host "Running YOLO sweep threshold=$t session=$sid"
    $res = Invoke-YoloSweepRun -SessionId $sid -ImagePaths $imagePaths -Threshold $t -YoloNmsThreshold $Nms -YoloInputSize $InputSize
    $runs.Add($res) | Out-Null
}

$best = $runs | Sort-Object -Property compositeScore -Descending | Select-Object -First 1
if ($null -eq $best) { throw "Sweep did not produce any results." }

$bestProfile = [pscustomobject]@{
    profileName = "yolo_best_default"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    basedOn = [pscustomobject]@{
        sweepThresholds = $thresholds
        imagesUsed = $imagePaths.Count
        targetClasses = @("person", "car", "bicycle", "dog", "cat")
    }
    sceneOptions = [pscustomobject]@{
        sceneModelBackend = "yolo"
        ssdConfidenceThreshold = $best.threshold
        yoloNmsThreshold = $Nms
        yoloInputSize = $InputSize
        processAllModels = $false
        enableSsdModel = $true
        enableFaceCascade = $false
        enableFullBodyCascade = $false
        enableCatCascade = $false
        includeHuman = $true
        includeAnimal = $true
        includeObject = $true
    }
    evaluation = $best
}

$outDir = "TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/frame-logs"
New-Item -Path $outDir -ItemType Directory -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

$report = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    apiBase = $ApiBase
    imagesUsed = $imagePaths.Count
    yoloInputSize = $InputSize
    yoloNmsThreshold = $Nms
    thresholds = $thresholds
    runs = $runs
    best = $best
}

$reportPath = Join-Path $outDir "yolo-threshold-sweep-$stamp.json"
$bestPath = Join-Path $outDir "yolo-best-profile-$stamp.json"
$report | ConvertTo-Json -Depth 14 | Set-Content -Path $reportPath -Encoding UTF8
$bestProfile | ConvertTo-Json -Depth 14 | Set-Content -Path $bestPath -Encoding UTF8

# Also publish a stable path that UI/scripts can read.
$stableBestPath = "TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/models/yolo-best-profile.json"
$bestProfile | ConvertTo-Json -Depth 14 | Set-Content -Path $stableBestPath -Encoding UTF8

Write-Host "Saved sweep report: $reportPath"
Write-Host "Saved best profile: $bestPath"
Write-Host "Updated stable best profile: $stableBestPath"

$bestProfile | ConvertTo-Json -Depth 10
