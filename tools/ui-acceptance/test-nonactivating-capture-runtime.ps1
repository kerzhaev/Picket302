# Runtime smoke для неактивирующего режима capture-harness.
#
# Скрипт запускает только изолированный UiAcceptance с временным профилем,
# проверяет реальный foreground HWND и native extended style главного окна,
# затем завершает harness штатной командой CLOSE. Снимки, COM, печать и
# production-профиль в этот smoke не входят.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class Picket302NonActivatingProbe
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);
}
"@

$exe = Join-Path $PSScriptRoot 'bin\x64\Release\net8.0-windows\UiAcceptance.exe'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('Picket302Nonactivating-' + [Guid]::NewGuid().ToString('N'))
$harness = $null
$passed = $false
$failure = $null
$foregroundBefore = [Picket302NonActivatingProbe]::GetForegroundWindow()
$foregroundAfter = [IntPtr]::Zero
$harnessHwnd = [IntPtr]::Zero

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "UiAcceptance executable not found: $exe"
    }

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    $harness = Start-Acceptance $exe 'screens' $tempRoot $false $true
    if (-not (Read-HarnessLines $harness '^READY ' 30)) {
        throw 'READY was not received within 30 seconds.'
    }

    $ready = Get-HarnessLines $harness | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $harnessHwnd = [IntPtr][long](($ready -split ' ')[1])
    $foregroundAfter = [Picket302NonActivatingProbe]::GetForegroundWindow()

    $extendedStyle = [Picket302NonActivatingProbe]::GetWindowLongPtr($harnessHwnd, -20).ToInt64()
    $noActivate = (($extendedStyle -band 0x08000000) -ne 0)
    $topmost = (($extendedStyle -band 0x00000008) -ne 0)
    $visible = [Picket302NonActivatingProbe]::IsWindowVisible($harnessHwnd)
    $harnessIsForeground = ($foregroundAfter -eq $harnessHwnd)

    Write-Output "NONACTIVATING_RUNTIME: READY hwnd=$harnessHwnd pid=$($harness.Proc.Id)"
    Write-Output "FOREGROUND_BEFORE=$foregroundBefore"
    Write-Output "FOREGROUND_AFTER=$foregroundAfter"
    Write-Output "HARNESS_IS_FOREGROUND=$harnessIsForeground"
    Write-Output "WS_EX_NOACTIVATE=$noActivate"
    Write-Output "WS_EX_TOPMOST=$topmost"
    Write-Output "WINDOW_VISIBLE=$visible"

    if ($harnessIsForeground) {
        throw 'The acceptance window became the foreground window.'
    }
    if (-not $noActivate) {
        throw 'WS_EX_NOACTIVATE is absent from the runtime window.'
    }
    if ($topmost) {
        throw 'The acceptance window has WS_EX_TOPMOST.'
    }
    if (-not $visible) {
        throw 'The main acceptance window was not visible after READY.'
    }

    Send-HarnessCommand $harness 'CLOSE'
    if (-not $harness.Proc.WaitForExit(5000)) {
        throw 'The harness did not exit gracefully within 5 seconds.'
    }
    if ($harness.Proc.ExitCode -ne 0) {
        throw "The harness exited with code $($harness.Proc.ExitCode)."
    }

    $passed = $true
}
catch {
    $failure = $_.Exception.Message
    Write-Error $failure
}
finally {
    if ($null -ne $harness -and -not $harness.Proc.HasExited) {
        try {
            $stop = Stop-Acceptance $harness
            Write-Output "CLEANUP_GRACEFUL=$($stop.Graceful) CLEANUP_TREE_OK=$($stop.TreeOk)"
        }
        catch {
            $failure = "Cleanup failed: $($_.Exception.Message)"
            Write-Error $failure
        }
    }

    if (Test-Path -LiteralPath $tempRoot) {
        try {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            $failure = "Temporary profile cleanup failed: $($_.Exception.Message)"
            Write-Error $failure
        }
    }
}

if ($passed -and [string]::IsNullOrWhiteSpace($failure)) {
    Write-Output 'NONACTIVATING_RUNTIME: PASS'
    exit 0
}

Write-Output "NONACTIVATING_RUNTIME: FAIL $failure"
exit 1
