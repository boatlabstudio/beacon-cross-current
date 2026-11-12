param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [string]$Python = 'python',
    [string]$Project = (Split-Path -Parent $PSScriptRoot),
    [string]$Data = (Join-Path $PSScriptRoot 'episode-01'),
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Preview/Beacon01-Recut'),
    [string]$MusicDirectory
)

$ErrorActionPreference = 'Stop'
$unityEditor = (Resolve-Path -LiteralPath $Unity).Path
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$dataPath = (Resolve-Path -LiteralPath $Data).Path
$outputPath = (New-Item -ItemType Directory -Force -Path $Output).FullName
$ffmpeg = (Get-Command ffmpeg -CommandType Application).Source
$env:BEACON_FILM_DIR = $outputPath
$env:BEACON_FFMPEG = $ffmpeg
$env:BEACON_STILLS = '0'

& $Python (Join-Path $PSScriptRoot 'episode.py') $outputPath --data-dir $dataPath
if ($LASTEXITCODE -ne 0) { throw "Episode preparation exited with $LASTEXITCODE." }

$log = Join-Path $outputPath 'unity-render.log'
$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', 'HarborLearning.BeaconEpisodeRenderer.Capture',
    '-logFile', ('"' + $log + '"')
)
$editor = Start-Process -FilePath $unityEditor -WorkingDirectory $projectPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($editor.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 35
    throw "Unity capture exited with $($editor.ExitCode)."
}

$scoreArgs = @((Join-Path $PSScriptRoot 'score.py'), $outputPath)
if ($MusicDirectory) {
    $scoreArgs += @('--music-dir', (Resolve-Path -LiteralPath $MusicDirectory).Path)
}
& $Python @scoreArgs
if ($LASTEXITCODE -ne 0) { throw "Score synthesis exited with $LASTEXITCODE." }

Push-Location $outputPath
try {
    $encode = @(
        '-hide_banner', '-loglevel', 'warning', '-y', '-i', 'capture.mp4', '-i', 'episode-score.wav',
        '-vf', 'ass=episode.ass', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '17',
        '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '256k', '-ar', '48000', '-map', '0:v:0',
        '-map', '1:a:0', '-shortest', '-movflags', '+faststart',
        '-metadata', 'title=Beacon 01 | AI Learns to Drive a Boat', '-metadata:s:a:0', 'language=eng',
        'Beacon-01-AI-Learns-to-Drive-a-Boat.mp4'
    )
    & $ffmpeg @encode
    if ($LASTEXITCODE -ne 0) { throw "Episode encoding exited with $LASTEXITCODE." }
    & $ffmpeg -hide_banner -loglevel error -y -ss 00:00:14 -i 'Beacon-01-AI-Learns-to-Drive-a-Boat.mp4' -frames:v 1 -vf 'scale=1280:720' -q:v 2 'Beacon-01-thumbnail.jpg'
    if ($LASTEXITCODE -ne 0) { throw "Thumbnail encoding exited with $LASTEXITCODE." }
} finally {
    Pop-Location
}

Write-Output (Join-Path $outputPath 'Beacon-01-AI-Learns-to-Drive-a-Boat.mp4')
