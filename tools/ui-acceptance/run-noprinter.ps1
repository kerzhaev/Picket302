# Сценарий «нет принтера» (stage3.1, §7.4): печать при отсутствии принтера
# по умолчанию. Реальный системный принтер стенда НЕ изменяется: harness
# подменяет принтерный менеджер производным классом, возвращающим null
# (GetDefaultPrinter => null), после чего РЕАЛЬНЫЙ код MainShell
# (ВыполнитьПечатьAsync) обязан заблокировать печать понятным сообщением
# PrinterManager.NoDefaultPrinterMessage.
#
# Контракт сценария (проверяется, а не декларируется):
#   1. F5 → карточка отправления → ОК → сообщение «Принтер по умолчанию
#      не установлен» (реальный код приложения, не заглушка драйвера);
#   2. диалог печати НЕ открывается, предпросмотр НЕ открывается;
#   3. отправление создано (данные не теряются — §7.4), печать не началась;
#   4. спулер печати: 0 заданий до и после (Win32_PrintJob);
#   5. сообщение закрывается, главное окно отвечает (WM_NULL);
#   6. реальный профиль не изменён (fingerprint + sentinel);
#   7. реестр HKCU\Software\PEPro\CurrentSender не пишется (контейнирование);
#   8. временный профиль удалён, процессов не осталось, exit code 0.
#
# Статус флага честный: RUNTIME_NO_PRINTER_VERIFIED=CONTRACT_ONLY — путь
# кода реальный (MainShell), но отсутствие принтера достигнуто подменяемым
# провайдером, а не удалением системного принтера стенда.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = Get-EvidenceRoot
# Каталог evidence создаётся ДО первых скриншотов: Capture пишет PNG
# напрямую и GDI+ падает с общей ошибкой на несуществующем каталоге
# (регрессия первого прогона 2026-11-08).
New-Item -ItemType Directory -Force -Path $out -ErrorAction Stop | Out-Null
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$скриншот = Join-Path $out '60-no-printer-warning.png'

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'noprinter-failure-evidence.md'
$заголовокEvidence = 'No-printer runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null
$таймаутШага = 20
$сообщениеЗакрыто = $false

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

function ЧислоЗаданийСпулера() {
    try {
        $задания = @(Get-CimInstance -ClassName Win32_PrintJob -ErrorAction Stop)
        return $заданий.Count
    } catch {
        Запись "SPOOLER-READ ERROR: $($_.Exception.Message)"
        return -1
    }
}

function Wait-ОкноСЗаголовком([uint32]$procId, [string]$заголовок, [int]$секунд) {
    $deadline = (Get-Date).AddSeconds($секунд)

    while ((Get-Date) -lt $deadline) {
        $окно = [AccWin]::FindByTitle($procId, $заголовок)

        if ($окно -ne [IntPtr]::Zero) { return $окно }

        Start-Sleep -Milliseconds 200
    }

    return [IntPtr]::Zero
}

function ОкноЗакрыто([IntPtr]$hwnd) {
    if ($hwnd -eq [IntPtr]::Zero) { return $true }
    return -not [AccWin]::IsWindowVisible($hwnd)
}

