param(
    # Build the VAD-free variant: no WebRtcVadSharp reference, no native WebRtcVad.dll.
    [switch]$NoVad,
    # Thunderstore profile whose VepMod.dll the build replaces, so the next launch of that profile runs it.
    [string]$DeployTo
)

$ErrorActionPreference = "Stop"

$ProjectName = "VepMod"
$DllPath     = Join-Path $PSScriptRoot "bin\Debug\netstandard2.1\$ProjectName.dll"
$BundlePath  = Join-Path $PSScriptRoot "Ressources\vepmod_prefabs"
$ResourceLogicalName = "VepMod.Resources.vepmod_prefabs"

Write-Host "=== Build Dev$(if ($NoVad) { ' (NoVad)' }) ===" -ForegroundColor Cyan

if (-not (Test-Path $BundlePath)) {
    Write-Error "Missing AssetBundle: $BundlePath"
    exit 1
}

$bundleTime = (Get-Item $BundlePath).LastWriteTimeUtc
$needsClean = (-not (Test-Path $DllPath)) -or ((Get-Item $DllPath).LastWriteTimeUtc -lt $bundleTime)
if ($needsClean) {
    Write-Host "Bundle newer than DLL (or DLL missing), forcing clean rebuild..." -ForegroundColor Yellow
    try { Remove-Item "$PSScriptRoot\obj\Debug" -Recurse -Force -ErrorAction Stop } catch {}
    try { Remove-Item $DllPath -Force -ErrorAction Stop } catch {}
}

Write-Host "Building $ProjectName in Debug mode..." -ForegroundColor Yellow
dotnet build "$PSScriptRoot\$ProjectName.csproj" -c Debug -p:VepModNoVad=$($NoVad.ToString().ToLower())
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}

if (-not (Test-Path $DllPath)) {
    Write-Error "Build reported success but DLL not found at $DllPath"
    exit 1
}

# The WebRTC VAD DLLs must sit next to VepMod.dll: WebRtcVadSharp.dll comes from the
# CopyWebRtcVadSharpManaged target, the native x64 WebRtcVad.dll from the NuGet package's .targets.
# A missing one silently disables the VAD at runtime (DllNotFoundException caught, vadValidator = null),
# so the build fails here instead. The NoVad variant leaves them out on purpose.
if (-not $NoVad) {
$OutputDir = Split-Path $DllPath -Parent
$vadDlls = @("WebRtcVadSharp.dll", "WebRtcVad.dll")
$missingVad = @()
foreach ($dll in $vadDlls) {
    if (-not (Test-Path (Join-Path $OutputDir $dll))) {
        $missingVad += $dll
    }
}
if ($missingVad.Count -gt 0) {
    Write-Error "VAD DLL(s) missing from output ($OutputDir): $($missingVad -join ', '). VAD would be silently disabled at runtime."
    exit 1
}
}

# The embedded bundle must match the one on disk. Loading from byte[] rather than LoadFile keeps the
# PowerShell session from locking the DLL, which would fail the next build (MSB3027).
$dllBytes = [IO.File]::ReadAllBytes($DllPath)
$asm = [Reflection.Assembly]::Load($dllBytes)
$stream = $asm.GetManifestResourceStream($ResourceLogicalName)
if ($null -eq $stream) {
    Write-Error "DLL built but does NOT embed $ResourceLogicalName"
    exit 1
}
$embeddedSize = $stream.Length
$stream.Dispose()

$diskSize = (Get-Item $BundlePath).Length
if ($embeddedSize -ne $diskSize) {
    Write-Error "Embedded bundle size ($embeddedSize) != disk bundle size ($diskSize) - DLL is stale"
    exit 1
}

Write-Host ""
Write-Host "OK: $DllPath" -ForegroundColor Green
Write-Host "    embeds vepmod_prefabs ($embeddedSize bytes, matches disk)" -ForegroundColor Green

if ($DeployTo) {
    $pluginDir = Get-Item "$env:APPDATA\Thunderstore Mod Manager\DataFolder\REPO\profiles\$DeployTo\BepInEx\plugins\*-VepMod-*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $pluginDir) {
        Write-Error "No VepMod plugin folder in Thunderstore profile '$DeployTo'"
        exit 1
    }
    if (Get-Process REPO -ErrorAction SilentlyContinue) {
        Write-Error "REPO is running and locks the deployed DLL: close the game, then deploy again"
        exit 1
    }
    Copy-Item $DllPath $pluginDir.FullName -Force
    Write-Host "    deployed to $($pluginDir.FullName)" -ForegroundColor Green
}
Write-Host "=== Done ===" -ForegroundColor Cyan
