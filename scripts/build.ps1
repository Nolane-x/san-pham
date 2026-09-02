param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not $IsWindows) { throw 'Magic Capture Desktop is a Windows application. Build it on Windows.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK was not found.' }

python .\scripts\verify-repo.py
if ($LASTEXITCODE -ne 0) { throw 'Repository verification failed.' }

dotnet restore .\Magic-Capture-Desktop.sln
& $PSScriptRoot\test.ps1

dotnet build .\src\Magic.Capture.Core\Magic.Capture.Core.csproj -c $Configuration --no-restore
foreach ($platform in @('x64','ARM64')) {
    Write-Host "Building Magic Capture Desktop $platform..." -ForegroundColor Cyan
    dotnet build .\src\Magic.Capture.App\Magic.Capture.App.csproj -c $Configuration -p:Platform=$platform --no-restore
}
