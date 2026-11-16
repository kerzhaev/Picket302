<#
.SYNOPSIS
    Динамическая проверка клиентского установщика Picket302.

.DESCRIPTION
    Проверяются в безопасном режиме те операции установщика, которые нельзя
    проверить компиляцией:

      * установка: состав файлов в каталоге установки, отсутствие отладочных
        символов и исходников, отсутствие серверной службы, наличие ярлыка в
        меню «Пуск»;
      * обновление: повторная установка поверх уже установленной копии не
        ломает установку и не трогает профиль пользователя;
      * удаление: программа и ярлыки удаляются, пользовательские данные
        (%AppData%\Picket302) сохраняются.

    Скрипт НИКОГДА не работает с настоящим каталогом Program Files и НИКОГДА
    не изменяет профиль пользователя. Для этого проверяется не сам
    поставляемый сценарий, а его точная копия, в которой изменены только
    два параметра:

      PrivilegesRequired=admin  ->  PrivilegesRequired=lowest
      DefaultDirName={autopf}\Picket302  ->  {localappdata}\Programs\Picket302-Smoke

    Остальное (состав файлов, ярлыки, задачи, запуск после установки,
    обработчики удаления) — побайтово тот же сценарий.

    Установка выполняется в тихом режиме (/VERYSILENT): флаг skipifsilent
    не даёт запустить программу, а проверка профиля дополнительно
    подтверждает, что профиль не изменился.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\verify-client-install.ps1 `
        -PublishDir C:\Users\Nachfin\p302-artifacts\client-pilot-20261116\runs\<метка>\publish
#>
[CmdletBinding()]
param(
    # Каталог опубликованной сборки (обязателен): проверяется именно она.
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    # Корень репозитория. По умолчанию — родительский каталог installer\.
    [string] $RepositoryRoot,

    # Каталог для артефактов проверки.
    [string] $ArtifactsRoot = 'C:\Users\Nachfin\p302-artifacts\client-pilot-20261116\smoke',

    # Путь к ISCC. Если не задан, выполняется поиск в стандартных местах.
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
}

$script:Checks = New-Object System.Collections.Generic.List[object]

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Add-Check {
    param(
        [string] $Name,
        [bool]   $Passed,
        [string] $Details = ''
    )

    $script:Checks.Add([pscustomobject]@{
        Name    = $Name
        Passed  = $Passed
        Details = $Details
    })

    if ($Passed) {
        Write-Host "    [ok]   $Name" -ForegroundColor Green
    }
    else {
        Write-Host "    [FAIL] $Name  $Details" -ForegroundColor Red
    }
}

function Get-ProfileSnapshot {
    <#
    Снимок профиля пользователя: пути, размеры и отметки времени файлов.
    #>
    $profile = Join-Path $env:APPDATA 'Picket302'

    if (-not (Test-Path -LiteralPath $profile)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $profile -Recurse -File -Force |
        Select-Object @{ n = 'Path'; e = { $_.FullName } },
                      @{ n = 'Length'; e = { $_.Length } },
                      @{ n = 'Modified'; e = { $_.LastWriteTimeUtc.Ticks } })
}

function Compare-Profile {
    param($Before, $After)

    $changed = New-Object System.Collections.Generic.List[string]

    foreach ($item in $Before) {
        $current = $After | Where-Object { $_.Path -eq $item.Path }

        if (-not $current) {
            $changed.Add("удалён: $($item.Path)")
        }
        elseif ($current.Length -ne $item.Length -or $current.Modified -ne $item.Modified) {
            $changed.Add("изменён: $($item.Path)")
        }
    }

    foreach ($item in $After) {
        if (-not ($Before | Where-Object { $_.Path -eq $item.Path })) {
            $changed.Add("создан: $($item.Path)")
        }
    }

    return @($changed)
}

# ---------------------------------------------------------------------------
# Подготовка
# ---------------------------------------------------------------------------

if (-not $RepositoryRoot) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepositoryRoot = Split-Path -Parent $scriptDirectory
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
$issPath = Join-Path $RepositoryRoot 'installer\picket302-client.iss'

if (-not $IsccPath) {
    $IsccPath = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
}

if (-not (Test-Path -LiteralPath $IsccPath)) {
    throw "Компилятор Inno Setup не найден: $IsccPath"
}

New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null

$smokeInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Picket302-Smoke'
$smokeStartMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Picket302-Smoke\Picket302.lnk'

# Группа меню «Пуск», которой касается поставляемый клиент: проверка не
# должна ни создать её, ни удалить.
$productionStartMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Picket302'

Write-Step 'Подготовка проверки'
Write-Host "    репозиторий     : $RepositoryRoot"
Write-Host "    публикация      : $PublishDir"
Write-Host "    каталог установки проверки: $smokeInstallDir"

$profileBefore = Get-ProfileSnapshot
Write-Host "    профиль до      : $($profileBefore.Count) файл(ов) в $env:APPDATA\Picket302"

# ---------------------------------------------------------------------------
# Сборка проверочной копии сценария
# ---------------------------------------------------------------------------

Write-Step 'Сборка проверочной копии сценария (каталог и права изменены)'

$twinIssPath = Join-Path $ArtifactsRoot 'picket302-client-smoke.iss'

# Явное чтение в UTF-8: по умолчанию Windows PowerShell 5.1 читает файл в
# кодировке текущей консоли, и русские тексты сценария исказились бы.
$issText = Get-Content -LiteralPath $issPath -Raw -Encoding UTF8

$twinText = $issText.Replace('PrivilegesRequired=admin', 'PrivilegesRequired=lowest')
$twinText = $twinText.Replace('DefaultDirName={autopf}\Picket302', 'DefaultDirName={localappdata}\Programs\Picket302-Smoke')
$twinText = $twinText.Replace('OutputBaseFilename=picket302-client-setup', 'OutputBaseFilename=picket302-client-smoke')

# Собственная группа меню «Пуск»: проверка не должна трогать группу
# «Picket302», в которой на серверной машине лежит ярлык серверной службы.
$twinText = $twinText.Replace('DefaultGroupName=Picket302', 'DefaultGroupName=Picket302-Smoke')

if ($twinText -eq $issText) {
    throw 'Проверочная копия сценария не отличается от поставляемого: подмены не сработали.'
}

# Файл напоминания копируется рядом: сценарий ссылается на него по имени.
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'installer\client-after-install.txt') `
          -Destination (Join-Path $ArtifactsRoot 'client-after-install.txt') -Force

