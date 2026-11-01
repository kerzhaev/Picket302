<#
  Stage S6 helper: builds and tests the solution in an isolated working copy.

  Reason: several implementation tasks run in parallel and each of them needs a
  green build without fighting for the same obj/bin directories. The script
  copies the repository sources into a private directory and runs the build and
  the tests there.

  Usage (from the repository root):
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\s6-verify.ps1 -WorkDir C:\temp\s6_workerA

  Parameters:
    -WorkDir     : private directory for the copy (created if missing).
    -TestFilter  : optional xunit filter, for example FullyQualifiedName~Domain.
    -NoTest      : build only.

  The script is intentionally ASCII-only: PowerShell 5.1 reads .ps1 files as
  ANSI when no BOM is present.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkDir,

    [string]$TestFilter = "",

    [switch]$NoTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Copy-Tree {
    param([string]$From, [string]$To)

    if (-not (Test-Path $From)) {
        return
    }

    # The destination is a private directory of this script: it is cleared
    # before the copy so that stale generated files cannot reach the build.
    if (Test-Path $To) {
        Remove-Item $To -Recurse -Force
    }

    New-Item -ItemType Directory -Path $To -Force | Out-Null

    $null = robocopy $From $To /E /XD obj* bin* /XF '*.user' /NFL /NDL /NJH /NJS /NP /R:1 /W:1
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed with exit code $LASTEXITCODE for $From"
    }
}

Copy-Tree (Join-Path $repo 'src') (Join-Path $WorkDir 'src')
Copy-Tree (Join-Path $repo 'tests') (Join-Path $WorkDir 'tests')

foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'Picket302.sln', '.editorconfig')) {
    $source = Join-Path $repo $file
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $WorkDir $file) -Force
    }
}

Write-Host "=== BUILD: $WorkDir ==="

# Several implementation tasks run in parallel in the same repository. A build
# can therefore fail because of a file that another agent has not finished
# writing yet; the build is retried a few times before the failure is reported.
$attempts = 3
$buildOk = $false

for ($attempt = 1; $attempt -le $attempts; $attempt++) {
    & dotnet build (Join-Path $WorkDir 'Picket302.sln') --nologo -v q
    if ($LASTEXITCODE -eq 0) {
        $buildOk = $true
        break
    }

    if ($attempt -lt $attempts) {
        Write-Host "BUILD FAILED ($LASTEXITCODE) - retry $attempt of $($attempts - 1) in 30s"
        Start-Sleep -Seconds 30
        Copy-Tree (Join-Path $repo 'src') (Join-Path $WorkDir 'src')
        Copy-Tree (Join-Path $repo 'tests') (Join-Path $WorkDir 'tests')
    }
}

if (-not $buildOk) {
    Write-Host "BUILD FAILED after $attempts attempts"
    exit 1
}

if ($NoTest) {
    Write-Host "BUILD OK (tests skipped)"
    exit 0
}

Write-Host "=== TEST: $WorkDir ==="
$testProject = Join-Path $WorkDir 'tests\Picket302.Tests\Picket302.Tests.csproj'

if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    & dotnet test $testProject --no-build --nologo
}
else {
    & dotnet test $testProject --no-build --nologo --filter $TestFilter
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "TESTS FAILED ($LASTEXITCODE)"
    exit $LASTEXITCODE
}

Write-Host "BUILD AND TESTS OK"
exit 0
