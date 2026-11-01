# Сборка библиотеки типов COM-сервера (ТЗ §9.3, функция 114).
#
# Скрипт собирает бинарный файл PrintEnvelopePro.tlb из описания
# src/Picket302.ComAutomation/idl/PrintEnvelopePro.idl компилятором MIDL
# (Windows SDK) и копирует результат в каталог сборки COM-сервера.
#
# Требуется Windows SDK: компилятор midl.exe. Путь к нему можно задать
# переменной MIDL_PATH, иначе выполняется поиск в стандартных каталогах
# Windows Kits.
#
# Запуск:  powershell -ExecutionPolicy Bypass -File tools/build-typelib.ps1

param(
    [string] $MidlPath,
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$idlDir = Join-Path $root 'src\Picket302.ComAutomation\idl'

function Find-Midl {
    param([string] $Requested)

    if ($Requested) {
        if (Test-Path $Requested) {
            return $Requested
        }

        throw "Компилятор MIDL не найден по указанному пути: $Requested"
    }

    $command = Get-Command midl.exe -ErrorAction SilentlyContinue

    if ($command) {
        return $command.Source
    }

    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'

    if (Test-Path $kits) {
        $found = Get-ChildItem -Path $kits -Filter midl.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($found) {
            return $found.FullName
        }
    }

    throw 'Компилятор MIDL не найден. Установите Windows SDK или задайте путь параметром -MidlPath.'
}

$midl = Find-Midl -Requested $MidlPath

Write-Host "Компилятор MIDL: $midl"

Push-Location $idlDir

try {
    & $midl /nologo /nocpp /env x64 /tlb PrintEnvelopePro.tlb /h PrintEnvelopePro.h /I . /I 'C:\Windows\System32' PrintEnvelopePro.idl

    if ($LASTEXITCODE -ne 0) {
        throw "MIDL завершился с кодом $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$tlb = Join-Path $idlDir 'PrintEnvelopePro.tlb'

if (-not (Test-Path $tlb)) {
    throw "Файл библиотеки типов не создан: $tlb"
}

Write-Host "Библиотека типов собрана: $tlb ($((Get-Item $tlb).Length) байт)"

# Файл копируется в каталог сборки правилом CopyToOutputDirectory проекта
# Picket302.ComAutomation, поэтому отдельного копирования не требуется.
Write-Host 'Библиотека типов будет скопирована в каталог сборки при dotnet build.'
