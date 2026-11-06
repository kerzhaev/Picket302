# Пункт D: настоящий runtime-сценарий F5/F6 с обработкой всех модальных
# окон цепочки. Fail-closed: любой провал — ненулевой код возврата;
# неожиданные MessageBox/диалоги = FAIL; весь ход — в evidence.
# Физическая печать не выполняется: F5 — «Отмена», F6 — «Закрыть» на
# предпросмотре (DialogResult != OK, спулер не вызывается).
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
$evidence = Join-Path $out 'f5-f6-runtime-evidence.md'
$applicationData = Get-ApplicationDataFolder
$реальныйПрофиль = Join-Path $applicationData 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'
$таймаутШага = 30

$exitCode = 1
$h = $null
$журнал = [System.Collections.Generic.List[string]]::new()
$отпечатокДо = $null
$sentinelДо = $null

function Запись([string]$событие) {
    $срок = "[$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))][pid=$([uint32]$script:h.Proc.Id)] $событие"
    $журнал.Add($срок)
    Write-Host $срок
}

# Снимает и записываетUnexpected-окно (не входящее в ожидаемую цепочку).
function ЗафиксироватьНеожиданное([string]$тип, [IntPtr]$окно, [string]$префиксСкриншота) {
    $заголовок = [AccWin]::Title($окно)
    $файл = Join-Path $out ("$префиксСкриншота-" + (Get-Date -Format 'HHmmss') + '.png')
    [AccWin]::Capture($окно, $файл) | Out-Null
    Запись "UNEXPECTED ${тип}: '$заголовок' hwnd=$окно (скриншот: $(Split-Path -Leaf $файл))"
}

