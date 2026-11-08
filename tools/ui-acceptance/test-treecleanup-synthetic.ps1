# Синтетический regression-тест дерева процессов и финализационных
# контрактов — БЕЗ Picket302, БЕЗ окон приложения, БЕЗ production-профиля:
#   A. timeout-kill: скрытый родительский PowerShell держит скрытого
#      ребёнка; graceful CLOSE не срабатывает (родитель не читает stdin);
#      Stop-HarnessTree по таймауту завершает ОБА, повторный скан
#      подтверждает отсутствие обоих.
#   B. parent-exited-child-remains: родитель завершается сам, ребёнок
#      жив; «родитель завершился» не считается чистотой — потомок
#      находится по сохранённому PID и завершается, повторный скан
#      подтверждает.
#   C/D/E. fault-инъекции (kill-fail, cim-fail, descendant-remains) —
#      cleanup обязан стать НЕ подтверждённым (TreeError/RemainingPids);
#      ни один fault не оставляет реальный процесс — финальная уборка
#      в finally по сохранённым PID.
#   F. evidence-запись без UI: невозможность записи → $false; проверка
#      маркеров на файле без маркера → $false.
#   G. блокировка без UI: happy Released+Disposed=true; fault
#      P302_LOCK_FAULT=release-fail → Released=$false (Error).
#   H. bounded timeout САМОЙ taskkill: seam P302_TASKKILL_FAULT=hang
#      подменяет runner на синтетический процесс, который спит дольше
#      таймаута; helper обязан вернуться в ограниченном бюджете с
#      TimedOut=true, Success=false, сохранив PID/Operation/Exception,
#      и не оставить зависший процесс живым.
#   I. Stop-HarnessTree при таймауте taskkill: без исключения, post-scan
#      выполнен, TreeOk=false, TreeError содержит таймаут, списки
#      RemainingPids/UnconfirmedPids не потеряны.
# Итог: exit 0 только если ВСЕ ожидания выполнены и synthetic-процессов
# не осталось; иначе exit 1.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exitCode = 1
$каталогВрем = Join-Path ([System.IO.Path]::GetTempPath()) ("P302TreeSim_" + [Guid]::NewGuid().ToString('N'))
$оставленныеПроцессы = [System.Collections.Generic.List[int]]::new()
$провалы = [System.Collections.Generic.List[string]]::new()

function Проверить([bool]$условие, [string]$имя, [string]$детали = '') {
    if ($условие) {
        Write-Host "  OK: $имя"
    } else {
        Write-Host "  FAIL: $имя $(if ($детали) { "— $детали" })"
        $script:провалы.Add($имя)
    }
}

