# Сценарий DPI-симуляции (stage3.1, Фаза 2): 125 % и 150 % через РЕАЛЬНЫЙ
# конвейер WinForms PerMonitorV2 — формам отправляется WM_DPICHANGED, и
# приложение само перемасштабирует шрифты, контролы и позицию.
#
# ВАЖНО (честный статус): это DPI_SIMULATION, а не OS-DPI-стенд. Общесистемный
# масштаб host-машины не меняется (запрещено менять настройки машины); поэтому
# флаги DPI_SIMULATION_125_VERIFIED / DPI_SIMULATION_150_VERIFIED, а
# DPI_RUNTIME_125/150 остаются NOT_RUN_ON_HOST.
#
# Проверки на каждый масштаб (критерии §8.3):
#   - LAYOUTCHECK каждой видимой формы: нулевых кнопок = 0;
#   - границы формы целиком в виртуальном экране (вЭкране=True);
#   - DeviceDpi формы равен цели (120 / 144);
#   - главное окно отвечает (WM_NULL);
#   - скриншоты сняты (байт > 0);
#   - спулер 0→0, профиль не изменён, cleanup.
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
$файлEvidence = 'dpi-failure-evidence.md'
$заголовокEvidence = 'DPI-симуляция (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null

$этапы = [ordered]@{
    'D1 dpi-125-главное-окно'   = $false
    'D2 dpi-125-карточка'       = $false
    'D3 dpi-150-главное-окно'   = $false
    'D4 dpi-150-карточка'       = $false
    'D5 возврат-100-процентов'  = $false
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

# Собирает LAYOUTCHECK-строки и проверяет: dpi=цель, нулевыхКнопок=0,
# вЭкране=True для каждой формы. Возвращает $true при полном успехе.
function ПроверитьКомпоновкуDpi([int]$цельDpi) {
    # Только НОВЫЕ строки stdout: Get-HarnessLines накапливает весь вывод,
    # и без окна чтения в проверку попадали строки прошлого вызова
    # (сталкер-строки чужих форм и DPI — регрессия 2026-11-08).
    $грань = @(Get-HarnessLines $h).Count

    Send-HarnessCommand $h 'LAYOUTCHECK'

    if (-not (Read-HarnessLines $h '^LAYOUTCHECK ' 15)) {
        Запись 'FAIL: LAYOUTCHECK не получен'
        return $false
    }

    $все = @(Get-HarnessLines $h)
    # Формат строки с маркером раскладки (свежесть бинарника, ENV3-01).
    $строки = @($все[$грань..($все.Count - 1)] | Where-Object { $_ -match '^LAYOUTCHECK маркер=lay3-01-top-dock форма=' })

    if ($строки.Count -eq 0) {
        Запись 'FAIL: строки LAYOUTCHECK отсутствуют'
        return $false
    }

    $всёХорошо = $true

    foreach ($строка in $строки) {
        Запись "LAYOUT: $($строка -replace '^LAYOUTCHECK ', '')"

        if ($строка -notmatch 'маркер=lay3-01-top-dock ') {
            $всёХорошо = $false
            Запись 'FAIL: маркер раскладки не свежий (ENV3-01 — устаревший бинарник)'
        }

        if ($строка -notmatch 'нулевыхКнопок=0 ') { $всёХорошо = $false }
        if ($строка -notmatch 'вЭкране=True') { $всёХорошо = $false }
        if ($строка -notmatch ' вне=0 ') { $всёХорошо = $false }
        if ($строка -notmatch "dpi=$цельDpi ") { $всёХорошо = $false }
    }

    return $всёХорошо
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

    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"

    $спулерДо = ЧислоЗаданийСпулера
    Запись "spooler jobs before: $спулерДо"

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

    # Компактное окно ДО масштабирования: после 150 % форма остаётся в экране.
    # 0x0054 = SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW.
    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 10, 10, 1000, 700, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    # --- D1: 125 % главное окно -------------------------------------------
    Запись '## D1: 125 % (DPI 120) — главное окно'
    $результатD1 = Команда 'SETDPI 125' '^OK SETDPI 125'

    if ($результатD1) {
        # Рескал сдвигает форму (центрирование suggested-rect): вернуть в экран.
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 0, 0, 0x0015) | Out-Null
        Start-Sleep -Seconds 2
        $компоновка = ПроверитьКомпоновкуDpi 120
        Запись "компоновка 125 %: $компоновка"

        Захват $h.Hwnd (Join-Path $out '50-main-dpi125.png') | Out-Null
        $размер50 = (Get-Item (Join-Path $out '50-main-dpi125.png') -ErrorAction SilentlyContinue).Length
        Запись "скриншот D1: 50-main-dpi125.png ($размер50 байт)"

        $жив1 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает на 125 %: $жив1"
        $этапы['D1 dpi-125-главное-окно'] = ($компоновка -and $размер50 -gt 0 -and $жив1)
    }

    # --- D2: 125 % карточка контрагента ------------------------------------
    Запись '## D2: 125 % — карточка контрагента'
    $результатD2 = Команда 'SHOW counterparty' '^OK SHOW counterparty'

    if ($результатD2) {
        # Рескал 125 % сдвигает и диалог: вернуть в видимую область.
        if ($результатD2 -match 'counterparty (\d+)') {
            $hwndКарточки2 = [IntPtr][long]$Matches[1]
            [AccWin]::SetWindowPos($hwndКарточки2, [IntPtr]::Zero, 20, 20, 0, 0, 0x0015) | Out-Null
        }

        Start-Sleep -Seconds 2
        $компоновка2 = ПроверитьКомпоновкуDpi 120
        Запись "компоновка 125 % (главное окно при открытой карточке; строки LAYOUT диалога в буфер не попадают): $компоновка2"

        # Диалог открывается для СУЩЕСТВУЮЩЕГО контрагента из демо-засева:
        # заголовок «Карточка контрагента» (run-screens.ps1 stage3).
        $карточка2 = [AccWin]::FindByTitle($pid_, 'Карточка контрагента')

        if ($карточка2 -ne [IntPtr]::Zero) {
            Захват $карточка2 (Join-Path $out '51-counterparty-dpi125.png') | Out-Null
            $размер51 = (Get-Item (Join-Path $out '51-counterparty-dpi125.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот D2: 51-counterparty-dpi125.png ($размер51 байт)"
            $этапы['D2 dpi-125-карточка'] = ($компоновка2 -and $размер51 -gt 0)
        } else {
            Запись 'FAIL: карточка контрагента не найдена на 125 %'
        }

        Команда 'HIDE' '^OK HIDE' | Out-Null
    }

    # --- D3: 150 % главное окно --------------------------------------------
    Запись '## D3: 150 % (DPI 144) — главное окно'
    $результатD3 = Команда 'SETDPI 150' '^OK SETDPI 150'

    if ($результатD3) {
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 0, 0, 0x0015) | Out-Null
        Start-Sleep -Seconds 2
        $компоновка3 = ПроверитьКомпоновкуDpi 144
        Запись "компоновка 150 %: $компоновка3"

        Захват $h.Hwnd (Join-Path $out '52-main-dpi150.png') | Out-Null
        $размер52 = (Get-Item (Join-Path $out '52-main-dpi150.png') -ErrorAction SilentlyContinue).Length
        Запись "скриншот D3: 52-main-dpi150.png ($размер52 байт)"

        $жив3 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает на 150 %: $жив3"
        $этапы['D3 dpi-150-главное-окно'] = ($компоновка3 -and $размер52 -gt 0 -and $жив3)
    }

    # --- D4: 150 % карточка отправления ------------------------------------
    Запись '## D4: 150 % — карточка отправления (пустая форма)'
    $результатD4 = Команда 'SHOW mailedit' '^OK SHOW mailedit'

    if ($результатD4) {
        if ($результатD4 -match 'mailedit (\d+)') {
            $hwndКарточки4 = [IntPtr][long]$Matches[1]
            [AccWin]::SetWindowPos($hwndКарточки4, [IntPtr]::Zero, 20, 20, 0, 0, 0x0015) | Out-Null
        }

        Start-Sleep -Seconds 2
        $компоновка4 = ПроверитьКомпоновкуDpi 144
        Запись "компоновка 150 % (главное окно при открытой карточке; строки LAYOUT диалога в буфер не попадают): $компоновка4"

        $карточка4 = [AccWin]::FindByTitle($pid_, 'Карточка отправления')

        if ($карточка4 -ne [IntPtr]::Zero) {
            Захват $карточка4 (Join-Path $out '53-mailcard-dpi150.png') | Out-Null
            $размер53 = (Get-Item (Join-Path $out '53-mailcard-dpi150.png') -ErrorAction SilentlyContinue).Length
            Запись "скриншот D4: 53-mailcard-dpi150.png ($размер53 байт)"
            $этапы['D4 dpi-150-карточка'] = ($компоновка4 -and $размер53 -gt 0)
        } else {
            Запись 'FAIL: карточка отправления не найдена на 150 %'
        }

        Команда 'HIDE' '^OK HIDE' | Out-Null
    }

    # --- D5: возврат к 100 % -------------------------------------------------
    Запись '## D5: возврат к 100 % (DPI 96)'
    $результатD5 = Команда 'SETDPI 100' '^OK SETDPI 100'

    if ($результатD5) {
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 10, 10, 0, 0, 0x0015) | Out-Null
        Start-Sleep -Seconds 2
        $компоновка5 = ПроверитьКомпоновкуDpi 96
        Запись "компоновка 100 %: $компоновка5"

        Захват $h.Hwnd (Join-Path $out '54-main-dpi100.png') | Out-Null
        $размер54 = (Get-Item (Join-Path $out '54-main-dpi100.png') -ErrorAction SilentlyContinue).Length
        Запись "скриншот D5: 54-main-dpi100.png ($размер54 байт)"

        $жив5 = [AccWin]::Responsive($h.Hwnd, 5000)
        $этапы['D5 возврат-100-процентов'] = ($компоновка5 -and $жив5 -and $размер54 -gt 0)
    }

    foreach ($ключ in $этапы.Keys) {
        if (-not $этапы[$ключ]) { $успех = $false }
        Запись "ЭТАП [$ключ]: $($этапы[$ключ])"
    }

    $остановка = Invoke-HarnessStop $h 'dpi'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит pid=$($остановка.Результат.Pid) TreeOk=$($остановка.Результат.TreeOk) остатки=[$($остановка.Результат.RemainingPids -join ';')]"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'dpi-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    Запись "harness exit code: $кодВыходаHarness; killed: $убит; HarnessNotExited: $harnessNotExited"

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
            $остановкаФинал = Invoke-HarnessStop $h 'dpi-finally'

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

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'dpi'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
        Запись "SAFETY-FAIL: освобождение блокировки не подтверждено: $($блокировкаРезультат.Error)"
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: dpi-симуляция'
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
        $файлEvidence = 'dpi-happy-evidence.md'
        $заголовокEvidence = 'DPI-симуляция (happy path)'
    }

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'DPI-симуляция' $журнал

    $маркеры = @('Режим: dpi-симуляция', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:', 'HARNESS NOT EXITED:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'DPI PASS: True'
    } else {
        $exitCode = 1
        Запись 'DPI PASS: False'
    }
}

exit $exitCode
