# Сценарий screens-2 (stage3.1, §7): оставшиеся runtime-поверхности на
# изолированном профиле БЕЗ шаблонов печати (шаблон сеется по требованию).
#
# Этапы (каждый — проверяемое условие, а не декларация):
#   S1 «Отправленные» пустая    — SETTAB 3, SENTINFO строк=0, скриншот;
#   S2 карточка + ОТМЕНА        — F5 на получателе с длинными значениями,
#                                 скриншот карточки, Отмена, MAILCOUNT 0;
#   S3 карточка + ОК → печать → Отмена — MAILCOUNT 1, вкладка «Отправленные»
#                                 показывает строку, скриншоты;
#   S4 «нет шаблона»            — F5 → карточка ОК → печать Печать → подача
#                                 Продолжить → сообщение «Шаблон не выбран.»,
#                                 скриншот, MAILCOUNT 2, спулер без изменений;
#   S5 «блокировка размера»     — SEEDTEMPLATE → F5 … Печать → подача →
#                                 «Проверка размера бумаги», скриншот,
#                                 Отмена, спулер без изменений;
#   S6 клипарт смоук            — SHOW → CLIPSELECT (10 значков, выбор,
#                                 закрытие) → повторный SHOW → HIDE;
#   S7 сброс вида таблиц        — SHOW → GRIDRESET (до/после) → скриншот → HIDE;
#   S8 редактор бланка          — SHOW → скриншот → HIDE (без GDI+ исключений).
#
# Fail-closed: спулер Win32_PrintJob до/после, sentinel + fingerprint, bounded
# waits, cleanup-флаги, единая запись evidence с пост-проверкой.
#
# Fault-инъекции (P302_SCREENS2_FAULT): sent-empty | mailcard | sent-data |
# no-template | paper-dialog | clipart | gridreset | final-delta.
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

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'screens2-failure-evidence.md'
$заголовокEvidence = 'Screens-2 runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null
$таймаутШага = 20

$этапы = [ordered]@{
    'S1 отправленные-пусто'      = $false
    'S2 карточка-отмена'         = $false
    'S3 отправленные-с-данными'  = $false
    'S4 нет-шаблона'             = $false
    'S5 блокировка-размера'      = $false
    'S6 клипарт-смоук'           = $false
    'S7 сброс-вида-таблиц'       = $false
    'S8 редактор-бланка'         = $false
    'S9 группировка-строк'       = $false
}

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

