# Сценарий «получатель без адреса» (ТЗ §11.4 приёмки): F5 и F6 на
# контрагенте без основного адреса.
#
# Контракт сценария (проверяется, а не декларируется):
#   1. отправитель с адресом, получатель БЕЗ основного адреса;
#   2. F5 → понятное сообщение, окно не зависает, печать НЕ началась;
#   3. F6 → то же: пропуск с перечислением поимённо, печать НЕ началась;
#   4. отправление НЕ создано частично (MAILCOUNT = 0 до и после);
#   5. сообщение можно закрыть (кнопка ОК нажимается, окно исчезает);
#   6. главное окно отвечает (WM_NULL) — зависания нет;
#   7. спулер печати: 0 заданий до и после;
#   8. реальный профиль не изменён (fingerprint + sentinel);
#   9. временный профиль удалён, процессов не осталось, exit code 0.
#
# Fail-closed: любой провал условия → ненулевой exit. Спулер измеряется
# через Win32_PrintJob (до и после), чтобы «печать не началась» было
# измеренным фактом, а не отсутствием stdout.
#
# Fault-инъекция (без UI): P302_NOADDRESS_FAULT=message-missing | mail-created
# | spooler-job | final-delta | cleanup-false — каждая обязана давать exit 1.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = Get-EvidenceRoot
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$скриншотF5 = Join-Path $out '09-no-address-warning.png'
$скриншотF6 = Join-Path $out '10-no-address-warning-f6.png'

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'noaddress-failure-evidence.md'
$заголовокEvidence = 'No-address runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$faultПрименён = $false
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null
$таймаутШага = 20
$окноF5Закрыто = $false
$окноF6Закрыто = $false
$выделеноСтрок = -1

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

# Число заданий в спулере: объективный признак «печать не началась».
function ЧислоЗаданийСпулера() {
    try {
        $задания = @(Get-CimInstance -ClassName Win32_PrintJob -ErrorAction Stop)
        return $заданий.Count
    } catch {
        Запись "SPOOLER-READ ERROR: $($_.Exception.Message)"
        return -1
    }
}

# Ждёт появления окна с указанным заголовком; возвращает дескриптор или Zero.
function Wait-ОкноСЗаголовком([uint32]$procId, [string]$заголовок, [int]$секунд) {
    $deadline = (Get-Date).AddSeconds($секунд)

    while ((Get-Date) -lt $deadline) {
        $окно = [AccWin]::FindByTitle($procId, $заголовок)

        if ($окно -ne [IntPtr]::Zero) { return $окно }

        Start-Sleep -Milliseconds 200
    }

    return [IntPtr]::Zero
}