# Читает тексты статических контролов окна (текст MessageBox лежит в Static).
function ТекстОкна([IntPtr]$hwnd) {
    # Отрисовка сообщения успевает завершиться до чтения Static-контролов.
    Start-Sleep -Milliseconds 500
    # Scriptblock-делегат не видит локальные переменные функции (PS 5.1):
    # результат читается только через scope script:.
    $script:текстСообщенияЧтение = ''
    [AccWin]::EnumChildWindows($hwnd, {
        param($child, $lp)
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($child, $cls, 256) | Out-Null
        if ($cls.ToString() -eq 'Static') {
            $t = [AccWin]::Title($child)
            if (-not [string]::IsNullOrWhiteSpace($t)) { $script:текстСообщенияЧтение = $t }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:текстСообщенияЧтение
}


# Скриншот с ожиданием отрисовки и одной повторной попыткой: BitBlt по
# только что появившемуся окну может снять пустую поверхность.
function Захват([IntPtr]$hwnd, [string]$путь) {
    Start-Sleep -Milliseconds 600

    try {
        [AccWin]::Capture($hwnd, $путь) | Out-Null
    } catch {
        Запись "capture повтор: $($_.Exception.Message)"
        Start-Sleep -Milliseconds 500
        [AccWin]::Capture($hwnd, $путь) | Out-Null
    }
}

try {
    $блокировка = Enter-SharedTestLock

    # --- 1. Sentinel и отпечатки реального профиля ДО ----------------------
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    $спулерДо = ЧислоЗаданийСпулера
    Запись "spooler jobs before: $спулерДо"

    # --- 2. Запуск harness в режиме noprinter ------------------------------
    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'noprinter' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    $pid_ = [uint32]$h.Proc.Id
    Запись "harness READY: $готовность"

    # Гард профиля и PID в stdout (журнал profile-sensitive операции).
    $гард = Get-HarnessLines $h | Where-Object { $_ -match '^GUARD PASS ' } | Select-Object -First 1
    Запись "guard line: $гард"
    $профильСтрокой = Get-HarnessLines $h | Where-Object { $_ -match '^PROFILE ' } | Select-Object -First 1
    Запись "profile line: $профильСтрокой"

    # 0x0054 = SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW.
    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 30, 30, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    $подменаПодтверждена = ($гард -match 'pid=\d+')
    & $всёОк $подменаПодтверждена 'гард профиля отчитался (pid + корень в stdout)'

    # --- 3. Выбор получателя и F5 ------------------------------------------
    Send-HarnessCommand $h 'SELECT'

    if (-not (Read-HarnessLines $h '^OK SELECT' 10)) {
        & $всёОк $false 'получатель не выбран'
    } else {
        Запись 'получатель выбран (строка 1 адресной книги)'
    }

    if ($успех) {
        Send-HarnessCommand $h 'F5'
    }

    # --- 4. Карточка отправления → ОК ---------------------------------------
    if ($успех) {
        $карточка = Wait-ОкноСЗаголовком $pid_ 'Карточка отправления' 10
        if ($карточка -ne [IntPtr]::Zero) {
            Запись "Карточка отправления появилась: hwnd=$карточка"
            $кнопкаКарточки = [AccWin]::FindButton($карточка, 'ОК')
            if ($кнопкаКарточки -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаКарточки)
                Запись 'Карточка: нажата ОК'
            } else {
                & $всёОк $false 'кнопка ОК карточки не найдена'
            }
        } else {
            & $всёОк $false 'карточка отправления не появилась'
        }
    }

    # --- 5. Сообщение «Принтер по умолчанию не установлен» ------------------
    $сообщениеНайдено = $false
    $текстСообщения = ''

    if ($успех) {
        $mb = Wait-ОкноСЗаголовком $pid_ 'Печать' $таймаутШага
        if ($mb -ne [IntPtr]::Zero) {
            $сообщениеНайдено = $true
            $текстСообщения = ТекстОкна $mb
            if ([string]::IsNullOrWhiteSpace($текстСообщения)) {
                $текстСообщения = [AccWin]::Title($mb)
            }
            Запись "текст сообщения: '$текстСообщения'"

            Захват $mb $скриншот | Out-Null
            $размерСкриншота = 0
            if (Test-Path $скриншот) { $размерСкриншота = (Get-Item $скриншот).Length }
            Запись "скриншот сообщения: $скриншот ($размерСкриншота байт)"

            & $всёОк ($текстСообщения -match 'Принтер по умолчанию не установлен') 'сообщение называет отсутствие принтера по умолчанию'
            & $всёОк ($размерСкриншота -gt 0) 'скриншот сообщения создан'

            $кнопкаОк = [AccWin]::FindButton($mb, 'ОК')
            & $всёОк ($кнопкаОк -ne [IntPtr]::Zero) 'кнопка ОК найдена'

            if ($кнопкаОк -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаОк)
                Start-Sleep -Seconds 2
                $сообщениеЗакрыто = ОкноЗакрыто $mb
                & $всёОк $сообщениеЗакрыто 'сообщение закрыто (окно исчезло)'
            }
        } else {
            & $всёОк $false 'сообщение об отсутствии принтера не появилось'
        }
    }

    # --- 6. Диалог печати и предпросмотр НЕ открылись -----------------------
    if ($успех) {
        Start-Sleep -Seconds 2
        $диалогПечати = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
        $предпросмотр = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
        & $всёОк ($диалогПечати -eq [IntPtr]::Zero) 'подача конвертов не открылась'
        & $всёОк ($предпросмотр -eq [IntPtr]::Zero) 'предпросмотр не открылся'

        $жив = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после сообщения (WM_NULL): $жив"
        if (-not $жив) { $успех = $false }
    }

    # --- 7. MAILCOUNT: отправление создано (данные не теряются) -------------
    Send-HarnessCommand $h 'MAILCOUNT'
    $отправлений = -1
    if (Read-HarnessLines $h '^MAILCOUNT ' 10) {
        $строка = Get-HarnessLines $h | Where-Object { $_ -match '^MAILCOUNT ' } | Select-Object -Last 1
        $отправлений = if ($строка -match 'всего=(\d+)') { [int]$Matches[1] } else { -1 }
        Запись "отправлений после F5: $отправлений"
        & $всёОк ($отправлений -eq 1) 'отправление создано (данные не потеряны)'
    } else {
        & $всёОк $false 'MAILCOUNT не получен'
    }

    # --- 8. Остановка harness ----------------------------------------------
    $остановка = Invoke-HarnessStop $h 'noprinter'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) остатки=[$($остановка.Результат.RemainingPids -join ';')]"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'noprinter-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

    # --- 9. Спулер и отпечатки ПОСЛЕ ---------------------------------------
    $спулерПосле = ЧислоЗаданийСпулера
    Запись "spooler jobs after: $спулерПосле"
    & $всёОк ($спулерПосле -eq $спулерДо) 'заданий в спулере не появилось'

    $stdout = Get-HarnessLines $h
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

    $профильОчищен = -not (Test-Path $корень)
    $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count

    $условия = [ordered]@{
        'гард профиля в stdout' = ($null -ne ($stdout | Where-Object { $_ -match '^GUARD PASS ' }))
        'сообщение показано' = $сообщениеНайдено
        'сообщение о принтере по умолчанию' = ($текстСообщения -match 'Принтер по умолчанию не установлен')
        'сообщение закрывается' = $сообщениеЗакрыто
        'отправление создано (данные не потеряны)' = ($отправлений -eq 1)
        'спулер не изменился' = ($спулерПосле -eq $спулерДо)
        'временный профиль удалён' = $профильОчищен
        'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
        'новых файлов реального профиля нет' = ($дельта.New.Count -eq 0)
        'изменённых файлов реального профиля нет' = ($дельта.Changed.Count -eq 0)
        'удалённых файлов реального профиля нет' = ($дельта.Removed.Count -eq 0)
        'harness exit 0' = ($кодВыходаHarness -eq 0)
        'harness не убит' = (-not $убит)
        'harness завершён (bounded)' = (-not $harnessNotExited)
        'нет FAIL в stdout' = ($отказовStdout -eq 0)
        'дерево процессов чисто' = $остановка.Результат.TreeOk
        'нет живых потомков harness' = ($остановка.Результат.RemainingPids.Count -eq 0)
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
        if ($h -ne $null -and $h.Proc -ne $null -and -not $h.Proc.HasExited) {
            $остановкаФинал = Invoke-HarnessStop $h 'noprinter-finally'

            foreach ($флаг in $остановкаФинал.Флаги) {
                ДобавитьФлагОчистки $флаг
                Запись "SAFETY-FLAG: $флаг"
            }

            if ($остановкаФинал.Результат -ne $null -and (-not $остановкаФинал.Результат.Graceful)) {
                $script:убит = $true
                ДобавитьФлагОчистки 'emergency-kill'
                Запись 'SAFETY-FAIL: harness убит в finally — happy-path PASS невозможен'
            }
        }

        if ($null -ne $отпечатокДо) {
            try {
                $отпечатокФинал = Test-ProfileFingerprint $реальныйПрофиль
                $дельтаФинал = Compare-Fingerprints $отпечатокДо $отпечатокФинал
                $финальнаяДельтаNew = $дельтаФинал.New.Count
                $финальнаяДельтаChanged = $дельтаФинал.Changed.Count
                $финальнаяДельтаRemoved = $дельтаФинал.Removed.Count
                Запись "production delta (финально): new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"

                if ($финальнаяДельтаNew -ne 0) { ДобавитьФлагОчистки 'final-delta-new'; Запись 'SAFETY-FAIL: финальная дельта — новые файлы' }
                if ($финальнаяДельтаChanged -ne 0) { ДобавитьФлагОчистки 'final-delta-changed'; Запись 'SAFETY-FAIL: финальная дельта — изменённые файлы' }
                if ($финальнаяДельтаRemoved -ne 0) { ДобавитьФлагОчистки 'final-delta-removed'; Запись 'SAFETY-FAIL: финальная дельта — удалённые файлы' }
            } catch {
                ДобавитьФлагОчистки 'fingerprint-final-check'
                Запись "SAFETY-FAIL: финальная fingerprint-проверка не выполнена: $($_.Exception.Message)"
            }
        } else {
            ДобавитьФлагОчистки 'fingerprint-before-missing'
            Запись 'SAFETY-FAIL: отпечаток ДО не снят'
        }

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
            Запись 'SAFETY-FAIL: sentinel остался в реальном профиле'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
        Запись "SAFETY-FAIL: ошибка финализации: $($_.Exception.Message)"
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'noprinter'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: noprinter'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if ($корень) { $корень } else { 'не создан' })"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($сценарийПройден -and ($неудачиОчистки.Count -eq 0)) {
        $файлEvidence = 'noprinter-happy-evidence.md'
        $заголовокEvidence = 'No-printer runtime-доказательство (happy path)'
    }

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'нет принтера' $журнал

    $маркеры = @('Режим: noprinter', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'NOPRINTER PASS: True'
    } else {
        $exitCode = 1
        Запись 'NOPRINTER PASS: False'
    }
}

exit $exitCode
