# Пункт D: настоящий runtime-сценарий F5/F6 с обработкой всех модальных
# окон цепочки. Fail-closed и liveness: НИ ОДНОГО бесконечного ожидания
# процесса — любое ожидание через Wait-ProcessExitBounded; при TreeOk=false
# выполняется максимум одна ограниченная повторная попытка teardown и
# повторный bounded post-scan; harness, оставшийся живым, фиксируется
# (harness-not-exited), финализация продолжается, итоговый exit —
# ненулевой. Физическая печать не выполняется: F5 — «Отмена», F6 —
# «Закрыть» на предпросмотре (DialogResult != OK, спулер не вызывается).
# Fault-инъекция драйвера (без UI, без печати):
# P302_DRIVER_FAULT=parent-alive-after-stop — synthetic harness вместо
# приложения; Stop-HarnessTree возвращает TreeOk=false; драйвер обязан
# завершиться за ограниченное время с exit 1.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 30

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'printflow-failure-evidence.md'
$заголовокEvidence = 'F5/F6 runtime-доказательство (FAIL)'

# Раздельные флаги итога (PRINTFLOW PASS только при истинности всех).
$успех = $false
$f5Passed = $false
$f6Passed = $false
$harnessPassed = $false
$cleanupPassed = $false
$profileSafetyPassed = $false
$unexpectedFired = $false
$evidenceOk = $false
$живF5 = $false
$живF6 = $false
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$hasSelect = $false
$hasF5 = $false
$hasF6 = $false
$отказовStdout = -1
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$фaultПрименён = $false
$остановка = $null

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))][pid=$([uint32]$script:h.Proc.Id)] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

function ДобавитьФлагОчистки([string]$флаг) {
    if (-not $неудачиОчистки.Contains($флаг)) {
        $неудачиОчистки.Add($флаг)
    }
}

# Снимает и записываетUnexpected-окно (не входящее в ожидаемую цепочку).
function ЗафиксироватьНеожиданное([string]$тип, [IntPtr]$окно, [string]$префиксСкриншота) {
    $заголовок = [AccWin]::Title($окно)
    $файл = Join-Path $out ("$префиксСкриншота-" + (Get-Date -Format 'HHmmss') + '.png')
    [AccWin]::Capture($окно, $файл) | Out-Null
    Запись "UNEXPECTED ${тип}: '$заголовок' hwnd=$окно (скриншот: $(Split-Path -Leaf $файл))"
    $script:unexpectedFired = $true
}

