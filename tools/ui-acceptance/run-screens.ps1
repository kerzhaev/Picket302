# Драйвер снимков экранов матрицы визуального сравнения (серия stage3
# 2026-11-08). Открывает рабочие диалоги НАСТОЯЩЕГО приложения на
# изолированном профиле и снимает их — снимки реальные, не постановочные.
#
# Ограничения (scope): печать не выполняется, COM не запускается, реальный
# профиль %AppData%\Picket302 не изменяется (fingerprint до/после, sentinel).
#
# Fail-closed: любое нарушение условия → ненулевой exit code.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = Get-EvidenceRoot
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
$файлEvidence = 'screens-failure-evidence.md'
$заголовокEvidence = 'Screens runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null
$снято = [System.Collections.Generic.List[string]]::new()

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

function Wait-Окно([uint32]$procId, [string]$заголовок, [int]$секунд) {
    $deadline = (Get-Date).AddSeconds($секунд)

    while ((Get-Date) -lt $deadline) {
        $окно = [AccWin]::FindByTitle($procId, $заголовок)
        if ($окно -ne [IntPtr]::Zero) { return $окно }
        Start-Sleep -Milliseconds 200
    }

    return [IntPtr]::Zero
}

function Снять([IntPtr]$окно, [string]$имяФайла) {
    if ($окно -eq [IntPtr]::Zero) { return 0 }

    $путь = Join-Path $out $имяФайла

    # Дать окну завершить первую отрисовку (значки ImageList рисуются на
    # первом WM_PAINT): ранний BitBlt захватывает непрокрашенную поверхность.
    Start-Sleep -Milliseconds 800
    [AccWin]::Capture($окно, $путь) | Out-Null
    Start-Sleep -Milliseconds 300

    $размер = 0
    if (Test-Path $путь) { $размер = (Get-Item $путь).Length }

    if ($размер -gt 0) {
        $снято.Add($имяФайла)
        Запись "снимок: $имяФайла ($размер байт)"
    } else {
        Запись "СНИМОК ПУСТ: $имяФайла"
    }

    return $размер
}

# Диагностика зависания команды SHOW: перечислить ВСЕ видимые окна процесса
# и снять unexpected-модалки. Модальный диалог исключения блокирует поток UI
# молча — без дампа окон причина невидима (регрессия 2026-11-08).
function ДампатьОкна([uint32]$procId, [string]$метка) {
    foreach ($w in [AccWin]::WindowsOf($procId)) {
        if (-not [AccWin]::IsWindowVisible($w)) { continue }

        $clsW = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($w, $clsW, 256) | Out-Null
        $классОкна = $clsW.ToString()
        $заголовокW = [AccWin]::Title($w)
        Запись "WINDOW-DUMP: hwnd=$w class='$классОкна' title='$заголовокW'"

        if ($классОкна -eq '#32770') {
            $файлДампа = 'diag-' + $метка + '-' + (Get-Date -Format 'HHmmss') + '.png'
            [AccWin]::Capture($w, (Join-Path $out $файлДампа)) | Out-Null
            Запись "WINDOW-DUMP скриншот: $файлДампа"
        }
    }
}

