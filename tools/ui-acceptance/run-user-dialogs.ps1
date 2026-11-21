# Снимки рабочих диалогов оболочки для пользовательского прогона
# (фазы 2/3/5 плана): карточка контрагента, карточка письма, редактор
# рассылки, настройка таблицы (столбцы/свойства), поиск адреса, вид
# таблицы, редактор бланка, клипарт.
#
# Основа: штатный драйвер tools/ui-acceptance (UiAcceptance.exe) на
# изолированном профиле. Диалоги открываются настоящие (SHOW), снимок
# снимается с окна.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = if ($env:P302_EVIDENCE_ROOT) {
    $env:P302_EVIDENCE_ROOT
} else {
    Join-Path 'Z:\Picket302\docs' ('ux-run-' + (Get-Date -Format 'yyyy-MM-dd') + '\dialogs')
}
New-Item -ItemType Directory -Force -Path $out -ErrorAction Stop | Out-Null

$корень = Join-Path $env:TEMP ('Picket302UserDialogs_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $корень | Out-Null

$h = Start-Acceptance $exe 'screens' $корень $false $true
$код = 1

try {
    $готово = Read-HarnessLines $h 'READY' 40
    if (-not $готово) { Write-Host 'Драйвер не поднялся'; exit 1 }

    $диалоги = @(
        @{ Имя = 'counterparty'; Файл = 'd1-counterparty-card.png'; Заголовок = 'Контрагент' },
        @{ Имя = 'mailedit'; Файл = 'd2-mail-card.png'; Заголовок = 'Отправление' },
        @{ Имя = 'distribution'; Файл = 'd3-distribution-editor.png'; Заголовок = 'Рассылка' },
        @{ Имя = 'tablecolumns'; Файл = 'd4-table-columns.png'; Заголовок = 'таблиц' },
        @{ Имя = 'tableproperties'; Файл = 'd5-table-properties.png'; Заголовок = 'таблиц' },
        @{ Имя = 'search'; Файл = 'd6-address-search.png'; Заголовок = 'Поиск' },
        @{ Имя = 'gridappearance'; Файл = 'd7-grid-appearance.png'; Заголовок = 'таблиц' },
        @{ Имя = 'blankeditor'; Файл = 'd8-blank-editor.png'; Заголовок = 'бланк' },
        @{ Имя = 'clipartpicker'; Файл = 'd9-clipart-picker.png'; Заголовок = 'Клипарт' }
    )

    foreach ($д in $диалоги) {
        Send-HarnessCommand $h ("SHOW " + $д.Имя)
        Start-Sleep -Milliseconds 900

        $окно = [IntPtr]::Zero
        foreach ($w in [AccWin]::WindowsOf($h.Proc.Id)) {
            $заголовок = [AccWin]::Title($w)
            if ($заголовок -and $заголовок -ne 'Picket302' -and $заголовок -like "*$($д.Заголовок)*") {
                $окно = $w
                break
            }
        }

        if ($окно -eq [IntPtr]::Zero) {
            foreach ($w in [AccWin]::WindowsOf($h.Proc.Id)) {
                $заголовок = [AccWin]::Title($w)
                if ($заголовок -and $заголовок -ne 'Picket302') { $окно = $w; break }
            }
        }

        if ($окно -ne [IntPtr]::Zero) {
            $путь = Join-Path $out $д.Файл
            [AccWin]::Capture($окно, $путь) | Out-Null
            $размер = if (Test-Path $путь) { (Get-Item $путь).Length } else { 0 }
            Write-Host "снимок: $($д.Файл) — '$([AccWin]::Title($окно))' ($размер байт)"
        }
        else {
            Write-Host "снимок: $($д.Файл) — ОКНО НЕ НАЙДЕНО"
        }

        Send-HarnessCommand $h 'HIDE'
        Start-Sleep -Milliseconds 400
    }

    $код = 0
}
finally {
    Stop-Acceptance $h | Out-Null
    if (Test-Path $корень) { Remove-Item -Recurse -Force $корень -ErrorAction SilentlyContinue }
}

Write-Host "ИТОГ: код=$код, каталог: $out"
exit $код
