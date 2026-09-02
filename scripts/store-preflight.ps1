param(
    [string]$ManifestPath = "$PSScriptRoot\..\src\Magic.Capture.App\Package.appxmanifest"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

python .\scripts\verify-repo.py
if ($LASTEXITCODE -ne 0) { throw 'Repository verification failed.' }

python .\scripts\verify-release-metadata.py
if ($LASTEXITCODE -ne 0) { throw 'Release metadata verification failed.' }

if (-not (Test-Path $ManifestPath)) { throw "Package manifest not found: $ManifestPath" }
[xml]$manifest = Get-Content -Raw $ManifestPath
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $ns)

if (-not $identity) { throw 'Package.appxmanifest has no Identity element.' }
$identityName = $identity.GetAttribute('Name')
$publisher = $identity.GetAttribute('Publisher')
$manifestVersion = $identity.GetAttribute('Version')
if ([string]::IsNullOrWhiteSpace($identityName)) { throw 'MSIX Identity Name is empty.' }
if ([string]::IsNullOrWhiteSpace($publisher)) { throw 'MSIX Publisher is empty.' }
if ($identityName -eq 'Magic.Capture.Desktop.Dev') {
    throw 'Development MSIX identity is still present. Associate Magic Capture Desktop with its Partner Center product before Store packaging.'
}
if ($publisher -eq 'CN=Magic Capture Desktop Dev') {
    throw 'Development Publisher is still present. Associate the project with Partner Center before Store packaging.'
}

$version = Get-Content -Raw .\release\version.json | ConvertFrom-Json
if ($manifestVersion -ne $version.msixVersion) {
    throw "Manifest version $manifestVersion does not match release/version.json $($version.msixVersion)."
}

$storeGuide = Get-Content -Raw .\packaging\STORE_SUBMISSION.md
if ($storeGuide -notmatch [regex]::Escape($version.proOfferToken)) {
    throw 'Store guide does not contain the configured Pro offer token.'
}

Write-Host "Store preflight passed for $identityName $manifestVersion." -ForegroundColor Green