function ТекстОкна([IntPtr]$hwnd) {
    # Отрисовка сообщения успевает завершиться до чтения Static-контролов.
    Start-Sleep -Milliseconds 500
    $script:текстДочернего = ''
    [AccWin]::EnumChildWindows($hwnd, {
        param($child, $lp)
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($child, $cls, 256) | Out-Null
        if ($cls.ToString() -eq 'Static') {
            $t = [AccWin]::Title($child)
            if (-not [string]::IsNullOrWhiteSpace($t)) { $script:текстДочернего = $t }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:текстДочернего
}

# Команда harness с bounded-ожиданием ответа по шаблону; возвращает строку.
function Команда([string]$текст, [string]$шаблонОтвета, [int]$секунд = 15) {
    Send-HarnessCommand $h $текст

    if (-not (Read-HarnessLines $h $шаблонОтвета $секунд)) {
        Запись "FAIL: ответ '$шаблонОтвета' на '$текст' не получен за $секунд c"
        return $null
    }

    $строка = Get-HarnessLines $h | Where-Object { $_ -match $шаблонОтвета } | Select-Object -Last 1
    Запись "$текст → $строка"
    return $строка
}

# Ждёт SENTINFO с ожидаемым числом строк (загрузка вкладки асинхронная).
function ЖдатьОтправленные([int]$ожидаемоеЧисло, [int]$секунд = 12) {
    $deadline = (Get-Date).AddSeconds($секунд)
    $последняя = $null

    while ((Get-Date) -lt $deadline) {
        $последняя = Команда 'SENTINFO' '^SENTINFO ' 8

        if ($последняя -match "строк=$ожидаемоеЧисло ") {
            return $последняя
        }

        Start-Sleep -Milliseconds 700
    }

    return $последняя
}

# Цепочка F5 до появления окна с указанным заголовком: карточка → ОК.
# Возвращает hwnd целевого окна или Zero.
function ЦепочкаФ5([uint32]$pid_, [string]$целевойЗаголовок) {
    $карточка = Wait-ОкноСЗаголовком $pid_ 'Карточка отправления' 12

    if ($карточка -eq [IntPtr]::Zero) {
        Запись 'FAIL: карточка отправления не появилась'
        return [IntPtr]::Zero
    }

    $кнопкаКарточки = [AccWin]::FindButton($карточка, 'ОК')

    if ($кнопкаКарточки -eq [IntPtr]::Zero) {
        Запись 'FAIL: кнопка ОК карточки не найдена'
        return [IntPtr]::Zero
    }

    [AccWin]::Click($кнопкаКарточки)
    Запись 'Карточка: нажата ОК'

    return Wait-ОкноСЗаголовком $pid_ $целевойЗаголовок $таймаутШага
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


# Число элементов комбобокса «Шаблон конверта:» в открытом диалоге печати.
# Комбобокс определяется по метке: её прямоугольник, затем COMBOBOX в той же
# строке правее. CB_GETCOUNT = 0x0146.
function ЧислоШаблоновВДиалоге([IntPtr]$окноПечати) {
    $script:числоШаблонов = -1
    $метка = $null

    # Scriptblock-делегат не видит локальные переменные функции (PS 5.1):
    # общий список читается только через scope script:.
    $script:детиДиалога = New-Object System.Collections.ArrayList

    [AccWin]::EnumChildWindows($окноПечати, {
        param($child, $lp)
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($child, $cls, 256) | Out-Null
        $класс = $cls.ToString()
        # RECT — вложенный тип AccWin+RECT (структура внутри сниппета Add-Type).
        $r = New-Object 'AccWin+RECT'
        [void][AccWin]::GetWindowRect($child, [ref]$r)
        $прямоугольник = New-Object System.Drawing.Rectangle -ArgumentList $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top)
        $текст = [AccWin]::Title($child)
        [void]$script:детиДиалога.Add(@{ Hwnd = $child; Class = $класс; Rect = $прямоугольник; Text = $текст })
        return $true
    }, [IntPtr]::Zero) | Out-Null

    foreach ($ребёнок in $script:детиДиалога) {
        if ($ребёнок.Class -like '*STATIC*' -and $ребёнок.Text -match 'Шаблон конверта') {
            $метка = $ребёнок
            break
        }
    }

    if ($метка -eq $null) {
        Запись 'FAIL: метка «Шаблон конверта:» не найдена в диалоге печати'
        return -1
    }

    $лучший = $null
    $лучшийСдвиг = [int]::MaxValue

    foreach ($ребёнок in $script:детиДиалога) {
        # Строгая привязка: тот же ряд (метка заканчивается левее) и комбо
        # правее КОНЦА метки — иначе рядом оказывается комбобокс соседней
        # строки (принтеров) с отступом рядов ~35 px.
        if ($ребёнок.Class -like 'WindowsForms10.COMBOBOX*' -and
            $ребёнок.Rect.Left -gt ($метка.Rect.Right) -and
            [Math]::Abs($ребёнок.Rect.Top - $метка.Rect.Top) -lt 25 -and
            [Math]::Abs($ребёнок.Rect.Top - $метка.Rect.Top) -lt $лучшийСдвиг) {
            $лучший = $ребёнок
            $лучшийСдвиг = [Math]::Abs($ребёнок.Rect.Top - $метка.Rect.Top)
        }
    }

    if ($лучший -ne $null) {
        $рез = [IntPtr]::Zero
        [AccWin]::SendMessageTimeout($лучший.Hwnd, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero, 2, 1000, [ref]$рез) | Out-Null
        $script:числоШаблонов = [int]$рез
    }

    return $script:числоШаблонов
}


# Диагностика: перечень видимых окон верхнего уровня процесса harness.
function ДампОкон([uint32]$procId) {
    $строки = @()
    foreach ($окно in [AccWin]::WindowsOf($procId)) {
        if ([AccWin]::IsWindowVisible($окно)) {
            $заголовок = [AccWin]::Title($окно)
            $класс = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($окно, $класс, 256) | Out-Null
            $описание = "$заголовок($($класс.ToString()))"
            if ($класс.ToString() -like '#32770*') {
                $текстСообщения = ''
                [AccWin]::EnumChildWindows($окно, {
                    param($child, $lp)
                    $cls = New-Object System.Text.StringBuilder 256
                    [AccWin]::GetClassName($child, $cls, 256) | Out-Null
                    if ($cls.ToString() -like '*STATIC*') {
                        $t = [AccWin]::Title($child)
                        if (-not [string]::IsNullOrWhiteSpace($t)) { $script:текстСообщения = $t; return $false }
                    }
                    return $true
                }, [IntPtr]::Zero) | Out-Null
                $описание = "$описание[$текстСообщения]"
            }
            if (-not [string]::IsNullOrWhiteSpace($заголовок)) {
                $строки += $описание
            }
        }
    }
    return ($строки -join ' | ')
}


# Кнопка окна с ретраем: сразу после создания окна дочерние контролы могут
# ещё не существовать (пролог S5 2026-11-08: FindButton вернул Zero в 1 с).
function Кнопка-Ретрай([IntPtr]$окно, [string]$текст, [int]$секунд) {
    $дедлайн = (Get-Date).AddSeconds($секунд)

    while ((Get-Date) -lt $дедлайн) {
        $кнопка = [AccWin]::FindButton($окно, $текст)

        if ($кнопка -ne [IntPtr]::Zero) { return $кнопка }

        Start-Sleep -Milliseconds 300
    }

    return [IntPtr]::Zero
}

try {
    $блокировка = Enter-SharedTestLock

    # --- Sentinel и отпечатки ДО ------------------------------------------
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    $спулерДо = ЧислоЗаданийСпулера
    Запись "spooler jobs before: $спулерДо"

    # --- Запуск harness screens2 (профиль БЕЗ шаблонов) --------------------
    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'screens2' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    $pid_ = [uint32]$h.Proc.Id
    Запись "harness READY: $готовность"

    $гард = Get-HarnessLines $h | Where-Object { $_ -match '^GUARD PASS ' } | Select-Object -First 1
    Запись "guard line: $гард"

    # 0x0054 = SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW.
    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 30, 30, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    # ======================================================================
    # S1 «Отправленные» в пустом состоянии
    # ======================================================================
    Запись '## S1: вкладка «Отправленные» пустая'
    $результатS1 = Команда 'SETTAB 3' '^OK SETTAB 3'

    if ($результатS1) {
        $инфо = ЖдатьОтправленные 0

        if ($инфо -match 'строк=0 подсказка=True') {
            Захват $h.Hwnd (Join-Path $out '40-sent-empty.png') | Out-Null
            $размер1 = (Get-Item (Join-Path $out '40-sent-empty.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот S1: 40-sent-empty.png ($размер1 байт)"
            $этапы['S1 отправленные-пусто'] = ($размер1 -gt 0)
        } else {
            Запись "FAIL: пустое состояние не подтверждено: $инфо"
        }

        Команда 'SETTAB 0' '^OK SETTAB 0' | Out-Null
    }

    if ($env:P302_SCREENS2_FAULT -eq 'sent-empty') { $этапы['S1 отправленные-пусто'] = $false; Запись 'FAULT-INJECT: sent-empty' }

    # ======================================================================
    # S2 карточка отправления: длинные значения + ОТМЕНА
    # ======================================================================
    Запись '## S2: карточка отправления (длинные значения) → Отмена'
    $результатS2 = Команда 'SELECTLAST' '^OK SELECTLAST'

    if ($результатS2) {
        Send-HarnessCommand $h 'F5'
        $карточка2 = Wait-ОкноСЗаголовком $pid_ 'Карточка отправления' 12

        if ($карточка2 -ne [IntPtr]::Zero) {
            Захват $карточка2 (Join-Path $out '41-mailcard-longvalues.png') | Out-Null
            $размер2 = (Get-Item (Join-Path $out '41-mailcard-longvalues.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот S2: 41-mailcard-longvalues.png ($размер2 байт)"

            $кнопкаОтмена = [AccWin]::FindButton($карточка2, 'Отмена')

            if ($кнопкаОтмена -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопкаОтмена)
                Start-Sleep -Seconds 2
                $закрыто = ОкноЗакрыто $карточка2
                Запись "карточка закрыта Отменой: $закрыто"

                if ($закрыто) {
                    $строка2 = Команда 'MAILCOUNT' '^MAILCOUNT ' 10

                    if ($строка2 -match 'всего=0') {
                        $жив2 = [AccWin]::Responsive($h.Hwnd, 5000)
                        Запись "UI отвечает после Отмены: $жив2"
                        $этапы['S2 карточка-отмена'] = ($размер2 -gt 0 -and $жив2)
                    } else {
                        Запись "FAIL: Отмена создала запись: $строка2"
                    }
                }
            } else {
                Запись 'FAIL: кнопка Отмена карточки не найдена'
            }
        } else {
            Запись 'FAIL: карточка отправления не появилась (F5)'
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'mailcard') { $этапы['S2 карточка-отмена'] = $false; Запись 'FAULT-INJECT: mailcard' }

    # ======================================================================
    # S3 карточка + ОК → печать → Отмена → «Отправленные» с данными
    # ======================================================================
    Запись '## S3: карточка → ОК → печать → Отмена → вкладка «Отправленные»'
    $результатS3 = Команда 'SELECT' '^OK SELECT'

    if ($результатS3) {
        Send-HarnessCommand $h 'F5'
        $диалогПечати3 = ЦепочкаФ5 $pid_ 'Печать'

        if ($диалогПечати3 -ne [IntPtr]::Zero) {
            # Это диалог печати (кнопка «Печать»), а не MessageBox.
            if ([AccWin]::FindButton($диалогПечати3, 'Печать') -ne [IntPtr]::Zero) {
                Захват $диалогПечати3 (Join-Path $out '42-print-dialog.png') | Out-Null
                $размер3а = (Get-Item (Join-Path $out '42-print-dialog.png') -ErrorAction SilentlyContinue).Length

                $кнопкаОтменаПечати = [AccWin]::FindButton($диалогПечати3, 'Отмена')

                if ($кнопкаОтменаПечати -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопкаОтменаПечати)
                    Start-Sleep -Seconds 2
                    $печатьЗакрыта = ОкноЗакрыто $диалогПечати3
                    Запись "диалог печати закрыт Отменой: $печатьЗакрыта"

                    if ($печатьЗакрыта) {
                        $строка3 = Команда 'MAILCOUNT' '^MAILCOUNT ' 10

                        if ($строка3 -match 'всего=1') {
                            # Вкладка «Отправленные» обязана показать строку.
                            Команда 'SETTAB 3' '^OK SETTAB 3' | Out-Null
                            $инфо3 = ЖдатьОтправленные 1

                            if ($инфо3 -match 'строк=1 ') {
                                Захват $h.Hwnd (Join-Path $out '43-sent-with-data.png') | Out-Null
                                $размер3б = (Get-Item (Join-Path $out '43-sent-with-data.png') -ErrorAction SilentlyContinue).Length
                                Запись "скриншот S3: 43-sent-with-data.png ($размер3б байт)"
                                $этапы['S3 отправленные-с-данными'] = ($размер3а -gt 0 -and $размер3б -gt 0)
                                Команда 'SETTAB 0' '^OK SETTAB 0' | Out-Null
                            } else {
                                Запись "FAIL: вкладка «Отправленные» не показывает запись: $инфо3"
                            }
                        } else {
                            Запись "FAIL: отправление не создано: $строка3"
                        }
                    }
                } else {
                    Запись 'FAIL: кнопка Отмена диалога печати не найдена'
                }
            } else {
                Запись 'FAIL: вместо диалога печати открылось другое окно «Печать»'
            }
        } else {
            Запись 'FAIL: диалог печати не открылся после ОК карточки'
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'sent-data') { $этапы['S3 отправленные-с-данными'] = $false; Запись 'FAULT-INJECT: sent-data' }

    # ======================================================================
    # S4 «нет шаблона»: подтверждение печати без шаблона
    # ======================================================================
    Запись '## S4: печать без шаблона — диалог без шаблонов, корректная отмена'
    $результатS4 = Команда 'SELECT' '^OK SELECT'

    if ($результатS4) {
        Send-HarnessCommand $h 'F5'
        $диалогПечати4 = ЦепочкаФ5 $pid_ 'Печать'

        if ($диалогПечати4 -ne [IntPtr]::Zero -and [AccWin]::FindButton($диалогПечати4, 'Печать') -ne [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 600
            $числоШаблонов = ЧислоШаблоновВДиалоге $диалогПечати4
            Запись "число шаблонов в диалоге печати: $числоШаблонов"

            Захват $диалогПечати4 (Join-Path $out '45-no-template-print-dialog.png') | Out-Null
            $размер4 = (Get-Item (Join-Path $out '45-no-template-print-dialog.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот S4: 45-no-template-print-dialog.png ($размер4 байт)"

            if ($числоШаблонов -eq 0) {
                $кнопкаОтменаПечати4 = [AccWin]::FindButton($диалогПечати4, 'Отмена')

                if ($кнопкаОтменаПечати4 -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопкаОтменаПечати4)
                    Start-Sleep -Seconds 2
                    $печать4Закрыта = ОкноЗакрыто $диалогПечати4
                    Запись "диалог печати закрыт Отменой: $печать4Закрыта"

                    if ($печать4Закрыта) {
                        $строка4 = Команда 'MAILCOUNT' '^MAILCOUNT ' 10

                        if ($строка4 -match 'всего=2') {
                            $жив4 = [AccWin]::Responsive($h.Hwnd, 5000)
                            $предпросмотр4 = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
                            Запись "UI отвечает: $жив4; предпросмотр не открыт: $($предпросмотр4 -eq [IntPtr]::Zero)"
                            $этапы['S4 нет-шаблона'] = ($размер4 -gt 0 -and $жив4 -and $предпросмотр4 -eq [IntPtr]::Zero)
                        } else {
                            Запись "FAIL: MAILCOUNT после S4: $строка4"
                        }
                    } else {
                        Запись 'FAIL: диалог печати S4 не закрылся'
                    }
                } else {
                    Запись 'FAIL: кнопка Отмена диалога печати не найдена'
                }
            } else {
                Запись "FAIL: в профиле без шаблонов диалог показывает $числоШаблонов шаблонов"
            }
        } else {
            Запись 'FAIL: диалог печати S4 не открылся'
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'no-template') { $этапы['S4 нет-шаблона'] = $false; Запись 'FAULT-INJECT: no-template' }

    # ======================================================================
    # S5 «блокировка размера бумаги»: шаблон больше печатной области
    # ======================================================================
    Запись '## S5: блокировка размера бумаги (F7 — прямая печать до конвейера валидации)'
    $результатS5 = Команда 'SEEDTEMPLATE' '^OK SEEDTEMPLATE'

    if ($результатS5) {
        $результатS5 = Команда 'SELECT' '^OK SELECT'
    }

    if ($результатS5) {
        Send-HarnessCommand $h 'F7'

        # F6/F7-цепочка: карточка → ОК → MessageBox «Отправленных сформировано:
        # N» (тоже «Печать») → ОК → диалог печати. Оба окна названы «Печать»,
        # различаются кнопками (ОК против Печать/Отмена).
        $диалогПечати5 = [IntPtr]::Zero
        $дедлайнЦепочки5 = (Get-Date).AddSeconds(30)
        $карточка5Закрыта = $false

        while ((Get-Date) -lt $дедлайнЦепочки5) {
            if (-not $карточка5Закрыта) {
                $карточка5 = Wait-ОкноСЗаголовком $pid_ 'Карточка отправления' 5

                if ($карточка5 -ne [IntPtr]::Zero) {
                    $кн5 = [AccWin]::FindButton($карточка5, 'ОК')

                    if ($кн5 -ne [IntPtr]::Zero) {
                        [AccWin]::Click($кн5)
                        Запись 'Карточка S5: нажата ОК'
                        $карточка5Закрыта = $true
                    }
                }

                Start-Sleep -Milliseconds 400
                continue
            }

            $окно5 = [AccWin]::FindByTitle($pid_, 'Печать')

            if ($окно5 -ne [IntPtr]::Zero) {
                if ([AccWin]::FindButton($окно5, 'ОК') -ne [IntPtr]::Zero) {
                    # MessageBox о сформированных отправлениях.
                    [AccWin]::Click([AccWin]::FindButton($окно5, 'ОК'))
                    Запись 'MessageBox сформировано: ОК'
                    Start-Sleep -Milliseconds 600
                    continue
                }

                $кнопкаПечать5 = [AccWin]::FindButton($окно5, 'Печать')

                if ($кнопкаПечать5 -ne [IntPtr]::Zero) {
                    $диалогПечати5 = $окно5
                    break
                }
            }

            Start-Sleep -Milliseconds 400
        }

        $кнопкаПечать5 = [IntPtr]::Zero

        if ($диалогПечати5 -ne [IntPtr]::Zero) {
            $кнопкаПечать5 = Кнопка-Ретрай $диалогПечати5 'Печать' 5
        }

        if ($кнопкаПечать5 -ne [IntPtr]::Zero) {
            [AccWin]::Click($кнопкаПечать5)
            Запись 'диалог печати S5: подтверждено «Печать»'

            # Без предпросмотра задание идёт прямо в конвейер. Цепочка модальных
            # окон может быть длиной в три шага (подача конвертов → предупреждение
            # «Конверт слишком большой» → «Проверка размера бумаги»), поэтому
            # окна закрываются по мере появления, а не фиксированной очередью.
            $блокировка5 = [IntPtr]::Zero
            $дедлайн5 = (Get-Date).AddSeconds(30)

            while ((Get-Date) -lt $дедлайн5) {
                $блокировка5 = [AccWin]::FindByTitle($pid_, 'Проверка размера бумаги')

                if ($блокировка5 -ne [IntPtr]::Zero) { break }

                $подача5 = [AccWin]::FindByTitle($pid_, 'Подача конвертов')

                if ($подача5 -ne [IntPtr]::Zero) {
                    $кнопкаПродолжить5 = [AccWin]::FindButton($подача5, 'Продолжить')

                    if ($кнопкаПродолжить5 -ne [IntPtr]::Zero) {
                        [AccWin]::Click($кнопкаПродолжить5)
                        Запись 'подача S5: Продолжить'
                    }

                    Start-Sleep -Milliseconds 800
                    continue
                }

                $большой5 = [AccWin]::FindByTitle($pid_, 'Конверт слишком большой для выбранного принтера')

                if ($большой5 -ne [IntPtr]::Zero) {
                    Захват $большой5 (Join-Path $out '46a-oversize-warning.png') | Out-Null
                    $размер5а = (Get-Item (Join-Path $out '46a-oversize-warning.png') -ErrorAction SilentlyContinue).Length
                    Запись "скриншот предупреждения: 46a-oversize-warning.png ($размер5а байт)"

                    $кнопкаОкБольшой5 = [AccWin]::FindButton($большой5, 'ОК')

                    if ($кнопкаОкБольшой5 -ne [IntPtr]::Zero) {
                        [AccWin]::Click($кнопкаОкБольшой5)
                        Запись 'предупреждение о размере: ОК'
                    }

                    Start-Sleep -Milliseconds 800
                    continue
                }

                Start-Sleep -Milliseconds 500
            }

            if ($блокировка5 -ne [IntPtr]::Zero) {
                Захват $блокировка5 (Join-Path $out '46-paper-size-blocked.png') | Out-Null
                $размер5 = (Get-Item (Join-Path $out '46-paper-size-blocked.png') -ErrorAction SilentlyContinue).Length
                Запись "скриншот S5: 46-paper-size-blocked.png ($размер5 байт)"

                $кнопкаОтмена5 = [AccWin]::FindButton($блокировка5, 'Отмена')

                if ($кнопкаОтмена5 -ne [IntPtr]::Zero) {
                    [AccWin]::Click($кнопкаОтмена5)
                    Start-Sleep -Seconds 2
                    $блокировкаЗакрыта = ОкноЗакрыто $блокировка5
                    Запись "диалог блокировки закрыт Отменой: $блокировкаЗакрыта"

                    if ($блокировкаЗакрыта) {
                        $жив5 = [AccWin]::Responsive($h.Hwnd, 5000)
                        Запись "UI отвечает после блокировки: $жив5"
                        $этапы['S5 блокировка-размера'] = ($размер5 -gt 0 -and $жив5)
                    }
                } else {
                    Запись 'FAIL: кнопка Отмена блокировки не найдена'
                }
            } else {
                Запись 'FAIL: диалог «Проверка размера бумаги» не появился'
                Запись "окна процесса: $(ДампОкон $pid_)"
            }
        } else {
            Запись 'FAIL: диалог печати S5 не открылся'
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'paper-dialog') { $этапы['S5 блокировка-размера'] = $false; Запись 'FAULT-INJECT: paper-dialog' }

    # ======================================================================
    # S6 клипарт: смоук-регрессия UX3-01
    # ======================================================================
    Запись '## S6: выбор клипарта — открытие, выбор значка, повторное открытие'
    $результатS6 = Команда 'SHOW clipartpicker' '^OK SHOW clipartpicker'

    if ($результатS6) {
        Start-Sleep -Milliseconds 800
        $выбор6 = Команда 'CLIPSELECT' '^OK CLIPSELECT'

        if ($выбор6 -match 'items=10 значки=10 выбрано=1 активна=True нажато=True закрыт=True') {
            # Повторное открытие: Dispose прежнего экземпляра не ломает ImageList.
            $повтор6 = Команда 'SHOW clipartpicker' '^OK SHOW clipartpicker'

            if ($повтор6) {
                Start-Sleep -Milliseconds 800
                Команда 'HIDE' '^OK HIDE' | Out-Null
                $жив6 = [AccWin]::Responsive($h.Hwnd, 5000)
                Запись "UI отвечает после клипарта: $жив6"
                $этапы['S6 клипарт-смоук'] = $жив6
            }
        } else {
            Запись "FAIL: смоук клипарта: $выбор6"
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'clipart') { $этапы['S6 клипарт-смоук'] = $false; Запись 'FAULT-INJECT: clipart' }

    # ======================================================================
    # S7 сброс вида таблиц
    # ======================================================================
    Запись '## S7: настройка вида таблиц — сброс к умолчаниям'
    $результатS7 = Команда 'SHOW gridappearance' '^OK SHOW gridappearance'

    if ($результатS7) {
        Start-Sleep -Milliseconds 800
        $сброс7 = Команда 'GRIDRESET' '^OK GRIDRESET'

        if ($сброс7 -match 'нажато=True до=\[focus=255 ' -and $сброс7 -match 'после=\[focus=4227327 selected=6205183 hint=3723516 zebra=False font=8 row=17\]') {
            Захват $h.Hwnd (Join-Path $out '47-grid-appearance-reset.png') | Out-Null
            $размер7 = (Get-Item (Join-Path $out '47-grid-appearance-reset.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот S7: 47-grid-appearance-reset.png ($размер7 байт)"
            Команда 'HIDE' '^OK HIDE' | Out-Null
            $этапы['S7 сброс-вида-таблиц'] = ($размер7 -gt 0)
        } else {
            Запись "FAIL: сброс вида таблиц: $сброс7"
        }
    }

    if ($env:P302_SCREENS2_FAULT -eq 'gridreset') { $этапы['S7 сброс-вида-таблиц'] = $false; Запись 'FAULT-INJECT: gridreset' }

    # ======================================================================
    # S8 редактор бланка (runtime-смоук без GDI+ исключений)
    # ======================================================================
    Запись '## S8: редактор бланка — открытие и закрытие'
    $результатS8 = Команда 'SHOW blankeditor' '^OK SHOW blankeditor'

    if ($результатS8) {
        Start-Sleep -Milliseconds 800
        # S03-ревью Q3-2: раньше снималось $h.Hwnd (главное окно), из-за чего
        # 48-blank-editor.png был байт-идентичен 47-grid-appearance-reset.png.
        # Кадр должен показывать сам диалог редактора бланка.
        $окноРедактора = Wait-ОкноСЗаголовком $pid_ 'Редактор бланков' 5
        if ($окноРедактора -eq [IntPtr]::Zero) {
            Запись 'FAIL: окно редактора бланков не найдено для снимка'
            $этапы['S8 редактор-бланка'] = $false
        } else {
            Захват $окноРедактора (Join-Path $out '48-blank-editor.png') | Out-Null
            $размер8 = (Get-Item (Join-Path $out '48-blank-editor.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот S8: 48-blank-editor.png ($размер8 байт)"
        }
        Команда 'HIDE' '^OK HIDE' | Out-Null
        $жив8 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после редактора бланка: $жив8"
        $этапы['S8 редактор-бланка'] = ($размер8 -gt 0 -and $жив8)
    }

    # ======================================================================
    # S9 группировка строк адресной книги (функция 10)
    # ======================================================================
    Запись '## S9: группировка строк — применение и снятие'
    $группаОтвет = Команда 'GROUP Индекс' '^OK GROUP column=Индекс headers=2'

    if ($группаОтвет -match 'headers=2') {
        Start-Sleep -Milliseconds 600
        # Снимается главное окно: заголовки групп видны в таблице получателей
        # (Легаси-сценарий: «− 101000 (1)», «− 125009 (2)»).
        Захват $h.Hwnd (Join-Path $out '49-grouping-by-index.png') | Out-Null
        $размер9 = (Get-Item (Join-Path $out '49-grouping-by-index.png') -ErrorAction SilentlyContinue).Length
        Запись "скриншот S9: 49-grouping-by-index.png ($размер9 байт)"
        $сброс9 = Команда 'GROUP none' '^OK GROUP column=none headers=0'
        $этапы['S9 группировка-строк'] = ($сброс9 -match 'headers=0' -and $размер9 -gt 0)
    } else {
        # fail-closed (ревью S07 F2): ключ предобъявлен $false выше; явно
        # присваиваем $false и в этой ветке — итоговые условия видят этап.
        $этапы['S9 группировка-строк'] = $false
        Запись "FAIL: группировка: $группаОтвет"
    }

    if ($env:P302_SCREENS2_FAULT -eq 'grouping') { $этапы['S9 группировка-строк'] = $false; Запись 'FAULT-INJECT: grouping' }

    # ======================================================================
    # Итог этапов + остановка harness
    # ======================================================================
    foreach ($ключ in $этапы.Keys) {
        if (-not $этапы[$ключ]) { $успех = $false }
        Запись "ЭТАП [$ключ]: $($этапы[$ключ])"
    }

    if ($env:P302_SCREENS2_FAULT -eq 'final-delta') { Запись 'FAULT-INJECT: final-delta (обрабатывается в finally)' }

    $остановка = Invoke-HarnessStop $h 'screens2'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) остатки=[$($остановка.Результат.RemainingPids -join ';')]"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'screens2-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

    # --- Спулер и отпечатки ПОСЛЕ ------------------------------------------
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

    foreach ($ключ in $этапы.Keys) {
        $условия["этап $ключ"] = $этапы[$ключ]
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
            $остановкаФинал = Invoke-HarnessStop $h 'screens2-finally'

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

                if ($env:P302_SCREENS2_FAULT -eq 'final-delta') {
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

        $fault = $env:P302_SCREENS2_FAULT
        $faultПрименён = @($журнал | Where-Object { $_ -match 'FAULT-INJECT:' }).Count -gt 0

        if ($сценарийПройден -and $fault) {
            $файлEvidence = "screens2-fault-$fault.md"
            $заголовокEvidence = "Screens-2 fault-injection: $fault"
            Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
        } elseif ($сценарийПройден) {
            $файлEvidence = 'screens2-happy-evidence.md'
            $заголовокEvidence = 'Screens-2 runtime-доказательство (happy path)'
        } elseif ($faultПрименён) {
            $файлEvidence = "screens2-fault-$fault.md"
            $заголовокEvidence = "Screens-2 fault-injection: $fault"
        } else {
            $файлEvidence = 'screens2-failure-evidence.md'
            $заголовокEvidence = 'Screens-2 runtime-доказательство (FAIL)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
        Запись "SAFETY-FAIL: ошибка финализации: $($_.Exception.Message)"
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'screens2'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0) -and (-not $env:P302_SCREENS2_FAULT)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: screens2'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if ($корень) { $корень } else { 'не создан' })"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "FAULT: $(if ($env:P302_SCREENS2_FAULT) { $env:P302_SCREENS2_FAULT } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($faultПрименён) {
        Запись "PASS_OF_CONTRACT: $(-not $happyПройден) (fault — инъекция корректно обнаружена, скрипт не вернёт 0)"
    }

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'screens-2 поверхности' $журнал

    $маркеры = @('Режим: screens2', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:')
    if ($faultПрименён) { $маркеры += 'PASS_OF_CONTRACT:' }
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'SCREENS2 PASS: True'
    } else {
        $exitCode = 1
        Запись 'SCREENS2 PASS: False'
    }
}

exit $exitCode
