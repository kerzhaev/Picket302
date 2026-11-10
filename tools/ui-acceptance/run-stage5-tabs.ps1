# Драйвер снимков вкладок «Конверты» и «Рассылки» (stage5-verify,
# 2026-11-10). Закрывает пробел инвентаря: вкладки 1 и 2 никогда не
# снимались драйверами b9e2803. Открывает НАСТОЯЩЕЕ приложение на
# изолированном профиле, переключает вкладки SETTAB и снимает их
# неактивирующим PrintWindow-захватом.
#
# Ограничения (scope): печать не выполняется, COM не запускается,
# реальный профиль %AppData%\Picket302 не изменяется (fingerprint
# до/после, sentinel). Fail-closed: любое нарушение → exit 1.
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
$файлEvidence = 'stage5-tabs-failure-evidence.md'
$заголовокEvidence = 'Stage5 tabs runtime-доказательство (FAIL)'
$финальнаяДельтаNew = -1
$финальнаяДельтаChanged = -1
$финальнаяДельтаRemoved = -1
$убит = $false
$кодВыходаHarness = $null
$harnessNotExited = $false
$остановка = $null
$корень = $null
$снято = [System.Collections.Generic.List[string]]::new()
$блокировка = $null

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

function Снять([IntPtr]$окно, [string]$имяФайла) {
    if ($окно -eq [IntPtr]::Zero) { return 0 }

    $путь = Join-Path $out $имяФайла

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

try {
    New-Item -ItemType Directory -Force -Path $out -ErrorAction Stop | Out-Null

    $блокировка = Enter-SharedTestLock

    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    # --- Прогон 1: seeded (screens) ---------------------------------------
    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile (seeded): $корень"

    $h = Start-Acceptance $exe 'screens' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received (seeded)'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    Запись "harness READY: $готовность"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    foreach ($план in @(
        @{ Tab = '1'; Файл = '70-tab1-konverty-seeded.png'; Метка = 'вкладка 1 «Конверты» (seeded)' },
        @{ Tab = '2'; Файл = '71-tab2-rassylki-seeded.png'; Метка = 'вкладка 2 «Рассылки» (seeded)' }
    )) {
        Send-HarnessCommand $h ("SETTAB " + $план.Tab)
        $okTab = Read-HarnessLines $h ('^OK SETTAB ' + $план.Tab) 15
        & $всёОк ([bool]$okTab) ("SETTAB " + $план.Tab)
        Start-Sleep -Seconds 2

        $байт = Снять $h.Hwnd $план.Файл
        & $всёОк ($байт -gt 0) ("снимок " + $план.Метка)

        Send-HarnessCommand $h 'LAYOUTCHECK'
        if (Read-HarnessLines $h '^LAYOUTCHECK ' 15) {
            $строки = Get-HarnessLines $h | Where-Object { $_ -match '^LAYOUTCHECK ' } | Select-Object -Last 3
            foreach ($с in $строки) { Запись "LAYOUTCHECK: $с" }
            $нольКнопок = ($строки | Where-Object { $_ -match 'нулевыхКнопок=(\d+)' -and [int]$Matches[1] -ne 0 })
            & $всёОк (-not $нольКнопок) ("LAYOUTCHECK без нулевых кнопок (SETTAB " + $план.Tab + ")")
        } else {
            & $всёОк $false ("LAYOUTCHECK получен (SETTAB " + $план.Tab + ")")
        }
    }

    Send-HarnessCommand $h 'SETTAB 0'
    [void](Read-HarnessLines $h '^OK SETTAB 0' 10)

    $остановка = Invoke-HarnessStop $h 'stage5-tabs-seeded'
    foreach ($флаг in $остановка.Флаги) { ДобавитьФлагОчистки $флаг; Запись "SAFETY-FLAG: $флаг" }
    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит TreeOk=$($остановка.Результат.TreeOk)"
    }
    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'stage5-tabs-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }
    & $всёОк ($кодВыходаHarness -eq 0) 'harness exit 0 (seeded)'
    & $всёОк (-not $убит) 'harness не убит (seeded)'
    & $всёОк (-not $harnessNotExited) 'harness завершён bounded (seeded)'
    & $всёОк ($остановка.Результат.TreeOk) 'дерево процессов чисто (seeded)'
    & $всёОк ($остановка.Результат.RemainingPids.Count -eq 0) 'нет живых потомков (seeded)'
    $h = $null

    # --- Прогон 2: empty (emptybook) ---------------------------------------
    $корень2 = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile (empty): $корень2"

    $h = Start-Acceptance $exe 'emptybook' $корень2 $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c (empty)'
        throw 'READY not received (empty)'
    }

    $готовность2 = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность2 -split ' ')[1])
    Запись "harness READY: $готовность2"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    foreach ($план in @(
        @{ Tab = '1'; Файл = '72-tab1-konverty-empty.png'; Метка = 'вкладка 1 «Конверты» (пусто)' },
        @{ Tab = '2'; Файл = '73-tab2-rassylki-empty.png'; Метка = 'вкладка 2 «Рассылки» (пусто)' }
    )) {
        Send-HarnessCommand $h ("SETTAB " + $план.Tab)
        $okTab = Read-HarnessLines $h ('^OK SETTAB ' + $план.Tab) 15
        & $всёОк ([bool]$okTab) ("SETTAB " + $план.Tab + " (empty)")
        Start-Sleep -Seconds 2

        $байт = Снять $h.Hwnd $план.Файл
        & $всёОк ($байт -gt 0) ("снимок " + $план.Метка)
    }

    Send-HarnessCommand $h 'SETTAB 0'
    [void](Read-HarnessLines $h '^OK SETTAB 0' 10)

    $остановка = Invoke-HarnessStop $h 'stage5-tabs-empty'
    foreach ($флаг in $остановка.Флаги) { ДобавитьФлагОчистки $флаг; Запись "SAFETY-FLAG: $флаг" }
    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит TreeOk=$($остановка.Результат.TreeOk)"
    }
    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'stage5-tabs-exitcode-empty'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }
    & $всёОк ($кодВыходаHarness -eq 0) 'harness exit 0 (empty)'
    & $всёОк (-not $убит) 'harness не убит (empty)'
    & $всёОк (-not $harnessNotExited) 'harness завершён bounded (empty)'
    & $всёОк ($остановка.Результат.TreeOk) 'дерево процессов чисто (empty)'
    & $всёОк ($остановка.Результат.RemainingPids.Count -eq 0) 'нет живых потомков (empty)'

    # --- Условия ------------------------------------------------------------
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    $профильОчищен = (-not (Test-Path $корень)) -and (-not (Test-Path $корень2))

    Запись "снято снимков: $($снято.Count)"
    foreach ($имя in $снято) { Запись "  - $имя" }

    $условия = [ordered]@{
        'снято 4 снимка вкладок' = ($снято.Count -ge 4)
        'временные профили удалены' = $профильОчищен
        'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
        'новых файлов реального профиля нет' = ($дельта.New.Count -eq 0)
        'изменённых файлов реального профиля нет' = ($дельта.Changed.Count -eq 0)
        'удалённых файлов реального профиля нет' = ($дельта.Removed.Count -eq 0)
        'все проверки пройдены' = $успех
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
            $остановкаФинал = Invoke-HarnessStop $h 'stage5-tabs-finally'
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
            $файлEvidence = 'stage5-tabs-happy-evidence.md'
            $заголовокEvidence = 'Stage5 tabs runtime-доказательство (happy path)'
        } else {
            $файлEvidence = 'stage5-tabs-failure-evidence.md'
            $заголовокEvidence = 'Stage5 tabs runtime-доказательство (FAIL)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'stage5-tabs'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: stage5-tabs (screens+emptybook)'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "снято снимков: $($снято.Count)"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "HARNESS NOT EXITED: $harnessNotExited"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'снимки вкладок 1/2' $журнал
    $маркеры = @('Режим: stage5-tabs', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'STAGE5 TABS PASS: True'
    } else {
        $exitCode = 1
        Запись 'STAGE5 TABS PASS: False'
    }
}

exit $exitCode