$twinText | Set-Content -LiteralPath $twinIssPath -Encoding UTF8

$twinSetup = Join-Path $ArtifactsRoot 'picket302-client-smoke.exe'
if (Test-Path -LiteralPath $twinSetup) {
    Remove-Item -LiteralPath $twinSetup -Force
}

& $IsccPath "/DPublishDir=$PublishDir" "/O$ArtifactsRoot" $twinIssPath | Out-Null
$isccExit = $LASTEXITCODE

Add-Check -Name 'Компиляция проверочной копии установщика' -Passed ($isccExit -eq 0) -Details "код ISCC = $isccExit"

if ($isccExit -ne 0) {
    throw 'Проверочная копия установщика не собрана: дальнейшие шаги невозможны.'
}

# ---------------------------------------------------------------------------
# Установка
# ---------------------------------------------------------------------------

Write-Step 'Шаг 1. Установка в тихом режиме'

$installLog = Join-Path $ArtifactsRoot 'install.log'

$process = Start-Process -FilePath $twinSetup `
    -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$installLog" `
    -Wait -PassThru

Add-Check -Name 'Установщик завершился с кодом 0' -Passed ($process.ExitCode -eq 0) -Details "код = $($process.ExitCode)"

if ($process.ExitCode -ne 0) {
    throw "Установка не удалась (код $($process.ExitCode)). Журнал: $installLog"
}

$expected = @(
    'Picket302.App.exe',
    'Picket302.App.dll',
    'Picket302.ComAutomation.exe',
    'Picket302.ComAutomation.runtimeconfig.json',
    'PrintEnvelopePro.tlb',
    'e_sqlite3.dll',
    'emailedit\default.html',
    'emailedit\content.css',
    'lang\en.xml',
    'lang\ru.xml',
    'lng\Rub.lng',
    'Resources\Toponyms\regions.txt'
)

$missing = @($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $smokeInstallDir $_)) })
Add-Check -Name 'В каталоге установки есть все ожидаемые файлы' -Passed ($missing.Count -eq 0) `
    -Details ("отсутствуют: " + ($missing -join ', '))

$installedFiles = Get-ChildItem -LiteralPath $smokeInstallDir -Recurse -File

$pdb = @($installedFiles | Where-Object { $_.Extension -eq '.pdb' })
Add-Check -Name 'Отладочные символы (PDB) в поставку не попали' -Passed ($pdb.Count -eq 0) `
    -Details ("найдено: " + (($pdb | ForEach-Object { $_.Name }) -join ', '))

