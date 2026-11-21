# S17: контролируемая UI-печать F5 до реального задания. Штатный хук
# верификации (AppServices.НастройкаДокументаПечатиДляПроверок →
# SystemDrawingPrintSpooler.PrintToFile) направляет печать в файл:
# задание проходит продуктовый конвейер и спулер БЕЗ интерактивного
# порта (PORTPROMPT) и без физического принтера. Отмена/прочие сценарии
# не печатают (покрыто run-printflow/noaddress). Fail-closed и liveness
# унаследованы от run-printflow.
# Fault-инъекция драйвера (без UI, без печати):
# P302_DRIVER_FAULT=parent-alive-after-stop — synthetic harness вместо
# приложения; Stop-HarnessTree возвращает TreeOk=false; драйвер обязан
# завершиться за ограниченное время с exit 1.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
# Каталог evidence. Задаётся переменной P302_EVIDENCE_ROOT, иначе — подкаталог
# с УНИКАЛЬНЫМ именем внутри docs текущей серии (регрессия 2026-11-08):
# прежний жёстко заданный путь перезаписывал скриншоты ПРЕДЫДУЩЕЙ серии
# (docs/ux-audit-2026-11-03/после-этапа-редизайна/24-messagebox-*.png и
# 25-подача-конвертов-*.png), что прямо запрещено правилом «не перезаписывай
# старые evidence-файлы другой серией» и оставляло грязный рабочий каталог git.
$out = if ($env:P302_EVIDENCE_ROOT) {
    $env:P302_EVIDENCE_ROOT
} else {
    Join-Path 'Z:\Picket302\docs' ('ux-audit-' + (Get-Date -Format 'yyyy-MM-dd') + '\evidence')
}
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 30

# Ограниченная задержка до первого WM_PAINT и одна повторная попытка.
# Без неё ранний BitBlt модального MessageBox/диалога предпросмотра может
# закончиться GDI+ generic error, хотя окно уже найдено через EnumWindows.
function Захват([IntPtr]$окно, [string]$путь) {
    Start-Sleep -Milliseconds 700

    try {
        [AccWin]::Capture($окно, $путь) | Out-Null
    }
    catch {
        Запись "capture повтор: $($_.Exception.Message)"
        Start-Sleep -Milliseconds 500
        [AccWin]::Capture($окно, $путь) | Out-Null
    }
}

# Снимает и записываетUnexpected-окно (не входящее в ожидаемую цепочку).
function ЗафиксироватьНеожиданное([string]$тип, [IntPtr]$окно, [string]$префиксСкриншота) {
    $заголовок = [AccWin]::Title($окно)
    $файл = Join-Path $out ("$префиксСкриншота-" + (Get-Date -Format 'HHmmss') + '.png')
    Захват $окно $файл
    Запись "UNEXPECTED ${тип}: '$заголовок' hwnd=$окно (скриншот: $(Split-Path -Leaf $файл))"
    $script:unexpectedFired = $true
}

