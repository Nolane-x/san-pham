param(
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\store"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not $IsWindows) { throw 'MSIX packaging must run on Windows.' }
& $PSScriptRoot\store-preflight.ps1

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($install) {
        $candidate = Join-Path $install 'MSBuild\Current\Bin\MSBuild.exe'
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}
if (-not $msbuild) {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { $msbuild = $cmd.Source }
}
if (-not $msbuild) { throw 'MSBuild from Visual Studio 2026 was not found. Install the Windows application development workload.' }

& $PSScriptRoot\test.ps1
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Store association replaces the development Identity/Publisher in Package.appxmanifest.
# GenerateAppxPackageOnBuild is the documented command-line switch for WinUI MSIX packaging.
& $msbuild .\src\Magic.Capture.App\Magic.Capture.App.csproj `
    /restore `
    /m `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:AppxBundle=Always `
    '/p:AppxBundlePlatforms=x64|ARM64' `
    /p:GenerateAppxPackageOnBuild=true `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxPackageDir="$OutputDirectory\" `
    /p:AppxPackageSigningEnabled=false

if ($LASTEXITCODE -ne 0) { throw "MSIX packaging failed with exit code $LASTEXITCODE." }

$bundles = @(Get-ChildItem $OutputDirectory -Recurse -File -Filter *.msixbundle)
if ($bundles.Count -ne 1) { throw "Expected exactly one Store MSIX bundle, found $($bundles.Count)." }
python .\scripts\verify-msix-bundle.py $bundles[0].FullName --root $root --require-store-identity
if ($LASTEXITCODE -ne 0) { throw 'Packaged Store MSIX contract verification failed.' }

$hash = (Get-FileHash -Algorithm SHA256 $bundles[0].FullName).Hash.ToLowerInvariant()
$checksumPath = "$($bundles[0].FullName).sha256"
"$hash  $($bundles[0].Name)" | Set-Content -Encoding ascii -NoNewline $checksumPath
Write-Host "Store package output: $OutputDirectory" -ForegroundColor Green
Write-Host "Bundle SHA256: $hash" -ForegroundColor Green
