# publish_portable.ps1
# This script publishes ClipDropPro as a single-file portable executable.

$projectName = "ClipDropPro.csproj"
$publishDir = "publish"
$runtime = "win-x64"

Write-Host "Publishing ClipDropPro as a single-file portable executable..." -ForegroundColor Cyan

# Clean previous publish
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

# Run dotnet publish
# Flags:
# -p:PublishSingleFile=true (Self-explanatory)
# -p:PublishReadyToRun=true (Pre-compiles for faster startup)
# -p:IncludeNativeLibrariesForSelfExtract=true (Bundles everything into the EXE)
# -p:EnableCompressionInSingleFile=true (Makes the EXE smaller)
# --self-contained true (Includes the .NET runtime)

dotnet publish $projectName `
    -c Release `
    -r $runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

Write-Host "`nPublish complete! The portable EXE is located in: " -NoNewline
Write-Host "$publishDir\ClipDropPro.exe" -ForegroundColor Green
Write-Host "All application data will be stored in a 'data' folder next to the EXE." -ForegroundColor Yellow