try {
    # Capture вызывается до финальной записи evidence. Поэтому каталог
    # должен существовать заранее, в том числе при P302_EVIDENCE_ROOT,
    # указывающем на новый временный корень. Иначе GDI+ Bitmap.Save даёт
    # неинформативный «A generic error occurred».
    New-Item -ItemType Directory -Force -Path $out -ErrorAction Stop | Out-Null

    $блокировка = Enter-SharedTestLock

    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'screens' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    $pid_ = [uint32]$h.Proc.Id
    Запись "harness READY: $готовность"

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    # --- Главное окно: 1280x720 и 1000x700 -------------------------------
    foreach ($размер in @(@(1280, 720), @(1000, 700))) {
        # 0x0054 = SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW.
        # Окно видно для собственного BitBlt, но не становится foreground и
        # не поднимается поверх пользовательских окон.
        [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, $размер[0], $размер[1], 0x0054) | Out-Null
        Start-Sleep -Seconds 2

        $файл = "main-{0}x{1}.png" -f $размер[0], $размер[1]
        $байт = Снять $h.Hwnd $файл
        & $всёОк ($байт -gt 0) "снимок главного окна $файл"
    }

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 1

    # --- Диалоги рабочих поверхностей -------------------------------------
    $диалоги = @(
        @{ Ключ = 'counterparty'; Заголовок = 'Карточка контрагента'; Файл = '05-counterparty-card.png' },
        @{ Ключ = 'mailedit'; Заголовок = 'Карточка отправления'; Файл = '06-mail-card.png' },
        @{ Ключ = 'gridappearance'; Заголовок = 'Настройка вида таблиц'; Файл = '07-grid-appearance.png' },
        @{ Ключ = 'blankeditor'; Заголовок = 'Редактор бланков'; Файл = '08-blank-editor.png' },
        @{ Ключ = 'clipartpicker'; Заголовок = 'Клипарт'; Файл = '09-clipart-picker.png' },
        @{ Ключ = 'distribution'; Заголовок = 'Новая рассылка'; Файл = '10-distribution-editor.png' }
    )

    foreach ($диалог in $диалоги) {
        Запись "## снимок: $($диалог.Ключ)"

        Send-HarnessCommand $h ("SHOW " + $диалог.Ключ)

        if (-not (Read-HarnessLines $h '^OK SHOW ' 15)) {
            foreach ($строка in (Get-HarnessLines $h | Select-Object -Last 12)) { Запись "HARNESS-OUT: $строка" }
            ДампатьОкна $pid_ $диалог.Ключ
            & $всёОк $false "диалог $($диалог.Ключ) не открылся"
            continue
        }

        $окно = Wait-Окно $pid_ $диалог.Заголовок 15

        if ($окно -ne [IntPtr]::Zero) {
            $байт = Снять $окно $диалог.Файл
            & $всёОк ($байт -gt 0) "снимок $($диалог.Файл)"
        } else {
            & $всёОк $false "окно «$($диалог.Заголовок)» не найдено для снимка"
        }

        Send-HarnessCommand $h 'HIDE'
        [void](Read-HarnessLines $h '^OK HIDE' 10)
        Start-Sleep -Milliseconds 500
    }

    # --- Остановка harness -------------------------------------------------
    $остановка = Invoke-HarnessStop $h 'screens'

    foreach ($флаг in $остановка.Флаги) {
        ДобавитьФлагОчистки $флаг
        Запись "SAFETY-FLAG: $флаг"
    }

    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит TreeOk=$($остановка.Результат.TreeOk)"
    }

    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'screens-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    # --- Условия -----------------------------------------------------------
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    $профильОчищен = -not (Test-Path $корень)

    Запись "снято снимков: $($снято.Count)"
    foreach ($имя in $снято) { Запись "  - $имя" }

    $условия = [ordered]@{
        'снято не менее 8 снимков' = ($снято.Count -ge 8)
        'временный профиль удалён' = $профильОчищен
        'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
        'новых файлов реального профиля нет' = ($дельта.New.Count -eq 0)
        'изменённых файлов реального профиля нет' = ($дельта.Changed.Count -eq 0)
        'удалённых файлов реального профиля нет' = ($дельта.Removed.Count -eq 0)
        'harness exit 0' = ($кодВыходаHarness -eq 0)
        'harness не убит' = (-not $убит)
        'harness завершён (bounded)' = (-not $harnessNotExited)
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
            $остановкаФинал = Invoke-HarnessStop $h 'screens-finally'
            foreach ($флаг in $остановкаФинал.Флаги) {
                ДобавитьФлагОчистки $флаг
                Запись "SAFETY-FLAG: $флаг"
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

                if ($финальнаяДельтаNew -ne 0) { ДобавитьФлагОчистки 'final-delta-new' }
                if ($финальнаяДельтаChanged -ne 0) { ДобавитьФлагОчистки 'final-delta-changed' }
                if ($финальнаяДельтаRemoved -ne 0) { ДобавитьФлагОчистки 'final-delta-removed' }
            } catch {
                ДобавитьФлагОчистки 'fingerprint-final-check'
            }
        }

        if (Test-Path $sentinel) {
            try { Remove-Item $sentinel -Force -ErrorAction Stop } catch {
                ДобавитьФлагОчистки 'sentinel-not-deleted'
            }
        }

        if (Test-Path $sentinel) { ДобавитьФлагОчистки 'sentinel-still-present' }

        if ($сценарийПройден) {
            $файлEvidence = 'screens-happy-evidence.md'
            $заголовокEvidence = 'Screens runtime-доказательство (happy path)'
        } else {
            $файлEvidence = 'screens-failure-evidence.md'
            $заголовокEvidence = 'Screens runtime-доказательство (FAIL)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'screens'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: screens'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { try { $h.Proc.Id } catch { 'недоступен' } } else { 'нет' })"
    Запись "временный профиль: $(if ($корень) { $корень } else { 'не создан' })"
    Запись "снято снимков: $($снято.Count)"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'снимки экранов' $журнал
    $маркеры = @('Режим: screens', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'SCREENS PASS: True'
    } else {
        $exitCode = 1
        Запись 'SCREENS PASS: False'
    }
}

exit $exitCode
