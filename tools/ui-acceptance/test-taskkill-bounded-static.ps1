# Статическая проверка контракта bounded taskkill.
#
# Дефект, который закрывает эта проверка: параметр $timeoutMs в
# сигнатуре Invoke-TaskKillSafe САМ ПО СЕБЕ не ограничивает время
# исполнения native-команды — `& taskkill ...` выполняется синхронно и
# может висеть бесконечно. Настоящее ограничение даёт только отдельный
# процесс + WaitForExit($timeoutMs).
#
# Проверка разбирает acceptance-lib.ps1 через PowerShell AST и требует:
#   1. Invoke-TaskKillSafe вызывает bounded runner и передаёт ему
#      $timeoutMs (параметр не «объявлен, но не используется»);
#   2. bounded runner использует ProcessStartInfo + WaitForExit($TimeoutMs);
#   3. stdout/stderr читаются асинхронно (ReadToEndAsync) — синхронный
#      ReadToEnd() ДО WaitForExit() запрещён (pipe-deadlock);
#   4. синхронного вызова `& taskkill` в helper-е не осталось;
#   5. Stop-HarnessTree использует helper для родителя и потомков;
#   6. во всём tools/ui-acceptance нет безлимитного WaitForExit().
# Итог: exit 0 только если ВСЕ проверки выполнены.
$ErrorActionPreference = 'Stop'

$каталог = $PSScriptRoot
$библиотека = Join-Path $каталог 'acceptance-lib.ps1'
$провалы = [System.Collections.Generic.List[string]]::new()

function Проверить([bool]$условие, [string]$имя, [string]$детали = '') {
    if ($условие) {
        Write-Host "  OK: $имя"
    } else {
        Write-Host "  FAIL: $имя $(if ($детали) { "— $детали" })"
        $script:провалы.Add($имя)
    }
}

# Тело функции по имени — точно (AST), без хрупкого текстового среза.
function ПолучитьТелоФункции($ast, [string]$имя) {
    $найденные = $ast.FindAll({
            param($узел)
            $узел -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $узел.Name -eq $имя
        }, $true)

    if ($найденные.Count -eq 0) { return $null }
    return $найденные[0].Body.Extent.Text
}

$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($библиотека, [ref]$tokens, [ref]$errors)

if (@($errors).Count -ne 0) {
    Write-Host "STATIC-TASKKILL-BOUNDED EXCEPTION: ошибка разбора $библиотека"
    $errors | ForEach-Object { Write-Host ('  ' + $_.Message) }
    exit 1
}

$телоHelper = ПолучитьТелоФункции $ast 'Invoke-TaskKillSafe'
$телоRunner = ПолучитьТелоФункции $ast 'Invoke-BoundedCommand'
$телоStop = ПолучитьТелоФункции $ast 'Stop-HarnessTree'

Проверить ($null -ne $телоHelper) 'Invoke-TaskKillSafe найдена'
Проверить ($null -ne $телоRunner) 'Invoke-BoundedCommand найдена'
Проверить ($null -ne $телоStop) 'Stop-HarnessTree найдена'

if ($телоHelper -ne $null) {
    # 1. Параметр таймаута РЕАЛЬНО используется: передаётся в runner.
    Проверить ($телоHelper -match 'Invoke-BoundedCommand') 'helper использует bounded runner'
    Проверить ($телоHelper -match '-TimeoutMs\s+\$timeoutMs') 'helper передаёт $timeoutMs в bounded runner'

    # 4. Синхронного native-вызова taskkill не осталось.
    Проверить ($телоHelper -notmatch '&(\s*)taskkill') 'helper не вызывает taskkill синхронно'

    # Структурированный результат сохраняет все обязательные поля:
    # поле либо объявлено в литерале результата, либо ему присваивается
    # значение (оба варианта — контракт «поле результата присутствует»).
    foreach ($поле in @('Attempted', 'Pid', 'Operation', 'ExitCode', 'Output', 'Exception', 'NotFound', 'AlreadyExited', 'TimedOut', 'Success')) {
        $объявлено = $телоHelper -match ("(?m)^\s*{0}\s*=" -f [regex]::Escape($поле))
        $присвоено = $телоHelper -match ("`$результат\.{0}\b" -f [regex]::Escape($поле))
        Проверить ($объявлено -or $присвоено) "helper сохраняет поле $поле"
    }
}

if ($телоRunner -ne $null) {
    # 2. Настоящий bounded runner.
    Проверить ($телоRunner -match 'System\.Diagnostics\.ProcessStartInfo') 'runner использует ProcessStartInfo'
    Проверить ($телоRunner -match '\.WaitForExit\(\$TimeoutMs\)') 'runner ждёт WaitForExit($TimeoutMs)'

    # 3. Асинхронный сбор вывода — pipe-deadlock невозможен.
    Проверить ($телоRunner -match 'ReadToEndAsync') 'runner читает вывод асинхронно'
    Проверить ($телоRunner -notmatch '\.ReadToEnd\(\)') 'runner не читает вывод синхронно'

    # После Kill — только ограниченное ожидание.
    Проверить ($телоRunner -match 'WaitForExit\(2000\)') 'runner после Kill ждёт ограниченно'
    Проверить ($телоRunner -notmatch 'WaitForExit\(\s*\)') 'runner не содержит безлимитного WaitForExit()'
}

if ($телоStop -ne $null) {
    # 5. Родитель и потомки завершаются ЧЕРЕЗ bounded helper.
    Проверить ($телоStop -match "Invoke-TaskKillSafe\s+\`$пид\s+'parent'") 'Stop-HarnessTree: родитель через bounded helper'
    Проверить ($телоStop -match "Invoke-TaskKillSafe\s+\`$остаток\.Pid\s+'descendant'") 'Stop-HarnessTree: потомок через bounded helper'

    # Таймаут → ошибка (fail-closed), а не PASS.
    Проверить ($телоStop -match 'TaskKillTimedOutPids') 'Stop-HarnessTree: таймаут фиксируется'
    Проверить ($телоStop -match 'не завершился за таймаут') 'Stop-HarnessTree: диагностика таймаута'
}

# 6. Во всём acceptance-наборе нет безлимитного WaitForExit().
$скрипты = @(Get-ChildItem -LiteralPath $каталог -Filter '*.ps1' -File)
$безлимиты = @()

foreach ($скрипт in $скрипты) {
    $содержимое = Get-Content -LiteralPath $скрипт.FullName -Raw
    if ($содержимое -match '\.WaitForExit\(\s*\)') {
        $безлимиты += $скрипт.Name
    }
}

Проверить ($безлимиты.Count -eq 0) 'нет безлимитных WaitForExit() в ui-acceptance' ($безлимиты -join ';')

if ($провалы.Count -eq 0) {
    Write-Host 'TASKKILL-BOUNDED-STATIC PASS: True'
    exit 0
} else {
    Write-Host ("TASKKILL-BOUNDED-STATIC PASS: False (" + ($провалы -join '; ') + ")")
    exit 1
}
