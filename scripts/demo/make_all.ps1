# Rebuilds every launch asset from scratch with one command:
#
#   powershell -ExecutionPolicy Bypass -File scripts\demo\make_all.ps1 [-SkipRecording]
#
#   1. builds Vigil.exe (publish\Vigil.exe)
#   2. renders the app's own screenshot of the finished recap   -> docs\recap.png, video\public\recap.png
#   3. records the real recap in --demo-playback               -> video\public\recap.mp4   (skip with -SkipRecording)
#   4. renders the videos                                       -> launch\vigil-linkedin.mp4, launch\vigil-x.mp4
#   5. renders the calm README cut and turns it into a GIF      -> docs\recap.gif
#   6. renders the stills                                       -> launch\vigil-linkedin-cover.png, launch\social-preview.png
#
# Needs: the .NET 10 SDK, Node 20+, ffmpeg (winget install Gyan.FFmpeg). Demo mode only: nothing on this PC
# is read or changed. Step 3 puts a Vigil window on top of the screen for about 10 seconds.
param([switch]$SkipRecording)
$ErrorActionPreference = "Continue"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$video = Join-Path $repo "video"
$launch = Join-Path $repo "launch"
$docs = Join-Path $repo "docs"
New-Item -ItemType Directory -Force $launch, $docs | Out-Null

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Need($ok, $why) { if (-not $ok) { Write-Host "FAILED: $why" -ForegroundColor Red; exit 1 } }

# Tools. Use a dotnet that actually has an SDK (a system-wide dotnet may only have runtimes).
$dotnet = $null
foreach ($cand in @((Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"), (Get-Command dotnet -ErrorAction SilentlyContinue).Source)) {
    if ($cand -and (Test-Path $cand) -and (& $cand --list-sdks 2>$null | Select-String "^10.")) { $dotnet = $cand; break }
}
Need $dotnet "No .NET 10 SDK found. Install it from https://dotnet.microsoft.com/download"
$ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ff) { $ff = (Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
Need $ff "ffmpeg not found. Install it with: winget install Gyan.FFmpeg"
$env:FFMPEG = $ff

Step "1. Build Vigil.exe"
$exe = Join-Path $repo "publish\Vigil.exe"
# Never reuse an old build: an old exe has old demo data.
Remove-Item $exe -Force -ErrorAction SilentlyContinue
& $dotnet publish (Join-Path $repo "src\Vigil\Vigil.csproj") -c Release -o (Join-Path $repo "publish") --nologo -v q
Need (Test-Path $exe) "Vigil.exe was not built."

Step "2. The finished recap, as the app draws it (2x)"
$shots = Join-Path $env:TEMP "vigil-shots"
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
$env:VIGIL_SNAPSHOT_SCALE = "2"
$p = Start-Process $exe -ArgumentList @("--snapshot", "`"$shots`"") -PassThru -Wait
$env:VIGIL_SNAPSHOT_SCALE = $null
Need (Test-Path (Join-Path $shots "welcome-back.png")) "The snapshot did not produce welcome-back.png."
Copy-Item (Join-Path $shots "welcome-back.png") (Join-Path $docs "recap.png") -Force
Copy-Item (Join-Path $shots "welcome-back.png") (Join-Path $video "public\recap.png") -Force

if (-not $SkipRecording) {
    Step "3. Record the real recap (a Vigil window appears for a few seconds)"
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "record_recap.ps1") -Exe $exe
    Need (Test-Path (Join-Path $video "public\recap.mp4")) "The recording failed."
}

Step "4. Videos"
Push-Location $video
if (-not (Test-Path "node_modules")) { & npm ci --no-audit --no-fund }
foreach ($v in @(@("LinkedIn", "vigil-linkedin.mp4"), @("X", "vigil-x.mp4"))) {
    $tmp = Join-Path $env:TEMP ("raw-" + $v[1])
    & npx remotion render $v[0] $tmp --codec h264 --crf 15 --pixel-format yuv420p --muted --log error
    # Remotion's JPEG frames come out full range (yuvj420p). Re-encode to standard yuv420p, BT.709, faststart.
    & $ff -y -loglevel error -i $tmp -vf "scale=in_range=full:out_range=tv,format=yuv420p" -c:v libx264 -preset slow -crf 15 -profile:v high -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -movflags +faststart -an (Join-Path $launch $v[1])
}

Step "5. README GIF (calm cut)"
$cut = Join-Path $launch "recap-readme.mp4"
& npx remotion render Readme $cut --codec h264 --crf 14 --pixel-format yuv420p --muted --log error
$gif = Join-Path $docs "recap.gif"
$raw = Join-Path $env:TEMP "vigil-recap-raw.gif"
$pal = Join-Path $env:TEMP "vigil-palette.png"
$gifsicle = Join-Path $video "node_modules\gifsicle\vendor\gifsicle.exe"
# Best quality: 15 fps, 900 px, two-pass palette as specified, a light denoise first (the recording's
# compression noise would change pixels every frame), then a gentle gifsicle pass. No size cap.
$vf = "fps=15,scale=900:-1:flags=lanczos,hqdn3d=3:3:6:6"
& $ff -y -loglevel error -i $cut -vf "$vf,palettegen=stats_mode=diff" $pal
& $ff -y -loglevel error -i $cut -i $pal -lavfi "$vf [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" -loop 0 $raw
if (Test-Path $gifsicle) { & $gifsicle -O3 --lossy=20 --no-warnings $raw -o $gif } else { Copy-Item $raw $gif -Force }
Need (Test-Path $gif) "The GIF was not made."
Write-Host ("  docs\recap.gif: {0:N1} MB" -f ((Get-Item $gif).Length / 1MB))

Step "6. Stills"
& npx remotion still Cover (Join-Path $launch "vigil-linkedin-cover.png") --log error
& npx remotion still Social (Join-Path $launch "social-preview.png") --log error
Pop-Location

Step "Done"
foreach ($f in @("launch\vigil-linkedin.mp4", "launch\vigil-x.mp4", "docs\recap.gif", "docs\recap.png", "launch\vigil-linkedin-cover.png", "launch\social-preview.png")) {
    $path = Join-Path $repo $f
    if (Test-Path $path) { Write-Host ("  {0,-34} {1,8:N2} MB" -f $f, ((Get-Item $path).Length / 1MB)) } else { Write-Host "  MISSING $f" -ForegroundColor Red }
}
