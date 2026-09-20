param(
    [int]$Minutes = 20,
    [string]$Exe = "D:\GitHub\MGA-Sonic-Anvil\bin\Debug\net8.0-windows\MGA Sonic Anvil.exe",
    [string]$Library = "V:\マイドライブ\iTunes\iTunes Media\Game\CAPCOM\Devil May Cry 4 Original Soundtrack",
    [string]$LogPath = "D:\GitHub\MGA-Sonic-Anvil\bin\Debug\net8.0-windows\stress-20min.log"
)

# 以前完走した mga-anvil-drive.ps1 と同じ駆動方式。
# （keybd_event + 連打/長押し。フォーカス喪失で Hold を中断しない）

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { throw "Missing $Exe" }
if (-not (Test-Path $Library)) { throw "Missing library $Library" }

$mp3s = @(Get-ChildItem -LiteralPath $Library -Filter *.mp3 -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
if ($mp3s.Count -eq 0) { throw "No mp3 files under $Library" }

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class AnvilInput {
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const byte VK_NUMLOCK = 0x90;
    public const byte VK_F10 = 0x79;
    public const byte VK_NUMPAD0 = 0x60;
    public const byte VK_NUMPAD1 = 0x61;
    public const byte VK_NUMPAD3 = 0x63;
    public const byte VK_NUMPAD4 = 0x64;
    public const byte VK_NUMPAD6 = 0x66;
    public const byte VK_NUMPAD7 = 0x67;
    public const byte VK_NUMPAD9 = 0x69;
    public const byte VK_UP = 0x26;
    public const byte VK_DOWN = 0x28;
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    public static void Down(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); }
    public static void Up(byte vk) { keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
    public static void Tap(byte vk, int downMs) {
        Down(vk); System.Threading.Thread.Sleep(downMs); Up(vk);
    }
    public static void Hold(byte vk, int holdMs) {
        Down(vk); System.Threading.Thread.Sleep(holdMs); Up(vk);
    }
    public static void EnsureNumLock() {
        if ((GetKeyState(VK_NUMLOCK) & 1) == 0) {
            Tap(VK_NUMLOCK, 40);
            System.Threading.Thread.Sleep(40);
        }
    }
}
"@

function Write-Log([string]$msg) {
    $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $msg
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
    Write-Output $line
}

function Find-AnvilWindow {
    $script:foundHwnd = [IntPtr]::Zero
    $proc = [AnvilInput+EnumProc] {
        param([IntPtr]$h, [IntPtr]$l)
        if (-not [AnvilInput]::IsWindowVisible($h)) { return $true }
        $sb = New-Object System.Text.StringBuilder 512
        [void][AnvilInput]::GetWindowText($h, $sb, $sb.Capacity)
        if ($sb.ToString() -like "MGA Sonic Anvil*") {
            $script:foundHwnd = $h
            return $false
        }
        return $true
    }
    [void][AnvilInput]::EnumWindows($proc, [IntPtr]::Zero)
    return $script:foundHwnd
}

function Get-AnvilProcess {
    Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq "MGA Sonic Anvil.exe" -or $_.CommandLine -match "MGA Sonic Anvil"
    } | Select-Object -First 1
}

function Focus-Anvil {
    $p = Get-Process | Where-Object { $_.MainWindowTitle -match "Sonic Anvil" } | Select-Object -First 1
    if (-not $p -or $p.MainWindowHandle -eq [IntPtr]::Zero) { return $false }
    [AnvilInput]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
    [AnvilInput]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    return $true
}

function Sample-Anvil([string]$tag) {
    $cim = Get-AnvilProcess
    if (-not $cim) {
        Write-Log "SAMPLE $tag PROCESS_GONE"
        return $null
    }
    $p = Get-Process -Id $cim.ProcessId -ErrorAction SilentlyContinue
    if (-not $p) {
        Write-Log "SAMPLE $tag PROCESS_GONE"
        return $null
    }
    $age = ((Get-Date) - $p.StartTime).TotalSeconds
    if ($age -lt 1) { $age = 1 }
    $avg = [math]::Round(100.0 * $p.CPU / $age, 0)
    $ws = [math]::Round($p.WorkingSet / 1MB, 1)
    Write-Log ("SAMPLE {0} pid={1} cpuSec={2:N1} avgPct={3} wsMB={4} threads={5} handles={6} responding={7} title={8}" -f $tag, $p.Id, $p.CPU, $avg, $ws, $p.Threads.Count, $p.HandleCount, $p.Responding, $p.MainWindowTitle)
    return $p
}

New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
"" | Set-Content -Path $LogPath -Encoding UTF8
Write-Log "START duration=$($Minutes)min library=$Library mp3Count=$($mp3s.Count)"

Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq "MGA Sonic Anvil.exe" -or $_.CommandLine -match "MGA Sonic Anvil"
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 600

# コマンドライン長制限を避けるため、先頭トラック + フォルダの両方を渡すのではなく
# 完走実績のある「ファイル列挙起動」に合わせ、先頭 24 曲を渡す。
$launchFiles = @($mp3s | Select-Object -First 24)
Write-Log ("launch files={0} (of {1})" -f $launchFiles.Count, $mp3s.Count)
$script:appProc = Start-Process -FilePath $Exe -ArgumentList $launchFiles -PassThru

