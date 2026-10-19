# Builds Vigil and installs (or updates) it for the current user. No admin needed.
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1            # build + install + start
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Connect   # also connect to Claude Code
param([switch]$Connect)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Vigil'
# Use a dotnet that actually has an SDK (the system one may be runtime-only).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -or -not (& $dotnet --list-sdks 2>$null)) { $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'The .NET 10 SDK is needed to build Vigil: winget install Microsoft.DotNet.SDK.10' }

Write-Host 'Building...'
& $dotnet publish (Join-Path $root 'src\Vigil\Vigil.csproj') -c Release -o (Join-Path $root 'publish') -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# Ask a running Vigil to quit cleanly (it restores power settings on the way out).
if (Get-Process Vigil -ErrorAction SilentlyContinue) {
    Write-Host 'Stopping the running Vigil...'
    $running = (Get-Process Vigil | Select-Object -First 1).Path
    if ($running) { & $running --quit }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Process Vigil -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    if (Get-Process Vigil -ErrorAction SilentlyContinue) { taskkill /F /IM Vigil.exe | Out-Null }
}

New-Item -ItemType Directory -Force $dest | Out-Null
# Windows can hold the old exe for a moment after the process exits.
for ($i = 0; ; $i++) {
    try { Copy-Item (Join-Path $root 'publish\Vigil.exe') (Join-Path $dest 'Vigil.exe') -Force; break }
    catch { if ($i -ge 20) { throw }; Start-Sleep -Milliseconds 500 }
}
Write-Host "Installed to $dest"

$args = if ($Connect) { '--connect' } else { '--background' }
Start-Process (Join-Path $dest 'Vigil.exe') -ArgumentList $args
Write-Host 'Vigil is running. Look for the eye in the taskbar tray.'
