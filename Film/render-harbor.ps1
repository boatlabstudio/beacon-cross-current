param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [Parameter(Mandatory = $true)][string]$MusicDirectory,
    [string]$Python = 'python',
    [string]$Project = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Preview/Motion-Trials-02')
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$unityEditor = (Resolve-Path -LiteralPath $Unity).Path
$musicPath = (Resolve-Path -LiteralPath $MusicDirectory).Path
$outputPath = (New-Item -ItemType Directory -Force -Path $Output).FullName
$ffmpeg = (Get-Command ffmpeg -CommandType Application).Source
$env:BEACON_FILM_DIR = $outputPath
$env:BEACON_FFMPEG = $ffmpeg
$env:BEACON_STILLS = '0'

& $Python (Join-Path $PSScriptRoot 'harbor_trial.py') $outputPath
if ($LASTEXITCODE -ne 0) { throw "Harbor edit preparation exited with $LASTEXITCODE." }

$log = Join-Path $outputPath 'unity-harbor-render.log'
$arguments = @(
    '-batchmode', '-quit', '-force-d3d11',
    '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', 'HarborLearning.BeaconEpisodeRenderer.Capture',
    '-logFile', ('"' + $log + '"')
)
$editor = Start-Process -FilePath $unityEditor -WorkingDirectory $projectPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($editor.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 35
    throw "Unity capture exited with $($editor.ExitCode)."
}

& $Python (Join-Path $PSScriptRoot 'score.py') $outputPath --music-dir $musicPath
if ($LASTEXITCODE -ne 0) { throw "Harbor score synthesis exited with $LASTEXITCODE." }

Push-Location $outputPath
try {
    & $ffmpeg -hide_banner -loglevel error -y -i 'capture.mp4' -i 'episode-score.wav' `
        -vf 'ass=episode.ass' -map '0:v:0' -map '1:a:0' -c:v libx264 -preset fast -crf 16 `
        -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709 `
        -c:a aac -b:a 256k -ar 48000 -shortest -movflags '+faststart' `
        -metadata 'title=I Trained an AI Boat to Dock | Motion Trials 02' -metadata:s:a:0 'language=eng' `
        'Motion-Trials-02-I-Trained-an-AI-Boat-to-Dock.mp4'
    if ($LASTEXITCODE -ne 0) { throw "Harbor episode encoding exited with $LASTEXITCODE." }
} finally {
    Pop-Location
}

Write-Output (Join-Path $outputPath 'Motion-Trials-02-I-Trained-an-AI-Boat-to-Dock.mp4')
