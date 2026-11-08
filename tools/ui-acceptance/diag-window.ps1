$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\acceptance-lib.ps1"

$корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = "emptybook `"$корень`" keep"
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
# Контракт кодировки: harness пишет UTF-8 без BOM — декодируем как UTF-8.
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
$p = [System.Diagnostics.Process]::Start($psi)
Write-Host ("pid=" + $p.Id)

for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 700
    $p.Refresh()
    $wins = [AccWin]::WindowsOf([uint32]$p.Id)
    Write-Host ("t=" + $i + " exited=" + $p.HasExited + " mainHwnd=" + $p.MainWindowHandle + " enumWindows=" + $wins.Count)
    if ($wins.Count -gt 0) {
        foreach ($w in $wins) {
            $cls = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($w, $cls, 256) | Out-Null
            Write-Host ("  hwnd=" + $w + " cls=" + $cls.ToString() + " title=[" + [AccWin]::Title($w) + "]")
        }
        break
    }
}

try {
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes("CLOSE`r`n")
    $p.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $p.StandardInput.BaseStream.Flush()
} catch {}
if (-not $p.WaitForExit(15000)) { $p.Kill() }
Write-Host "exit=" + $p.ExitCode
