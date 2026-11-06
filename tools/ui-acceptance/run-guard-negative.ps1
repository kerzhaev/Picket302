# Негативная проверка гарда acceptance-harness:
#   1) корень = родитель производственного каталога (ДляКорня превращает
#      его в производственный профиль);
#   2) корень = сам производственный каталог данных.
# Ожидание: код возврата 3, stdout без PROFILE/READY, реальный профиль
# не изменён, sentinel удалён в finally.
$ErrorActionPreference = 'Continue'

Add-Type -AssemblyName System.Security

$applicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$родительПроизводственного = Split-Path -Parent $реальныйПрофиль
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$exe = Join-Path $PSScriptRoot 'bin\x64\Release\net8.0-windows\UiAcceptance.exe'

function Test-Fingerprint([string]$dir) {
    $map = @{}
    if (-not (Test-Path $dir)) { return $map }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        Get-ChildItem -Recurse -File $dir -ErrorAction SilentlyContinue | ForEach-Object {
            $байты = $хеш.ComputeHash([System.IO.File]::ReadAllBytes($_.FullName))
            $sha = [System.BitConverter]::ToString($байты).Replace('-', '')
            $map[$_.FullName] = "$sha|$($_.LastWriteTimeUtc.Ticks)"
        }
    } finally { $хеш.Dispose() }
    return $map
}

function Get-SentinelState([string]$path) {
    if (-not (Test-Path $path)) { return '<absent>' }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        $байты = $хеш.ComputeHash([System.IO.File]::ReadAllBytes($path))
        $sha = [System.BitConverter]::ToString($байты).Replace('-', '')
        return "$sha|$( (Get-Item $path).LastWriteTimeUtc.Ticks )"
    } finally { $хеш.Dispose() }
}

$exitCode = 1
New-Item -ItemType Directory -Force -Path $реальныйПрофиль | Out-Null
Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N'))

try {
    $отпечатокДо = Test-Fingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Write-Host "sentinel before: $sentinelДо"
    Write-Host "production profile files: $($отпечатокДо.Count)"

    foreach ($случай in @(
        @{ Имя = 'родитель производственного каталога'; Корень = $родительПроизводственного },
        @{ Имя = 'сам производственный каталог данных'; Корень = $реальныйПрофиль }
    )) {
        Write-Host "=== случай: $($случай.Имя) -> $($случай.Корень)"

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = "emptybook `"$($случай.Корень)`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdout = $p.StandardOutput.ReadToEnd()
        $stderr = $p.StandardError.ReadToEnd()
        $p.WaitForExit()

        Write-Host "  exit code: $($p.ExitCode)"
        $nl = "`n"
        Write-Host ("  stdout: " + ($stdout -replace $nl, ' | '))
        Write-Host ("  stderr (первые 3 строки): " + ((($stderr -split $nl) | Select-Object -First 3) -join ' | '))

        if ($p.ExitCode -ne 3) {
            Write-Host "  FAIL: ожидается код возврата 3"
            $exitCode = 1
            return
        }

        if ($stdout -match 'PROFILE' -or $stdout -match 'READY') {
            Write-Host "  FAIL: stdout содержит PROFILE/READY — гарда не было"
            $exitCode = 1
            return
        }

        Write-Host "  OK: код 3, приложение не запускалось"
    }

    # Отпечатки после обоих отказов
    $отпечатокПосле = Test-Fingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel

    $новые = @($отпечатокПосле.Keys | Where-Object { -not $отпечатокДо.ContainsKey($_) })
    $изменённые = @($отпечатокПосле.Keys | Where-Object { $отпечатокДо.ContainsKey($_) -and $отпечатокДо[$_] -ne $отпечатокПосле[$_] })

    Write-Host "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Write-Host "new files: $($новые.Count); changed files: $($изменённые.Count)"

    if ($sentinelДо -ne $sentinelПосле) { Write-Host 'FAIL: sentinel изменён'; return }
    if ($новые.Count -ne 0) { Write-Host 'FAIL: появились новые файлы'; return }
    if ($изменённые.Count -ne 0) { Write-Host 'FAIL: изменены файлы'; return }

    Write-Host 'GUARD-NEGATIVE PASS: True'
    $exitCode = 0
}
finally {
    if (Test-Path $sentinel) { Remove-Item $sentinel -Force }
}

exit $exitCode