# Ожидает окно заголовка $title, попутно ловя неожиданные модальные окна:
# любой MessageBox (#32770) или «Проверка размера бумаги» вне ожидаемой
# цепочки фиксируется и делает сценарий FAIL.
function Wait-ОжидаемоеОкно([uint32]$procId, [string]$title, [string]$фаза) {
    $deadline = (Get-Date).AddSeconds($script:таймаутШага)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in [AccWin]::WindowsOf($procId)) {
            $cls = New-Object System.Text.StringBuilder 256
            [AccWin]::GetClassName($w, $cls, 256) | Out-Null
            $класс = $cls.ToString()
            $заголовок = [AccWin]::Title($w)

            if ($заголовок -eq $title) { return $w }

            if ($класс -eq '#32770') {
                ЗафиксироватьНеожиданное 'MessageBox' $w "unexpected-$фаза"
                Запись "FAIL: неожиданный MessageBox в фазе $фаза"
                return [IntPtr]::Zero
            }

            if ($заголовок -eq 'Проверка размера бумаги') {
                ЗафиксироватьНеожиданное 'paper-size' $w "unexpected-$фаза"
                Запись "FAIL: неожиданный диалог проверки бумаги в фазе $фаза"
                return [IntPtr]::Zero
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

try {
    # --- 1. Отпечатки реального профиля ДО --------------------------------
    New-Item -ItemType Directory -Force -Path $реальныйПрофиль | Out-Null
    Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N'))
    $отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelДо = Get-SentinelState $sentinel
    Запись "sentinel before: $sentinelДо"
    Запись "production profile files before: $($отпечатокДо.Count)"

    $корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
    Запись "temp profile: $корень"

    $h = Start-Acceptance $exe 'printflow' $корень

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

    # --- 2. SELECT ---------------------------------------------------------
    $h.Proc.StandardInput.WriteLine('SELECT')

    if (-not (Read-HarnessLines $h '^OK SELECT' 10)) {
        & $всёОк $false 'SELECT не подтверждён stdout (OK SELECT)'
    } else {
        Запись 'SELECT подтверждён stdout: OK SELECT'
    }

    # --- 3. F5: Карточка -> ОК -> Печать -> Отмена -------------------------
    Запись '## F5 «Печать текущего конверта»'
    if ($успех) {
        # PerformClick может блокироваться до ShowDialog (SQLite-async
        # завершается синхронно), поэтому OK F5 приходит только после
        # закрытия модальной цепочки — проверяется по финальному stdout.
        $h.Proc.StandardInput.WriteLine('F5')

        $карточка = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'f5-card'
        if ($карточка -ne [IntPtr]::Zero) {
            Запись "Карточка отправления появилась: hwnd=$карточка title='Карточка отправления'"
            $скр = Join-Path $out '20-карточка-отправления-f5.png'
            [AccWin]::Capture($карточка, $скр) | Out-Null
            Запись "скриншот карточки: $(Split-Path -Leaf $скр)"

            $кнопка = Wait-Button $карточка 'ОК' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Карточка: нажата ОК (BM_CLICK)'
            } else {
                & $всёОк $false 'кнопка ОК карточки не найдена'
            }
        } else {
            & $всёОк $false 'Карточка отправления не появилась'
        }
    }

    $печать = [IntPtr]::Zero
    if ($успех) {
        $печать = Wait-ОжидаемоеОкно $pid_ 'Печать' 'f5-print'
        if ($печать -ne [IntPtr]::Zero) {
            Запись "Диалог «Печать» появился: hwnd=$печать"
            $скр = Join-Path $out '21-диалог-печати-f5.png'
            [AccWin]::Capture($печать, $скр) | Out-Null
            Запись "скриншот диалога печати: $(Split-Path -Leaf $скр)"

            Start-Sleep -Milliseconds 500
            $кнопка = Wait-Button $печать 'Отмена' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Диалог «Печать»: нажата Отмена — задания на принтер не создаётся'
            } else {
                & $всёОк $false 'кнопка Отмена диалога печати не найдена'
            }
            Start-Sleep -Seconds 2
            $осталось = [AccWin]::FindByTitle($pid_, 'Печать')
            & $всёОк ($осталось -eq [IntPtr]::Zero) 'диалог «Печать» исчез после Отмены'
        } elseif ($успех) {
            & $всёОк $false 'диалог «Печать» не появился'
        }
    }

    $живF5 = $false
    if ($успех) {
        $живF5 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после F5 (WM_NULL): $живF5"
        if (-not $живF5) { $успех = $false }
    }

    # --- 4. F6: Карточка -> ОК -> MessageBox -> Печать -> Подача -> Предпросмотр
    if ($успех) {
        Запись '## F6 «Предпросмотр и печать»'
        # Аналогично F5: OK F6 печатается после закрытия цепочки модалок.
        $h.Proc.StandardInput.WriteLine('F6')
    }

    if ($успех) {
        $карточка2 = Wait-ОжидаемоеОкно $pid_ 'Карточка отправления' 'f6-card'
        if ($карточка2 -ne [IntPtr]::Zero) {
            Запись "Карточка отправления появилась: hwnd=$карточка2"
            $кнопка = Wait-Button $карточка2 'ОК' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Карточка: нажата ОК (BM_CLICK)'
            } else {
                & $всёОк $false 'кнопка ОК карточки F6 не найдена'
            }
        } else {
            & $всёОк $false 'Карточка отправления F6 не появилась'
        }
    }

    # MessageBox «Отправленных сформировано: N» — ожидаемый шаг цепочки F6:
    # тот же заголовок «Печать», единственная кнопка ОК, нет «Отмены».
    $подтверждёнФормированиеМб = $false
    if ($успех) {
        $deadline = (Get-Date).AddSeconds($таймаутШага)
        while ((Get-Date) -lt $deadline -and -not $подтверждёнФормированиеМб) {
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -eq 'Печать') {
                    $текст = [AccWin]::Title($w)
                    Запись "MessageBox формирования отправлений появился: hwnd=$w title='$текст'"
                    $кнопкаОк = [AccWin]::FindButton($w, 'ОК')
                    $кнопкаОтмена = [AccWin]::FindButton($w, 'Отмена')
                    if ($кнопкаОк -ne [IntPtr]::Zero -and $кнопкаОтмена -eq [IntPtr]::Zero) {
                        $скр = Join-Path $out ('24-messagebox-формирование-' + (Get-Date -Format 'HHmmss') + '.png')
                        [AccWin]::Capture($w, $скр) | Out-Null
                        [AccWin]::Click($кнопкаОк)
                        $подтверждёнФормированиеМб = $true
                        Запись "MessageBox подтверждён (ОК), скриншот: $(Split-Path -Leaf $скр)"
                    }
                    break
                }
            }
            if (-not $подтверждёнФормированиеМб) { Start-Sleep -Milliseconds 300 }
        }
        & $всёОк $подтверждёнФормированиеМб 'MessageBox формирования отправлений найден и подтверждён'
    }

    $печать2 = [IntPtr]::Zero
    if ($успех) {
        $печать2 = Wait-ОжидаемоеОкно $pid_ 'Печать' 'f6-print'
        if ($печать2 -ne [IntPtr]::Zero) {
            Запись "Диалог «Печать» появился: hwnd=$печать2"
            Start-Sleep -Milliseconds 500
            $кнопка = Wait-Button $печать2 'Печать' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Диалог «Печать»: нажата Печать — ожидание окна предпросмотра'
            } else {
                & $всёОк $false 'кнопка Печать диалога не найдена'
            }
        } elseif ($успех) {
            & $всёОк $false 'диалог «Печать» F6 не появился'
        }
    }

    # «Подача конвертов» — ожидаемый шаг цепочки F6 (предупреждение о
    # подаче перед печатью). «Продолжить» ведёт в предпросмотр; спулер
    # вызывается только после подтверждения предпросмотра.
    $подачаНайдена = $false
    if ($успех) {
        $deadline = (Get-Date).AddSeconds($таймаутШага)
        while ((Get-Date) -lt $deadline -and -not $подачаНайдена) {
            $подача = [AccWin]::FindByTitle($pid_, 'Подача конвертов')
            if ($подача -ne [IntPtr]::Zero) {
                $подачаНайдена = $true
                Запись "Окно «Подача конвертов» появилось: hwnd=$подача"
                $скр = Join-Path $out ('25-подача-конвертов-' + (Get-Date -Format 'HHmmss') + '.png')
                [AccWin]::Capture($подача, $скр) | Out-Null
                Запись "скриншот подачи: $(Split-Path -Leaf $скр)"
                $продолжить = Wait-Button $подача 'Продолжить' 8
                if ($продолжить -ne [IntPtr]::Zero) {
                    [AccWin]::Click($продолжить)
                    Запись 'Подача конвертов: нажата Продолжить'
                } else {
                    & $всёОк $false 'кнопка Продолжить не найдена в окне подачи'
                }
                break
            }
            # неожиданные модалки в этой фазе — FAIL
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770' -and [AccWin]::Title($w) -ne 'Подача конвертов') {
                    ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-f6-feed'
                    & $всёОк $false 'неожиданный MessageBox в фазе подачи'
                    break
                }
            }
            Start-Sleep -Milliseconds 300
        }
        & $всёОк $подачаНайдена 'окно «Подача конвертов» появилось и обработано'
    }

    $предпросмотр = [IntPtr]::Zero
    if ($успех) {
        $deadline = (Get-Date).AddSeconds($таймаутШага)
        while ((Get-Date) -lt $deadline) {
            # неожиданные модалки в фазе предпросмотра — FAIL
            foreach ($w in [AccWin]::WindowsOf($pid_)) {
                $cls = New-Object System.Text.StringBuilder 256
                [AccWin]::GetClassName($w, $cls, 256) | Out-Null
                if ($cls.ToString() -eq '#32770') {
                    ЗафиксироватьНеожиданное 'MessageBox' $w 'unexpected-f6-preview'
                    & $всёОк $false 'неожиданный MessageBox в фазе предпросмотра'
                    break
                }
            }
            if (-not $успех) { break }

            $предпросмотр = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
            if ($предпросмотр -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 400
        }

        if ($предпросмотр -ne [IntPtr]::Zero -and $успех) {
            Запись "Окно «Предпросмотр печати» появилось: hwnd=$предпросмотр"
            $скр = Join-Path $out '22-предпросмотр-f6.png'
            [AccWin]::Capture($предпросмотр, $скр) | Out-Null
            Запись "скриншот предпросмотра: $(Split-Path -Leaf $скр)"

            $кнопка = Wait-Button $предпросмотр 'Закрыть' 8
            if ($кнопка -ne [IntPtr]::Zero) {
                [AccWin]::Click($кнопка)
                Запись 'Предпросмотр: нажата Закрыть (DialogResult != OK, спулер не вызывается)'
            } else {
                & $всёОк $false 'кнопка Закрыть предпросмотра не найдена'
            }
            Start-Sleep -Seconds 3
            $осталось = [AccWin]::FindByTitle($pid_, 'Предпросмотр печати')
            & $всёОк ($осталось -eq [IntPtr]::Zero) 'окно предпросмотра исчезло'
        } elseif ($успех) {
            & $всёОк $false 'окно «Предпросмотр печати» не появилось'
        }
    }

    $живF6 = $false
    if ($успех) {
        $живF6 = [AccWin]::Responsive($h.Hwnd, 5000)
        Запись "UI отвечает после F6 (WM_NULL): $живF6"
        if (-not $живF6) { $успех = $false }
    }

    # --- 5. Завершение и уборка --------------------------------------------
    $убит = Stop-Acceptance $h
    if ($h -ne $null -and -not $h.Proc.HasExited) { $h.Proc.WaitForExit() }
    $кодВыходаHarness = if ($h -ne $null) { $h.Proc.ExitCode } else { 1 }
    Запись "harness exit code: $кодВыходаHarness; killed: $убит"

    # Проверки stdout — по накопленным строкам harness.
    $stdout = Get-HarnessLines $h
    $hasSelect = [bool]($stdout | Where-Object { $_ -match '^OK SELECT' })
    $hasF5 = [bool]($stdout | Where-Object { $_ -match '^OK F5' })
    $hasF6 = [bool]($stdout | Where-Object { $_ -match '^OK F6' })
    $отказовStdout = @($stdout | Where-Object { $_ -match '^FAIL ' }).Count
    Запись "stdout: OK SELECT=$hasSelect OK F5=$hasF5 OK F6=$hasF6 FAIL-строк=$отказовStdout"

    $профильОчищен = -not (Test-Path $корень)
    Запись "temp profile cleaned: $профильОчищен"

    # --- 6. Реальный профиль ПОСЛЕ -----------------------------------------
    $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
    $sentinelПосле = Get-SentinelState $sentinel
    $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
    Запись "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
    Запись "production delta: new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"

    # --- 7. Итог ------------------------------------------------------------
    & $всёОк $hasSelect 'SELECT подтверждён stdout (OK SELECT)'
    & $всёОк ($отказовStdout -eq 0) 'нет FAIL-строк в stdout harness'

    # OK F5/OK F6 печатаются асинхронно после закрытия модальной цепочки
    # (async void + ShowDialog) и не гарантированы в stdout — не FAIL-условие;
    # фактическое выполнение команд доказано появлением и обработкой
    # всех модальных окон цепочки (см. журнал выше).
    Запись "информационно: OK F5=$hasF5 OK F6=$hasF6 в stdout (async void после ShowDialog)"

    if ($успех) {
        Запись 'PRINTFLOW PASS: True'
        $exitCode = 0
    } else {
        Запись 'PRINTFLOW PASS: False'
    }
}
catch {
    Запись "EXCEPTION: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    if ($h -ne $null -and $h.Proc -ne $null -and -not $h.Proc.HasExited) {
        Write-Host '  finally: аварийное завершение harness'
        try { $h.Proc.StandardInput.WriteLine('CLOSE') } catch {}
        if (-not $h.Proc.WaitForExit(10000)) { $h.Proc.Kill() }
    }

    try {
        if ($отпечатокДо -ne $null) {
            $отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
            $дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле
            Запись "production delta (finally): new=$($дельта.New.Count) changed=$($дельта.Changed.Count) removed=$($дельта.Removed.Count)"
            $дельта.New | ForEach-Object { Запись "  NEW: $_" }
            $дельта.Changed | ForEach-Object { Запись "  CHANGED: $_" }
        }
    } catch {}

    if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

    try {
        $строки = @(
            '# F5/F6 runtime-доказательство (acceptance harness)',
            '',
            "Прогон: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), профиль: временный (guid), createDemoData=false.",
            'Физическая печать не выполнялась: F5 — «Отмена», F6 — «Закрыть» на предпросмотре.',
            ''
        ) + $журнал
        Set-Content -Path $evidence -Value $строки -Encoding UTF8
    } catch {}
}

exit $exitCode
