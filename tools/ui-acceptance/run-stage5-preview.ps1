# Драйвер полностраничного кадра предпросмотра (stage5-verify, 2026-11-10).
# Цепочка F6 до окна «Предпросмотр печати» (как run-previewflow), затем окно
# предпросмотра РАСШИРЯЕТСЯ неактивирующим SetWindowPos так, чтобы лист C6
# (324×229 мм ≈ 1225×865 px при 96 dpi) помещался целиком, и снимается
# полный лист — честный текущий кадр для reference-пары R2.
#
# Ограничения (scope): печать не выполняется (спулер контролируется до/после),
# COM не запускается, реальный профиль не изменяется (fingerprint, sentinel).
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = Get-EvidenceRoot
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 20

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null
$сценарийПройден = $false
$неудачиОчистки = [System.Collections.Generic.List[string]]::new()
$файлEvidence = 'stage5-preview-failure-evidence.md'
$заголовокEvidence = 'Stage5 preview fullsheet (FAIL)'
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
$спулерДо = -1
$спулерПосле = -1

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

function Захват([IntPtr]$окно, [string]$путь) {
    Start-Sleep -Milliseconds 700
    [AccWin]::Capture($окно, $путь) | Out-Null
    Start-Sleep -Milliseconds 300
}

function Wait-ОжидаемоеОкно([uint32]$procId, [string]$title, [string]$фаза) {
    $deadline = (Get-Date).AddSeconds($script:таймаутШага)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in [AccWin]::WindowsOf($procId)) {
            $cls = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($w, $cls, 256) | Out-Null
            if ([AccWin]::Title($w) -eq $title) { return $w }
            if ($cls.ToString() -eq '#32770') {
                Запись ("UNEXPECTED MessageBox в фазе " + $фаза + ": '" + [AccWin]::Title($w) + "'")
                return [IntPtr]::Zero
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

function Get-SpoolerJobCount {
    $всего = 0
    foreach ($pn in @('Microsoft Print to PDF', 'PDF-XChange 5.0 for ABBYY', 'OneNote (Desktop)')) {
        try { $всего += @(Get-PrintJob -PrinterName $pn -ErrorAction Stop).Count } catch { }
    }
    return $всего
}

try {
    New-Item -ItemType Directory -Force -Path $out -ErrorAction Stop | Out-Null
    $блокировка = Enter-SharedTestLock

    New-Item -ItemType Directory -Force -Path $реальныйПрофиль -ErrorAction Stop | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel

    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    # Режим printflow (как run-previewflow): сеет отправителя, получателя и
    # шаблон «Конверт C6 (приёмка)», который требует SEEDFIELDS.
    $h = Start-Acceptance $exe 'printflow' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) { throw 'READY not received' }
    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    $pid_ = [uint32]$h.Proc.Id
    Запись "harness READY: $готовность"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 30, 30, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    Send-HarnessCommand $h 'SEEDFIELDS'
    if (-not (Read-HarnessLines $h '^OK SEEDFIELDS' 10)) { throw 'SEEDFIELDS not confirmed' }
    Запись 'SEEDFIELDS подтверждён'

    $спулерДо = Get-SpoolerJobCount
    Запись "spooler jobs before: $спулерДо"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 30, 30, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    Send-HarnessCommand $h 'SELECTLAST'
    [void](Read-HarnessLines $h '^OK SELECTLAST' 10)

    Send-HarnessCommand $h 'F6'
    $карточка = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'card'
    & $всёОк ($карточка -ne [IntPtr]::Zero) 'карточка отправления появилась'
    if ($карточка -ne [IntPtr]::Zero) {
        $кнопка = Wait-Button $карточка 'ОК' 8
        if ($кнопка -ne [IntPtr]::Zero) { [AccWin]::Click($кнопка); Запись 'карточка: ОК' } else { & $всёОк $false 'ОК карточки не найдена' }
    }

    # MessageBox формирования
    $mb = $false
    $deadline = (Get-Date).AddSeconds($таймаутШага)
    while ((Get-Date) -lt $deadline -and -not $mb) {
        foreach ($w in [AccWin]::WindowsOf($pid_)) {
            $cls = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($w, $cls, 256) | Out-Null
            if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -eq 'Печать') {
                $ok = [AccWin]::FindButton($w, 'ОК')
                if ($ok -ne [IntPtr]::Zero) { [AccWin]::Click($ok); $mb = $true; Запись 'MessageBox формирования: ОК' }
                break
            }
        }
        if (-not $mb) { Start-Sleep -Milliseconds 300 }
    }
    & $всёОк $mb 'MessageBox формирования подтверждён'

    $диалогПечать = Wait-WindowWithButton $pid_ 'Печать' 'Печать' 10
    if ($null -ne $диалогПечать) {
        [AccWin]::Click($диалогПечать.Button)
        Запись 'диалог «Печать»: нажата Печать'
    } else {
        & $всёОк $false 'диалог «Печать» с кнопкой не найден'
    }

    $подача = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($таймаутШага)
    while ((Get-Date) -lt $deadline -and $подача -eq [IntPtr]::Zero) {
        $подача = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
        if ($подача -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
    }
    if ($подача -ne [IntPtr]::Zero) {
        $продолжить = Wait-Button $подача 'Продолжить' 8
        if ($продолжить -ne [IntPtr]::Zero) { [AccWin]::Click($продолжить); Запись 'подача: Продолжить' } else { & $всёОк $false 'Продолжить не найдена' }
    } else {
        & $всёОк $false 'окно подачи не появилось'
    }

    $предпросмотр = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($таймаутШага)
    while ((Get-Date) -lt $deadline -and $предпросмотр -eq [IntPtr]::Zero) {
        $предпросмотр = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
        if ($предпросмотр -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 400 }
    }
    & $всёОк ($предпросмотр -ne [IntPtr]::Zero) 'предпросмотр появился'

    if ($предпросмотр -ne [IntPtr]::Zero) {
        # Расширяем окно, чтобы лист 324×229 мм поместился целиком при 100 %.
        [AccWin]::SetWindowPos($предпросмотр, [IntPtr]::Zero, 20, 20, 1400, 1010, 0x0054) | Out-Null
        Start-Sleep -Seconds 2
        Захват $предпросмотр (Join-Path $out '78-preview-fullsheet.png')
        if ((Test-Path (Join-Path $out '78-preview-fullsheet.png')) -and ((Get-Item (Join-Path $out '78-preview-fullsheet.png')).Length -gt 2000)) {
            $снято.Add('78-preview-fullsheet.png')
            Запись 'снимок полного листа предпросмотра получен'
        } else {
            & $всёОк $false 'снимок полного листа непустой'
        }

        $закрыть = Wait-Button $предпросмотр 'Закрыть' 8
        if ($закрыть -ne [IntPtr]::Zero) { [AccWin]::Click($закрыть); Запись 'предпросмотр: Закрыть' } else { & $всёОк $false 'Закрыть не найдена' }
    }

    $спулерПосле = Get-SpoolerJobCount
    Запись "spooler jobs after: $спулерПосле"
    & $всёОк ($спулерПосле -eq $спулерДо) 'spooler не изменился (печать не выполнялась)'

    $остановка = Invoke-HarnessStop $h 'stage5-preview'
    foreach ($флаг in $остановка.Флаги) { ДобавитьФлагОчистки $флаг; Запись "SAFETY-FLAG: $флаг" }
    if ($остановка.Результат -is [hashtable]) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) TreeOk=$($остановка.Результат.TreeOk)"
    }
    if ($h.Proc.HasExited) {
        $кодВыходаHarness = (Wait-ProcessExitBounded $h.Proc 3000 'stage5-preview-exit').ExitCode
    } else {
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле

    $условия = [ordered]@{
        'снимок полного листа получен' = ($снято.Count -ge 1)
        'временный профиль удалён' = (-not (Test-Path $корень))
        'sentinel совпал' = ($sentinelДо -eq $sentinelПосле)
        'дельта профиля 0/0/0' = (($дельта.New.Count + $дельта.Changed.Count + $дельта.Removed.Count) -eq 0)
        'harness exit 0' = ($кодВыходаHarness -eq 0)
        'harness не убит' = (-not $убит)
        'harness завершён' = (-not $harnessNotExited)
        'дерево процессов чисто' = $остановка.Результат.TreeOk
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
            $остановкаФинал = Invoke-HarnessStop $h 'stage5-preview-finally'
            foreach ($флаг in $остановкаФинал.Флаги) { ДобавитьФлагОчистки $флаг }
        }
        if ($null -ne $отпечатокДо) {
            $отпечатокФинал = Test-ProfileFingerprint $реальныйПрофиль
            $дельтаФинал = Compare-Fingerprints $отпечатокДо $отпечатокФинал
            $финальнаяДельтаNew = $дельтаФинал.New.Count
            $финальнаяДельтаChanged = $дельтаФинал.Changed.Count
            $финальнаяДельтаRemoved = $дельтаФинал.Removed.Count
            Запись "production delta (финально): new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
        }
        if (Test-Path $sentinel) { Remove-Item $sentinel -Force -ErrorAction SilentlyContinue }
        if (Test-Path $sentinel) { ДобавитьФлагОчистки 'sentinel-still-present' }
        if ($сценарийПройден) {
            $файлEvidence = 'stage5-preview-happy-evidence.md'
            $заголовокEvidence = 'Stage5 preview fullsheet (happy path)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'stage5-preview'
    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) { ДобавитьФлагОчистки 'lock-release-failed' }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: stage5-preview'
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "spooler before/after: $спулерДо/$спулерПосле"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'полный лист предпросмотра' $журнал
    $маркеры = @('Режим: stage5-preview', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод)
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($happyПройден -and $evidenceOk) { $exitCode = 0; Запись 'STAGE5 PREVIEW PASS: True' }
    else { $exitCode = 1; Запись 'STAGE5 PREVIEW PASS: False' }
}

exit $exitCode
