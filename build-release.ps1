param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$ProjectName   = "VepMod"
$ZipName       = "VieuxPoulpe-$ProjectName-$Version.zip"
$DistDir       = Join-Path $PSScriptRoot "dist"
$StagingDir    = Join-Path $DistDir "staging"
$DllPath       = Join-Path $PSScriptRoot "bin\Release\netstandard2.1\$ProjectName.dll"

Write-Host "=== Build Release $Version ===" -ForegroundColor Cyan

# 1. Build du projet en mode Release
Write-Host "Building $ProjectName in Release mode..." -ForegroundColor Yellow
dotnet build "$PSScriptRoot\$ProjectName.csproj" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}

# 2. Definir les fichiers requis et verifier leur existence AVANT toute action
$requiredFiles = @(
    @{ Source = $DllPath;                                                       Dest = "$ProjectName.dll" },
    @{ Source = Join-Path $PSScriptRoot "CHANGELOG.md";                         Dest = "CHANGELOG.md" },
    @{ Source = Join-Path $PSScriptRoot "README.md";                            Dest = "README.md" },
    @{ Source = Join-Path $PSScriptRoot "Ressources\manifest.json";             Dest = "manifest.json" },
    @{ Source = Join-Path $PSScriptRoot "Ressources\$ProjectName.repobundle";   Dest = "$ProjectName.repobundle" },
    @{ Source = Join-Path $PSScriptRoot "Ressources\icon.png";                  Dest = "icon.png" }
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file.Source)) {
        $missingFiles += $file.Source
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Error "Cannot create release - missing required files:`n  $($missingFiles -join "`n  ")"
    exit 1
}

# 3. Preparer le dossier dist/staging
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# 4. Copier les fichiers requis
foreach ($file in $requiredFiles) {
    Copy-Item $file.Source -Destination (Join-Path $StagingDir $file.Dest) -Force
    Write-Host "  Copied $($file.Dest)" -ForegroundColor Green
}

# 5. Creer le ZIP
$ZipPath = Join-Path $DistDir $ZipName
Compress-Archive -Path "$StagingDir\*" -DestinationPath $ZipPath -Force
Write-Host ""
Write-Host "Release package created: dist\$ZipName" -ForegroundColor Cyan
Write-Host "=== Done ===" -ForegroundColor Cyan