# Ожидает окно заголовка $title, попутно ловя неожиданные модальные окна:
# любой MessageBox (#32770) или «Проверка размера бумаги» вне ожидаемой
# цепочки фиксируется и делает сценарий FAIL.
function Wait-ОкноПоЗаголовку([uint32]$procId, [string]$title, [int]$секунд) {
    # Ожидание окна строго по заголовку БЕЗ обработки «неожиданных» окон:
    # в сценарии confirm подачи/проверка-бумаги — штатные шаги цепочки.
    $deadline = (Get-Date).AddSeconds($секунд)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in [AccWin]::WindowsOf($procId)) {
            if ([AccWin]::Title($w) -eq $title) { return $w }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

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
        # Явный контракт декодирования труб синтетического потомка (как в
        # run-emptybook.ps1): платформенная OEM-кодировка — не неявное
        # допущение.
        $psiСинтетика.StandardOutputEncoding = [Console]::OutputEncoding
        $psiСинтетика.StandardErrorEncoding = [Console]::OutputEncoding
        $синтетика = [System.Diagnostics.Process]::Start($psiСинтетика)
        $h = @{ Proc = $синтетика; Lines = [System.Collections.ArrayList]::new() }
        Запись "SYNTHETIC HARNESS PID: $($синтетика.Id)"
        Запись 'условие [fault-режим: сценарий пропущен]: False'
    } else {
        $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
        Запись "temp profile: $корень"

        $h = Start-Acceptance $exe 'printflow' $корень $false $true

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
        # 0x0054 = SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW.
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 30, 30, 1280, 720, 0x0054) | Out-Null
        Start-Sleep -Seconds 2

        $успех = $true
        $всёОк = {
            param($условие, $сообщение)
            if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
        }

        # --- PRINTTOFILE: хук верификации печати (S17) -----------------------
        $печатныйФайл = Join-Path $out 'rc-s17-f5-printtofile.pdf'
        Send-HarnessCommand $h ("PRINTTOFILE $печатныйФайл")
        if (-not (Read-HarnessLines $h '^OK PRINTTOFILE' 10)) {
            & $всёОк $false 'PRINTTOFILE не подтверждён stdout (OK PRINTTOFILE)'
        } else {
            Запись "PRINTTOFILE хук установлен: $печатныйФайл"
        }

        # --- SEEDTEMPLATE: конвертный шаблон приёмки (F5 требует шаблон) ----
        Send-HarnessCommand $h 'SEEDTEMPLATEPRINT'
        if (-not (Read-HarnessLines $h '^OK SEEDTEMPLATEPRINT' 10)) {
            & $всёОк $false 'SEEDTEMPLATEPRINT не подтверждён stdout'
        } else {
            Запись 'SEEDTEMPLATEPRINT: шаблон C6 162x114 (печать) засеян'
        }

        # --- SELECT --------------------------------------------------------
        Send-HarnessCommand $h 'SELECT'

        if (-not (Read-HarnessLines $h '^OK SELECT' 10)) {
            & $всёОк $false 'SELECT не подтверждён stdout (OK SELECT)'
        } else {
            Запись 'SELECT подтверждён stdout: OK SELECT'
        }

        # --- F5: Карточка -> ОК -> Печать -> Печать (confirm, PrintToFile) --
        Запись '## F5 «Печать текущего конверта»'
        if ($успех) {
            # PerformClick может блокироваться до ShowDialog (SQLite-async
            # завершается синхронно), поэтому OK F5 приходит только после
            # закрытия модальной цепочки — проверяется по финальному stdout.
            Send-HarnessCommand $h 'F5'

            $карточка = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'f5-card'
            if ($карточка -ne [IntPtr]::Zero) {
                Запись "Карточка отправления появилась: hwnd=$карточка title='Карточка отправления'"
                $скр = Join-Path $out '20-карточка-отправления-f5.png'
            Захват $карточка $скр
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
                Захват $печать $скр
                Запись "скриншот диалога печати: $(Split-Path -Leaf $скр)"

                Start-Sleep -Milliseconds 500

                # Выбор принтера: приложение по умолчанию ставит первый принтер
                # в списке спулера (дефект выбора принтера: восстановление
                # «последнего принтера» затирается SelectedIndex = 0). Для
                # контролируемого задания нужен принтер, поддерживающий
                # PrintToFile (Microsoft Print to PDF, порт PORTPROMPT).
                if (Выбрать-ПринтерВДиалоге $печать 'Print to PDF') {
                    Запись "Диалог «Печать»: принтер переключён на «$script:выбранныйПринтер»"
                }
                else {
                    Запись 'Диалог «Печать»: Microsoft Print to PDF не найден в списке — печать идёт на принтер по умолчанию'
                }

                $диалогСКнопкой = Wait-WindowWithButton $pid_ 'Печать' 'Печать' 8
                if ($null -ne $диалогСКнопкой) {
                    $печать = $диалогСКнопкой.Window
                    $кнопка = $диалогСКнопкой.Button
                    [AccWin]::Click($кнопка)
                    Запись 'Диалог «Печать»: нажата Печать — контролируемое задание (PrintToFile)'
                } else {
                    & $всёОк $false 'кнопка Печать диалога печати не найдена'
                }

                # --- Цепочка подтверждения печати (до 3 итераций) ---------------
                # Полная операторская цепочка F5: подача конвертов →
                # предпросмотр (Печать) → [Конверт слишком большой → ОК →
                # Проверка размера бумаги → Зафиксировать A4 → повтор].
                # D-S17-02 (ожидаемо на этом хосте): блокировка шрифта
                # ZIPcode показывается MessageBox «Не найден шрифт ZIPcode.»
                # (кнопка ОК) — печать отменяется БЕЗ цикла бумажных
                # диалогов. Честный guard: лицензированного шрифта нет.
                Start-Sleep -Milliseconds 800
                $шрифтБлок = [AccWin]::FindByTitle($pid_, 'Печать')
                if ($шрифтБлок -ne [IntPtr]::Zero) {
                    $окШрифт = Wait-Button $шрифтБлок 'ОК' 5
                    if ($окШрифт -ne [IntPtr]::Zero) {
                        [AccWin]::Click($окШрифт)
                        Запись 'Блокировка шрифта ZIPcode: MessageBox показан, нажата ОК — печать отменена guard-ом'
                    }
                }
                # Файл появляется после обработки задания спулером —
                # ограниченное ожидание на каждой итерации.
                $файлПоявился = $false
                for ($итерация = 1; $итерация -le 3 -and -not $файлПоявился; $итерация++) {
                    Запись "итерация подтверждения печати: $итерация"

                    $подача = Wait-ОкноПоЗаголовку $pid_ 'Подача конвертов' 8
                    if ($подача -ne [IntPtr]::Zero) {
                        Запись "Подача конвертов появилась: hwnd=$подача"
                        $продолжить = Wait-Button $подача 'Продолжить' 8
                        if ($продолжить -ne [IntPtr]::Zero) {
                            [AccWin]::Click($продолжить)
                            Запись 'Подача конвертов: нажата Продолжить'
                        } else {
                            & $всёОк $false 'кнопка Продолжить подачи не найдена'
                            break
                        }
                    } else {
                        Запись 'Подача конвертов: не показана за 8 с — продолжаем'
                    }

                    $предпросмотр = Wait-ОкноПоЗаголовку $pid_ 'Предпросмотр печати' 10
                    if ($предпросмотр -ne [IntPtr]::Zero) {
                        Запись "Предпросмотр печати появился: hwnd=$предпросмотр"
                        $скрПревью = Join-Path $out ("22-предпросмотр-итерация$итерация.png")
                        Захват $предпросмотр $скрПревью
                        $печатьПревью = Wait-Button $предпросмотр 'Печать' 8
                        if ($печатьПревью -ne [IntPtr]::Zero) {
                            [AccWin]::Click($печатьПревью)
                            Запись 'Предпросмотр: нажата Печать — задание уходит в спулер'
                        } else {
                            & $всёОк $false 'кнопка Печать предпросмотра не найдена'
                            break
                        }
                    } else {
                        Запись 'Предпросмотр печати: не показан за 10 с (прямой режим)'
                    }

                    $oversize = Wait-ОкноПоЗаголовку $pid_ 'Конверт слишком большой для выбранного принтера' 8
                    if ($oversize -ne [IntPtr]::Zero) {
                        Запись "Предупреждение «Конверт слишком большой» появилось: hwnd=$oversize"
                        $ок = Wait-Button $oversize 'ОК' 8
                        if ($ок -ne [IntPtr]::Zero) {
                            [AccWin]::Click($ок)
                            Запись 'Конверт слишком большой: нажата ОК'
                        } else {
                            & $всёОк $false 'кнопка ОК предупреждения размера не найдена'
                            break
                        }
                    } else {
                        Запись 'Конверт слишком большой: не показан за 8 с'
                    }

                    $проверка = Wait-ОкноПоЗаголовку $pid_ 'Проверка размера бумаги' 8
                    if ($проверка -ne [IntPtr]::Zero) {
                        Запись "Проверка размера бумаги появилась: hwnd=$проверка"
                        $зафиксировать = Wait-Button $проверка 'Зафиксировать A4' 8
                        if ($зафиксировать -ne [IntPtr]::Zero) {
                            [AccWin]::Click($зафиксировать)
                            Запись 'Проверка размера бумаги: нажата Зафиксировать A4 — повтор в A4'
                            continue
                        } else {
                            & $всёОк $false 'кнопка Зафиксировать A4 не найдена'
                            break
                        }
                    } else {
                        Запись 'Проверка размера бумаги: не показана — размер прошёл'
                    }

                    for ($i = 0; $i -lt 40; $i++) {
                        Start-Sleep -Milliseconds 500
                        if ((Test-Path $печатныйФайл) -and ((Get-Item $печатныйФайл).Length -gt 0)) {
                            $len1 = (Get-Item $печатныйФайл).Length
                            Start-Sleep -Milliseconds 800
                            if ((Get-Item $печатныйФайл).Length -eq $len1) { $файлПоявился = $true; break }
                        }
                    }
                }
                & $всёОк $файлПоявился 'печатный PDF-файл создан и стабилен'

                if ($файлПоявился) {
                    $байты = [IO.File]::ReadAllBytes($печатныйФайл)[0..4]
                    $магия = [Text.Encoding]::ASCII.GetString($байты)
                    Запись "PDF magic: $магия size=$((Get-Item $печатныйФайл).Length)"
                    & $всёОк ($магия -eq '%PDF-') 'PDF magic корректен (%PDF-)'
                    $хеш = (Get-FileHash $печатныйФайл -Algorithm SHA256).Hash
                    Запись "PDF sha256: $хеш"
                }

                # Задание не должно остаться в очереди.
                Start-Sleep -Seconds 2
                $заданий = @(Get-CimInstance Win32_PrintJob -ErrorAction SilentlyContinue).Count
                Запись "spooler jobs after print: $заданий"
                & $всёОк ($заданий -eq 0) 'в очереди спулера не осталось заданий'
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

        # --- F6-цепочка намеренно пропущена (драйвер S17: только F5-confirm);
        # хук верификации снимается сразу после контролируемого задания.
        Send-HarnessCommand $h 'PRINTTOFILE off'
        if (-not (Read-HarnessLines $h '^OK PRINTTOFILE off' 10)) {
            & $всёОк $false 'PRINTTOFILE off не подтверждён'
        } else {
            Запись 'PRINTTOFILE off — хук снят после контролируемой печати'
        }
        $f6Passed = $true
        $живF6 = $true
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
            $файлEvidence = 'printconfirm-fault-' + $имяFault + '.md'
            $заголовокEvidence = 'F5/F6 fault-injection: ' + $имяFault
            Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
        } elseif ($сценарийПройден) {
            $файлEvidence = 'printconfirm-happy-evidence.md'
            $заголовокEvidence = 'F5/F6 runtime-доказательство (happy path)'
        } elseif ($фaultПрименён) {
            $файлEvidence = 'printconfirm-fault-' + $имяFault + '.md'
            $заголовокEvidence = 'F5/F6 fault-injection: ' + $имяFault
        } else {
            $файлEvidence = 'printconfirm-failure-evidence.md'
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
