# Driver-level bounded-wait suite (без UI, без печати, без production-
# изменений кроме sentinel-контроля): для run-emptybook.ps1 и
# run-printflow.ps1 с fault-инъекцией P302_DRIVER_FAULT=parent-alive-after-stop:
#   - Stop-HarnessTree возвращает TreeOk=false, synthetic harness остаётся
#     живым (скрытый PowerShell, sleep 1800, stdin не читает);
#   - внешний launcher держит watchdog: $proc.WaitForExit($driverTimeoutMs) —
#     зависание драйвера дольше бюджета = провал;
#   - драйвер обязан завершиться, exit 1, диагностика HarnessNotExited/
#     TreeCleanupFailed/RemainingPids в stdout;
#   - lock освобождён (строка LOCK RELEASE: released=True disposed=True в
#     evidence; флаг lock-release-failed отсутствует);
#   - sentinel удалён; synthetic PID убивается внешней уборкой и
#     проверяется на отсутствие.
# Итог: exit 0 только если ВСЕ проверки обоих драйверов выполнены.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exitCode = 1
$каталогЛогов = Join-Path ([System.IO.Path]::GetTempPath()) ("P302DriverWait_" + [Guid]::NewGuid().ToString('N'))
$driverTimeoutMs = 120000
$провалы = [System.Collections.Generic.List[string]]::new()
$синтетическиеПиды = [System.Collections.Generic.List[int]]::new()

function Проверить([bool]$условие, [string]$имя, [string]$детали = '') {
    if ($условие) {
        Write-Host "  OK: $имя"
    } else {
        Write-Host "  FAIL: $имя $(if ($детали) { "— $детали" })"
        $script:провалы.Add($имя)
    }
}

function ЗапуститьДрайверСFault([string]$скриптДрайвера, [string]$имя) {
    # Файловые редиректы через cmd /c: у драйвера НЕТ труб — блокировка на
    # записи в заполненный pipe-буфер (вывод драйвера больше 4 КБ) и
    # pipe-deadlock невозможны в принципе. cmd /c пробрасывает код возврата
    # драйвера, вывод читается из файлов после ограниченного ожидания.
    $логOut = Join-Path $каталогЛогов ($имя + '.stdout.log')
    $логErr = Join-Path $каталогЛогов ($имя + '.stderr.log')
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'cmd.exe'
    $psi.Arguments = '/c powershell -NoProfile -ExecutionPolicy Bypass -File "' + $скриптДрайвера + '" > "' + $логOut + '" 2> "' + $логErr + '"'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['P302_DRIVER_FAULT'] = 'parent-alive-after-stop'
    # Evidence fault-прогона — во временный каталог: отслеживаемые
    # файлы документации в репозитории НЕ перезаписываются тестом.
    $psi.EnvironmentVariables['P302_EVIDENCE_ROOT'] = $каталогЛогов
    $процесс = [System.Diagnostics.Process]::Start($psi)

    Write-Host "=== ${имя}: драйвер запущен (pid=$($процесс.Id), watchdog=$driverTimeoutMs мс)"

    # Watchdog: бесконечное ожидание ЗАПРЕЩЕНО и в тестере.
    $завершён = $процесс.WaitForExit($driverTimeoutMs)

    if (-not $завершён) {
        Write-Host "  DRIVER HUNG: драйвер не завершился за $driverTimeoutMs мс — принудительное завершение дерева"
        # Безопасный helper: гонка «процесс исчез» не бросает исключение.
        [void](Invoke-TaskKillSafe $процесс.Id 'driver-hang')
        try { $процесс.Kill() } catch { }
        try { $процесс.WaitForExit(5000) | Out-Null } catch { }
        $script:провалы.Add("${имя}: драйвер завис дольше bounded timeout")
        return $null
    }

    # Читатели cmd дописывают хвост файлов после выхода.
    Start-Sleep -Milliseconds 700

    $кодВыхода = $процесс.ExitCode  # cmd /c пробрасывает код драйвера
    $stdout = ''
    if (Test-Path -LiteralPath $логOut) {
        $stdout = Get-Content -LiteralPath $логOut -Raw
    }

    $stderr = ''
    if (Test-Path -LiteralPath $логErr) {
        $stderr = Get-Content -LiteralPath $логErr -Raw
    }

    Write-Host "  драйвер завершился: exit=$кодВыхода ($($stdout.Length) символов stdout)"
    return @{ ExitCode = $кодВыхода; Stdout = $stdout; Stderr = $stderr; Лог = $логOut }
}

