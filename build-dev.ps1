$ErrorActionPreference = "Stop"

$ProjectName = "VepMod"
$DllPath     = Join-Path $PSScriptRoot "bin\Debug\netstandard2.1\$ProjectName.dll"
$BundlePath  = Join-Path $PSScriptRoot "Ressources\vepmod_prefabs"
$ResourceLogicalName = "VepMod.Resources.vepmod_prefabs"

Write-Host "=== Build Dev ===" -ForegroundColor Cyan

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
dotnet build "$PSScriptRoot\$ProjectName.csproj" -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}

if (-not (Test-Path $DllPath)) {
    Write-Error "Build reported success but DLL not found at $DllPath"
    exit 1
}

# Sanity check: les DLLs WebRTC VAD doivent etre presentes a cote de VepMod.dll.
# WebRtcVadSharp.dll est copiee par le Target CopyWebRtcVadSharpManaged, WebRtcVad.dll
# (natif x64) par le .targets du package NuGet. Si l'une manque, le VAD est silencieusement
# desactive au runtime (DllNotFoundException -> try/catch -> vadValidator = null), donc on
# echoue ici plutot que de produire un build au VAD muet.
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

# Sanity check: la ressource embarquee doit matcher le fichier sur disque
# NB: on charge via byte[] (et non LoadFile) pour ne PAS verrouiller la DLL
# dans la session PowerShell, sinon le prochain build echoue (MSB3027).
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
Write-Host "=== Done ===" -ForegroundColor Cyan
