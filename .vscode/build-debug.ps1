$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'bin\Debug\net8.0-windows10.0.17763.0'

function Stop-LockedApp {
    Write-Host 'build-debug: kill running app (release file locks)'

    Get-Process -Name 'MGA Sonic Anvil' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    # デバッガ付き F5 はプロセス名が ".NET Host" / dotnet になることがある。
    # 出力フォルダを CommandLine に含むものだけ止めて、他の dotnet には触らない。
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine.IndexOf($outDir, [StringComparison]::OrdinalIgnoreCase) -ge 0
        } |
        ForEach-Object {
            Write-Host "build-debug: stop pid=$($_.ProcessId) $($_.Name)"
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 800
}

Stop-LockedApp

Write-Host 'build-debug: dotnet build'
& dotnet build (Join-Path $root 'MgaSonicAnvil.sln') -c Debug --nologo
exit $LASTEXITCODE
