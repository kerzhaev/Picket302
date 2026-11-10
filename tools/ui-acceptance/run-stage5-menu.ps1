# Драйвер снимка выпадающего меню «Печать» (stage5-verify, 2026-11-10).
# Закрывает reference-поверхность R3 (legacy-print-menu.png) настоящим
# ToolStripDropDown-кадром из текущей сборки, без активации окон.
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
$файлEvidence = 'stage5-menu-failure-evidence.md'
$заголовокEvidence = 'Stage5 print-menu runtime-доказательство (FAIL)'
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

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class FgMenu {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@

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

    $fgДо = [FgMenu]::GetForegroundWindow()
    Запись "foreground before: $fgДо"

    $успех = $true
    $всёОк = {
        param($условие, $сообщение)
        if ($условие) { Запись "OK: $сообщение" } else { Запись "FAIL: $сообщение"; $script:успех = $false }
    }

    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'screens' $корень $false $true

    if (-not (Read-HarnessLines $h '^READY ' 30)) {
        Запись 'FAIL: READY не получен за 30 c'
        throw 'READY not received'
    }

    $готовность = Get-HarnessLines $h | Where-Object { $_ -match '^READY ' } | Select-Object -First 1
    $h.Hwnd = [IntPtr][long](($готовность -split ' ')[1])
    Запись "harness READY: $готовность"

    [AccWin]::SetWindowPos($h.Hwnd, [IntPtr]::Zero, 20, 20, 1280, 720, 0x0054) | Out-Null
    Start-Sleep -Seconds 2

    Send-HarnessCommand $h 'PRINTMENU'
    if (-not (Read-HarnessLines $h '^OK PRINTMENU ' 15)) {
        foreach ($строка in (Get-HarnessLines $h | Select-Object -Last 8)) { Запись "HARNESS-OUT: $строка" }
        throw 'PRINTMENU failed'
    }

    $okСтрока = Get-HarnessLines $h | Where-Object { $_ -match '^OK PRINTMENU ' } | Select-Object -Last 1
    $hwndМеню = [IntPtr][long](($okСтрока -split ' ')[2])
    Запись "меню «Печать» открыто: hwnd=$hwndМеню"

    $байт = Снять $hwndМеню '74-print-menu-dropdown.png'
    & $всёОк ($байт -gt 500) 'снимок dropdown «Печать» непустой'

    Send-HarnessCommand $h 'HIDE'
    [void](Read-HarnessLines $h '^OK HIDE' 10)

    $fgПосле = [FgMenu]::GetForegroundWindow()
    Запись "foreground after: $fgПосле"
    & $всёОк ($fgДо -eq $fgПосле) 'foreground пользователя не менялся'

    $остановка = Invoke-HarnessStop $h 'stage5-menu'
    foreach ($флаг in $остановка.Флаги) { ДобавитьФлагОчистки $флаг; Запись "SAFETY-FLAG: $флаг" }
    if ($остановка.Результат -ne $null -and ($остановка.Результат -is [hashtable])) {
        $убит = -not $остановка.Результат.Graceful
        Запись "остановка: graceful=$($остановка.Результат.Graceful) killed=$убит TreeOk=$($остановка.Результат.TreeOk)"
    }
    if ($h.Proc.HasExited) {
        $ожиданиеВыхода = Wait-ProcessExitBounded $h.Proc 3000 'stage5-menu-exitcode'
        $кодВыходаHarness = $ожиданиеВыхода.ExitCode
    } else {
        $кодВыходаHarness = $null
        $harnessNotExited = $true
        ДобавитьФлагОчистки 'harness-not-exited'
    }

    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    $профильОчищен = -not (Test-Path $корень)

    $условия = [ordered]@{
        'снимок меню получен' = ($снято.Count -ge 1)
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
            $остановкаФинал = Invoke-HarnessStop $h 'stage5-menu-finally'
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
            $файлEvidence = 'stage5-menu-happy-evidence.md'
            $заголовокEvidence = 'Stage5 print-menu runtime-доказательство (happy path)'
        }
    } catch {
        ДобавитьФлагОчистки 'finalization-error'
    }

    $блокировкаРезультат = Exit-SharedTestLock $блокировка 'stage5-menu'

    if (-not ($блокировкаРезультат.Released -and $блокировкаРезультат.Disposed)) {
        ДобавитьФлагОчистки 'lock-release-failed'
    }

    $happyПройден = $сценарийПройден -and ($неудачиОчистки.Count -eq 0)
    $финальныйВердикт = if ($happyПройден) { 'PASS' } else { 'FAIL' }
    $ожидаемыйКод = if ($happyПройден) { 0 } else { 1 }

    Запись 'Режим: stage5-menu (screens)'
    Запись "UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    Запись "FINAL DELTA: new=$финальнаяДельтаNew changed=$финальнаяДельтаChanged removed=$финальнаяДельтаRemoved"
    Запись "LOCK RELEASE: released=$($блокировкаРезультат.Released) disposed=$($блокировкаРезультат.Disposed)"
    Запись "неудачи очистки: $(if ($неудачиОчистки.Count -gt 0) { $неудачиОчистки -join ';' } else { 'нет' })"
    Запись "EXPECTED FINAL EXIT: $ожидаемыйКод"
    Запись "FINAL VERDICT: $финальныйВердикт"

    $evidenceOk = ЗаписатьEvidence $файлEvidence $заголовокEvidence 'снимок меню «Печать»' $журнал
    $маркеры = @('Режим: stage5-menu', 'FINAL VERDICT: ' + $финальныйВердикт, 'EXPECTED FINAL EXIT: ' + $ожидаемыйКод, 'LOCK RELEASE:')
    $evidenceOk = $evidenceOk -and (Test-EvidenceWritten $файлEvidence $маркеры)

    if ($happyПройден -and $evidenceOk) {
        $exitCode = 0
        Запись 'STAGE5 MENU PASS: True'
    } else {
        $exitCode = 1
        Запись 'STAGE5 MENU PASS: False'
    }
}

exit $exitCode
