# Пункт B: настоящий runtime-сценарий пустой адресной книги.
# Изолированный профиль с createDemoData=false; sentinel реального профиля;
# скриншот состояния; проверка очистки временного профиля.
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\acceptance-lib.ps1"

$exe = "$PSScriptRoot\bin\x64\Release\net8.0-windows\UiAcceptance.exe"
$out = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
$реальныйПрофиль = Join-Path $env:APPDATA 'Picket302'
$sentinel = Join-Path $реальныйПрофиль 'picket302-review-sentinel.tmp'

# --- 1. Отпечатки реального профиля ДО -------------------------------
New-Item -ItemType Directory -Force -Path $реальныйПрофиль | Out-Null
Set-Content -Path $sentinel -Value ("sentinel-" + [Guid]::NewGuid().ToString('N'))
$отпечатокДо = Test-ProfileFingerprint $реальныйПрофиль
$sentinelДо = Get-SentinelState $sentinel
Write-Host "sentinel before: $sentinelДо"
Write-Host "real profile files before: $($отпечатокДо.Count)"

# --- 2. Уникальный временный профиль ---------------------------------
$корень = Join-Path ([System.IO.Path]::GetTempPath()) ("Picket302Acceptance\" + [Guid]::NewGuid().ToString('N'))
Write-Host "temp profile: $корень"

# --- 3. Запуск harness в режиме emptybook ----------------------------
$h = Start-Acceptance $exe 'emptybook' $корень
if ($h -eq $null) { Write-Host 'HARNESS FAILED'; Remove-Item $sentinel -Force; exit 1 }

# --- 4. Окно 1280x720 и снимок ---------------------------------------
[AccWin]::SetWindowPos($h.Hwnd, [IntPtr](-1), 30, 30, 1280, 720, 0x0040) | Out-Null
Start-Sleep -Seconds 2
[AccWin]::Capture($h.Hwnd, (Join-Path $out '01-адресная-книга-пустая-1280x720.png')) | Out-Null
Write-Host 'captured true empty state'

# --- 5. Закрытие и очистка -------------------------------------------
Stop-Acceptance $h

$профильОчищен = -not (Test-Path $корень)
Write-Host "temp profile cleaned: $профильОчищен"

# --- 6. Реальный профиль ПОСЛЕ ---------------------------------------
$отпечатокПосле = Test-ProfileFingerprint $реальныйПрофиль
$sentinelПосле = Get-SentinelState $sentinel
Remove-Item $sentinel -Force
$дельта = Compare-Fingerprints $отпечатокДо $отпечатокПосле

Write-Host "sentinel after:  $sentinelПосле"
Write-Host "sentinel unchanged: $($sentinelДо -eq $sentinelПосле)"
Write-Host "real profile new files: $($дельта.New.Count) changed: $($дельта.Changed.Count) removed: $($дельта.Removed.Count)"
$дельта.New | ForEach-Object { Write-Host "  NEW: $_" }
$дельта.Changed | ForEach-Object { Write-Host "  CHANGED: $_" }

$итог = ($sentinelДо -eq $sentinelПосле) -and ($дельта.New.Count -eq 0) -and ($дельта.Changed.Count -eq 0) -and $профильОчищен
Write-Host "EMPTYBOOK PASS: $итог"