$ready = $false
for ($i = 0; $i -lt 50; $i++) {
    Start-Sleep -Milliseconds 400
    if (Focus-Anvil) { $ready = $true; break }
}
if (-not $ready) {
    Write-Log "STRESS_FAIL no window"
    exit 2
}
Write-Log ("window ready hwnd={0}" -f (Find-AnvilWindow))
Start-Sleep -Seconds 4
[AnvilInput]::EnsureNumLock()
Focus-Anvil | Out-Null
[AnvilInput]::Tap([AnvilInput]::VK_F10, 50)
Start-Sleep -Seconds 2
Focus-Anvil | Out-Null
[AnvilInput]::Tap([AnvilInput]::VK_NUMPAD0, 50)
Start-Sleep -Seconds 1
Sample-Anvil "start" | Out-Null

$tap7 = 0; $tap9 = 0; $tap1 = 0; $tap3 = 0
$hold7ms = 0; $hold9ms = 0; $hold1ms = 0; $hold3ms = 0
$tap4 = 0; $tap6 = 0; $arrows = 0
$cycles = 0
$lastSample = Get-Date
$deadline = (Get-Date).AddMinutes($Minutes)

Write-Log "DRIVE begin until $($deadline.ToString('HH:mm:ss')) (mash 7/9 + hold shuttle 1/3)"

while ((Get-Date) -lt $deadline) {
    if (-not (Get-AnvilProcess)) {
        $code = "unknown"
        $when = "unknown"
        if ($script:appProc -and $script:appProc.HasExited) {
            $code = $script:appProc.ExitCode
            $when = $script:appProc.ExitTime.ToString("HH:mm:ss.fff")
        }
        Write-Log "STRESS_FAIL process died cycle=$cycles exitCode=$code exitTime=$when"
        exit 3
    }

    Focus-Anvil | Out-Null
    [AnvilInput]::EnsureNumLock()

    # mash 9
    for ($i = 0; $i -lt 35; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD9, 18); Start-Sleep -Milliseconds 12; $tap9++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD9, 2800); $hold9ms += 2800
    Start-Sleep -Milliseconds 80

    # mash 7
    for ($i = 0; $i -lt 35; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD7, 18); Start-Sleep -Milliseconds 12; $tap7++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD7, 2800); $hold7ms += 2800
    Start-Sleep -Milliseconds 80

    # hold shuttle 3 then 1
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD3, 3500); $hold3ms += 3500
    Start-Sleep -Milliseconds 120
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD1, 3500); $hold1ms += 3500
    Start-Sleep -Milliseconds 120

    # mash shuttle
    for ($i = 0; $i -lt 20; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD3, 25); Start-Sleep -Milliseconds 20; $tap3++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD3, 1800); $hold3ms += 1800
    for ($i = 0; $i -lt 20; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD1, 25); Start-Sleep -Milliseconds 20; $tap1++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD1, 1800); $hold1ms += 1800

    # track skip + more jumps
    [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD6, 40); $tap6++
    Start-Sleep -Milliseconds 250
    for ($i = 0; $i -lt 25; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD9, 18); Start-Sleep -Milliseconds 12; $tap9++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD9, 1500); $hold9ms += 1500
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD3, 1200); $hold3ms += 1200
    [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD4, 40); $tap4++
    Start-Sleep -Milliseconds 250
    for ($i = 0; $i -lt 25; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_NUMPAD7, 18); Start-Sleep -Milliseconds 12; $tap7++ }
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD7, 1500); $hold7ms += 1500
    [AnvilInput]::Hold([AnvilInput]::VK_NUMPAD1, 1200); $hold1ms += 1200

    # playlist cursor
    for ($i = 0; $i -lt 6; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_DOWN, 30); Start-Sleep -Milliseconds 40; $arrows++ }
    for ($i = 0; $i -lt 6; $i++) { [AnvilInput]::Tap([AnvilInput]::VK_UP, 30); Start-Sleep -Milliseconds 40; $arrows++ }

    $cycles++
    if (((Get-Date) - $lastSample).TotalSeconds -ge 60) {
        Write-Log ("progress cycles={0} tap7={1} tap9={2} tap1={3} tap3={4} hold7ms={5} hold9ms={6} hold1ms={7} hold3ms={8} skip={9}/{10} arrows={11}" -f $cycles, $tap7, $tap9, $tap1, $tap3, $hold7ms, $hold9ms, $hold1ms, $hold3ms, $tap4, $tap6, $arrows)
        $p = Sample-Anvil "min"
        if (-not $p) {
            Write-Log "STRESS_FAIL process gone cycle=$cycles"
            exit 4
        }
        if (-not $p.Responding) {
            Write-Log "STRESS_FAIL not responding cycle=$cycles"
            exit 5
        }
        $lastSample = Get-Date
    }
}

Write-Log ("STRESS_DONE cycles={0} tap7={1} tap9={2} tap1={3} tap3={4} hold7s={5:N0} hold9s={6:N0} hold1s={7:N0} hold3s={8:N0}" -f $cycles, $tap7, $tap9, $tap1, $tap3, ($hold7ms/1000), ($hold9ms/1000), ($hold1ms/1000), ($hold3ms/1000))
Sample-Anvil "end" | Out-Null
Write-Log "DRIVE end"
exit 0
