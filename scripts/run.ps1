param(
    [ValidateSet('x64','ARM64')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not $IsWindows) { throw 'Magic Capture Desktop runs on Windows only.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK was not found.' }

# Microsoft.Windows.SDK.BuildTools.WinApp integrates `dotnet run` with package identity.
dotnet run --project .\src\Magic.Capture.App\Magic.Capture.App.csproj -c Debug -p:Platform=$Platform
