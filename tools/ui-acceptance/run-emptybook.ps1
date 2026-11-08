# Пункт B: настоящий runtime-сценарий пустой адресной книги. Fail-closed
# и liveness: НИ ОДНОГО бесконечного ожидания процесса — любое ожидание
# через Wait-ProcessExitBounded; при TreeOk=false выполняется максимум
# одна ограниченная повторная попытка teardown и повторный bounded
# post-scan; harness, оставшийся живым, фиксируется (harness-not-exited),
# финализация продолжается, итоговый exit — ненулевой. Порядок
# финализации: уборка → fingerprint → sentinel → гарантированное
# освобождение блокировки (результат входит в failure) → финальный
# статус → evidence ОДИН раз с обязательными маркерами.
# Fault-инъекция драйвера (без UI): P302_DRIVER_FAULT=parent-alive-after-stop
# — synthetic harness (скрытый PowerShell, не читает stdin) вместо
# приложения; Stop-HarnessTree возвращает TreeOk=false; драйвер обязан
# завершиться за ограниченное время с exit 1.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$скриншот = Join-Path $out '01-адресная-книга-пустая-1280x720.png'

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'emptybook-failure-evidence.md'
$заголовокEvidence = 'Emptybook runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$фaultПрименён = $false
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

function ДобавитьФлагОчистки([string]$флаг) {
    if (-not $неудачиОчистки.Contains($флаг)) {
        $неудачиОчистки.Add($флаг)
    }
}