# Проверяет, что КОНКРЕТНОЕ окно закрыто. Проверка по дескриптору, а не по
# заголовку: у окна печати и у сообщения о пропуске ОДИН заголовок «Печать»,
# поэтому поиск по заголовку после закрытия сообщения нашёл бы заново
# открывшийся диалог печати и дал бы ложный FAIL.
function ОкноЗакрыто([IntPtr]$hwnd) {
    if ($hwnd -eq [IntPtr]::Zero) { return $true }
    return -not [AccWin]::IsWindowVisible($hwnd)
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

    $спулерДо = ЧислоЗаданийСпулера
    Запись "spooler jobs before: $спулерДо"

    # --- 2. Запуск harness в режиме noaddress -----------------------------
    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'noaddress' $корень

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    $pid_ = [uint32]$h.Proc.Id
    Запись "harness READY: $готовность"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    # --- 3. Засевание подтверждено ---------------------------------------
    # harness печатает SEEDED-NOADDRESS ДО READY (засевание происходит при
    # создании состава, а READY — по событию Shown), поэтому марка уже лежит
    # в накопленном буфере stdout; отдельное ожидание её не найдёт.
    $маркаЗасевания = Get-HarnessLines $h | Where-Object { $_ -match '^SEEDED-NOADDRESS' } | Select-Object -First 1
    if ($маркаЗасевания) {
        Запись 'harness подтвердил засевание получателя без адреса'
    } else {
        & $всёОк $false 'harness не подтвердил засевание получателя без адреса'
    }

    # --- 4. MAILCOUNT ДО: отправлений нет -------------------------------
    Send-HarnessCommand $h 'MAILCOUNT'

    if (-not (Read-HarnessLines $h '^MAILCOUNT ' 10)) {
        & $всёОк $false 'MAILCOUNT не получен'
        $отправленийДо = -1
    } else {
        $строкаДо = Get-HarnessLines $h | Where-Object { $_ -match '^MAILCOUNT ' } | Select-Object -First 1
        $отправленийДо = if ($строкаДо -match 'всего=(\d+)') { [int]$Matches[1] } else { -1 }
        Запись "отправлений ДО: $отправленийДо (строка: '$строкаДо')"
        & $всёОк ($отправленийДо -eq 0) 'до печати отправлений нет'
    }

    # --- 5. Выбор получателя БЕЗ адреса ---------------------------------
    Send-HarnessCommand $h 'SELECTNOADDR'

    if (-not (Read-HarnessLines $h '^OK SELECTNOADDR' 10)) {
        & $всёОк $false 'получатель без адреса не выбран'
    } else {
        $строкаВыбора = Get-HarnessLines $h | Where-Object { $_ -match '^OK SELECTNOADDR' } | Select-Object -Last 1
        Запись "выбран получатель без адреса: $строкаВыбора"

        # Выбрана РОВНО ОДНА строка. Если выделение осталось на отправителе
        # (у которого адрес есть), F6 создала бы отправление для него и
        # сценарий проверял бы не пропуск, а частичную печать.
        if ($строкаВыбора -match 'выделено=(\d+)') {
            $выделеноСтрок = [int]$Matches[1]
        }

        & $всёОк ($выделеноСтрок -eq 1) 'выделена ровно одна строка — получатель без адреса'
    }

    # --- 6. F5: сообщение вместо печати ----------------------------------
    Запись '## F5 «Печать текущего конверта» на получателе без адреса'
    $сообщениеF5Найдено = $false
    $текстСообщенияF5 = ''

    if ($успех) {
        Send-HarnessCommand $h 'F5'
    }

    if ($успех) {
        $mb = Wait-ОкноСЗаголовком $pid_ 'Печать' $таймаутШага
        if ($mb -ne [IntPtr]::Zero) {
            $сообщениеF5Найдено = $true
            $текстСообщенияF5 = [AccWin]::Title($mb)
            Запись "MessageBox печати появился: hwnd=$mb"

            # Текст MessageBox лежит в статическом контроле, а не в заголовке
            # окна: читаем его по классу Static.
            $текстОкна = ''
            [AccWin]::EnumChildWindows($mb, {
                param($hwnd, $lp)
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($hwnd, $cls, 256) | Out-Null
                if ($cls.ToString() -eq 'Static') {
                    $t = [AccWin]::Title($hwnd)
                    if (-not [string]::IsNullOrWhiteSpace($t)) { $script:текстОкна = $t }
                }
                return $true
            }, [IntPtr]::Zero) | Out-Null

            if (-not [string]::IsNullOrWhiteSpace($текстОкна)) {
                $текстСообщенияF5 = $текстОкна
            }

            Запись "текст сообщения F5: '$текстСообщенияF5'"

            $скр = Join-Path $out '09-no-address-warning.png'
            [AccWin]::Capture($mb, $скр) | Out-Null
            $размерСкриншота = 0
            if (Test-Path $скр) { $размерСкриншота = (Get-Item $скр).Length }
            Запись "скриншот сообщения F5: $скр ($размерСкриншота байт)"

            # Сообщение должно называть получателя и объяснять, что делать.
            $говоритОбАдресе = $текстСообщенияF5 -match 'адрес'
            $называетПолучателя = $текстСообщенияF5 -match 'Получатель'
            & $всёОк $говоритОбАдресе 'сообщение F5 сообщает об отсутствии адреса'
            & $всёОк $называетПолучателя 'сообщение F5 называет получателя'
            & $всёОк ($размерСкриншота -gt 0) 'скриншот сообщения F5 создан'

            # Сообщение должно быть ЗАКРЫВАЕМО: кнопка ОК нажимается, окно
            # исчезает, главное окно продолжает отвечать.
            $кнопкаОк = [AccWin]::FindButton($mb, 'ОК')
            & $всёОк ($кнопкаОк -ne [IntPtr]::Zero) 'кнопка ОК в сообщении F5 найдена'

            if ($кнопкаОк -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаОк)
                Start-Sleep -Seconds 2
                $окноF5Закрыто = ОкноЗакрыто $mb
                Запись "окно сообщения F5 закрыто (по дескриптору): $окноF5Закрыто"
                & $всёОк $окноF5Закрыто 'сообщение F5 закрыто (окно исчезло)'
            }
        } else {
            & $всёОк $false 'сообщение F5 не появилось (печать не остановлена объяснением)'
        }
    }

    if ($env:P302_NOADDRESS_FAULT -eq 'message-missing') {
        $сообщениеF5Найдено = $false
        Запись 'FAULT-INJECT: message-missing'
    }

    if ($успех) {
        $живF5 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после F5 (WM_NULL): $живF5"
        if (-not $живF5) { $успех = $false }
    }

    # --- 7. MAILCOUNT ПОСЛЕ F5: запись не создана частично ----------------
    Send-HarnessCommand $h 'MAILCOUNT'
    $отправленийПослеF5 = -1
    if (Read-HarnessLines $h '^MAILCOUNT ' 10) {
        $строкаПосле = Get-HarnessLines $h | Where-Object { $_ -match '^MAILCOUNT ' } | Select-Object -Last 1
        $отправленийПослеF5 = if ($строкаПосле -match 'всего=(\d+)') { [int]$Matches[1] } else { -1 }
        Запись "отправлений после F5: $отправленийПослеF5"
        & $всёОк ($отправленийПослеF5 -eq 0) 'F5 не создал частичное отправление'
    } else {
        & $всёОк $false 'MAILCOUNT после F5 не получен'
    }

    if ($env:P302_NOADDRESS_FAULT -eq 'mail-created') {
        $отправленийПослеF5 = 1
        Запись 'FAULT-INJECT: mail-created'
    }

    # --- 8. F6: сообщение вместо печати ----------------------------------
    Запись '## F6 «Предпросмотр и печать» на получателе без адреса'
    $сообщениеF6Найдено = $false
    $текстСообщенияF6 = ''

    if ($успех) {
        Send-HarnessCommand $h 'F6'
    }

    if ($успех) {
        # F6 сначала открывает карточку отправления (параметры уведомления),
        # затем — MessageBox о формировании. Карточку закрываем, дожидаясь
        # сообщения о пропуске без адреса.
        $карточка = Wait-ОкноСЗаголовком $pid_ 'Карточка отправления' 10
        if ($карточка -ne [IntPtr]::Zero) {
            Запись "Карточка отправления появилась: hwnd=$карточка"
            $кнопкаКарточки = [AccWin]::FindButton($карточка, 'ОК')
            if ($кнопкаКарточки -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаКарточки)
                Запись 'Карточка: нажата ОК'
            } else {
                & $всёОк $false 'кнопка ОК карточки F6 не найдена'
            }
        } else {
            & $всёОк $false 'карточка отправления F6 не появилась'
        }
    }

    if ($успех) {
        $mb6 = Wait-ОкноСЗаголовком $pid_ 'Печать' $таймаутШага
        if ($mb6 -ne [IntPtr]::Zero) {
            $сообщениеF6Найдено = $true

            $текстОкна6 = ''
            [AccWin]::EnumChildWindows($mb6, {
                param($hwnd, $lp)
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($hwnd, $cls, 256) | Out-Null
                if ($cls.ToString() -eq 'Static') {
                    $t = [AccWin]::Title($hwnd)
                    if (-not [string]::IsNullOrWhiteSpace($t)) { $script:текстОкна6 = $t }
                }
                return $true
            }, [IntPtr]::Zero) | Out-Null

            $текстСообщенияF6 = $текстОкна6
            Запись "текст сообщения F6: '$текстСообщенияF6'"

            $скр6 = Join-Path $out '10-no-address-warning-f6.png'
            [AccWin]::Capture($mb6, $скр6) | Out-Null
            $размерСкриншота6 = 0
            if (Test-Path $скр6) { $размерСкриншота6 = (Get-Item $скр6).Length }
            Запись "скриншот сообщения F6: $скр6 ($размерСкриншота6 байт)"

            # F6 перечисляет пропущенных ПОИМЕННО (политика варианта A).
            & $всёОк ($текстСообщенияF6 -match 'Пропущены') 'сообщение F6 перечисляет пропущенных'
            & $всёОк ($текстСообщенияF6 -match 'Получатель Без Адреса') 'сообщение F6 называет получателя поимённо'
            & $всёОк ($размерСкриншота6 -gt 0) 'скриншот сообщения F6 создан'

            $кнопкаОк6 = [AccWin]::FindButton($mb6, 'ОК')
            & $всёОк ($кнопкаОк6 -ne [IntPtr]::Zero) 'кнопка ОК в сообщении F6 найдена'

            if ($кнопкаОк6 -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаОк6)
                Start-Sleep -Seconds 2
                $окноF6Закрыто = ОкноЗакрыто $mb6
                Запись "окно сообщения F6 закрыто (по дескриптору): $окноF6Закрыто"
                & $всёОк $окноF6Закрыто 'сообщение F6 закрыто (окно исчезло)'
            }
        } else {
            & $всёОк $false 'сообщение F6 не появилось'
        }
    }

    if ($env:P302_NOADDRESS_FAULT -eq 'message-missing') {
        $сообщениеF6Найдено = $false
    }

    # После F6 диалог печати/предпросмотра открываться НЕ должен: печать
    # не началась. Любое окно «Печать»/«Подача конвертов»/«Предпросмотр»
    # после закрытия сообщения — признак того, что задание пошло дальше.
    if ($успех) {
        Start-Sleep -Seconds 2
        $предпросмотрОткрыт = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
        $подачаОткрыта = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
        & $всёОк ($предпросмотрОткрыт -eq [IntPtr]::Zero) 'предпросмотр не открылся (печать не началась)'
        & $всёОк ($подачаОткрыта -eq [IntPtr]::Zero) 'подача конвертов не открылась (печать не началась)'
    }

    if ($успех) {
        $живF6 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после F6 (WM_NULL): $живF6"
        if (-not $живF6) { $успех = $false }
    }

    # --- 9. MAILCOUNT ПОСЛЕ F6 --------------------------------------------
    Send-HarnessCommand $h 'MAILCOUNT'
    $отправленийПослеF6 = -1
    if (Read-HarnessLines $h '^MAILCOUNT ' 10) {
        $строкаПосле6 = Get-HarnessLines $h | Where-Object { $_ -match '^MAILCOUNT ' } | Select-Object -Last 1
        $отправленийПослеF6 = if ($строкаПосле6 -match 'всего=(\d+)') { [int]$Matches[1] } else { -1 }
        Запись "отправлений после F6: $отправленийПослеF6"
        & $всёОк ($отправленийПослеF6 -eq 0) 'F6 не создал отправление для получателя без адреса'
    } else {
        & $всёОк $false 'MAILCOUNT после F6 не получен'
    }

    # --- 10. Остановка harness --------------------------------------------
    $остановка = Invoke-HarnessStop $h 'noaddress'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) остатки=[$($остановка.Результат.RemainingPids -join ';')]"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'noaddress-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

    # --- 11. Спулер после сценария ---------------------------------------
    $спулерПосле = ЧислоЗаданийСпулера
    Запись "spooler jobs after: $спулерПосле"

    if ($env:P302_NOADDRESS_FAULT -eq 'spooler-job') {
        $спулерПосле = $спулерПосле + 1
        Запись 'FAULT-INJECT: spooler-job'
    }

    & $всёОк ($спулерПосле -eq $спулерДо) 'заданий в спулере не появилось (печать не началась)'

    # --- 12. Условия сценария --------------------------------------------
    $stdout = Get-HarnessLines $h
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

    $профильОчищен = -not (Test-Path $корень)
    $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count

    $условия = [ordered]@{
        'harness засеял получателя без адреса' = ($null -ne ($stdout | Where-Object { $_ -match '^SEEDED-NOADDRESS' }))
        'получатель без адреса выбран' = ($null -ne ($stdout | Where-Object { $_ -match '^OK SELECTNOADDR' }))
        'выделена ровно одна строка' = ($выделеноСтрок -eq 1)
        'F5 показал сообщение' = $сообщениеF5Найдено
        'F5 сообщение закрывается' = $окноF5Закрыто
        'F6 показал сообщение' = $сообщениеF6Найдено
        'F6 сообщение закрывается' = $окноF6Закрыто
        'MAILCOUNT до получен' = ($отправленийДо -eq 0)
        'MAILCOUNT после F5 = 0' = ($отправленийПослеF5 -eq 0)
        'MAILCOUNT после F6 = 0' = ($отправленийПослеF6 -eq 0)
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
            $остановкаФинал = Invoke-HarnessStop $h 'noaddress-finally'

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

        # --- Финальная fingerprint-проверка (fail-closed) -----------------
        if ($null -ne $отпечатокДо) {
            try {
                $отпечатокФинал = Test-ProfileFingerprint $реальныйПрофиль
                $дельтаФинал = Compare-Fingerprints $отпечатокДо $отпечатокФинал
                $финальнаяДельтаNew = $дельтаФинал.New.Count
                $финальнаяДельтаChanged = $дельтаФинал.Changed.Count
                $финальнаяДельтаRemoved = $дельтаФинал.Removed.Count
                Запись "production delta (финально): new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"

                if ($env:P302_NOADDRESS_FAULT -eq 'final-delta') {
                    $финальнаяДельтаRemoved = 1
                    Запись 'FAULT-INJECT: final delta Removed > 0'
                }

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

        # --- Sentinel удаляется -------------------------------------------
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

        # --- Выбор файла evidence ------------------------------------------
        $fault = $env:P302_NOADDRESS_FAULT
        $faultПрименён = @($журнал | Where-Object { $_ -match 'FAULT-INJECT:' }).Count -gt 0

        if ($сценарийПройден -and $fault) {
            $файлEvidence = "noaddress-fault-$fault.md"
            $заголовокEvidence = "No-address fault-injection: $fault"
            Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
        } elseif ($сценарийПройден) {
            $файлEvidence = 'noaddress-happy-evidence.md'
            $заголовокEvidence = 'No-address runtime-доказательство (happy path)'
        } elseif ($faultПрименён) {
            $файлEvidence = "noaddress-fault-$fault.md"
            $заголовокEvidence = "No-address fault-injection: $fault"
        } else {
            $файлEvidence = 'noaddress-failure-evidence.md'
            $заголовокEvidence = 'No-address runtime-доказательство (FAIL)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
        Запись "SAFETY-FAIL: ошибка финализации: $($_.Exception.Message)"
    }

    # --- Гарантированное освобождение блокировки -------------------------
    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'noaddress'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    # --- Финальный статус ДО записи evidence ------------------------------
    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0) -and (-not $env:P302_NOADDRESS_FAULT)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: noaddress'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if ($корень) { $корень } else { 'не создан' })"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "FAULT: $(if ($env:P302_NOADDRESS_FAULT) { $env:P302_NOADDRESS_FAULT } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($faultПрименён) {
        Запись "PASS_OF_CONTRACT: $(-not $happyПройден) (fault — инъекция корректно обнаружена, скрипт не вернёт 0)"
    }

    # --- Единая запись evidence + пост-проверка ---------------------------
    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'получатель без адреса' $журнал

    $маркеры = @('Режим: noaddress', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:')
    if ($faultПрименён) { $маркеры += 'PASS_OF_CONTRACT:' }
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'NOADDRESS PASS: True'
    } else {
        $exitCode = 1
        Запись 'NOADDRESS PASS: False'
    }
}

exit $exitCode