function ЗаписатьДочернийСкрипт([string]$путь, [string]$маркер, [int]$режимРодителя) {
    # Режим родителя: 1 — живёт (timeout-путь), 2 — завершается сам после
    # запуска ребёнка (parent-exited). Ребёнок пишет маркер и спит.
    $дочернийПуть = Join-Path $каталогВрем 'child.ps1'
    $кодРебёнка = "Set-Content -LiteralPath '$маркер' -Value 'child' -ErrorAction Stop; Start-Sleep -Seconds 120"
    Set-Content -LiteralPath $дочернийПуть -Value $кодРебёнка -Encoding UTF8 -ErrorAction Stop

    $кодРодителя = @"
`$psi = New-Object System.Diagnostics.ProcessStartInfo
`$psi.FileName = 'powershell.exe'
`$psi.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "$дочернийПуть"'
`$psi.UseShellExecute = `$false
`$psi.CreateNoWindow = `$true
`$ребёнок = [System.Diagnostics.Process]::Start(`$psi)
Set-Content -LiteralPath '$маркер.родитель' -Value `$ребёнок.Id -ErrorAction Stop
if ($режимРодителя -eq 1) { Start-Sleep -Seconds 120 }
"@
    Set-Content -LiteralPath $путь -Value $кодРодителя -Encoding UTF8 -ErrorAction Stop
}

function ЗапуститьРодителя([string]$скриптРодителя) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'powershell.exe'
    $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$скриптРодителя`""
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $процесс = [System.Diagnostics.Process]::Start($psi)
    $оставленныеПроцессы.Add($процесс.Id)
    return $процесс
}

function ПодождатьФайл([string]$путь, [int]$секунд) {
    $дедлайн = (Get-Date).AddSeconds($секунд)
    while ((Get-Date) -lt $дедлайн -and -not (Test-Path -LiteralPath $путь)) {
        Start-Sleep -Milliseconds 100
    }
    return (Test-Path -LiteralPath $путь)
}

# Завершение синтетического процесса: через безопасный helper
# Invoke-TaskKillSafe — гонка «процесс исчез между проверкой и
# taskkill» диагностируется (already-exited race), а НЕ превращается в
# необработанное исключение (иначе аварийная уборка в finally сама
# роняла бы suite).
function ЗавершитьПоПид([int]$идентификатор, [string]$метка = 'synthetic') {
    if ($идентификатор -le 0) { return }

    $результат = Invoke-TaskKillSafe $идентификатор $метка

    if ($результат.NotFound -or $результат.AlreadyExited) {
        Write-Host "  TREE RACE: процесс уже завершён (pid=$идентификатор, метка=$метка) — benign race"
        return
    }

    if ($результат.Exception) {
        Write-Host "  ошибка taskkill (pid=$идентификатор, метка=$метка): $($результат.Exception)"
        return
    }

    if (-not $результат.Success) {
        Write-Host "  taskkill вернул $($результат.ExitCode) (pid=$идентификатор, метка=$метка)"
        return
    }
}

try {
    New-Item -ItemType Directory -Path $каталогВрем -ErrorAction Stop | Out-Null

    # --- G1. Блокировка без UI: happy -------------------------------------
    Write-Host '=== G1: Enter/Exit-SharedTestLock (happy)'
    $блокировка = Enter-SharedTestLock 60
    $результатБлокировки = Exit-SharedTestLock $блокировка 'tree-sim-g1'
    Проверить ($результатБлокировки.Released -and $результатБлокировки.Disposed) 'G1 happy: Released+Disposed' $результатБлокировки.Error

    # --- G2. Блокировка без UI: fault release-fail ------------------------
    Write-Host '=== G2: Exit-SharedTestLock fault release-fail'
    $env:P302_LOCK_FAULT = 'release-fail'
    try {
        $блокировка2 = Enter-SharedTestLock 60
        $результат2 = Exit-SharedTestLock $блокировка2 'tree-sim-g2'
        Проверить (-not $результат2.Released) 'G2 fault: Released=false' ($результат2 | Out-String)
        Проверить ($результат2.Error -match 'fault-inject: lock release failure') 'G2 fault: маркер диагностики' $результат2.Error
    } finally {
        Remove-Item Env:P302_LOCK_FAULT -ErrorAction SilentlyContinue
    }

    # --- F. Evidence-запись без UI -----------------------------------------
    Write-Host '=== F: evidence write/verify без UI'
    $записьНеудачна = -not (ЗаписатьEvidence 'bad|name-evidence-sim.md' 'Синтетика' 'симуляция' @('строка'))
    Проверить $записьНеудачна 'F: невозможность записи evidence → $false'
    $маркерОтсутствует = -not (Test-EvidenceWritten 'f5-f6-runtime-evidence.md' @('НЕСУЩЕСТВУЮЩИЙ-МАРКЕР-СИМУЛЯЦИИ'))
    Проверить $маркерОтсутствует 'F: проверка маркеров на файле без маркера → $false'

    # --- A. timeout-kill ----------------------------------------------------
    Write-Host '=== A: timeout-kill (родитель жив, ребёнок жив)'
    $маркерА = Join-Path $каталогВрем 'a-child.marker'
    $скриптА = Join-Path $каталогВрем 'parent-a.ps1'
    ЗаписатьДочернийСкрипт $скриптА $маркерА 1
    $родительА = ЗапуститьРодителя $скриптА
    $стартА = $родительА.StartTime
    $идРебёнкаА = 0

    if (-not (ПодождатьФайл "$маркерА.родитель" 15)) {
        $провалы.Add('A: родитель не сообщил о ребёнке')
        Write-Host '  FAIL: родитель не сообщил о ребёнке'
    } else {
        $идРебёнкаА = [int](Get-Content -LiteralPath "$маркерА.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаА)
    }

    $результатА = Stop-HarnessTree $родительА 2000

    Проверить (-not $результатА.Graceful) 'A: graceful не состоялся (timeout-путь)'
    Проверить $результатА.Killed 'A: потребовался force'
    Проверить $результатА.TreeOk 'A: дерево чисто после cleanup' $результатА.TreeError
    Проверить $результатА.TreeConfirmed 'A: повторный скан выполнен'
    Проверить ($результатА.RemainingPids.Count -eq 0) 'A: остатков нет' ($результатА.RemainingPids -join ';')
    Проверить ($результатА.KillOutput.Count -gt 0) 'A: вывод taskkill сохранён для диагностики'

    if ($идРебёнкаА -gt 0) {
        Проверить (-not (Test-ProcessAlive $идРебёнкаА)) "A: ребёнок pid=$идРебёнкаА завершён"
    }

    # --- B. parent-exited-child-remains --------------------------------------
    Write-Host '=== B: родитель завершился сам, ребёнок жив'
    $маркерБ = Join-Path $каталогВрем 'b-child.marker'
    $скриптБ = Join-Path $каталогВрем 'parent-b.ps1'
    ЗаписатьДочернийСкрипт $скриптБ $маркерБ 2
    $родительБ = ЗапуститьРодителя $скриптБ
    # StartTime читается ДО самозавершения родителя (PID reuse).
    $стартБ = $родительБ.StartTime
    $идРебёнкаБ = 0

    if (-not (ПодождатьФайл "$маркерБ.родитель" 15)) {
        $провалы.Add('B: родитель не сообщил о ребёнке')
        Write-Host '  FAIL: родитель не сообщил о ребёнке'
    } else {
        $идРебёнкаБ = [int](Get-Content -LiteralPath "$маркерБ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаБ)
    }

    # Ждём самозавершение родителя: «родитель завершился» — не доказательство.
    $дедлайнБ = (Get-Date).AddSeconds(15)
    while (-not $родительБ.HasExited -and (Get-Date) -lt $дедлайнБ) {
        Start-Sleep -Milliseconds 100
    }

    Проверить ($родительБ.HasExited) 'B: родитель завершился до уборки'
    Проверить ($идРебёнкаБ -gt 0 -and (Test-ProcessAlive $идРебёнкаБ)) 'B: ребёнок был жив после выхода родителя'

    $результатБ = Stop-HarnessTree $родительБ 2000

    Проверить ($результатБ.RemainingPids.Count -eq 0) 'B: после cleanup остатков нет' ($результатБ.RemainingPids -join ';')
    Проверить $результатБ.TreeOk 'B: дерево чисто' $результатБ.TreeError

    if ($идРебёнкаБ -gt 0) {
        Проверить (-not (Test-ProcessAlive $идРебёнкаБ)) "B: ребёнок pid=$идРебёнкаБ завершён уборкой"
    }

    # --- C. fault kill-fail ---------------------------------------------------
    Write-Host '=== C: fault kill-fail (taskkill nonzero)'
    $маркерВ = Join-Path $каталогВрем 'c-child.marker'
    $скриптВ = Join-Path $каталогВрем 'parent-c.ps1'
    ЗаписатьДочернийСкрипт $скриптВ $маркерВ 1
    $родительВ = ЗапуститьРодителя $скриптВ
    $стартВ = $родительВ.StartTime
    $идРебёнкаВ = 0

    if (ПодождатьФайл "$маркерВ.родитель" 15) {
        $идРебёнкаВ = [int](Get-Content -LiteralPath "$маркерВ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаВ)
    }

    $env:P302_TREE_FAULT = 'kill-fail'
    try {
        $результатВ = Stop-HarnessTree $родительВ 2000
    } finally {
        Remove-Item Env:P302_TREE_FAULT -ErrorAction SilentlyContinue
    }

    Проверить ($результатВ.TreeError -match 'fault-inject: taskkill failure simulated') 'C: fault обнаружен в TreeError' $результатВ.TreeError
    Проверить (-not $результатВ.TreeOk) 'C: cleanup НЕ подтверждён при fault'

    # --- D. fault cim-fail ------------------------------------------------------
    Write-Host '=== D: fault cim-fail (перечисление дерева)'
    $маркерГ = Join-Path $каталогВрем 'd-child.marker'
    $скриптГ = Join-Path $каталогВрем 'parent-d.ps1'
    ЗаписатьДочернийСкрипт $скриптГ $маркерГ 1
    $родительГ = ЗапуститьРодителя $скриптГ
    $стартГ = $родительГ.StartTime
    $идРебёнкаГ = 0

    if (ПодождатьФайл "$маркерГ.родитель" 15) {
        $идРебёнкаГ = [int](Get-Content -LiteralPath "$маркерГ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаГ)
    }

    $env:P302_TREE_FAULT = 'cim-fail'
    try {
        $результатГ = Stop-HarnessTree $родительГ 2000
    } finally {
        Remove-Item Env:P302_TREE_FAULT -ErrorAction SilentlyContinue
    }

    Проверить ($результатГ.TreeError -match 'fault-inject: CIM tree enumeration failure') 'D: CIM-ошибка не превращена в пустой список' $результатГ.TreeError
    Проверить (-not $результатГ.TreeConfirmed) 'D: TreeConfirmed=false при ошибке CIM'
    Проверить (-not $результатГ.TreeOk) 'D: cleanup НЕ подтверждён при fault'

    # --- E. fault descendant-remains ---------------------------------------------
    Write-Host '=== E: fault descendant-remains (родитель вышел, ребёнка не добили)'
    $маркерД = Join-Path $каталогВрем 'e-child.marker'
    $скриптД = Join-Path $каталогВрем 'parent-e.ps1'
    ЗаписатьДочернийСкрипт $скриптД $маркерД 2
    $родительД = ЗапуститьРодителя $скриптД
    $стартД = $родительД.StartTime
    $идРебёнкаД = 0

    if (ПодождатьФайл "$маркерД.родитель" 15) {
        $идРебёнкаД = [int](Get-Content -LiteralPath "$маркерД.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаД)
    }

    $дедлайнД = (Get-Date).AddSeconds(15)
    while (-not $родительД.HasExited -and (Get-Date) -lt $дедлайнД) {
        Start-Sleep -Milliseconds 100
    }

    $env:P302_TREE_FAULT = 'descendant-remains'
    try {
        $результатД = Stop-HarnessTree $родительД 2000
    } finally {
        Remove-Item Env:P302_TREE_FAULT -ErrorAction SilentlyContinue
    }

    Проверить ($результатД.TreeError -match 'fault-inject: descendant remains') 'E: fault обнаружен' $результатД.TreeError
    Проверить (-not $результатД.TreeOk) 'E: cleanup НЕ подтверждён при fault'
    Проверить ($результатД.RemainingPids.Count -gt 0) 'E: остаток descendant виден в RemainingPids' ($результатД.RemainingPids -join ';')

    if ($идРебёнкаД -gt 0 -and (Test-ProcessAlive $идРебёнкаД)) {
        # Fault не оставляет реальный процесс: ручная уборка остатка.
        ЗавершитьПоПид $идРебёнкаД
        Start-Sleep -Milliseconds 500
    }

    # --- F. Детерминированная гонка: descendant исчез до taskkill --------------
    Write-Host '=== F: race descendant-disappears-before-taskkill (исчез между scan и taskkill)'
    $маркерФ = Join-Path $каталогВрем 'f-child.marker'
    $скриптФ = Join-Path $каталогВрем 'parent-f.ps1'
    ЗаписатьДочернийСкрипт $скриптФ $маркерФ 1
    $родительФ = ЗапуститьРодителя $скриптФ
    $идРебёнкаФ = 0

    if (ПодождатьФайл "$маркерФ.родитель" 15) {
        $идРебёнкаФ = [int](Get-Content -LiteralPath "$маркерФ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаФ)
    } else {
        $провалы.Add('F: родитель не сообщил о ребёнке')
    }

    $env:P302_TREE_FAULT = 'descendant-disappears-before-taskkill'
    try {
        # Шов гарантирует: потомок найден первым scan И исчезает до
        # taskkill (taskkill честно вернёт not-found) — гонка
        # воспроизводится детерминированно, без случайного окна.
        $результатФ = Stop-HarnessTree $родительФ 2000
    } catch {
        $результатФ = $null
        $провалы.Add('F: Stop-HarnessTree бросил необработанное исключение')
        Write-Host "  FAIL: Stop-HarnessTree бросил исключение: $($_.Exception.Message)"
    } finally {
        Remove-Item Env:P302_TREE_FAULT -ErrorAction SilentlyContinue
    }

    if ($результатФ -ne $null) {
        Проверить ($результатФ.TreeConfirmed -eq $true) 'F: post-scan выполнен (TreeConfirmed)'
        Проверить ($результатФ.RemainingPids.Count -eq 0) 'F: остатков нет' ($результатФ.RemainingPids -join ';')
        Проверить ($результатФ.UnconfirmedPids.Count -eq 0) 'F: неподтверждённых нет' ($результатФ.UnconfirmedPids -join ';')
        Проверить ($результатФ.TaskKillRacePids.Count -ge 1) 'F: диагностическая отметка race присутствует'
        Проверить ($результатФ.TaskKillTimedOutPids.Count -eq 0) 'F: таймаутов taskkill нет'
        Проверить ($результатФ.TreeOk -eq $true) 'F: benign race → TreeOk=true' $результатФ.TreeError
    }

    # --- G. Тот же сценарий, но PID ЖИВ: результат обязан остаться failure ----
    Write-Host '=== G: race descendant-not-found-but-alive (PID жив)'
    $маркерГ = Join-Path $каталогВрем 'g-child.marker'
    $скриптГ = Join-Path $каталогВрем 'parent-g.ps1'
    ЗаписатьДочернийСкрипт $скриптГ $маркерГ 1
    $родительГ = ЗапуститьРодителя $скриптГ
    $идРебёнкаГ = 0

    if (ПодождатьФайл "$маркерГ.родитель" 15) {
        $идРебёнкаГ = [int](Get-Content -LiteralPath "$маркерГ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаГ)
    } else {
        $провалы.Add('G: родитель не сообщил о ребёнке')
    }

    $env:P302_TREE_FAULT = 'descendant-not-found-but-alive'
    try {
        $результатГ = Stop-HarnessTree $родительГ 2000
    } catch {
        $результатГ = $null
        $провалы.Add('G: Stop-HarnessTree бросил необработанное исключение')
        Write-Host "  FAIL: Stop-HarnessTree бросил исключение: $($_.Exception.Message)"
    } finally {
        Remove-Item Env:P302_TREE_FAULT -ErrorAction SilentlyContinue
    }

    if ($результатГ -ne $null) {
        Проверить ($результатГ.TreeOk -eq $false) 'G: живой PID → TreeOk=false' $результатГ.TreeError
        Проверить ($результатГ.RemainingPids.Count -ge 1) 'G: живой PID в RemainingPids' ($результатГ.RemainingPids -join ';')
        Проверить ($результатГ.TreeError -ne '') 'G: TreeError содержит причину'
    }

    if ($идРебёнкаГ -gt 0 -and (Test-ProcessAlive $идРебёнкаГ)) {
        # Fault намеренно оставляет потомка живым: внешняя уборка.
        ЗавершитьПоПид $идРебёнкаГ 'g-manual'
        Start-Sleep -Milliseconds 500
    }

    # --- H. Внутренний bounded timeout САМОЙ taskkill ---------------------------
    # Дефект: параметр $timeoutMs в сигнатуре сам по себе не ограничивает
    # время исполнения native-команды. Контракт: taskkill — отдельный
    # процесс с WaitForExit($timeoutMs); при зависании helper возвращает
    # TimedOut=true, Success=false, сохраняет PID/Operation/Exception и
    # НЕ зависает.
    Write-Host '=== H: Invoke-TaskKillSafe — bounded timeout нативной команды'
    $маркерХ = Join-Path $каталогВрем 'h-child.marker'
    $скриптХ = Join-Path $каталогВрем 'parent-h.ps1'
    ЗаписатьДочернийСкрипт $скриптХ $маркерХ 1
    $родительХ = ЗапуститьРодителя $скриптХ
    $идРебёнкаХ = 0
    $timeoutMsХ = 800
    $elapsedХ = -1

    if (ПодождатьФайл "$маркерХ.родитель" 15) {
        $идРебёнкаХ = [int](Get-Content -LiteralPath "$маркерХ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаХ)
    } else {
        $провалы.Add('H: родитель не сообщил о ребёнке')
    }

    $env:P302_TASKKILL_FAULT = 'hang'
    try {
        # Системный taskkill подвешивать не нужно: seam подменяет runner
        # на синтетический процесс, который спит дольше таймаута.
        $часыХ = [System.Diagnostics.Stopwatch]::StartNew()
        $результатХ = Invoke-TaskKillSafe $родительХ.Id 'helper-timeout' $timeoutMsХ $true
        $часыХ.Stop()
        $elapsedХ = $часыХ.ElapsedMilliseconds
    } catch {
        $результатХ = $null
        $провалы.Add('H: helper бросил необработанное исключение')
        Write-Host "  FAIL: helper бросил исключение: $($_.Exception.Message)"
    } finally {
        Remove-Item Env:P302_TASKKILL_FAULT -ErrorAction SilentlyContinue
    }

    if ($результатХ -ne $null) {
        Проверить ($результатХ.Attempted -eq $true) 'H: Attempted=true'
        Проверить ($результатХ.Pid -eq $родительХ.Id) 'H: PID сохранён' ("$($результатХ.Pid)")
        Проверить ($результатХ.Operation -eq 'helper-timeout') 'H: Operation сохранён' ("$($результатХ.Operation)")
        Проверить ($результатХ.TimedOut -eq $true) 'H: TimedOut=true'
        Проверить ($результатХ.Success -eq $false) 'H: Success=false'
        Проверить ($null -eq $результатХ.ExitCode) 'H: ExitCode=null (процесс не завершился штатно)' ("$($результатХ.ExitCode)")
        Проверить ($результатХ.ContainsKey('Exception')) 'H: поле Exception присутствует'
        Проверить ($результатХ.NotFound -eq $false) 'H: таймаут НЕ трактуется как not-found'
        # Фактическое ожидание ограничено таймаутом; общий elapsed — таймаут
        # плюс небольшой фиксированный запас на запуск и сбор диагностики.
        Проверить ($результатХ.WaitMs -le ($timeoutMsХ + 1000)) 'H: фактическое ожидание ограничено таймаутом' ("$($результатХ.WaitMs) мс")
        Проверить ($elapsedХ -le ($timeoutMsХ + 8000)) 'H: elapsed в пределах бюджета' ("$elapsedХ мс")
        # Зависший синтетический runner не остаётся жить.
        Проверить ($null -eq $результатХ.ChildPid -or -not (Test-ProcessAlive $результатХ.ChildPid)) 'H: зависший runner не остался' ("pid=$($результатХ.ChildPid)")
    } else {
        $провалы.Add('H: результат helper не получен')
    }

    # --- I. Stop-HarnessTree: таймаут taskkill → TreeOk=false --------------------
    Write-Host '=== I: Stop-HarnessTree — таймаут taskkill (fail-closed)'
    $маркерИ = Join-Path $каталогВрем 'i-child.marker'
    $скриптИ = Join-Path $каталогВрем 'parent-i.ps1'
    ЗаписатьДочернийСкрипт $скриптИ $маркерИ 1
    $родительИ = ЗапуститьРодителя $скриптИ
    $идРебёнкаИ = 0
    $elapsedИ = -1

    if (ПодождатьФайл "$маркерИ.родитель" 15) {
        $идРебёнкаИ = [int](Get-Content -LiteralPath "$маркерИ.родитель" -Raw).Trim()
        $оставленныеПроцессы.Add($идРебёнкаИ)
    } else {
        $провалы.Add('I: родитель не сообщил о ребёнке')
    }

    $env:P302_TASKKILL_FAULT = 'hang'
    try {
        $часыИ = [System.Diagnostics.Stopwatch]::StartNew()
        $результатИ = Stop-HarnessTree $родительИ 1000
        $часыИ.Stop()
        $elapsedИ = $часыИ.ElapsedMilliseconds
    } catch {
        $результатИ = $null
        $провалы.Add('I: Stop-HarnessTree бросил необработанное исключение')
        Write-Host "  FAIL: Stop-HarnessTree бросил исключение: $($_.Exception.Message)"
    } finally {
        Remove-Item Env:P302_TASKKILL_FAULT -ErrorAction SilentlyContinue
    }

    if ($результатИ -ne $null) {
        Проверить ($результатИ.TreeOk -eq $false) 'I: таймаут → TreeOk=false' $результатИ.TreeError
        Проверить ($результатИ.TreeConfirmed -eq $true) 'I: финальный post-scan выполнен'
        Проверить ($результатИ.TreeError -match 'таймаут') 'I: TreeError содержит таймаут' $результатИ.TreeError
        Проверить ($результатИ.TaskKillTimedOutPids.Count -ge 1) 'I: PID с таймаутом зафиксирован' ($результатИ.TaskKillTimedOutPids -join ';')
        Проверить ($null -ne $результатИ.RemainingPids) 'I: RemainingPids не потеряны'
        Проверить ($null -ne $результатИ.UnconfirmedPids) 'I: UnconfirmedPids не потеряны'
        Проверить ($результатИ.RemainingPids.Count -ge 1) 'I: живой потомок виден в RemainingPids' ($результатИ.RemainingPids -join ';')
        Проверить ($elapsedИ -le 60000) 'I: остановка не зависла' ("$elapsedИ мс")
    } else {
        $провалы.Add('I: результат Stop-HarnessTree не получен')
    }

    # --- Итог: synthetic-процессов не осталось -----------------------------------
    Write-Host '=== Финал: synthetic-процессы'
    foreach ($идентификатор in $оставленныеПроцессы) {
        if (Test-ProcessAlive $идентификатор) {
            ЗавершитьПоПид $идентификатор
            Start-Sleep -Milliseconds 500
        }

        Проверить (-not (Test-ProcessAlive $идентификатор)) "synthetic pid=$идентификатор не остался"
    }

    if ($провалы.Count -eq 0) {
        Write-Host 'TREE-CLEANUP-SYNTHETIC PASS: True'
        $exitCode = 0
    } else {
        Write-Host ("TREE-CLEANUP-SYNTHETIC PASS: False (" + ($провалы -join '; ') + ")")
        $exitCode = 1
    }
}
catch {
    Write-Host "TREE-CLEANUP-SYNTHETIC EXCEPTION: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    # Аварийная уборка синтетики: ни один fault не оставляет процесс.
    # Каждый вызов изолирован: ошибка уборки одного PID не прерывает
    # уборку остальных и не роняет suite.
    foreach ($идентификатор in $оставленныеПроцессы) {
        try {
            ЗавершитьПоПид $идентификатор 'finally'
        } catch {
            Write-Host "  ошибка финальной уборки (pid=$идентификатор): $($_.Exception.Message)"
            $exitCode = 1
        }
    }

    Start-Sleep -Milliseconds 300

    foreach ($идентификатор in $оставленныеПроцессы) {
        if (Test-ProcessAlive $идентификатор) {
            Write-Host "  SAFETY-FAIL: synthetic pid=$идентификатор остался после уборки"
            $exitCode = 1
        }
    }

    if (Test-Path $каталогВрем) {
        try {
            Remove-Item -LiteralPath $каталогВрем -Recurse -Force -ErrorAction Stop
        } catch {
            Write-Host "  SAFETY-FAIL: каталог синтетики не удалён: $($_.Exception.Message)"
            $exitCode = 1
        }
    }
}

exit $exitCode
