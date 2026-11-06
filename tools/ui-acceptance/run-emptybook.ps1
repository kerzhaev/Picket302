# Пункт B: настоящий runtime-сценарий пустой адресной книги. Fail-closed:
# любой провал — ненулевой код возврата; sentinel удаляется только в finally.
$ErrorActionPreference = 'Continue'
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

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

try {
    # --- 1. Отпечатки реального профиля ДО --------------------------------
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N'))
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
    $скриншотРазмер = (Get-Item $скриншот -ErrorAction SilentlyContinue).Length
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

    # --- 7. Итог: все обязательные условия ---------------------------------
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
        'harness exit 0' = ($кодВыходаHarness -eq 0)
        'harness не убит' = (-not $убит)
        'нет FAIL в stdout' = ($отказовStdout -eq 0)
    }

    $всёХорошо = $true
    foreach ($ключ in $условия.Keys) {
        $значение = $условия[$ключ]
        Запись ("условие [{0}]: {1}" -f $ключ, $значение)
        if (-not $значение) { $всёХорошо = $false }
    }

    if ($всёХорошо) {
        Запись 'EMPTYBOOK PASS: True'
        $exitCode = 0
    } else {
        Запись 'EMPTYBOOK PASS: False'
    }
}
catch {
    Запись "EXCEPTION: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    # Аварийная уборка: Kill допустим только здесь, FAIL уже зафиксирован.
    if ($h -ne $null -and $h.Proc -ne $null -and -not $h.Proc.HasExited) {
        Write-Host '  finally: аварийное завершение harness'
        try { $h.Proc.StandardInput.WriteLine('CLOSE') } catch {}
        if (-not $h.Proc.WaitForExit(10000)) { $h.Proc.Kill() }
    }

    # Отпечатки после — даже при провале, если приложение запускалось.
    try {
        $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
        $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
        Запись "production delta (finally): new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"
        $дельта.New | ForEach-Object { Запись "  NEW: $_" }
        $дельта.Changed | ForEach-Object { Запись "  CHANGED: $_" }
    } catch {}

    if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

    # Evidence-файл прогона
    try {
        $evidence = Join-Path $out 'emptybook-runtime-evidence.md'
        $строки = @(
            '# Emptybook runtime-доказательство',
            '',
            "Прогон: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), профиль: временный (guid), createDemoData=false.",
            ''
        ) + $журнал
        Set-Content -Path $evidence -Value $строки -Encoding UTF8
    } catch {}
}

exit $exitCode
