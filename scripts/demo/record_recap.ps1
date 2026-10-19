# Records the real Vigil recap for the launch video.
#
#   powershell -ExecutionPolicy Bypass -File scripts\demo\record_recap.ps1 [-Exe path\to\Vigil.exe]
#
# Vigil runs in --demo-playback: fixed fake data, nothing on this PC is read or changed, and the
# entrance plays on a fixed timeline. ffmpeg records the whole screen (gdigrab can return a black
# image for some windows), then the clip is cropped to the window's own rectangle, which Vigil
# writes in device pixels, and trimmed to start on the frame the window appears.
#
# Output: video/public/recap.mp4 (and recap-still.png, the last frame).
# The window is on top of everything for about 8 seconds. Keep the cursor away from it, and the screen on.
# The raw capture is the whole screen, so it is deleted on every path, including failures.
param(
    [string]$Exe = "",
    [double]$Seconds = 7.0
)
# Native tools write progress to stderr; PowerShell 5.1 would treat that as an error under "Stop".
$ErrorActionPreference = "Continue"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$out = Join-Path $repo "video\public"
$raw = Join-Path $out "recordings"
New-Item -ItemType Directory -Force $raw | Out-Null

function Find-Ffmpeg {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $pkgs = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    $hit = Get-ChildItem $pkgs -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    throw "ffmpeg not found. Install it with: winget install Gyan.FFmpeg"
}
$ff = Find-Ffmpeg
$probe = Join-Path (Split-Path $ff) "ffprobe.exe"
if (-not $Exe) { $Exe = Join-Path $repo "publish\Vigil.exe" }
if (-not (Test-Path $Exe)) { throw "Vigil.exe not found at $Exe. Build it first (see scripts\demo\make_all.ps1)." }

$rectFile = Join-Path $raw "rect.json"
$screen = Join-Path $raw "screen.mkv"
Remove-Item $rectFile, $screen -ErrorAction SilentlyContinue

# 1. Start recording the screen, then start Vigil. The window opens 1.5 s later.
$recArgs = @("-y", "-loglevel", "error", "-f", "gdigrab", "-framerate", "30", "-draw_mouse", "0",
    "-t", [string]($Seconds + 9), "-i", "desktop", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-pix_fmt", "yuv444p", "`"$screen`"")
$rec = Start-Process $ff -ArgumentList $recArgs -PassThru -NoNewWindow
Start-Sleep -Milliseconds 800
$vigil = Start-Process $Exe -ArgumentList @("--demo-playback", "--playback-delay", "1.5", "--playback-rect", "`"$rectFile`"") -PassThru
$rec.WaitForExit(60000) | Out-Null
Stop-Process -Id $vigil.Id -ErrorAction SilentlyContinue
if (-not (Test-Path $rectFile)) { Remove-Item $screen -ErrorAction SilentlyContinue; throw "Vigil did not report its window rectangle. Is the screen on?" }
$r = Get-Content $rectFile -Raw | ConvertFrom-Json

# 2. Find the frame where the window appears: the first big change inside its rectangle.
$crop = "crop=$($r.width):$($r.height):$($r.x):$($r.y)"
$scan = & $ff -hide_banner -i $screen -vf "$crop,select='gt(scene\,0.08)',showinfo" -f null - 2>&1 | Select-String "pts_time:([0-9.]+)" | Select-Object -First 1
if (-not $scan) { Remove-Item $screen -ErrorAction SilentlyContinue; throw "Could not find the moment the window appeared." }
$start = [double]$scan.Matches[0].Groups[1].Value
Write-Host "Window appears at $start s; scale $($r.scale); size $($r.width)x$($r.height)"

# 3. Crop and trim. Even dimensions for H.264.
$w = [int]([math]::Floor($r.width / 2) * 2); $h = [int]([math]::Floor($r.height / 2) * 2)
$clip = Join-Path $out "recap.mp4"
& $ff -y -loglevel error -ss $start -i $screen -t $Seconds -vf "crop=$($w):$($h):$($r.x):$($r.y)" -c:v libx264 -crf 14 -pix_fmt yuv420p -r 30 -an $clip
& $ff -y -loglevel error -sseof -0.1 -i $clip -frames:v 1 (Join-Path $out "recap-still.png")
$meta = @{ start = $start; width = $w; height = $h; scale = $r.scale; seconds = $Seconds } | ConvertTo-Json
Set-Content -Path (Join-Path $out "recap.json") -Value $meta -Encoding utf8
# The raw capture is the whole screen, so it can hold anything that was open. Never keep it.
Remove-Item $screen -ErrorAction SilentlyContinue
Write-Host "Wrote $clip"
