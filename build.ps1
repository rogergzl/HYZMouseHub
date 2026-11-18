$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

dotnet build .\HYZMouseHub.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -Force .\bin\Release\net48\HYZMouseHub.exe .\HYZMouseHub.exe
Write-Host ''
Write-Host ('已生成: ' + (Join-Path $PSScriptRoot 'HYZMouseHub.exe'))
