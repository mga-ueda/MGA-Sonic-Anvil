$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host 'build-debug: kill running app (release file locks)'
Get-Process -Name 'MGA Sonic Anvil' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

Write-Host 'build-debug: dotnet build'
& dotnet build (Join-Path $root 'MgaSonicAnvil.sln') -c Debug --nologo
exit $LASTEXITCODE
