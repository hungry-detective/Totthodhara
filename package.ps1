# Build the portable release: dist\Totthodhara-windows-portable.zip (+ .sha256).
#
#   .\package.ps1                  # build + zip
#   .\package.ps1 -Publish         # also create the GitHub release (needs gh)
#
# The in-app updater looks for EXACTLY "Totthodhara-windows-portable.zip" on
# the latest release and verifies it against the ".sha256" asset. Bump
# `version` in Cargo.toml before publishing — the tag is v<version>.
param([switch]$Publish, [string]$Notes = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = (Select-String -Path Cargo.toml -Pattern '^version\s*=\s*"([^"]+)"' | Select-Object -First 1).Matches[0].Groups[1].Value
Write-Host "Building Totthodhara v$version (release)..."
cargo build --release
if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }

$dist = Join-Path $PSScriptRoot "dist"
$stage = Join-Path $dist "Totthodhara"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item "target\release\Totthodhara.exe" $stage
# Migration shim: Qt-edition installs (v0.x) only accept a package that has
# BOTH <root>\Totthodhara.exe and <root>\library\Totthodhara.exe. Shipping a
# real copy here lets those users auto-upgrade straight to this build.
New-Item -ItemType Directory -Path (Join-Path $stage "library") | Out-Null
Copy-Item "target\release\Totthodhara.exe" (Join-Path $stage "library")
@"
Totthodhara v$version — portable

Run Totthodhara.exe. Everything lives next to it in the "data" folder
(history, settings, clips), so the whole folder can move between PCs.

Updates: Settings > About > Check for updates (also checked on start).
The updater replaces this folder's files and never touches "data".
"@ | Set-Content -Path (Join-Path $stage "README.txt") -Encoding utf8

$zip = Join-Path $dist "Totthodhara-windows-portable.zip"
# Files at the zip ROOT (both updaters and Explorer "Extract All" handle it).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
$za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
Get-ChildItem $stage -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $_.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$za.Dispose()
$fs.Dispose()
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  Totthodhara-windows-portable.zip" | Set-Content -Path "$zip.sha256" -Encoding ascii -NoNewline
Write-Host "Packaged: $zip"
Write-Host "SHA-256:  $hash"

if ($Publish) {
    if ($Notes -eq "") { $Notes = "Totthodhara v$version" }
    gh release create "v$version" $zip "$zip.sha256" --repo hungry-detective/Totthodhara --title "Totthodhara v$version" --notes $Notes --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}
