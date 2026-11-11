# Coverage guard (S05 review recommendation, Wave-1 checkpoint item 5.1).
# Fails (exit 1) unless the cobertura report contains EXACTLY the six
# expected Picket302 packages. Catches the silent drop of an assembly from
# coverage (regression class S05-1: Picket302.App vanished after 6ff1d01
# with a swallowed Cecil error and no collector message).
#
# Usage: powershell -NoProfile -File verify-coverage-guard.ps1 <coverage.cobertura.xml>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "COVERAGE-GUARD FAIL: cobertura file not found: $Path"
    exit 1
}

[xml] $report = Get-Content -LiteralPath $Path -Encoding UTF8
$expected = @(
    'Picket302.App',
    'Picket302.Application',
    'Picket302.ComAutomation',
    'Picket302.Domain',
    'Picket302.Infrastructure',
    'Picket302.Printing'
)
# NOTE: the PowerShell XML adapter may collapse repeated children to the
# first node here, so navigate with XPath explicitly.
$actual = @($report.SelectNodes('//package') | ForEach-Object { $_.name })

$missing = @($expected | Where-Object { $actual -notcontains $_ })
$extra = @($actual | Where-Object { $expected -notcontains $_ })

if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $actual.Count -ne 6) {
    Write-Host "COVERAGE-GUARD FAIL: packages=$($actual.Count) expected=6"
    if ($missing.Count -gt 0) { Write-Host "  missing: $($missing -join ', ')" }
    if ($extra.Count -gt 0) { Write-Host "  extra: $($extra -join ', ')" }
    Write-Host '  (class S05-1: an assembly silently dropped out of instrumentation)'
    exit 1
}

Write-Host "COVERAGE-GUARD PASS: 6/6 packages present ($($actual -join ', '))"
exit 0
