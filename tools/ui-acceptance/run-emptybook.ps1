# Пункт B: настоящий runtime-сценарий пустой адресной книги. Fail-closed:
# любой провал — ненулевой код возврата. Единый контролируемый путь записи
# evidence: сначала завершается cleanup (harness, финальный отпечаток с
# fail-флагами дельты, sentinel), вычисляется ФИНАЛЬНЫЙ статус, затем
# evidence записывается ОДИН раз и проверяется на наличие обязательных
# маркеров текущего прогона. Happy-прогон — emptybook-happy-evidence.md;
# fault-инъекция — отдельный emptybook-fault-<fault>.md (happy-файл не
# перезаписывается); обычный провал — emptybook-failure-evidence.md.
# Общий ресурс (реальный профиль + sentinel) защищён межпроцессной
# блокировкой Enter-SharedTestLock.
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

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

try {
    # Межпроцессная блокировка общего ресурса: отказ — fail-closed exit 1.
    $блокировка = Enter-SharedTestLock

    # --- 1. Sentinel и отпечатки реального профиля ДО (fail-closed) -------
    # Невозможность создать sentinel = исключение = FAIL с exit 1.
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    # --- 2. Уникальный временный профиль и запуск -------------------------
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

    # --- 3. Окно 1280x720 и запрос счётчиков ------------------------------
    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
    Start-Sleep -Seconds 2

    $h.Proc.StandardInput.WriteLine('COUNTS')

    if (-not (Read-HarnessLines $h '^COUNTS ' 10)) {
        Запись 'FAIL: COUNTS не получен'
        throw 'COUNTS not received'
    }

    # --- 4. Скриншот -------------------------------------------------------
    [AccWin]::Capture($h.Hwnd, $скриншот)
    Start-Sleep -Milliseconds 300
    $скриншотРазмер = 0
    if (Test-Path $скриншот) { $скриншотРазмер = (Get-Item $скриншот).Length }
    Запись "скриншот: $скриншот ($скриншотРазмер байт)"

    # --- 5. Штатное завершение --------------------------------------------
    $убит = Stop-Acceptance $h
    if ($h -ne $null -and -not $h.Proc.HasExited) { $h.Proc.WaitForExit() }
    $кодВыходаHarness = $h.Proc.ExitCode
    Запись "harness exit code: $кодВыходаHarness; killed: $убит"

    # Проверки stdout — по накопленным строкам harness.
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

    # --- 6. Реальный профиль ПОСЛЕ -----------------------------------------
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

    # --- 7. Fault-inject (в памяти, production не трогается): --------------
    # доказательство fail-closed для Removed>0, провала cleanup и
    # невозможности записи evidence.
    $фault = $env:P302_EMPTYBOOK_FAULT

    if ($фault -eq 'delta-removed') {
        $дельта.Removed = @('C:' + [IO.Path]::DirectorySeparatorChar + 'fake' + [IO.Path]::DirectorySeparatorChar + 'injected.file')
        Запись 'FAULT-INJECT: delta Removed > 0'
    }

    if ($фault -eq 'cleanup-false') {
        $профильОчищен = $false
        Запись 'FAULT-INJECT: cleanup = False'
    }

    # --- 8. Условия happy-path ---------------------------------------------
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
        'нет FAIL в stdout' = ($отказовStdout -eq 0)
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
    # --- A. Аварийное завершение harness (дерево процессов, fail-флаг) ----
    $аварийноУбит = Stop-HarnessTree $h

    if ($аварийноУбит) {
        $script:убит = $true
        $неудачиОчистки.Add('emergency-kill')
        Запись "SAFETY-FAIL: harness убит в finally (pid=$(if ($h -ne $null -and $h.Proc -ne $null) { $h.Proc.Id } else { '?' })) — happy-path PASS невозможен"
    }

    # --- B. Финальная fingerprint-проверка (fail-closed, дельта = FAIL) ----
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
                $неудачиОчистки.Add('final-delta-new')
                Запись 'SAFETY-FAIL: финальная дельта — новые файлы реального профиля'
            }
            if ($финальнаяДельтаChanged -ne 0) {
                $неудачиОчистки.Add('final-delta-changed')
                Запись 'SAFETY-FAIL: финальная дельта — изменённые файлы реального профиля'
            }
            if ($финальнаяДельтаRemoved -ne 0) {
                $неудачиОчистки.Add('final-delta-removed')
                Запись 'SAFETY-FAIL: финальная дельта — удалённые файлы реального профиля'
            }
        } catch {
            $неудачиОчистки.Add('fingerprint-final-check')
            Запись "SAFETY-FAIL: финальная fingerprint-проверка не выполнена: $($_.Exception.Message)"
        }
    } else {
        $неудачиОчистки.Add('fingerprint-before-missing')
        Запись 'SAFETY-FAIL: отпечаток ДО не снят — финальная проверка невозможна'
    }

    # --- C. Sentinel удаляется с -ErrorAction Stop -------------------------
    if (Test-Path $sentinel) {
        try {
            Remove-Item $sentinel -Force -ErrorAction Stop
        } catch {
            $неудачиОчистки.Add('sentinel-not-deleted')
            Запись "SAFETY-FAIL: sentinel не удалён: $($_.Exception.Message)"
        }
    }

    if (Test-Path $sentinel) {
        $неудачиОчистки.Add('sentinel-still-present')
        Запись 'SAFETY-FAIL: sentinel остался в реальном профиле после завершения'
    }

    # --- D. Выбор файла evidence: happy / fault / failure ------------------
    $фault = $env:P302_EMPTYBOOK_FAULT
    $фaultПрименён = @($журнал | Where-Object { $_ -match 'FAULT-INJECT:' }).Count -gt 0

    if ($сценарийПройден -and $фault) {
        $файлEvidence = 'emptybook-fault-' + $фault + '.md'
        $заголовокEvidence = 'Emptybook fault-injection: ' + $фault
        Запись 'ANOMALY: fault применён, но условия прошли — файл fault-evidence'
    } elseif ($сценарийПройден) {
        $файлEvidence = 'emptybook-happy-evidence.md'
        $заголовокEvidence = 'Emptybook runtime-доказательство (happy path)'
    } elseif ($фaultПрименён) {
        $файлEvidence = 'emptybook-fault-' + $фault + '.md'
        $заголовокEvidence = 'Emptybook fault-injection: ' + $фault
    } else {
        $файлEvidence = 'emptybook-failure-evidence.md'
        $заголовокEvidence = 'Emptybook runtime-доказательство (FAIL)'
    }

    if ($фault -eq 'evidence-fail') {
        # Fault невозможности записи: имя с недопустимым символом Windows —
        # Set-Content обязан бросить; happy-файл не пишется.
        $файлEvidence = 'bad|name-evidence-fail.md'
        Запись 'FAULT-INJECT: evidence write target invalid'
    }

    # --- E. Финальный статус ДО записи evidence (входит в evidence) --------
    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0) -and (-not $фault)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись "Режим: emptybook"
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "PID harness: $(if ($h -ne $null -and $h.Proc -ne $null) { $h.Proc.Id } else { 'нет' })"
    Запись "реальный профиль: $реальныйПрофиль"
    Запись "временный профиль: $(if (Get-Variable -Name корень -ErrorAction SilentlyContinue) { $корень } else { 'не создан' })"
    Запись "createDemoData=false"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "FAULT: $(if ($фault) { $фault } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    if ($фaultПрименён) {
        Запись "PASS_OF_CONTRACT: $(-not $happyПройден) (fault=$фault — инъекция корректно обнаружена, скрипт не вернёт 0)"
    }

    # --- F. Единая запись evidence + пост-проверка -------------------------
    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'createDemoData=false' $журнал

    $маркеры = @('Режим: emptybook', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'createDemoData=false')
    if ($фaultПрименён) { $маркеры += 'PASS_OF_CONTRACT:' }
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($evidenceOk) {
        Запись "evidence записан и проверен: $файлEvidence"
    } else {
        Запись 'FAIL: запись evidence не удалась или не подтверждена — exit 1'
    }

    Exit-SharedTestLock $блокировка

    # --- G. Код возврата ----------------------------------------------------
    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'EMPTYBOOK PASS: True'
    } else {
        $exitCode = 1
        Запись 'EMPTYBOOK PASS: False'
    }
}

exit $exitCode
