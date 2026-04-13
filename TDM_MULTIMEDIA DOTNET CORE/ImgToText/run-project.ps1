param(
    [string]$Url = "http://localhost:5078",
    [string]$Configuration = "Debug",
    [string]$Environment = "Production",
    [string]$LogDirectory = "logs",
    [switch]$OpenBrowser = $true,
    [int]$BrowserDelaySeconds = 3,
    [switch]$BuildFirst,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Write-RunnerLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message
    )
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $line = "[$stamp] $Message"
    Write-Host $line
    Add-Content -Path $script:RunnerLogFile -Value $line
}

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$resolvedLogDirectory = if ([System.IO.Path]::IsPathRooted($LogDirectory)) {
    $LogDirectory
} else {
    Join-Path $projectPath $LogDirectory
}

if (-not (Test-Path -Path $resolvedLogDirectory)) {
    New-Item -Path $resolvedLogDirectory -ItemType Directory | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$script:RunnerLogFile = Join-Path $resolvedLogDirectory "runner_$timestamp.log"
$stdoutLogFile = Join-Path $resolvedLogDirectory "app_stdout_$timestamp.log"
$stderrLogFile = Join-Path $resolvedLogDirectory "app_stderr_$timestamp.log"

Write-RunnerLog "Project path: $projectPath"
Write-RunnerLog "Environment: $Environment"
Write-RunnerLog "Configuration: $Configuration"
Write-RunnerLog "URL: $Url"
Write-RunnerLog "StdOut log: $stdoutLogFile"
Write-RunnerLog "StdErr log: $stderrLogFile"

if ($DryRun) {
    Write-RunnerLog "DryRun enabled. Exiting before build/run."
    exit 0
}

if ($BuildFirst) {
    Write-RunnerLog "Running dotnet build..."
    Push-Location $projectPath
    try {
        & dotnet build --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
    Write-RunnerLog "Build completed successfully."
}

$env:ASPNETCORE_ENVIRONMENT = $Environment

$argumentList = @(
    "run"
    "--configuration", $Configuration
    "--urls", $Url
)

Write-RunnerLog "Starting application process..."
$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList $argumentList `
    -WorkingDirectory $projectPath `
    -RedirectStandardOutput $stdoutLogFile `
    -RedirectStandardError $stderrLogFile `
    -PassThru

Write-RunnerLog "Application started with PID $($process.Id). Press Ctrl+C to stop script."

if ($OpenBrowser) {
    Write-RunnerLog "Waiting $BrowserDelaySeconds second(s) before opening browser..."
    Start-Sleep -Seconds $BrowserDelaySeconds
    try {
        Start-Process $Url | Out-Null
        Write-RunnerLog "Opened browser at $Url"
    }
    catch {
        Write-RunnerLog "Failed to open browser automatically: $($_.Exception.Message)"
    }
}

try {
    while (-not $process.HasExited) {
        Start-Sleep -Seconds 2
    }
}
finally {
    if (-not $process.HasExited) {
        Write-RunnerLog "Stopping application process $($process.Id)..."
        Stop-Process -Id $process.Id -Force
    }
}

Write-RunnerLog "Application exited with code $($process.ExitCode)."
exit $process.ExitCode
