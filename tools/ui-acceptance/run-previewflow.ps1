# Сценарий предпросмотра с реальными полями бланка (доказательство
# рендеринга 2026-11-08). Отличия от run-printflow.ps1:
# - в шаблон приёмки добавляются реальные BlankField: [edt_str_to1],
#   [edt_str_to2], [edtIndeks_to] (команда harness SEEDFIELDS);
# - после появления «Предпросмотр печати» выполняется команда PREVIEWSCAN:
#   пиксельный скан страницы предпросмотра — белый пустой холст (dark≈0)
#   PASS-ом НЕ считается;
# - после Закрыть проверяется отсутствие заданий в спулере
#   (Win32_PrintJob) — закрытие предпросмотра не создаёт печатное задание.
# Fail-closed и liveness: как в остальных драйверах — все ожидания
# ограниченные, дерево процессов проверяется, evidence пишется один раз.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = if ($env:P302_EVIDENCE_ROOT) { $env:P302_EVIDENCE_ROOT } else { 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна' }
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 30

# Пороговые значения пиксельного скана (4 px/мм): текст адресата в поле
# 80×8 мм и штамп индекса в поле 40×12 мм обязаны дать более 30 тёмных
# пикселей каждый; страница целиком — более 200. Пустой белый холст даёт
# значения около нуля и ловится этими порогами.
$порогТёмныхВсего = 200
$порогТёмныхВПоле = 30

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'previewflow-failure-evidence.md'
$заголовокEvidence = 'Предпросмотр с полями бланка: runtime-доказательство (FAIL)'
$спулерДо = -1
$спулерПосле = -1
$спулерОшибка = ''
$кодВыходаHarness = $null
$harnessNotExited = $false
$убит = $false
$unexpectedFired = $false
$живПредпросмотр = $false
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$остановка = $null

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))][pid=$(if ($script:h -ne $null -and $script:h.Proc -ne $null) { try { $script:h.Proc.Id } catch { '?' } } else { '?' })] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

function ДобавитьФлагОчистки([string]$флаг) {
    if (-not $неудачиОчистки.Contains($флаг)) {
        $неудачиОчистки.Add($флаг)
    }
}