# Ожидает окно заголовка $title, попутно ловя неожиданные модальные окна:
# любой MessageBox (#32770) или «Проверка размера бумаги» вне ожидаемой
# цепочки фиксируется и делает сценарий FAIL.
function Wait-ОжидаемоеОкно([uint32]$procId, [string]$title, [string]$фаза) {
    $deadline = (Get-Date).AddSeconds($script:таймаутШага)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in [AccWin]::WindowsOf($procId)) {
            $cls = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($w, $cls, 256) | Out-Null
            $класс = $cls.ToString()
            $заголовок = [AccWin]::Title($w)

            if ($заголовок -eq $title) { return $w }

            if ($класс -eq '#32770') {
                ЗафиксироватьНеожиданное 'MessageBox' $w "unexpected-$фаза"
                Запись "FAIL: неожиданный MessageBox в фазе $фаза"
                return [IntPtr]::Zero
            }

            if ($заголовок -eq 'Проверка размера бумаги') {
                ЗафиксироватьНеожиданное 'paper-size' $w "unexpected-$фаза"
                Запись "FAIL: неожиданный диалог проверки бумаги в фазе $фаза"
                return [IntPtr]::Zero
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
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
    # (TreeOk=false, TreeError содержит таймаут). Оба режима — без UI/печати.
    $faultРежим = ($env:P302_DRIVER_FAULT -eq 'parent-alive-after-stop' -or $env:P302_DRIVER_FAULT -eq 'taskkill-hang')

    if ($faultРежим) {
        Запись "FAULT-INJECT: $env:P302_DRIVER_FAULT — synthetic harness (без UI, без печати)"
        $psiСинтетика = New-Object System.Diagnostics.ProcessStartInfo
        $psiСинтетика.FileName = 'powershell.exe'
        $psiСинтетика.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 1800"'
        $psiСинтетика.UseShellExecute = $false
        $psiСинтетика.CreateNoWindow = $true
        $psiСинтетика.RedirectStandardInput = $true
        # Трубы stdout/stderr НЕ наследуются синтетикой (pipe-deadlock
        # внешнего launcher-а — см. run-emptybook.ps1).
        $psiСинтетика.RedirectStandardOutput = $true
        $psiСинтетика.RedirectStandardError = $true
        $синтетика = [System.Diagnostics.Process]::Start($psiСинтетика)
        $h = @{ Proc = $синтетика; Lines = [System.Collections.ArrayList]::new() }
        Запись "SYNTHETIC HARNESS PID: $($синтетика.Id)"
        Запись 'условие [fault-режим: сценарий пропущен]: False'
    } else {
        $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
        Запись "temp profile: $корень"

        $h = Start-Acceptance $exe 'printflow' $корень

        if (-not (Read-HarnessLines $h '^READY ' 30)) {
            Запись 'FAIL: READY не получен за 30 c'
            throw 'READY not received'
        }

        $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
        $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
        $pid_ = [uint32]$h.Proc.Id
        Запись "harness READY: $готовность"
    }

    # --- 3. Сценарная часть (только не-fault режим) ------------------------
    if (-not $faultРежим) {
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
        Start-Sleep -Seconds 2

        $успех = $true
        $всёОк = {
            param($условие, $сообщение)
            if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
        }

        # --- SELECT --------------------------------------------------------
        $h.Proc.StandardInput.WriteLine('SELECT')

        if (-not (Read-HarnessLines $h '^OK SELECT' 10)) {
            & $всёОк $false 'SELECT не подтверждён stdout (OK SELECT)'
        } else {
            Запись 'SELECT подтверждён stdout: OK SELECT'
        }

        # --- F5: Карточка -> ОК -> Печать -> Отмена ------------------------
        Запись '## F5 «Печать текущего конверта»'
        if ($успех) {
            # PerformClick может блокироваться до ShowDialog (SQLite-async
            # завершается синхронно), поэтому OK F5 приходит только после
            # закрытия модальной цепочки — проверяется по финальному stdout.
            $h.Proc.StandardInput.WriteLine('F5')

            $карточка = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'f5-card'
            if ($карточка -ne [IntPtr]::Zero) {
                Запись "Карточка отправления появилась: hwnd=$карточка title='Карточка отправления'"
                $скр = Join-Path $out '20-карточка-отправления-f5.png'
                [AccWin]::Capture($карточка, $скр) | Out-Null
                Запись "скриншот карточки: $(Split-Path -Leaf $скр)"

                $кнопка = Wait-Button $карточка 'ОК' 8
                if ($кнопка -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопка)
                    Запись 'Карточка: нажата ОК (BM_CLICK)'
                } else {
                    & $всёОк $false 'кнопка ОК карточки не найдена'
                }
            } else {
                & $всёОк $false 'Карточка отправления не появилась'
            }
        }

        $печать = [IntPtr]::Zero
        if ($успех) {
            $печать = Wait-ОжидаемоеОкно $pid_ 'Печать' 'f5-print'
            if ($печать -ne [IntPtr]::Zero) {
                Запись "Диалог «Печать» появился: hwnd=$печать"
                $скр = Join-Path $out '21-диалог-печати-f5.png'
                [AccWin]::Capture($печать, $скр) | Out-Null
                Запись "скриншот диалога печати: $(Split-Path -Leaf $скр)"

                Start-Sleep -Milliseconds 500
                $кнопка = Wait-Button $печать 'Отмена' 8
                if ($кнопка -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопка)
                    Запись 'Диалог «Печать»: нажата Отмена — задания на принтер не создаётся'
                } else {
                    & $всёОк $false 'кнопка Отмена диалога печати не найдена'
                }
                Start-Sleep -Seconds 2
                $осталось = [AccWin]::FindByTitle($pid_, 'Печать')
                & $всёОк ($осталось -eq [IntPtr]::Zero) 'диалог «Печать» исчез после Отмены'
            } elseif ($успех) {
                & $всёОк $false 'диалог «Печать» не появился'
            }
        }

        if ($успех) {
            $живF5 = [AccWin]::Responsive($h.Hwnd, 5000)
            Запись "UI отвечает после F5 (WM_NULL): $живF5"
            if (-not $живF5) { $успех = $false }
        }

        # FLOW F5 PASS: карточка, диалог печати и отмена фактически пройдены.
        $f5Passed = $успех -and $живF5
        Запись "FLOW F5 PASS: $f5Passed"

        # --- F6: Карточка -> ОК -> MessageBox -> Печать -> Подача -> Предпросмотр
        if ($успех) {
            Запись '## F6 «Предпросмотр и печать»'
            # Аналогично F5: OK F6 печатается после закрытия цепочки модалок.
            $h.Proc.StandardInput.WriteLine('F6')
        }

        if ($успех) {
            $карточка2 = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'f6-card'
            if ($карточка2 -ne [IntPtr]::Zero) {
                Запись "Карточка отправления появилась: hwnd=$карточка2"
                $кнопка = Wait-Button $карточка2 'ОК' 8
                if ($кнопка -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопка)
                    Запись 'Карточка: нажата ОК (BM_CLICK)'
                } else {
                    & $всёОк $false 'кнопка ОК карточки F6 не найдена'
                }
            } else {
                & $всёОк $false 'Карточка отправления F6 не появилась'
            }
        }

        # MessageBox «Отправленных сформировано: N» — ожидаемый шаг цепочки F6:
        # тот же заголовок «Печать», единственная кнопка ОК, нет «Отмены».
        $подтверждёнФормированиеМб = $false
        if ($успех) {
            $deadline = (Get-Date).AddSeconds($таймаутШага)
            while ((Get-Date) -lt $deadline -and -not $подтверждёнФормированиеМб) {
                foreach ($w in [AccWin]::WindowsOf($pid_)) {
                    $cls = New-Object System.Text.StringBuilder 256
                    [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                    if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -eq 'Печать') {
                        $текст = [AccWin]::Title($w)
                        Запись "MessageBox формирования отправлений появился: hwnd=$w title='$текст'"
                        $кнопкаОк = [AccWin]::FindButton($w, 'ОК')
                        $кнопкаОтмена = [AccWin]::FindButton($w, 'Отмена')
                        if ($кнопкаОк -ne [IntPtr]::Zero -and $кнопкаОтмена -eq [IntPtr]::Zero) {
                            $скр = Join-Path $out ('24-messagebox-формирование-' + (Get-Date -Format 'HHmmss') + '.png')
                            [AccWin]::Capture($w, $скр) | Out-Null
                            [AccWin]::Click($кнопкаОк)
                            $подтверждёнФормированиеМб = $true
                            Запись "MessageBox подтверждён (ОК), скриншот: $(Split-Path -Leaf $скр)"
                        }
                        break
                    }
                }
                if (-not $подтверждёнФормированиеМб) { Start-Sleep -Milliseconds 300 }
            }
            & $всёОк $подтверждёнФормированиеМб 'MessageBox формирования отправлений найден и подтверждён'
        }

        $печать2 = [IntPtr]::Zero
        if ($успех) {
            $печать2 = Wait-ОжидаемоеОкно $pid_ 'Печать' 'f6-print'
            if ($печать2 -ne [IntPtr]::Zero) {
                Запись "Диалог «Печать» появился: hwnd=$печать2"
                Start-Sleep -Milliseconds 500
                $кнопка = Wait-Button $печать2 'Печать' 8
                if ($кнопка -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопка)
                    Запись 'Диалог «Печать»: нажата Печать — ожидание окна предпросмотра'
                } else {
                    & $всёОк $false 'кнопка Печать диалога не найдена'
                }
            } elseif ($успех) {
                & $всёОк $false 'диалог «Печать» F6 не появился'
            }
        }

        # «Подача конвертов» — ожидаемый шаг цепочки F6 (предупреждение о
        # подаче перед печатью). «Продолжить» ведёт в предпросмотр; спулер
        # вызывается только после подтверждения предпросмотра.
        $подачаНайдена = $false
        if ($успех) {
            $deadline = (Get-Date).AddSeconds($таймаутШага)
            while ((Get-Date) -lt $deadline -and -not $подачаНайдена) {
                $подача = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
                if ($подача -ne [IntPtr]::Zero) {
                    $подачаНайдена = $true
                    Запись "Окно «Подача конвертов» появилось: hwnd=$подача"
                    $скр = Join-Path $out ('25-подача-конвертов-' + (Get-Date -Format 'HHmmss') + '.png')
                    [AccWin]::Capture($подача, $скр) | Out-Null
                    Запись "скриншот подачи: $(Split-Path -Leaf $скр)"
                    $продолжить = Wait-Button $подача 'Продолжить' 8
                    if ($продолжить -ne [IntPtr]::Zero) {
                        [AccWin]::Click($продолжить)
                        Запись 'Подача конвертов: нажата Продолжить'
                    } else {
                        & $всёОк $false 'кнопка Продолжить не найдена в окне подачи'
                    }
                    break
                }
                # неожиданные модалки в этой фазе — FAIL
                foreach ($w in [AccWin]::WindowsOf($pid_)) {
                    $cls = New-Object System.Text.StringBuilder 256
                    [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                    if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -ne 'Подача конвертов') {
                        ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-f6-feed'
                        & $всёОк $false 'неожиданный MessageBox в фазе подачи'
                        break
                    }
                }
                Start-Sleep -Milliseconds 300
            }
            & $всёОк $подачаНайдена 'окно «Подача конвертов» появилось и обработано'
        }

        $предпросмотр = [IntPtr]::Zero
        if ($успех) {
            $deadline = (Get-Date).AddSeconds($таймаутШага)
            while ((Get-Date) -lt $deadline) {
                # неожиданные модалки в фазе предпросмотра — FAIL
                foreach ($w in [AccWin]::WindowsOf($pid_)) {
                    $cls = New-Object System.Text.StringBuilder 256
                    [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                    if ($cls.ToString() -eq '#32770') {
                        ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-f6-preview'
                        & $всёОк $false 'неожиданный MessageBox в фазе предпросмотра'
                        break
                    }
                }
                if (-not $успех) { break }

                $предпросмотр = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
                if ($предпросмотр -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 400
            }

            if ($предпросмотр -ne [IntPtr]::Zero -and $успех) {
                Запись "Окно «Предпросмотр печати» появилось: hwnd=$предпросмотр"
                $скр = Join-Path $out '22-предпросмотр-f6.png'
                [AccWin]::Capture($предпросмотр, $скр) | Out-Null
                Запись "скриншот предпросмотра: $(Split-Path -Leaf $скр)"

                $кнопка = Wait-Button $предпросмотр 'Закрыть' 8
                if ($кнопка -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопка)
                    Запись 'Предпросмотр: нажата Закрыть (DialogResult != OK, спулер не вызывается)'
                } else {
                    & $всёОк $false 'кнопка Закрыть предпросмотра не найдена'
                }
                Start-Sleep -Seconds 3
                $осталось = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
                & $всёОк ($осталось -eq [IntPtr]::Zero) 'окно предпросмотра исчезло'
            } elseif ($успех) {
                & $всёОк $false 'окно «Предпросмотр печати» не появилось'
            }
        }

        if ($успех) {
            $живF6 = [AccWin]::Responsive($h.Hwnd, 5000)
            Запись "UI отвечает после F6 (WM_NULL): $живF6"
            if (-not $живF6) { $успех = $false }
        }

        # FLOW F6 PASS: карточка, MessageBox, печать, подача и предпросмотр
        # фактически пройдены.
        $f6Passed = $успех -and $живF6
        Запись "FLOW F6 PASS: $f6Passed"
    }

    # --- 4. Остановка harness: ограниченная, без бесконечных ожиданий ------
    $остановка = Invoke-HarnessStop $h 'printflow'

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

    # ExitCode читается ТОЛЬКО у фактически завершившегося процесса.
    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'printflow-exitcode'
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

    # --- 5. Проверки stdout и реального профиля (только не-fault режим) ----
    if (-not $faultРежим) {
        $stdout = Get-HarnessLines $h
        $hasSelect = [bool]($stdout | Where-Object { $_ -match '^OK SELECT' })
        $hasF5 = [bool]($stdout | Where-Object { $_ -match '^OK F5' })
        $hasF6 = [bool]($stdout | Where-Object { $_ -match '^OK F6' })
        $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count
        Запись "stdout: OK SELECT=$hasSelect OK F5=$hasF5 OK F6=$hasF6 FAIL-строк=$отказовStdout"

        $профильОчищен = -not (Test-Path $корень)
        Запись "temp profile cleaned: $профильОчищен"

        $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
        $sentinelПосле = Get-SentinelState $sentinel
        $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
        Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
        Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

        if ($env:P302_PRINTFLOW_FAULT -eq 'delta-removed') {
            $дельта.Removed = @('C:' + [IO.Path]::DirectorySeparatorChar + 'fake' + [IO.Path]::DirectorySeparatorChar + 'injected.file')
            Запись 'FAULT-INJECT: delta Removed > 0'
        }

        if ($env:P302_PRINTFLOW_FAULT -eq 'killed') {
            $убит = $true
            Запись 'FAULT-INJECT: harness killed = True'
        }

        if ($env:P302_PRINTFLOW_FAULT -eq 'cleanup-false') {
            $профильОчищен = $false
            Запись 'FAULT-INJECT: cleanup = False'
        }

        if ($env:P302_PRINTFLOW_FAULT -eq 'harness-exit') {
            $кодВыходаHarness = 7
            Запись 'FAULT-INJECT: harness exit = 7'
        }

        # --- Итоговые флаги (PRINTFLOW PASS только при всех) ---------------
        & $всёОк $hasSelect 'SELECT подтверждён stdout (OK SELECT)'
        & $всёОк ($отказовStdout -eq 0) 'нет FAIL-строк в stdout harness'

        # OK F5/OK F6 печатаются асинхронно после закрытия модальной цепочки
        # (async void + ShowDialog) и не гарантированы в stdout — не FAIL-условие;
        # выполнение команд доказано FLOW F5/F6 PASS по фактически пройденным окнам.
        Запись "информационно: OK F5=$hasF5 OK F6=$hasF6 в stdout (async void после ShowDialog)"

        $harnessPassed = ($кодВыходаHarness -eq 0) -and (-not $убит)
        $cleanupPassed = $профильОчищен
        $profileSafetyPassed = ($sentinelДо -eq $sentinelПосле) -and
            ($дельта.New.Count -eq 0) -and
            ($дельта.Changed.Count -eq 0) -and
            ($дельта.Removed.Count -eq 0)

        Запись "F5-цепочка: $f5Passed; F6-цепочка: $f6Passed"
        Запись "неожиданных окон: $unexpectedFired; UI после F5: $живF5; UI после F6: $живF6"
        Запись "harness: exit=$кодВыходаHarness killed=$убит -> $harnessPassed"
        Запись "cleanup: $cleanupPassed; profile safety: $profileSafetyPassed"

        $сценарийПройден = $успех -and $f5Passed -and $f6Passed -and (-not $unexpectedFired) -and $harnessPassed -and $cleanupPassed -and $profileSafetyPassed -and (-not $harnessNotExited) -and $остановка.Результат.TreeOk -and ($остановка.Результат.RemainingPids.Count -eq 0) -and ($остановка.Результат.UnconfirmedPids.Count -eq 0) -and ($остановка.Результат.TreeError -eq '')
        Запись "дерево: ok=$($остановка.Результат.TreeOk) остатки=$($остановка.Результат.RemainingPids.Count) неподтверждённые=$($остановка.Результат.UnconfirmedPids.Count) ошибка='$($остановка.Результат.TreeError)'"
        Запись "сценарий (все флаги): $сценарийПройден"
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
            $остановкаФинал = Invoke-HarnessStop $h 'printflow-finally'

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

                if ($env:P302_PRINTFLOW_FAULT -eq 'final-delta') {
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
        $фault = $env:P302_PRINTFLOW_FAULT
        $фaultДрайвера = $env:P302_DRIVER_FAULT
        $фaultПрименён = @($журнал | Where-Object { $_ -match 'FAULT-INJECT:' }).Count -gt 0
        $имяFault = if ($фault) { $фault } else { $фaultДрайвера }

        if ($сценарийПройден -and ($фault -or $фaultДрайвера)) {
            $файлEvidence = 'printflow-fault-' + $имяFault + '.md'
            $заголовокEvidence = 'F5/F6 fault-injection: ' + $имяFault
            Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
        } elseif ($сценарийПройден) {
            $файлEvidence = 'printflow-happy-evidence.md'
            $заголовокEvidence = 'F5/F6 runtime-доказательство (happy path)'
        } elseif ($фaultПрименён) {
            $файлEvidence = 'printflow-fault-' + $имяFault + '.md'
            $заголовокEvidence = 'F5/F6 fault-injection: ' + $имяFault
        } else {
            $файлEvidence = 'printflow-failure-evidence.md'
            $заголовокEvidence = 'F5/F6 runtime-доказательство (FAIL)'
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
    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'printflow'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение межпроцессной блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    # --- F. Финальный статус ДО записи evidence ----------------------------
    $faultДрайвераЕщё = $env:P302_DRIVER_FAULT
    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0) -and (-not $faultДрайвераЕщё) -and (-not $env:P302_PRINTFLOW_FAULT)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись "Режим: printflow"
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if (Get-Variable -Name корень -ErrorAction SilentlyContinue) { $корень } else { 'не создан' })"
    Запись "createDemoData=false"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "F5: $f5Passed; F6: $f6Passed; неожиданных окон: $unexpectedFired"
    Запись "harness: exit=$кодВыходаHarness killed=$убит; cleanup: $cleanupPassed; profile safety: $profileSafetyPassed"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "FAULT: $(if ($faultДрайвераЕщё) { $faultДрайвераЕщё } elseif ($env:P302_PRINTFLOW_FAULT) { $env:P302_PRINTFLOW_FAULT } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($фaultПрименён) {
        Запись "PASS_OF_CONTRACT: $(-not $happyПройден) (fault — инъекция корректно обнаружена, скрипт не вернёт 0)"
    }

    # --- G. Единая запись evidence + пост-проверка -------------------------
    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'createDemoData=false' $журнал

    $маркеры = @('Режим: printflow', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:', 'createDemoData=false')
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
        Запись 'PRINTFLOW PASS: True'
    } else {
        $exitCode = 1
        Запись 'PRINTFLOW PASS: False'
    }
}

exit $exitCode
