# Статический контракт screenshots/DPI-harness: эти драйверы не должны
# поднимать окно поверх пользователя или активировать его. Скрипт ничего не
# запускает и не изменяет файлы — только читает исходники и возвращает код.
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$violations = [System.Collections.Generic.List[string]]::new()

function Assert-Static([bool]$condition, [string]$message) {
    if (-not $condition) {
        $violations.Add($message)
    }
}

$program = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Raw
$library = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'acceptance-lib.ps1') -Raw

Assert-Static ($program.Contains('PICKET302_ACCEPTANCE_NONACTIVATING')) 'Program.cs missing non-activating mode variable.'
Assert-Static ($program.Contains('WS_EX_NOACTIVATE')) 'Program.cs missing WS_EX_NOACTIVATE documentation.'
Assert-Static ($program.Contains('GetWindowLongPtrW')) 'Program.cs missing native extended-style read.'
Assert-Static ($program.Contains('SetWindowLongPtrW')) 'Program.cs missing native extended-style write.'
Assert-Static ($program.Contains('ShowInTaskbar = false')) 'MainShell/dialog preparation does not hide taskbar entries.'
Assert-Static ($program.Contains('BringToFront();')) 'Program.cs no longer contains the normal-mode foreground action.'
Assert-Static ($program.Contains('Activate();')) 'Program.cs no longer contains the normal-mode activation action.'
Assert-Static ($library.Contains('EnvironmentVariables[''PICKET302_ACCEPTANCE_NONACTIVATING''] = ''1''')) 'Start-Acceptance does not pass the child-process variable.'

foreach ($name in @('run-screens.ps1', 'run-screens2.ps1', 'run-dpi.ps1')) {
    $path = Join-Path $PSScriptRoot $name
    $text = Get-Content -LiteralPath $path -Raw

    Assert-Static (-not $text.Contains('[IntPtr](-1)')) "$name still contains HWND_TOPMOST via IntPtr(-1)."
    Assert-Static ($text.Contains('$false $true')) "$name does not start the harness in non-activating mode."
    Assert-Static ($text.Contains('[IntPtr]::Zero')) "$name does not use a neutral z-order handle."
}

if ($violations.Count -gt 0) {
    Write-Error ($violations -join [Environment]::NewLine)
    exit 1
}

Write-Output 'NONACTIVATING_CAPTURE_STATIC: PASS'
Write-Output 'Checked Program.cs, acceptance-lib.ps1, run-screens.ps1, run-screens2.ps1, run-dpi.ps1.'
exit 0
