$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Debug\net8.0-windows10.0.17763.0\MGA Sonic Anvil.exe'
$dir = Split-Path -Parent $exe

Write-Host 'f5-handoff: kill running app'
Get-Process -Name 'MGA Sonic Anvil' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

Write-Host 'f5-handoff: dotnet build'
& dotnet build (Join-Path $root 'MgaSonicAnvil.sln') -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "f5-handoff: build failed exit=$LASTEXITCODE"
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "f5-handoff: missing exe: $exe"
    exit 1
}

Write-Host "f5-handoff: start $exe"
Start-Process -FilePath $exe -WorkingDirectory $dir
Write-Host 'f5-handoff: done'
exit 0