$idl = @($installedFiles | Where-Object { $_.Extension -eq '.idl' })
Add-Check -Name 'Исходные файлы интерфейса (IDL) в поставку не попали' -Passed ($idl.Count -eq 0) `
    -Details ("найдено: " + (($idl | ForEach-Object { $_.Name }) -join ', '))

$service = @($installedFiles | Where-Object { $_.Name -like 'Picket302.Service*' })
Add-Check -Name 'Серверной службы в установленном составе нет' -Passed ($service.Count -eq 0) `
    -Details ("найдено: " + (($service | ForEach-Object { $_.Name }) -join ', '))

Add-Check -Name 'Ярлык в меню «Пуск» создан' -Passed (Test-Path -LiteralPath $smokeStartMenu) `
    -Details $smokeStartMenu

$profileAfterInstall = Get-ProfileSnapshot
$changedByInstall = @(Compare-Profile -Before $profileBefore -After $profileAfterInstall)
Add-Check -Name 'Установка не изменила профиль пользователя' -Passed ($changedByInstall.Count -eq 0) `
    -Details ($changedByInstall -join '; ')

Add-Check -Name 'Группа меню «Пуск» Picket302 не создана и не тронута' `
    -Passed (-not (Test-Path -LiteralPath $productionStartMenu)) `
    -Details $productionStartMenu

# ---------------------------------------------------------------------------
# Обновление
# ---------------------------------------------------------------------------

Write-Step 'Шаг 2. Повторная установка (обновление поверх установленной копии)'

$upgradeLog = Join-Path $ArtifactsRoot 'upgrade.log'

$process = Start-Process -FilePath $twinSetup `
    -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$upgradeLog" `
    -Wait -PassThru

Add-Check -Name 'Обновление завершилось с кодом 0' -Passed ($process.ExitCode -eq 0) -Details "код = $($process.ExitCode)"

$missingAfterUpgrade = @($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $smokeInstallDir $_)) })
Add-Check -Name 'После обновления состав каталога не пострадал' -Passed ($missingAfterUpgrade.Count -eq 0) `
    -Details ("отсутствуют: " + ($missingAfterUpgrade -join ', '))

Add-Check -Name 'После обновления ярлык в меню «Пуск» на месте' -Passed (Test-Path -LiteralPath $smokeStartMenu)

$profileAfterUpgrade = Get-ProfileSnapshot
$changedByUpgrade = @(Compare-Profile -Before $profileBefore -After $profileAfterUpgrade)
Add-Check -Name 'Обновление не изменило профиль пользователя' -Passed ($changedByUpgrade.Count -eq 0) `
    -Details ($changedByUpgrade -join '; ')

# ---------------------------------------------------------------------------
# Удаление
# ---------------------------------------------------------------------------

Write-Step 'Шаг 3. Удаление'

$uninstaller = Join-Path $smokeInstallDir 'unins000.exe'

if (Test-Path -LiteralPath $uninstaller) {
    $uninstallLog = Join-Path $ArtifactsRoot 'uninstall.log'

    $process = Start-Process -FilePath $uninstaller `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$uninstallLog" `
        -Wait -PassThru

    Add-Check -Name 'Удаление завершилось с кодом 0' -Passed ($process.ExitCode -eq 0) -Details "код = $($process.ExitCode)"
}
else {
    Add-Check -Name 'Удаление завершилось с кодом 0' -Passed $false -Details 'Деинсталлятор не найден в каталоге установки.'
}

Add-Check -Name 'Каталог установки удалён' -Passed (-not (Test-Path -LiteralPath $smokeInstallDir))

Add-Check -Name 'Ярлык в меню «Пуск» удалён' -Passed (-not (Test-Path -LiteralPath $smokeStartMenu))

Add-Check -Name 'Группа меню «Пуск» Picket302 не появилась после проверки' `
    -Passed (-not (Test-Path -LiteralPath $productionStartMenu)) `
    -Details $productionStartMenu

$profileAfterUninstall = Get-ProfileSnapshot
$changedByUninstall = @(Compare-Profile -Before $profileBefore -After $profileAfterUninstall)
Add-Check -Name 'Удаление не изменило профиль пользователя' -Passed ($changedByUninstall.Count -eq 0) `
    -Details ($changedByUninstall -join '; ')

# ---------------------------------------------------------------------------
# Итог
# ---------------------------------------------------------------------------

Write-Step 'Итог проверки'

$failed = @($script:Checks | Where-Object { -not $_.Passed })

Write-Host ("    проверок: {0}, успешно: {1}, неуспешно: {2}" -f `
    $script:Checks.Count,
    @($script:Checks | Where-Object { $_.Passed }).Count,
    $failed.Count)

$reportPath = Join-Path $ArtifactsRoot 'verify-report.json'
$script:Checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host "    отчёт: $reportPath"

if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host 'Проверка не пройдена.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Проверка пройдена.' -ForegroundColor Green
exit 0
