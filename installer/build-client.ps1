<#
.SYNOPSIS
    Сборка клиентского установщика Picket302 (Inno Setup 6) из опубликованной
    сборки оболочки Picket302.App.

.DESCRIPTION
    Скрипт выполняет полный цикл и останавливается на первом нарушении:

      1. dotnet publish оболочки (Release, win-x64, self-contained) в отдельный
         каталог артефактов ВНЕ Git-репозитория. Проверяется код возврата.
      2. Проверка состава публикации: обязательные файлы на месте, COM-сервер
         самодостаточный, серверной службы нет, пользовательских данных нет.
      3. Компиляция установщика Inno Setup (ISCC). Проверяется код возврата.
      4. Проверка итогового пакета: файл существует, размер в разумных
         пределах, вычисляется SHA-256.
      5. Целевые тесты контракта упаковки с указанием каталога публикации.
      6. Отчёт сборки (JSON + текст) и указатель LATEST на последний
         УСПЕШНЫЙ пакет.

    Скрипт ничего не удаляет: каждый запуск пишет в собственный каталог
    runs\<метка времени>. Указатель LATEST обновляется только после того, как
    пакет собран и проверен, поэтому старый пакет нельзя выдать за новый.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-client.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-client.ps1 `
        -ArtifactsRoot C:\Users\Nachfin\p302-artifacts\client-pilot-20261116 `
        -SkipTests
#>
[CmdletBinding()]
param(
    # Корень репозитория. По умолчанию — родительский каталог installer\.
    [string] $RepositoryRoot,

    # Каталог артефактов ВНЕ Git-репозитория.
    [string] $ArtifactsRoot = 'C:\Users\Nachfin\p302-artifacts\client-pilot-20261116',

    # Конфигурация сборки оболочки.
    [string] $Configuration = 'Release',

    # Идентификатор среды выполнения.
    [string] $RuntimeIdentifier = 'win-x64',

    # Путь к ISCC. Если не задан, выполняется поиск в стандартных местах.
    [string] $IsccPath,

    # Целевые тесты контракта упаковки можно пропустить (только для отладки
    # самого скрипта; в обычном прогоне тесты выполняются).
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Вывод дочерних программ (dotnet) приходит в UTF-8: без этого консоль
# Windows показывает русские сообщения сборки как «кракозябры».
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
    # Консоль без поддержки UTF-8 — не повод прерывать сборку.
}

# ---------------------------------------------------------------------------
# Константы контракта упаковки
# ---------------------------------------------------------------------------

# Файлы, без которых опубликованная сборка неработоспособна.
$script:RequiredPublishFiles = @(
    'Picket302.App.exe',
    'Picket302.App.dll',
    'Picket302.App.deps.json',
    'Picket302.App.runtimeconfig.json',
    'Picket302.ComAutomation.exe',
    'Picket302.ComAutomation.dll',
    'Picket302.ComAutomation.deps.json',
    'Picket302.ComAutomation.runtimeconfig.json',
    'PrintEnvelopePro.tlb',
    'emailedit\default.html',
    'e_sqlite3.dll'
)

# Серверная служба не устанавливается клиентским пакетом.
$script:ForbiddenServiceArtifacts = @(
    'Picket302.Service.exe',
    'Picket302.Service.dll',
    'Picket302.Service.deps.json',
    'Picket302.Service.runtimeconfig.json',
    'service.json'
)

# Пользовательские данные не попадают в каталог установки.
$script:ForbiddenUserData = @(
    'picket302.db',
    'shared.json',
    'pending-operations.json',
    'connection.json',
    'servers.ini',
    'print_audit.log',
    'license.dat',
    'ui-settings.json',
    'mail-settings.json',
    'com-automation.log',
    'Secrets',
    'logs',
    'scans',
    'integrity-manifest.json'
)

# Токены, которых не должно быть в сценарии установщика: установщик клиента
# не регистрирует службу, не трогает брандмауэр и не меняет сертификаты.
$script:ForbiddenInstallerTokens = @(
    'sc.exe',
    'sc create',
    'netsh',
    'advfirewall',
    'New-Service',
    'Install-Service',
    'ServiceInstall',
    'CertAdd',
    'certutil',
    'regsvr32'
)

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok([string] $Message) {
    Write-Host "    [ok]   $Message" -ForegroundColor Green
}

