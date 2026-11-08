# Негативная проверка гарда acceptance-harness:
#   1) корень = родитель производственного каталога (ДляКорня превращает
#      его в производственный профиль);
#   2) корень = сам производственный каталог данных.
# Ожидание: код возврата 3, stdout без PROFILE/READY, реальный профиль
# не изменён, sentinel создаётся внутри try/finally и удаляется с
# -ErrorAction Stop (неудача удаления — exit 1).
# Fail-closed: $ErrorActionPreference='Stop'; финальная fingerprint-
# проверка в finally переводит прогон в FAIL при ненулевой дельте;
# общий ресурс (реальный профиль + sentinel) защищён межпроцессной
# блокировкой Enter-SharedTestLock.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Security

. "$PSScriptRoot\acceptance-lib.ps1"

$applicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$родительПроизводственного = Split-Path -Parent $реальныйПрофиль
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$exe = Join-Path $PSScriptRoot 'bin\x64\Release\net8.0-windows\UiAcceptance.exe'

function Test-Fingerprint([string]$dir) {
    # Fail-closed: ошибка перечисления или чтения файла = исключение.
    # -Force включает скрытые файлы — частичный отпечаток невозможен.
    $map = @{}
    if (-not (Test-Path $dir)) { return $map }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        Get-ChildItem -Recurse -File -Force $dir | ForEach-Object {
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
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$сценарийПройден = $false
$отпечатокДо = $null
$sentinelДо = $null
$блокировка = $null

try {
    # Межпроцессная блокировка общего ресурса (реальный профиль + sentinel):
    # отказ получения — fail-closed exit 1.
    $блокировка = Enter-SharedTestLock

    # Создание production-каталога и sentinel внутри try/finally:
    # невозможность создать sentinel — FAIL с exit 1 (EAP='Stop').
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop

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
        # Контракт кодировки: harness пишет UTF-8 без BOM — декодируем как UTF-8.
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdout = $p.StandardOutput.ReadToEnd()
        $stderr = $p.StandardError.ReadToEnd()
        # Ограниченное ожидание вместо бесконечного WaitForExit: stdout
        # закрыт (ReadToEnd вернулся), но liveness-контракт требует явного
        # бюджета; незавершённый процесс — FAIL, а не зависание.
        $завершение = Wait-ProcessExitBounded $p 15000 'guard-negative-case'

        if (-not $завершение.Exited) {
            throw ("FAIL: harness не завершился за 15000 мс после закрытия stdout — ограниченное ожидание истекло; " + $завершение.Диагностика)
        }

        Write-Host "  exit code: $($p.ExitCode)"
        $nl = "`n"
        Write-Host ("  stdout: " + ($stdout -replace $nl, ' | '))
        Write-Host ("  stderr (первые 3 строки): " + ((($stderr -split $nl) | Select-Object -First 3) -join ' | '))

        # Fault-inject (без UI: harness завершается с кодом 3 до создания
        # окна): намеренно неверное ожидание доказывает fail-closed —
        # несработавший assertion обязан дать ненулевой exit.
        $ожидаемыйКод = 3
        if ($env:P302_GUARD_FAULT -eq 'wrong-expectation') { $ожидаемыйКод = 0 }

        if ($p.ExitCode -ne $ожидаемыйКод) {
            throw "FAIL: ожидается код возврата $ожидаемыйКод, фактически $($p.ExitCode)"
        }

        if ($stdout -match 'PROFILE' -or $stdout -match 'READY') {
            throw 'FAIL: stdout содержит PROFILE/READY — гарда не было'
        }

        Write-Host '  OK: код 3, приложение не запускалось'

        # Пост-проверка дерева процессов harness: процесс завершился,
        # живых потомков нет. Ошибка CIM или остатки — FAIL (fail-closed);
        # «родитель завершился» не считается доказательством чистоты.
        $проверкаДерева = Test-ProcessTreeGone $p

        if (-not $проверкаДерева.Ok) {
            throw ("FAIL: дерево процессов harness не чистое (pid=" + $проверкаДерева.Pid +
                "): остатки [" + ($проверкаДерева.RemainingPids -join ';') +
                "] неподтверждённые [" + ($проверкаДерева.UnconfirmedPids -join ';') +
                "] ошибка: " + $проверкаДерева.Error)
        }

        Write-Host ("  OK: дерево процессов чисто (pid=" + $проверкаДерева.Pid + ")")
    }

    $sentinelПосле = Get-SentinelState $sentinel
    Write-Host "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"

    if ($sentinelДо -ne $sentinelПосле) { throw 'FAIL: sentinel изменён' }

    $сценарийПройден = $true
}
catch {
    Write-Host "GUARD-NEGATIVE FAIL: $($_.Exception.Message)"
    $сценарийПройден = $false
}
finally {
    # --- Финальная fingerprint-проверка (fail-closed, ненулевая дельта = FAIL)
    if ($null -ne $отпечатокДо) {
        try {
            $отпечатокФинал = Test-Fingerprint $реальныйПрофиль

            if ($env:P302_GUARD_FAULT -eq 'final-delta') {
                if ($отпечатокФинал.Count -gt 0) {
                    $первый = @($отпечатокФинал.Keys)[0]
                    $отпечатокФинал.Remove($первый)
                }
                Write-Host 'FAULT-INJECT: final delta Removed > 0'
            }

            $новые = @($отпечатокФинал.Keys | Where-Object { -not $отпечатокДо.ContainsKey($_) })
            $изменённые = @($отпечатокФинал.Keys | Where-Object { $отпечатокДо.ContainsKey($_) -and $отпечатокДо[$_] -ne $отпечатокФинал[$_] })
            $удалённые = @($отпечатокДо.Keys | Where-Object { -not $отпечатокФинал.ContainsKey($_) })

            Write-Host "final delta: new=$($новые.Count) changed=$($изменённые.Count) removed=$($удалённые.Count)"

            if ($новые.Count -ne 0) {
                Write-Host "  SAFETY-FAIL: появились новые файлы реального профиля: $($новые -join '; ')"
                $неудачиОчистки.Add('final-delta-new')
            }
            if ($изменённые.Count -ne 0) {
                Write-Host "  SAFETY-FAIL: изменены файлы реального профиля: $($изменённые -join '; ')"
                $неудачиОчистки.Add('final-delta-changed')
            }
            if ($удалённые.Count -ne 0) {
                Write-Host "  SAFETY-FAIL: удалены файлы реального профиля: $($удалённые -join '; ')"
                $неудачиОчистки.Add('final-delta-removed')
            }
        } catch {
            $неудачиОчистки.Add('fingerprint-final-check')
            Write-Host "SAFETY-FAIL: финальная fingerprint-проверка не выполнена: $($_.Exception.Message)"
        }
    } else {
        $неудачиОчистки.Add('fingerprint-before-missing')
        Write-Host 'SAFETY-FAIL: отпечаток ДО не снят — финальная проверка невозможна'
    }

    # Sentinel удаляется с -ErrorAction Stop: неудача удаления — FAIL.
    if (Test-Path $sentinel) {
        try {
            Remove-Item $sentinel -Force -ErrorAction Stop
        } catch {
            Write-Host "SAFETY-FAIL: sentinel не удалён: $($_.Exception.Message)"
            $неудачиОчистки.Add('sentinel-not-deleted')
        }
    }

    # Реальный профиль не должен содержать sentinel после завершения.
    if (Test-Path $sentinel) {
        Write-Host 'SAFETY-FAIL: sentinel остался в реальном профиле после завершения'
        $неудачиОчистки.Add('sentinel-still-present')
    }

    # Гарантированное освобождение межпроцессной блокировки: результат
    # входит в итоговый failure — PASS без подтверждённого освобождения
    # невозможен.
    $результатБлокировки = Exit-SharedTestLock $блокировка 'guard-negative'

    if (-not ($результатБлокировки.Released -and $результатБлокировки.Disposed)) {
        Write-Host ("SAFETY-FAIL: освобождение межпроцессной блокировки не подтверждено: " + $результатБлокировки.Error)
        $неудачиОчистки.Add('lock-release-failed')
    }

    if ($сценарийПройден -and $неудачиОчистки.Count -eq 0) {
        $exitCode = 0
        Write-Host 'GUARD-NEGATIVE PASS: True'
    } else {
        $exitCode = 1
        Write-Host 'GUARD-NEGATIVE PASS: False'
    }
}

exit $exitCode
