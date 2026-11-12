# S15 delivery COM model: dedicated register/unregister cleanliness cycle.
# Registers from isolated copy, enumerates ALL created HKCU keys, unregisters,
# verifies every key removed and no processes left. No activation, no printing.
$ErrorActionPreference = 'Stop'
$scratch = Split-Path -Parent $MyInvocation.MyCommand.Path
$evidenceDir = Join-Path $scratch 'com-evidence'
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$out = [ordered]@{ run = 'regclean'; startedUtc = (Get-Date).ToUniversalTime().ToString('o') }

$serverBin = 'C:\Users\Nachfin\p302-worktrees\rc-20261110\src\Picket302.ComAutomation\bin\x64\Release\net8.0-windows'
$serverTmp = Join-Path $env:TEMP ("p302-rc-com-" + [guid]::NewGuid().ToString('N'))
$serverExe = Join-Path $serverTmp 'Picket302.ComAutomation.exe'
$clsid = '{5715286E-F98F-4C1C-AC86-AB943AFAE004}'
$iid   = '{3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6}'
$tlb   = '{22BC298D-DF0E-3C29-B589-7A50D3A8B871}'
$keys = @(
  "HKCU:\Software\Classes\PEPro.Application",
  "HKCU:\Software\Classes\CLSID\$clsid",
  "HKCU:\Software\Classes\AppID\$clsid",
  "HKCU:\Software\Classes\Interface\$iid",
  "HKCU:\Software\Classes\TypeLib\$tlb"
)
function KeyStates { $keys | ForEach-Object { [pscustomobject]@{ Key = $_; Present = (Test-Path $_) } } }
function Get-ServerProcs { @(Get-Process -Name 'Picket302.ComAutomation' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) }

$out.preKeys = KeyStates
$out.preProcs = Get-ServerProcs
if (($out.preKeys | Where-Object Present).Count -gt 0 -or $out.preProcs.Count -gt 0) {
  $out.aborted = 'PRECONDITION_DIRTY'; $out | ConvertTo-Json -Depth 5 | Out-File (Join-Path $evidenceDir 'regclean.json') -Encoding utf8; Write-Output 'ABORTED_DIRTY'; return
}
try {
  New-Item -ItemType Directory -Force -Path $serverTmp | Out-Null
  Copy-Item -Path (Join-Path $serverBin '*') -Destination $serverTmp -Recurse -Force
  $regP = Start-Process -FilePath $serverExe -ArgumentList '--register' -NoNewWindow -PassThru -Wait
  $out.registerExit = $regP.ExitCode
  $out.afterRegisterKeys = KeyStates
  $ls = Get-ItemProperty -Path "HKCU:\Software\Classes\CLSID\$clsid\LocalServer32" -ErrorAction SilentlyContinue
  $out.localServer32 = $ls.'(default)'
  $out.serverExeSha = (Get-FileHash $serverExe -Algorithm SHA256).Hash
  # bitness / rights context
  $out.procArch = $env:PROCESSOR_ARCHITECTURE
  $out.user = $env:USERNAME
  $out.isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

  $un = Start-Process -FilePath $serverExe -ArgumentList '--unregister' -NoNewWindow -PassThru -Wait
  $out.unregisterExit = $un.ExitCode
  $out.afterUnregisterKeys = KeyStates
  $out.postProcs = Get-ServerProcs
  $out.allKeysRemoved = -not (($out.afterUnregisterKeys | Where-Object Present).Count -gt 0)
  $out.noProcsLeft = ($out.postProcs.Count -eq 0)
}
finally {
  if ((Test-Path $serverTmp) -and $serverTmp -like '*p302-rc-com-*') { Remove-Item -LiteralPath $serverTmp -Recurse -Force; $out.tmpRemoved = $true }
  $out.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
  $out | ConvertTo-Json -Depth 6 | Out-File (Join-Path $evidenceDir 'regclean.json') -Encoding utf8
}
Write-Output 'REGCLEAN_COMPLETE'