function Write-Fail([string] $Message) {
    Write-Host "    [FAIL] $Message" -ForegroundColor Red
}

function Assert-Contract {
    param(
        [bool]   $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Remove-InnoComments {
    <#
    Снимает комментарии сценария Inno Setup: строки целиком с «;», блоки в
    фигурных скобках и хвостовые комментарии с «//». Блоки в фигурных
    скобках встречаются только в секции [Code]: в директивах фигурные
    скобки означают подстановку константы ({app}, {group}).
    #>
    param([string] $Text)

    $строки = $Text -split "`r?`n"
    $результат = New-Object System.Collections.Generic.List[string]
    $вКоде = $false
    $внутриБлока = $false

    foreach ($строка in $строки) {
        $обрезанная = $строка.Trim()

        if ($обрезанная.StartsWith('[') -and $обрезанная.EndsWith(']')) {
            $вКоде = $обрезанная -ieq '[Code]'
            $результат.Add($строка)
            continue
        }

        if (-not $вКоде) {
            if ($обрезанная.StartsWith(';')) {
                $результат.Add('')
            }
            else {
                $результат.Add($строка)
            }
            continue
        }

        $собрано = New-Object System.Text.StringBuilder
        $index = 0

        while ($index -lt $строка.Length) {
            if ($внутриБлока) {
                $закрытие = $строка.IndexOf('}', $index)
                if ($закрытие -lt 0) { break }
                $внутриБлока = $false
                $index = $закрытие + 1
                continue
            }

            if ($строка[$index] -eq '{') {
                $внутриБлока = $true
                $index++
                continue
            }

            if ($строка[$index] -eq '/' -and ($index + 1) -lt $строка.Length -and $строка[$index + 1] -eq '/') {
                break
            }

            [void] $собрано.Append($строка[$index])
            $index++
        }

        $результат.Add($собрано.ToString())
    }

    return ($результат -join "`n")
}

function Find-Iscc {
    param([string] $ExplicitPath)

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "Компилятор Inno Setup не найден по указанному пути: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $uninstallRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6'
    )

    foreach ($root in $uninstallRoots) {
        if (Test-Path -LiteralPath $root) {
            $location = (Get-ItemProperty -LiteralPath $root -ErrorAction SilentlyContinue).InstallLocation
            if ($location) {
                $candidate = Join-Path $location 'ISCC.exe'
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }

    throw 'Компилятор Inno Setup 6 (ISCC.exe) не найден. Укажите его путь параметром -IsccPath.'
}

# ---------------------------------------------------------------------------
# Подготовка каталогов
# ---------------------------------------------------------------------------

if (-not $RepositoryRoot) {
    # $PSScriptRoot недоступен в значениях по умолчанию блока param в
    # Windows PowerShell 5.1, поэтому каталог сценария определяется здесь.
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepositoryRoot = Split-Path -Parent $scriptDirectory
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$projectPath = Join-Path $RepositoryRoot 'src\Picket302.App\Picket302.App.csproj'
$issPath = Join-Path $RepositoryRoot 'installer\picket302-client.iss'

Assert-Contract (Test-Path -LiteralPath $projectPath) "Проект оболочки не найден: $projectPath"
Assert-Contract (Test-Path -LiteralPath $issPath) "Сценарий установщика не найден: $issPath"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $ArtifactsRoot (Join-Path 'runs' $stamp)
$publishDir = Join-Path $runRoot 'publish'
$distDir = Join-Path $runRoot 'dist'

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Step 'Сборка клиентского установщика Picket302'
Write-Host "    репозиторий : $RepositoryRoot"
Write-Host "    каталог прогона: $runRoot"
Write-Host "    git HEAD    : $(& git -C $RepositoryRoot rev-parse HEAD)"

$iscc = Find-Iscc -ExplicitPath $IsccPath
Write-Host "    ISCC        : $iscc"

# ---------------------------------------------------------------------------
# 1. Публикация оболочки
# ---------------------------------------------------------------------------

Write-Step 'Шаг 1/5. Публикация оболочки (dotnet publish)'

$publishArgs = @(
    'publish',
    $projectPath,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '--self-contained', 'true',
    '-o', $publishDir
)

& dotnet @publishArgs
$publishExit = $LASTEXITCODE

Assert-Contract ($publishExit -eq 0) "dotnet publish завершился с кодом $publishExit. Пакет не собирается."

Write-Ok "dotnet publish завершён с кодом 0. Каталог: $publishDir"

# ---------------------------------------------------------------------------
# 2. Проверка состава публикации
# ---------------------------------------------------------------------------

Write-Step 'Шаг 2/5. Проверка состава публикации'

$missing = @()
foreach ($relative in $script:RequiredPublishFiles) {
    $path = Join-Path $publishDir $relative
    if (-not (Test-Path -LiteralPath $path)) {
        $missing += $relative
    }
}

Assert-Contract ($missing.Count -eq 0) ("В публикации отсутствуют обязательные файлы: " + ($missing -join ', '))
Write-Ok ("Обязательные файлы на месте: {0} из {1}" -f $script:RequiredPublishFiles.Count, $script:RequiredPublishFiles.Count)

# Самодостаточность: без установленного .NET Desktop Runtime клиентский
# компьютер не запустит программу.
foreach ($runtimeConfig in @('Picket302.App.runtimeconfig.json', 'Picket302.ComAutomation.runtimeconfig.json')) {
    $path = Join-Path $publishDir $runtimeConfig
    $content = Get-Content -LiteralPath $path -Raw
    Assert-Contract ($content -match 'includedFrameworks') "$runtimeConfig не самодостаточный: программа потребует установленного .NET Desktop Runtime."
}

Write-Ok 'Оболочка и COM-сервер самодостаточны (установленный .NET не требуется).'

# Серверной службы в поставке быть не должно.
$publishedFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File | ForEach-Object { $_.FullName }
foreach ($artifact in $script:ForbiddenServiceArtifacts) {
    $found = $publishedFiles | Where-Object { [System.IO.Path]::GetFileName($_) -eq $artifact }
    Assert-Contract (-not $found) "В публикации найден файл серверной службы: $artifact"
}

Write-Ok 'Серверной службы в публикации нет.'

# Пользовательских данных в поставке быть не должно.
foreach ($data in $script:ForbiddenUserData) {
    $found = $publishedFiles | Where-Object {
        $relative = $_.Substring($publishDir.Length).TrimStart('\')
        $relative -eq $data -or $relative.StartsWith("$data\")
    }
    Assert-Contract (-not $found) "В публикации найдены пользовательские данные: $data"
}

Write-Ok 'Пользовательских данных (база, секрет сопряжения, журналы) в публикации нет.'

# Сценарий установщика не должен содержать операций со службой и брандмауэром.
$issText = Remove-InnoComments (Get-Content -LiteralPath $issPath -Raw -Encoding UTF8)
foreach ($token in $script:ForbiddenInstallerTokens) {
    Assert-Contract ($issText -notmatch [regex]::Escape($token)) "Сценарий установщика содержит запрещённую операцию '$token'."
}

Write-Ok 'Сценарий установщика не содержит операций со службой, брандмауэром и сертификатами.'

# ---------------------------------------------------------------------------
# 3. Компиляция установщика
# ---------------------------------------------------------------------------

Write-Step 'Шаг 3/5. Компиляция установщика (Inno Setup 6)'

& $iscc "/DPublishDir=$publishDir" "/O$distDir" $issPath
$isccExit = $LASTEXITCODE

Assert-Contract ($isccExit -eq 0) "ISCC завершился с кодом $isccExit. Пакет не считается собранным."

Write-Ok "ISCC завершён с кодом 0."

$setupExe = Get-ChildItem -LiteralPath $distDir -Filter '*.exe' -File | Select-Object -First 1
Assert-Contract ($null -ne $setupExe) "В каталоге $distDir нет итогового EXE-установщика."

$sizeBytes = (Get-Item -LiteralPath $setupExe.FullName).Length
Assert-Contract ($sizeBytes -gt 1MB) "Итоговый пакет подозрительно мал: $sizeBytes байт."

$sha256 = (Get-FileHash -LiteralPath $setupExe.FullName -Algorithm SHA256).Hash

Write-Ok ("Пакет: {0}" -f $setupExe.FullName)
Write-Ok ("Размер: {0:N0} байт" -f $sizeBytes)
Write-Ok ("SHA-256: {0}" -f $sha256)

# ---------------------------------------------------------------------------
# 4. Целевые тесты контракта упаковки
# ---------------------------------------------------------------------------

$testsPassed = $null
$testsFailed = $null
$testsSkipped = $null

if (-not $SkipTests) {
    Write-Step 'Шаг 4/5. Целевые тесты контракта упаковки'

    $env:PICKET302_CLIENT_PUBLISH_DIR = $publishDir
    $testProject = Join-Path $RepositoryRoot 'tests\Picket302.Tests\Picket302.Tests.csproj'

    & dotnet test $testProject -c $Configuration --filter 'FullyQualifiedName~Packaging' --nologo
    $testExit = $LASTEXITCODE

    Assert-Contract ($testExit -eq 0) "Целевые тесты контракта упаковки провалились (код $testExit)."
    Write-Ok 'Целевые тесты контракта упаковки пройдены.'
}
else {
    Write-Step 'Шаг 4/5. Целевые тесты пропущены параметром -SkipTests'
}

# ---------------------------------------------------------------------------
# 5. Отчёт сборки
# ---------------------------------------------------------------------------

Write-Step 'Шаг 5/5. Отчёт сборки'

$gitHead = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
$gitBranch = (& git -C $RepositoryRoot rev-parse --abbrev-ref HEAD).Trim()

# Нечистое дерево означает, что пакет собран не из того состояния
# репозитория, на которое указывает gitHead. Это фиксируется в отчёте.
$gitStatus = @(& git -C $RepositoryRoot status --porcelain=v1)
$gitDirty = $gitStatus.Count -gt 0
$publishFileCount = (Get-ChildItem -LiteralPath $publishDir -Recurse -File).Count
$publishSizeBytes = (Get-ChildItem -LiteralPath $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum

$report = [ordered]@{
    builtAtUtc       = (Get-Date).ToUniversalTime().ToString('o')
    gitHead          = $gitHead
    gitBranch        = $gitBranch
    gitTreeDirty     = $gitDirty
    gitChangedFiles  = @($gitStatus)
    configuration    = $Configuration
    runtimeIdentifier = $RuntimeIdentifier
    selfContained    = $true
    publishDirectory = $publishDir
    publishFileCount = $publishFileCount
    publishSizeBytes = $publishSizeBytes
    packagePath      = $setupExe.FullName
    packageSizeBytes = $sizeBytes
    packageSha256    = $sha256
    isccPath         = $iscc
    isccExitCode     = $isccExit
    publishExitCode  = $publishExit
    testsExecuted    = (-not $SkipTests)
}

$reportPath = Join-Path $runRoot 'build-report.json'
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8

$latestPath = Join-Path $ArtifactsRoot 'LATEST.json'
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $latestPath -Encoding UTF8

$latestText = @"
Picket302 — клиентский установщик (последняя УСПЕШНАЯ сборка)

Собрано (UTC) : $($report.builtAtUtc)
Git HEAD      : $gitHead
Ветка         : $gitBranch
Дерево        : $(if ($gitDirty) { 'НЕЧИСТОЕ (пакет собран из незакоммиченных изменений)' } else { 'чистое' })
Конфигурация  : $Configuration / $RuntimeIdentifier / self-contained

Пакет         : $($setupExe.FullName)
Размер        : $sizeBytes байт
SHA-256       : $sha256

Публикация    : $publishDir ($publishFileCount файлов, $publishSizeBytes байт)
Отчёт         : $reportPath
"@
$latestText | Set-Content -LiteralPath (Join-Path $ArtifactsRoot 'LATEST.txt') -Encoding UTF8

Write-Ok "Отчёт: $reportPath"
Write-Ok "Указатель на последний успешный пакет: $latestPath"

Write-Host ''
Write-Host 'Сборка завершена успешно.' -ForegroundColor Green
Write-Host "Пакет      : $($setupExe.FullName)"
Write-Host "SHA-256    : $sha256"
Write-Host ''
Write-Host 'Пакет НЕ подписан: Windows может показать предупреждение об неизвестном издателе.' -ForegroundColor Yellow
