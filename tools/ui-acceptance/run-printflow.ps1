# Пункт D: настоящий runtime-сценарий F5/F6 с обработкой всех модальных
# окон (Карточка отправления -> Печать -> Предпросмотр). Никакой физической
# печати: диалог печати отменяется, предпросмотр закрывается кнопкой
# «Закрыть» (DialogResult != OK). Sentinel реального профиля до/после.
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
$evidence = Join-Path $out 'f5-f6-runtime-evidence.md'
$реальныйПрофиль = Join-Path $env:APPDATA 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 30

# --- 1. Отпечатки реального профиля ДО -------------------------------
New-Item -ItemType Directory -Force -Path $реальныйПрофиль | Out-Null
Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N'))
$отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
$sentinelДо = Get-SentinelState $sentinel

$корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
Write-Host "temp profile: $корень"

$журнал = [System.Collections.Generic.List[string]]::new()
function Запись([string]$строка) { $журнал.Add($строка); Write-Host $строка }

$h = Start-Acceptance $exe 'printflow' $корень
if ($h -eq $null) { Write-Host 'HARNESS FAILED'; Remove-Item $sentinel -Force; exit 1 }
$pid_ = [uint32]$h.Proc.Id
[AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
Start-Sleep -Seconds 2

$успех = $true

# --- 2. Выбор получателя ----------------------------------------------
$h.Proc.StandardInput.WriteLine('SELECT')
Start-Sleep -Seconds 1

# --- 3. F5: Карточка -> Печать -> Отмена ------------------------------
Запись "## F5 «Печать текущего конверта»"
$h.Proc.StandardInput.WriteLine('F5')

$карточка = Wait-AppWindow $pid_ 'Карточка отправления' $таймаутШага
if ($карточка -eq [IntPtr]::Zero) {
    Запись "- РЕЗУЛЬТАТ: FAIL — «Карточка отправления» не появилась за ${таймаутШага} с"
    $успех = $false
} else {
    Show-Evidence 'F5:card' $карточка
    [AccWin]::Capture($карточка, (Join-Path $out '20-карточка-отправления-f5.png')) | Out-Null
    Запись "- «Карточка отправления» появилась, hwnd=$карточка, зафиксирована скриншотом 20"
    $кнопка = [AccWin]::FindButton($карточка, 'ОК')
    if ($кнопка -eq [IntPtr]::Zero) {
        Запись "- РЕЗУЛЬТАТ: FAIL — кнопка «ОК» не найдена"
        $успех = $false
    } else {
        [AccWin]::Click($кнопка)
        Запись "- «ОК» нажата (BM_CLICK)"
    }
}

if ($успех) {
    $печать = Wait-AppWindow $pid_ 'Печать' $таймаутШага
    if ($печать -eq [IntPtr]::Zero) {
        Запись "- РЕЗУЛЬТАТ: FAIL — диалог «Печать» не появился за ${таймаутШага} с"
        $успех = $false
    } else {
        Show-Evidence 'F5:print-dialog' $печать
        [AccWin]::Capture($печать, (Join-Path $out '21-диалог-печати-f5.png')) | Out-Null
        Запись "- Диалог «Печать» появился, hwnd=$печать, скриншот 21"
        Start-Sleep -Seconds 1
        $кнопка = Wait-Button $печать 'Отмена' 8
        if ($кнопка -eq [IntPtr]::Zero) {
            Запись "- РЕЗУЛЬТАТ: FAIL — кнопка «Отмена» не найдена"
            $успех = $false
        } else {
            [AccWin]::Click($кнопка)
            Запись "- «Отмена» нажата — задания на принтер не создаётся"
            Start-Sleep -Seconds 2
            $осталось = [AccWin]::FindByTitle($pid_, 'Печать')
            Запись "- Диалог «Печать» закрыт: $($осталось -eq [IntPtr]::Zero)"
        }
    }
}

if ($успех) {
    $жив = [AccWin]::Responsive($h.Hwnd, 5000)
    Запись "- Главный поток отзывчив после F5: $жив"
    if (-not $жив) { $успех = $false }
}

# --- 4. F6: Карточка -> Печать -> Предпросмотр -> Закрыть -------------
if ($успех) {
    Запись "## F6 «Предпросмотр и печать»"
    $h.Proc.StandardInput.WriteLine('F6')

    $карточка2 = Wait-AppWindow $pid_ 'Карточка отправления' $таймаутШага
    if ($карточка2 -eq [IntPtr]::Zero) {
        Запись "- РЕЗУЛЬТАТ: FAIL — «Карточка отправления» не появилась"
        $успех = $false
    } else {
        Show-Evidence 'F6:card' $карточка2
        Запись "- «Карточка отправления» появилась, hwnd=$карточка2"
        $кнопка = [AccWin]::FindButton($карточка2, 'ОК')
        [AccWin]::Click($кнопка)
        Запись "- «ОК» нажата"
    }
}

$печать2 = [IntPtr]::Zero
if ($успех) {
    $печать2 = Wait-DialogOrConfirm $pid_ 'Печать' $таймаутШага
    if ($печать2 -eq [IntPtr]::Zero) {
        Запись "- РЕЗУЛЬТАТ: FAIL — диалог «Печать» не появился"
        $успех = $false
    } else {
        Show-Evidence 'F6:print-dialog' $печать2
        Start-Sleep -Seconds 1
        $кнопка = Wait-Button $печать2 'Печать' 8
        if ($кнопка -eq [IntPtr]::Zero) {
            Запись "- РЕЗУЛЬТАТ: FAIL — кнопка «Печать» не найдена"
            [AccWin]::Capture($печать2, (Join-Path $out '23-f6-неожиданное-окно.png')) | Out-Null
            Запись "- Неожиданное окно снято: 23-f6-неожиданное-окно.png"
            $успех = $false
        } else {
            [AccWin]::Click($кнопка)
            Запись "- «Печать» нажата — ожидание окна предпросмотра"
        }
    }
}

$предпросмотр = [IntPtr]::Zero
if ($успех) {
    $дедлайн = (Get-Date).AddSeconds($таймаутШага)
    while ((Get-Date) -lt $дедлайн) {
        [void](Close-MessageBoxes $pid_ $out)
        $предпросмотр = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
        if ($предпросмотр -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 400
    }
    if ($предпросмотр -eq [IntPtr]::Zero) {
        Запись "- РЕЗУЛЬТАТ: FAIL — «Предпросмотр печати» не появился за ${таймаутШага} с"
        Запись "- Диагностика: окна процесса на момент таймаута:"
        & { Dump-AllWindows $pid_ } 3>&1 | ForEach-Object { Запись "  $_" }
        Stop-Acceptance $h
        $логКаталог = Join-Path $корень 'Picket302\logs'
        if (Test-Path $логКаталог) {
            Запись "- Хвост журнала приложения:"
            Get-ChildItem $логКаталог -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object {
                Get-Content $_.FullName -Tail 12 | ForEach-Object { Запись "  | $_" }
            }
        } else {
            Запись "- Журнал приложения не создан: $логКаталог"
        }
        $успех = $false
    } else {
        Show-Evidence 'F6:preview' $предпросмотр
        [AccWin]::Capture($предпросмотр, (Join-Path $out '22-предпросмотр-f6.png')) | Out-Null
        Запись "- «Предпросмотр печати» появился, hwnd=$предпросмотр, скриншот 22"
        Start-Sleep -Seconds 1
        $кнопка = Wait-Button $предпросмотр 'Закрыть' 8
        if ($кнопка -eq [IntPtr]::Zero) {
            Запись "- РЕЗУЛЬТАТ: FAIL — кнопка «Закрыть» не найдена"
            $успех = $false
        } else {
            [AccWin]::Click($кнопка)
            Запись "- «Закрыть» нажата — DialogResult не OK, печать не выполняется"
            Start-Sleep -Seconds 3
            $осталось = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
            Запись "- Предпросмотр закрыт: $($осталось -eq [IntPtr]::Zero)"
        }
    }
}

if ($успех) {
    $жив = [AccWin]::Responsive($h.Hwnd, 5000)
    Запись "- Главный поток отзывчив после F6: $жив"
    if (-not $жив) { $успех = $false }
}

# --- 5. Завершение и уборка -------------------------------------------
if ($h -ne $null) { Stop-Acceptance $h; $h = $null }

$профильОчищен = -not (Test-Path $корень)
Запись "- Временный профиль очищен при выходе: $профильОчищен"

$отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
$sentinelПосле = Get-SentinelState $sentinel
Remove-Item $sentinel -Force
$дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле

Запись "## Реальный профиль"
Запись "- Sentinel до:  ``$sentinelДо``"
Запись "- Sentinel после: ``$sentinelПосле``"
Запись "- Sentinel неизменён: $($sentinelДо -eq $sentinelПосле)"
Запись "- Новых файлов: $($дельта.New.Count); изменённых: $($дельта.Changed.Count); удалённых: $($дельта.Removed.Count)"
$дельта.New | ForEach-Object { Запись "  - NEW: $_" }
$дельта.Changed | ForEach-Object { Запись "  - CHANGED: $_" }
Запись "- Итог F5/F6: $($(if ($успех) { 'PASS' } else { 'FAIL' }))"

# Запись журнала-доказательства
$строки = @(
    '# F5/F6 runtime-доказательство (acceptance harness)',
    '',
    "Дата: $(Get-Date).ToString('yyyy-MM-dd HH:mm'), профиль: временный (guid), createDemoData=false.",
    'Физическая печать не выполнялась: диалог печати — «Отмена», предпросмотр — «Закрыть».'
) + $журнал
Set-Content -Path $evidence -Value $строки -Encoding UTF8
Write-Host "evidence written: $evidence"
Write-Host "PRINTFLOW PASS: $успех"