try {
    New-Item -ItemType Directory -Path $каталогЛогов -ErrorAction Stop | Out-Null

    # Side-effect hygiene: рабочее дерево до прогона (baseline).
    $репозиторий = Split-Path -Parent $PSScriptRoot
    $gitДо = @(& git -C $репозиторий status --porcelain 2>$null)

    foreach ($случай in @(
        @{ Имя = 'emptybook'; Файл = Join-Path $PSScriptRoot 'run-emptybook.ps1' },
        @{ Имя = 'printflow'; Файл = Join-Path $PSScriptRoot 'run-printflow.ps1' }
    )) {
        $имя = $случай.Имя
        $результат = ЗапуститьДрайверСFault $случай.Файл $имя

        if ($результат -eq $null) {
            continue
        }

        # Exit code обязан быть 1 (failure остановки не превращается в PASS).
        Проверить ($результат.ExitCode -eq 1) "${имя}: exit code = 1" "фактический: $($результат.ExitCode)"

        # Диагностика fault-пути в stdout.
        Проверить ($результат.Stdout -match 'FAULT-INJECT: parent-alive-after-stop') "${имя}: маркер fault-injection"
        Проверить ($результат.Stdout -match 'SYNTHETIC HARNESS PID: (\d+)') "${имя}: synthetic PID в диагностике"
        Проверить ($результат.Stdout -match 'SAFETY-FAIL') "${имя}: SAFETY-FAIL диагностика"
        Проверить (
            ($результат.Stdout -match 'HarnessNotExited') -or
            ($результат.Stdout -match 'harness-not-exited') -or
            ($результат.Stdout -match 'TreeCleanupFailed') -or
            ($результат.Stdout -match 'tree-cleanup-failed') -or
            ($результат.Stdout -match 'RemainingPids') -or
            ($результат.Stdout -match 'remaining-descendants')) "${имя}: причина остановки (HarnessNotExited/TreeCleanupFailed/RemainingPids)"

        # PASS-верdict невозможен в fault-режиме.
        Проверить (-not ($результат.Stdout -match 'PASS: True')) "${имя}: PASS: True отсутствует"

        # Lock освобождён: evidence содержит LOCK RELEASE released=True disposed=True
        # и не содержит lock-release-failed.
        $строкаБлокировки = $результат.Stdout | Select-String -Pattern 'LOCK RELEASE: released=(\w+) disposed=(\w+)' | Select-Object -First 1
        Проверить ($null -ne $строкаБлокировки) "${имя}: строка LOCK RELEASE присутствует"

        if ($null -ne $строкаБлокировки) {
            Проверить (
                $строкаБлокировки.Matches[0].Groups[1].Value -eq 'True' -and
                $строкаБлокировки.Matches[0].Groups[2].Value -eq 'True') "${имя}: lock released=True disposed=True"
        }

        Проверить (-not ($результат.Stdout -match 'неудачи очистки: .*lock-release-failed')) "${имя}: lock-release-failed отсутствует"

        # Sentinel удалён драйвером.
        $sentinel = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData) + '\Picket302') 'picket302-review-sentinel.tmp'
        Проверить (-not (Test-Path $sentinel)) "${имя}: sentinel удалён"

        # Synthetic PID: извлечь, убить, проверить отсутствие.
        $совпадение = [regex]::Match($результат.Stdout, 'SYNTHETIC HARNESS PID: (\d+)')

        if ($совпадение.Success) {
            $syntheticPid = [int]$совпадение.Groups[1].Value
            $синтетическиеПиды.Add($syntheticPid)

            if (Test-ProcessAlive $syntheticPid) {
                [void](Invoke-TaskKillSafe $syntheticPid 'synthetic')
                Start-Sleep -Milliseconds 500
            }

            Проверить (-not (Test-ProcessAlive $syntheticPid)) "${имя}: synthetic pid=$syntheticPid не остался"
        }
    }

    # Side-effect hygiene: рабочее дерево после прогона должно совпасть
    # с baseline (tracked-файлы не изменены тестом).
    $gitПосле = @(& git -C $репозиторий status --porcelain 2>$null)
    $изменения = @(Compare-Object -ReferenceObject $gitДо -DifferenceObject $gitПосле)

    if ($изменения.Count -ne 0) {
        Write-Host ("  FAIL: рабочее дерево изменилось после fault-теста: " + ($изменения | ForEach-Object { $_.InputObject + ' ' + $_.SideIndicator } | Out-String))
        $провалы.Add('рабочее дерево изменилось после fault-теста')
    } else {
        Write-Host '  OK: рабочее дерево не изменилось (tracked-файлы не затронуты)'
    }

    if ($провалы.Count -eq 0) {
        Write-Host 'DRIVER-BOUNDED-WAIT PASS: True'
        $exitCode = 0
    } else {
        Write-Host ("DRIVER-BOUNDED-WAIT PASS: False (" + ($провалы -join '; ') + ")")
        $exitCode = 1
    }
}
catch {
    Write-Host "DRIVER-BOUNDED-WAIT EXCEPTION: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    # Аварийная уборка синтетики и драйверов: ни один тестовый процесс не остаётся.
    foreach ($идентификатор in $синтетическиеПиды) {
        try {
            if (Test-ProcessAlive $идентификатор) {
                [void](Invoke-TaskKillSafe $идентификатор 'finally')
            }
        } catch {
            Write-Host "  ошибка финальной уборки (pid=$идентификатор): $($_.Exception.Message)"
            $exitCode = 1
        }
    }

    Start-Sleep -Milliseconds 300

    foreach ($идентификатор in $синтетическиеПиды) {
        if (Test-ProcessAlive $идентификатор) {
            Write-Host "  SAFETY-FAIL: synthetic pid=$идентификатор остался"
            $exitCode = 1
        }
    }

    if (Test-Path $каталогЛогов) {
        try {
            Remove-Item -LiteralPath $каталогЛогов -Recurse -Force -ErrorAction Stop
        } catch {
            Write-Host "  логи диагностики сохранены в $каталогЛогов (удаление не удалось)"
        }
    }
}

exit $exitCode