function ЗафиксироватьНеожиданное([string]$тип, [IntPtr]$окно, [string]$префиксСкриншота) {
    $заголовок = [AccWin]::Title($окно)
    $файл = Join-Path $out ("$префиксСкриншота-" + (Get-Date -Format 'HHmmss') + '.png')
    [AccWin]::Capture($окно, $файл) | Out-Null
    Запись "UNEXPECTED ${тип}: '$заголовок' hwnd=$окно (скриншот: $(Split-Path -Leaf $файл))"
    $script:unexpectedFired = $true
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
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

# Число заданий в спулере на момент вызова. Ошибка CIM — не «ноль заданий»,
# а явная ошибка (fail-closed): отсутствие доказательства ≠ доказательство
# отсутствия.
function Get-SpoolerJobCount {
    try {
        $задания = @(Get-CimInstance -ClassName Win32_PrintJob -ErrorAction Stop)
        return @{ Count = $задания.Count; Error = '' }
    } catch {
        return @{ Count = -1; Error = $_.Exception.Message }
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

    # --- 2. Запуск harness (printflow: отправитель+получатель с адресами,
    #        шаблон приёмки; поля добавляются командой SEEDFIELDS) ----------
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

    # --- 3. Реальные поля бланка в шаблоне приёмки ------------------------
    Send-HarnessCommand $h 'SEEDFIELDS'

    if (-not (Read-HarnessLines $h '^OK SEEDFIELDS' 10)) {
        Запись 'FAIL: SEEDFIELDS не подтверждён (OK SEEDFIELDS не получен)'
        throw 'SEEDFIELDS not confirmed'
    }

    Запись 'SEEDFIELDS подтверждён: [edt_str_to1], [edt_str_to2], [edtIndeks_to] заданы'

    # --- 4. Снимок спулера ДО цепочки --------------------------------------
    $спулерСнимокДо = Get-SpoolerJobCount
    $спулерДо = $спулерСнимокДо.Count
    $спулерОшибка = $спулерСнимокДо.Error
    Запись "заданий в спулере до: $спулерДо (ошибка: '$спулерОшибка')"

    # --- 5. Цепочка F6: Карточка -> ОК -> MessageBox -> Печать -> Подача -> Предпросмотр
    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    Send-HarnessCommand $h 'SELECTLAST'

    if (-not (Read-HarnessLines $h '^OK SELECTLAST' 10)) {
        & $всёОк $false 'SELECTLAST не подтверждён stdout (OK SELECTLAST)'
    } else {
        $выбранная = (Get-HarnessLines $h | Where-Object { $_ -match '^OK SELECTLAST ' } | Select-Object -First 1)
        Запись "SELECTLAST подтверждён: $выбранная"
        & $всёОк ($выбранная -match 'Получатель') 'выбрана запись получателя (не отправителя)'
    }

    if ($успех) {
        Запись '## F6 «Предпросмотр и печать»'
        Send-HarnessCommand $h 'F6'
    }

    if ($успех) {
        $карточка = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'pf-card'
        if ($карточка -ne [IntPtr]::Zero) {
            Запись "Карточка отправления появилась: hwnd=$карточка"
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

    $подтверждёнФормированиеМб = $false
    if ($успех) {
        $deadline = (Get-Date).AddSeconds($таймаутШага)
        while ((Get-Date) -lt $deadline -and -not $подтверждёнФормированиеМб) {
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -eq 'Печать') {
                    $кнопкаОк = [AccWin]::FindButton($w, 'ОК')
                    $кнопкаОтмена = [AccWin]::FindButton($w, 'Отмена')
                    if ($кнопкаОк -ne [IntPtr]::Zero -and $кнопкаОтмена -eq [IntPtr]::Zero) {
                        $скр = Join-Path $out ('31-messagebox-формирование-' + (Get-Date -Format 'HHmmss') + '.png')
                        [AccWin]::Capture($w, $скр) | Out-Null
                        [AccWin]::Click($кнопкаОк)
                        $подтверждёнФормированиеМб = $true
                        Запись "MessageBox формирования подтверждён (ОК), скриншот: $(Split-Path -Leaf $скр)"
                    }
                    break
                }
            }
            if (-not $подтверждёнФормированиеМб) { Start-Sleep -Milliseconds 300 }
        }
        & $всёОк $подтверждёнФормированиеМб 'MessageBox формирования отправлений найден и подтверждён'
    }

    if ($успех) {
        $печать = Wait-ОжидаемоеОкно $pid_ 'Печать' 'pf-print'
        if ($печать -ne [IntPtr]::Zero) {
            Запись "Диалог «Печать» появился: hwnd=$печать"
            Start-Sleep -Milliseconds 500
            $кнопка = Wait-Button $печать 'Печать' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Диалог «Печать»: нажата Печать — ожидание окна подачи'
            } else {
                & $всёОк $false 'кнопка Печать диалога не найдена'
            }
        } elseif ($успех) {
            & $всёОк $false 'диалог «Печать» не появился'
        }
    }

    $подачаНайдена = $false
    if ($успех) {
        $deadline = (Get-Date).AddSeconds($таймаутШага)
        while ((Get-Date) -lt $deadline -and -not $подачаНайдена) {
            $подача = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
            if ($подача -ne [IntPtr]::Zero) {
                $подачаНайдена = $true
                Запись "Окно «Подача конвертов» появилось: hwnd=$подача"
                $продолжить = Wait-Button $подача 'Продолжить' 8
                if ($продолжить -ne [IntPtr]::Zero) {
                    [AccWin]::Click($продолжить)
                    Запись 'Подача конвертов: нажата Продолжить'
                } else {
                    & $всёОк $false 'кнопка Продолжить не найдена в окне подачи'
                }
                break
            }
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -ne 'Подача конвертов') {
                    ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-pf-feed'
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
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770') {
                    ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-pf-preview'
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
        } elseif ($успех) {
            & $всёОк $false 'окно «Предпросмотр печати» не появилось'
        }
    }

    # --- 6. Пиксельный скан страницы предпросмотра -------------------------
    $скан = $null
    $поляСкан = @()
    if ($предпросмотр -ne [IntPtr]::Zero -and $успех) {
        Send-HarnessCommand $h 'PREVIEWSCAN'

        if (-not (Read-HarnessLines $h '^PREVIEW-SCAN ' 15)) {
            & $всёОк $false 'PREVIEW-SCAN не получен от harness'
        }

        # Поля печатаются сразу после строки PREVIEW-SCAN в том же
        # обработчике команды; читаем до 6 строк PREVIEW-FIELD.
        for ($попытка = 0; $попытка -lt 6; $попытка++) {
            if (-not (Read-HarnessLines $h '^PREVIEW-FIELD ' 5)) { break }
        }

        $скан = Get-HarnessLines $h | Where-Object { $_ -match '^PREVIEW-SCAN ' } | Select-Object -First 1
        $поляСкан = @(Get-HarnessLines $h | Where-Object { $_ -match '^PREVIEW-FIELD ' })
        Запись "PREVIEW-SCAN: $скан"
        foreach ($строкаПоля in $поляСкан) {
            Запись "  $строкаПоля"
        }

        $скр = Join-Path $out '32-предпросмотр-с-полями.png'
        [AccWin]::Capture($предпросмотр, $скр) | Out-Null
        Start-Sleep -Milliseconds 300
        $скриншотРазмер = 0
        if (Test-Path $скр) { $скриншотРазмер = (Get-Item $скр).Length }
        Запись "скриншот предпросмотра: $скр ($скриншотРазмер байт)"

        # --- 7. Закрытие предпросмотра без задания на печать ---------------
        # После PREVIEWSCAN UI-поток может быть ещё занят (скан исполнялся
        # через BeginInvoke на UI-потоке поверх модального окна): ограниченно
        # ждём отзывчивости окна (до 10 c) перед поиском кнопки.
        $окноОтвечает = $false
        $дедлайнКнопки = (Get-Date).AddSeconds(10)

        while ((Get-Date) -lt $дедлайнКнопки) {
            if ([AccWin]::Responsive($предпросмотр, 5000)) {
                $окноОтвечает = $true
                break
            }
            Start-Sleep -Milliseconds 500
        }

        Запись "окно предпросмотра отзывчиво перед Закрыть: $окноОтвечает"

        $кнопка = if ($окноОтвечает) { Wait-Button $предпросмотр 'Закрыть' 8 } else { [IntPtr]::Zero }
        if ($кнопка -ne [IntPtr]::Zero) {
            [AccWin]::Click($кнопка)
            Запись 'Предпросмотр: нажата Закрыть (DialogResult != OK)'
        } else {
            & $всёОк $false 'кнопка Закрыть предпросмотра не найдена'
        }

        Start-Sleep -Seconds 3
        $осталось = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
        & $всёОк ($осталось -eq [IntPtr]::Zero) 'окно предпросмотра исчезло после Закрыть'

        # Отзывчивость сразу после сворачивания модального окна: UI-поток
        # может быть ещё занят разбором ShowDialog и перезагрузкой таблиц
        # (ЗагрузитьОтправленныеAsync), поэтому проверка — ограниченным
        # опросом до 10 c (все попытки WM_NULL с таймаутом 5 c, суммарный
        # бюджет жёстко ограничен).
        $живПредпросмотр = $false
        $дедлайнОтзыва = (Get-Date).AddSeconds(10)

        while ((Get-Date) -lt $дедлайнОтзыва) {
            if ([AccWin]::Responsive($h.Hwnd, 5000)) {
                
                $живПредпросмотр = $true
                break
            }
            Start-Sleep -Milliseconds 500
        }

        Запись "UI отвечает после закрытия предпросмотра (WM_NULL): $живПредпросмотр"
        if (-not $живПредпросмотр) { $успех = $false }
    }

    # --- 8. Спулер ПОСЛЕ закрытия предпросмотра ---------------------------
    $спулерСнимокПосле = Get-SpoolerJobCount
    $спулерПосле = $спулерСнимокПосле.Count
    if (-not $спулерСнимокПосле.Error) { $спулерОшибка = '' }
    Запись "заданий в спулере после: $спулерПосле (ошибка: '$($спулерСнимокПосле.Error)')"

    # --- 9. Остановка harness: ограниченная --------------------------------
    $остановка = Invoke-HarnessStop $h 'previewflow'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) TreeConfirmed=$($остановка.Результат.TreeConfirmed) остатки=[$($остановка.Результат.RemainingPids -join ';')] неподтверждённые=[$($остановка.Результат.UnconfirmedPids -join ';')] taskkill=$($остановка.Результат.TaskKillExitCode) ошибка: $($остановка.Результат.TreeError)"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'previewflow-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode

        if ($ожиданиеВыхода.Диагностика) {
            Запись "BOUNDED-WAIT: $($ожиданиеВыхода.Диагностика)"
        }
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
        Запись 'SAFETY-FAIL: harness не завершён после остановки (HarnessNotExited)'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

    # --- 10. Проверки stdout и реального профиля ---------------------------
    $stdout = Get-HarnessLines $h
    $профильОчищен = -not (Test-Path $корень)
    Запись "temp profile cleaned: $профильОчищен"

    # Разбор результатов скана
    $dimsOk = ($скан -match 'dimsOk=true')
    $тёмныхВсего = if ($скан -match 'dark=(\d+)') { [int]$Matches[1] } else { -1 }
    $внеПечатной = if ($скан -match 'outsidePrintable=(\d+)') { [int]$Matches[1] } else { -1 }

    function ТёмныхВПоле([string]$имяПоля) {
        foreach ($строкаПоля in $script:поляСкан) {
            if ($строкаПоля -match ('^PREVIEW-FIELD name=' + [regex]::Escape($имяПоля) + ' kind=\S+ dark=(\d+)')) {
                return [int]$Matches[1]
            }
        }
        return -1
    }

    $тёмныхТо1 = ТёмныхВПоле '[edt_str_to1]'
    $тёмныхТо2 = ТёмныхВПоле '[edt_str_to2]'
    $тёмныхИндекс = ТёмныхВПоле '[edtIndeks_to]'

    $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count
    Запись "stdout: FAIL-строк=$отказовStdout"
    Запись "скан: dimsOk=$dimsOk dark=$тёмныхВсего outsidePrintable=$внеПечатной to1=$тёмныхТо1 to2=$тёмныхТо2 индекс=$тёмныхИндекс"

    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

    # --- 11. Условия happy-path ---------------------------------------------
    $условия = [ordered]@{
        'SEEDFIELDS подтверждён' = ($null -ne (Get-HarnessLines $h | Where-Object { $_ -match '^OK SEEDFIELDS' }))
        'цепочка F6 пройдена' = $успех
        'предпросмотр открыт' = ($предпросмотр -ne [IntPtr]::Zero)
        'PREVIEW-SCAN получен' = ($null -ne $скан)
        'размер холста точен (не обрезан)' = $dimsOk
        'страницы предпросмотра есть' = ($скан -match 'страниц=[1-9]')
        'тёмных пикселей достаточно (не белый холст)' = ($тёмныхВсего -ge $порогТёмныхВсего)
        'текст адресата строка 1 виден' = ($тёмныхТо1 -ge $порогТёмныхВПоле)
        'текст адресата строка 2 виден' = ($тёмныхТо2 -ge $порогТёмныхВПоле)
        'индекс виден (штамп отрисован)' = ($тёмныхИндекс -ge $порогТёмныхВПоле)
        'нет тёмных пикселей вне печатной области' = ($внеПечатной -eq 0)
        'UI отвечает после закрытия' = $живПредпросмотр
        'нет FAIL в stdout' = ($отказовStdout -eq 0)
        'нет неожиданных окон' = (-not $unexpectedFired)
        'спулер до: доказано' = ($спулерДо -ge 0)
        'спулер после: доказано' = ($спулерПосле -ge 0)
        'закрытие preview не создало задание' = (($спулерДо -eq $спулерПосле) -and ($спулерПосле -eq 0))
        'временный профиль удалён' = $профильОчищен
        'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
        'новых файлов нет' = ($дельта.New.Count -eq 0)
        'изменённых файлов нет' = ($дельта.Changed.Count -eq 0)
        'удалённых файлов нет' = ($дельта.Removed.Count -eq 0)
        'harness exit 0' = ($кодВыходаHarness -eq 0)
        'harness не убит' = (-not $убит)
        'harness завершён (bounded)' = (-not $harnessNotExited)
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
catch {
    Запись "EXCEPTION: $($_.Exception.Message)"
    $сценарийПройден = $false
}
finally {
    try {
        # --- A. Аварийная ограниченная остановка, если harness ещё жив ----
        if ($h -ne $null -and $h.Proc -ne $null -and -not $h.Proc.HasExited) {
            $остановкаФинал = Invoke-HarnessStop $h 'previewflow-finally'

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
                Запись "SAFETY-FAIL: harness остался жив даже после аварийной остановки — exit 1 без дальнейших ожиданий"
            }
        }

        # --- B. Финальная fingerprint-проверка (fail-closed) ---------------
        if ($null -ne $отпечатокДо) {
            try {
                $отпечатокФинал = Test-ProfileFingerprint $реальныйПрофиль
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
        if ($сценарийПройден) {
            $файлEvidence = 'previewflow-happy-evidence.md'
            $заголовокEvidence = 'Предпросмотр с полями бланка: runtime-доказательство (happy path)'
        } else {
            $файлEvidence = 'previewflow-failure-evidence.md'
            $заголовокEvidence = 'Предпросмотр с полями бланка: runtime-доказательство (FAIL)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
        Запись "SAFETY-FAIL: ошибка финализации: $($_.Exception.Message)"
    }

    # --- E. Гарантированное освобождение блокировки ------------------------
    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'previewflow'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение межпроцессной блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    # --- F. Финальный статус ДО записи evidence ----------------------------
    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись "Режим: previewflow (предпросмотр с реальными полями бланка)"
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if (Get-Variable -Name корень -ErrorAction SilentlyContinue) { $корень } else { 'не создан' })"
    Запись "createDemoData=false"
    Запись "SPOOLER: до=$спулерДо после=$спулерПосле ошибка='$спулерОшибка'"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    # --- G. Единая запись evidence + пост-проверка -------------------------
    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'createDemoData=false' $журнал

    $маркеры = @('Режим: previewflow', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:', 'createDemoData=false', 'SPOOLER:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    # --- H. Код возврата ----------------------------------------------------
    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'PREVIEWFLOW PASS: True'
    } else {
        $exitCode = 1
        Запись 'PREVIEWFLOW PASS: False'
    }
}

exit $exitCode