try {
    # Межпроцессная блокировка общего ресурса: отказ — fail-closed exit 1.
    $блокировка = Enter-SharedTestLock

    # --- 1. Sentinel и отпечатки реального профиля ДО (fail-closed) -------
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    # --- 2. Synthetic harness (fault) или реальный запуск ------------------
    # parent-alive-after-stop — процесс переживает остановку (TreeOk=false);
    # taskkill-hang — внутренний bounded timeout САМОЙ taskkill срабатывает
    # (TreeOk=false, TreeError содержит таймаут). Оба режима — без UI.
    $faultРежим = ($env:P302_DRIVER_FAULT -eq 'parent-alive-after-stop' -or $env:P302_DRIVER_FAULT -eq 'taskkill-hang')

    if ($faultРежим) {
        # FAULT-режим: без UI, без печати, без production-профиля —
        # скрытый PowerShell вместо harness; не читает stdin, поэтому
        # graceful CLOSE не завершает его; Stop-HarnessTree (шов) вернёт
        # TreeOk=false, оставив процесс живым.
        Запись "FAULT-INJECT: $env:P302_DRIVER_FAULT — synthetic harness (без UI)"
        $psiСинтетика = New-Object System.Diagnostics.ProcessStartInfo
        $psiСинтетика.FileName = 'powershell.exe'
        $psiСинтетика.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 1800"'
        $psiСинтетика.UseShellExecute = $false
        $psiСинтетика.CreateNoWindow = $true
        $psiСинтетика.RedirectStandardInput = $true
        # Трубы stdout/stderr НЕ наследуются синтетикой: иначе наследник
        # держит дескрипторы внешнего launcher-а и его ReadToEnd не
        # получает EOF до смерти synthetic (pipe-deadlock тестера).
        $psiСинтетика.RedirectStandardOutput = $true
        $psiСинтетика.RedirectStandardError = $true
        $синтетика = [System.Diagnostics.Process]::Start($psiСинтетика)
        $h = @{ Proc = $синтетика; Lines = [System.Collections.ArrayList]::new() }
        Запись "SYNTHETIC HARNESS PID: $($синтетика.Id)"
        $сценарийПройден = $false
        Запись 'условие [fault-режим: сценарий пропущен]: False'
    } else {
        $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
        Запись "temp profile: $корень"

        $h = Start-Acceptance $exe 'emptybook' $корень

        if (-not (Read-HarnessLines $h '^READY ' 30)) {
            Запись 'FAIL: READY не получен за 30 c'
            throw 'READY not received'
        }

        $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
        $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
        Запись "harness READY: $готовность"

        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
        Start-Sleep -Seconds 2

        $h.Proc.StandardInput.WriteLine('COUNTS')

        if (-not (Read-HarnessLines $h '^COUNTS ' 10)) {
            Запись 'FAIL: COUNTS не получен'
            throw 'COUNTS not received'
        }

        [AccWin]::Capture($h.Hwnd, $скриншот)
        Start-Sleep -Milliseconds 300
        $скриншотРазмер = 0
        if (Test-Path $скриншот) { $скриншотРазмер = (Get-Item $скриншот).Length }
        Запись "скриншот: $скриншот ($скриншотРазмер байт)"
    }

    # --- 3. Остановка harness: ограниченная, без бесконечных ожиданий -----
    # Stop-Acceptance при таймауте делегирует Stop-HarnessTree; при
    # TreeOk=false — максимум одна ограниченная повторная попытка и
    # повторный bounded post-scan (Invoke-HarnessStop).
    $остановка = Invoke-HarnessStop $h 'emptybook'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) TreeConfirmed=$($остановка.Результат.TreeConfirmed) остатки=[$($остановка.Результат.RemainingPids -join ';')] неподтверждённые=[$($остановка.Результат.UnconfirmedPids -join ';')] taskkill=$($остановка.Результат.TaskKillExitCode) ошибка: $($остановка.Результат.TreeError)"

        # Внутренний bounded timeout САМОЙ taskkill — отдельная диагностика
        # (не путать с watchdog уровня драйвера).
        if ($остановка.Результат.TaskKillTimedOutPids.Count -gt 0) {
            Запись "SAFETY-FAIL: TASKKILL TIMEOUT: внутренний bounded timeout taskkill сработал (pid=$($остановка.Результат.TaskKillTimedOutPids -join ';'))"
        }
    }

    # ExitCode читается ТОЛЬКО у фактически завершившегося процесса;
    # ожидание — ограниченное; недоступный код уничтоженного процесса не
    # превращается в исключение.
    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'emptybook-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode

        if ($ожиданиеВыхода.Диагностика) {
            Запись "BOUNDED-WAIT: $($ожиданиеВыхода.Диагностика)"
        }
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
        Запись 'SAFETY-FAIL: harness не завершён после остановки (HarnessNotExited) — дальнейшие ожидания запрещены'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

    if (-not $faultРежим) {
        # --- 4. Проверки stdout и реального профиля (только happy-путь) ---
        $stdout = Get-HarnessLines $h
        $строкаСчётчиков = $stdout | Where-Object { $_ -match '^COUNTS ' } | Select-Object -First 1
        $всего = if ($строкаСчётчиков -and $строкаСчётчиков -match 'всего=(\d+)') { $Matches[1] } else { '?' }
        $отправителей = if ($строкаСчётчиков -and $строкаСчётчиков -match 'отправителей=(\d+)') { $Matches[1] } else { '?' }
        $получателей = if ($строкаСчётчиков -and $строкаСчётчиков -match 'получателей=(\d+)') { $Matches[1] } else { '?' }
        Запись "пустое состояние (stdout): всего=$всего отправителей=$отправителей получателей=$получателей"
        $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count
        Запись "FAIL-строк в stdout: $отказовStdout"

        $профильОчищен = -not (Test-Path $корень)
        Запись "temp profile cleaned: $профильОчищен"

        $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
        $sentinelПосле = Get-SentinelState $sentinel
        $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
        Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
        Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

        $фault = $env:P302_EMPTYBOOK_FAULT

        if ($фault -eq 'delta-removed') {
            $дельта.Removed = @('C:' + [IO.Path]::DirectorySeparatorChar + 'fake' + [IO.Path]::DirectorySeparatorChar + 'injected.file')
            Запись 'FAULT-INJECT: delta Removed > 0'
        }

        if ($фault -eq 'cleanup-false') {
            $профильОчищен = $false
            Запись 'FAULT-INJECT: cleanup = False'
        }

        # --- 5. Условия happy-path (включая дерево и liveness) -------------
        $условия = [ordered]@{
            'READY получен' = ($null -ne ($stdout | Where-Object { $_ -match '^READY ' }))
            'COUNTS получен' = ($null -ne $строкаСчётчиков)
            'Всего: 0' = ($всего -eq '0')
            'таблица отправителей пуста' = ($отправителей -eq '0')
            'таблица получателей пуста' = ($получателей -eq '0')
            'скриншот создан' = ($скриншотРазмер -gt 0)
            'временный профиль удалён' = $профильОчищен
            'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
            'новых файлов нет' = ($дельта.New.Count -eq 0)
            'изменённых файлов нет' = ($дельта.Changed.Count -eq 0)
            'удалённых файлов нет' = ($дельта.Removed.Count -eq 0)
            'harness exit 0' = ($кодВыходаHarness -eq 0)
            'harness не убит' = (-not $убит)
            'harness завершён (bounded)' = (-not $harnessNotExited)
            'нет FAIL в stdout' = ($отказовStdout -eq 0)
            'дерево процессов чисто' = $остановка.Результат.TreeOk
            'нет живых потомков harness' = ($остановка.Результат.RemainingPids.Count -eq 0)
            'нет неподтверждённых потомков' = ($остановка.Результат.UnconfirmedPids.Count -eq 0)
            'нет ошибки проверки дерева' = ($остановка.Результат.TreeError -eq '')
        }

        $сценарийПройден = $true
        foreach ($ключ in $условия.Keys) {
            $значение = $условия[$ключ]
            Запись ("условие [{0}]: {1}" -f $ключ, $значение)
            if (-not $значение) { $сценарийПройден = $false }
        }
    }
}
catch {
    Запись "EXCEPTION: $($_.Exception.Message)"
    $сценарийПройден = $false
}
finally {
    try {
        # --- A. Аварийная ограниченная остановка, если harness ещё жив ----
        if ($h -ne $null -and $h.Proc -ne $null -and -not $h.Proc.HasExited) {
            $остановкаФинал = Invoke-HarnessStop $h 'emptybook-finally'

            foreach ($флаг in $остановкаФинал.Флаги) {
                ДобавитьФлагОчистки $флаг
                Запись "SAFETY-FLAG: $флаг"
            }

            if ($остановкаФинал.Результат -ne $null -and (-not $остановкаФинал.Результат.Graceful)) {
                $script:убит = $true
                ДобавитьФлагОчистки 'emergency-kill'
                Запись "SAFETY-FAIL: harness убит в finally (pid=$($остановкаФинал.Результат.Pid)) — happy-path PASS невозможен"
            }

            if ($остановкаФинал.Результат -ne $null -and $остановкаФинал.Результат.HarnessNotExited) {
                ДобавитьФлагОчистки 'harness-not-exited'
                Запись "SAFETY-FAIL: harness остался жив даже после аварийной остановки (pid=$($остановкаФинал.Результат.Pid)) — драйвер завершается с exit 1 без дальнейших ожиданий"
            }
        }

        # --- B. Финальная fingerprint-проверка (fail-closed) ---------------
        if ($null -ne $отпечатокДо) {
            try {
                $отпечатокФинал = Test-ProfileFingerprint $реальныйПрофиль

                if ($env:P302_EMPTYBOOK_FAULT -eq 'final-delta') {
                    if ($отпечатокФинал.Count -gt 0) {
                        $первый = @($отпечатокФинал.Keys)[0]
                        $отпечатокФинал.Remove($первый)
                    }
                    Запись 'FAULT-INJECT: final delta Removed > 0'
                }

                $дельтаФинал = Compare-Fingerprints $отпечатокДо $отпечатокФинал
                $финальнаяДельтаNew = $дельтаФинал.New.Count
                $финальнаяДельтаChanged = $дельтаФинал.Changed.Count
                $финальнаяДельтаRemoved = $дельтаФинал.Removed.Count
                Запись "production delta (финально): new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
                $дельтаФинал.New | ForEach-Object { Запись "  NEW: $_" }
                $дельтаФинал.Changed | ForEach-Object { Запись "  CHANGED: $_" }
                $дельтаФинал.Removed | ForEach-Object { Запись "  REMOVED: $_" }

                if ($финальнаяДельтаNew -ne 0) {
                    ДобавитьФлагОчистки 'final-delta-new'
                    Запись 'SAFETY-FAIL: финальная дельта — новые файлы реального профиля'
                }
                if ($финальнаяДельтаChanged -ne 0) {
                    ДобавитьФлагОчистки 'final-delta-changed'
                    Запись 'SAFETY-FAIL: финальная дельта — изменённые файлы реального профиля'
                }
                if ($финальнаяДельтаRemoved -ne 0) {
                    ДобавитьФлагОчистки 'final-delta-removed'
                    Запись 'SAFETY-FAIL: финальная дельта — удалённые файлы реального профиля'
                }
            } catch {
                ДобавитьФлагОчистки 'fingerprint-final-check'
                Запись "SAFETY-FAIL: финальная fingerprint-проверка не выполнена: $($_.Exception.Message)"
            }
        } else {
            ДобавитьФлагОчистки 'fingerprint-before-missing'
            Запись 'SAFETY-FAIL: отпечаток ДО не снят — финальная проверка невозможна'
        }

        # --- C. Sentinel удаляется с -ErrorAction Stop ---------------------
        if (Test-Path $sentinel) {
            try {
                Remove-Item $sentinel -Force -ErrorAction Stop
            } catch {
                ДобавитьФлагОчистки 'sentinel-not-deleted'
                Запись "SAFETY-FAIL: sentinel не удалён: $($_.Exception.Message)"
            }
        }

        if (Test-Path $sentinel) {
            ДобавитьФлагОчистки 'sentinel-still-present'
            Запись 'SAFETY-FAIL: sentinel остался в реальном профиле после завершения'
        }

        # --- D. Выбор файла evidence ---------------------------------------
        $фault = $env:P302_EMPTYBOOK_FAULT
        $фaultДрайвера = $env:P302_DRIVER_FAULT
        $фaultПрименён = @($журнал | Where-Object { $_ -match 'FAULT-INJECT:' }).Count -gt 0

        if ($сценарийПройден -and ($фault -or $фaultДрайвера)) {
            $файлEvidence = 'emptybook-fault-' + $(if ($фault) { $фault } else { $фaultДрайвера }) + '.md'
            $заголовокEvidence = 'Emptybook fault-injection: ' + $(if ($фault) { $фault } else { $фaultДрайвера })
            Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
        } elseif ($сценарийПройден) {
            $файлEvidence = 'emptybook-happy-evidence.md'
            $заголовокEvidence = 'Emptybook runtime-доказательство (happy path)'
        } elseif ($фaultПрименён) {
            $файлEvidence = 'emptybook-fault-' + $(if ($фault) { $фault } else { $фaultДрайвера }) + '.md'
            $заголовокEvidence = 'Emptybook fault-injection: ' + $(if ($фault) { $фault } else { $фaultДрайвера })
        } else {
            $файлEvidence = 'emptybook-failure-evidence.md'
            $заголовокEvidence = 'Emptybook runtime-доказательство (FAIL)'
        }

        if ($фault -eq 'evidence-fail') {
            $файлEvidence = 'bad|name-evidence-fail.md'
            Запись 'FAULT-INJECT: evidence write target invalid'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
        Запись "SAFETY-FAIL: ошибка финализации: $($_.Exception.Message)"
    }

    # --- E. Гарантированное освобождение блокировки ------------------------
    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'emptybook'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение межпроцессной блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    # --- F. Финальный статус ДО записи evidence ----------------------------
    $faultДрайвераЕщё = $env:P302_DRIVER_FAULT
    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0) -and (-not $faultДрайвераЕщё) -and (-not $env:P302_EMPTYBOOK_FAULT)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись "Режим: emptybook"
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if (Get-Variable -Name корень -ErrorAction SilentlyContinue) { $корень } else { 'не создан' })"
    Запись "createDemoData=false"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "FAULT: $(if ($faultДрайвераЕщё) { $faultДрайвераЕщё } elseif ($env:P302_EMPTYBOOK_FAULT) { $env:P302_EMPTYBOOK_FAULT } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($фaultПрименён) {
        Запись "PASS_OF_CONTRACT: $(-not $happyПройден) (fault — инъекция корректно обнаружена, скрипт не вернёт 0)"
    }

    # --- G. Единая запись evidence + пост-проверка -------------------------
    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'createDemoData=false' $журнал

    $маркеры = @('Режим: emptybook', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:', 'createDemoData=false')
    if ($фaultПрименён) { $маркеры += 'PASS_OF_CONTRACT:' }
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    # --- H. Код возврата ----------------------------------------------------
    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'EMPTYBOOK PASS: True'
    } else {
        $exitCode = 1
        Запись 'EMPTYBOOK PASS: False'
    }
}

exit $exitCode
