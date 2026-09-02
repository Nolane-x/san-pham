$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not $IsWindows) { throw 'Magic Capture Desktop tests target Windows/.NET. Run this script on Windows.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK was not found.' }

python .\scripts\verify-repo.py
if ($LASTEXITCODE -ne 0) { throw 'Repository verification failed.' }

dotnet restore .\Magic-Capture-Desktop.sln
dotnet test .\tests\Magic.Capture.Core.Tests\Magic.Capture.Core.Tests.csproj -c Release --no-restore --logger "console;verbosity=normal"
