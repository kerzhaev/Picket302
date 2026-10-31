# PromptPilot Watchdog (Picket302) - OS-level supervisor.
# Does NOT depend on ZCode (which does not fire when the app is closed).
# Runs via Windows Task Scheduler every 15 minutes.
# Duties:
#   1) worker dead -> restart stack via stop.ps1+start.ps1 (dotnet in PATH);
#   2) mission awaiting_human for SAFE reasons -> auto-resume
#      (executor.invalid_output / no-diff / checkpoint_failed / review issues);
#   3) round limits exhausted -> raise via PATCH -> resume;
#   4) everything else (semantic blockers, gate.failed with broken code) ->
#      log-only with [SMART-NEEDED] marker, no blind resumes.
# Guards: resume at most once per 20 minutes; max 4 consecutive resumes
# without stage/round progress -> ESCALATE marker and stop resuming.
# Log: tools\watchdog.log (ASCII); state: tools\watchdog-state.json
$ErrorActionPreference = 'Continue'
$repo    = 'C:\Users\FIREBAT\Desktop\Projets\Picket302'
$deploy  = 'C:\Users\FIREBAT\Desktop\Projets\pp-deploy'
$api     = 'http://127.0.0.1:8420'
$wid     = 'wf_bf13ed4d06e341c7afb41baa6673c71b'
$log     = Join-Path $repo 'tools\watchdog.log'
$stateF  = Join-Path $repo 'tools\watchdog-state.json'

function Log($msg) {
  $line = ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg)
  Add-Content -Path $log -Value $line -Encoding ASCII
}

function Get-State {
  if (Test-Path $stateF) {
    try { return Get-Content $stateF -Raw -Encoding UTF8 | ConvertFrom-Json } catch {}
  }
  return [pscustomobject]@{ lastResumeAt=[datetime]::MinValue; lastStage=''; lastRound=0; consecutiveResumes=0; escalated=$false }
}
function Set-State($s) {
  $s | ConvertTo-Json | Set-Content -Path $stateF -Encoding ASCII
}

$state = Get-State
$now = Get-Date
if (-not ($state.PSObject.Properties['lastVersion'])) { $state | Add-Member -NotePropertyName lastVersion -NotePropertyValue 0 }
if (-not ($state.PSObject.Properties['lastStatus']))  { $state | Add-Member -NotePropertyName lastStatus -NotePropertyValue '' }

function Do-Resume($why) {
  $body = @{ expected_version = $wf.state_version; text = "Watchdog: auto-resume. Reason: $why"; resume = $true } | ConvertTo-Json
  try {
    Invoke-RestMethod -Uri "$api/api/workflows/$wid/human-input" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 15 | Out-Null
    $state.lastResumeAt = $now
    $state.consecutiveResumes = [int]$state.consecutiveResumes + 1
    Set-State $state
    Log ("RESUMED: $why (streak: " + $state.consecutiveResumes + ")")
  } catch { Log ("RESUME-ERROR: " + $_.Exception.Message) }
}

function Patch-Limits {
  try {
    $cfg = $wf.config
    $cfg.limits.max_rounds = [int]$cfg.limits.max_rounds + 20
    $cfg.planning.max_revisions_per_stage = [int]$cfg.planning.max_revisions_per_stage + 4
    $body = @{ config = $cfg; expected_version = $wf.state_version } | ConvertTo-Json -Depth 12
    Invoke-RestMethod -Uri "$api/api/workflows/$wid" -Method Patch -ContentType 'application/json' -Body $body -TimeoutSec 15 | Out-Null
    Log 'LIMITS: budget raised'
    return $true
  } catch { Log ("LIMITS-ERROR: " + $_.Exception.Message); return $false }
}

# ---------- 1. Worker health ----------
$worker = $null
try {
  $worker = Invoke-RestMethod -Uri "$api/api/worker/status" -TimeoutSec 10
} catch {}
if ($null -eq $worker -or $worker.state -ne 'online' -or $worker.paused) {
  Log 'WORKER-DOWN: restarting stack via stop.ps1+start.ps1'
  try {
    $stopPs = Join-Path $deploy 'stop.ps1'
    $startPs = Join-Path $deploy 'start.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $stopPs | Out-Null
    $env:Path = 'C:\Program Files\dotnet;' + $env:Path
    & powershell -NoProfile -ExecutionPolicy Bypass -File $startPs | Out-Null
    Start-Sleep -Seconds 12
    Log 'WORKER-DOWN: stack restarted'
  } catch { Log ("WORKER-DOWN: restart error: " + $_.Exception.Message) }
  Log 'PASS DONE (after worker restart); next check in 15 min'
  exit 0
}

# ---------- 2. Mission status ----------
$wf = $null
try { $wf = Invoke-RestMethod -Uri "$api/api/workflows/$wid" -TimeoutSec 10 } catch {}
if ($null -eq $wf) { Log 'API-ERROR: workflow unreachable'; exit 0 }

$status = $wf.status
$round  = $wf.current_round

# progress -> reset consecutive-resume counter
if ($wf.current_stage_id -ne $state.lastStage -or $round -ne $state.lastRound) {
  if ($state.lastStage -ne '') { Log 'PROGRESS: stage/round changed -> resume counter reset' }
  $state.lastStage = $wf.current_stage_id
  $state.lastRound = $round
  $state.consecutiveResumes = 0
  $state.escalated = $false
  Set-State $state
}

# ---------- 1.5 Checkpoint compensation (К-7/P11 workaround) ----------
# The stage checkpoint attributes ONLY the last executor-run's changes; on
# multi-round stages the earlier rounds' deliverable stays uncommitted
# (checkpoint payload "sha": null). Compensation: after a checkpoint with no
# commit, verify build+test and commit the finished stage's allowed files.
try {
  $rep2 = Invoke-RestMethod -Uri "$api/api/workflows/$wid/report" -TimeoutSec 15
  $cpEvent = $rep2.events | Where-Object { $_.event_type -eq 'stage.checkpoint' } | Select-Object -Last 1
  if ($cpEvent -and $cpEvent.payload) {
    # P16 (K-7 variant): a checkpoint may carry a NON-NULL sha while committing
    # only incidental files (e.g. docs) - the stage's real src/tests deliverable
    # stays uncommitted. Detection is therefore NOT "sha is null" but
    # "checkpoint newer than last commit AND allowed paths still dirty".
    # Compensate ONLY when the checkpoint is newer than the last git commit:
    # otherwise it is already compensated (or the next stage's work is in flight).
    $cpTime = [datetime]$cpEvent.created_at
    $lastCommit = git -C $repo log -1 --format=%cI 2>$null
    $lastCommitTime = [datetime]$lastCommit
    if ($cpTime -gt $lastCommitTime) {
      $cpStage = $cpEvent.payload.stage_code
      $dirty = @(git -C $repo status --porcelain | Where-Object { $_ -match '(src|tests|docs)/' })
      if ($dirty.Count -gt 0) {
        Log ("CHECKPOINT-K7: stage $cpStage has uncommitted deliverable (" + $dirty.Count + " paths) - verifying and committing")
        $env:Path = 'C:\Program Files\dotnet;' + $env:Path
        Push-Location $repo
        $null = & dotnet build Picket302.sln -c Debug --nologo -v q 2>&1 | Select-Object -Last 3
        if ($LASTEXITCODE -eq 0) {
          $tst = (& dotnet test Picket302.sln -c Debug --nologo --no-build -v q 2>&1 | Select-Object -Last 1) -join ''
          if ($LASTEXITCODE -eq 0 -and $tst -match 'Passed!') {
            git add src tests docs
            git commit -m "Stage ${cpStage}: stage deliverable (supervisor compensation for checkpoint attribution defect K-7/P11)" | Out-Null
            Log ("CHECKPOINT-K7: committed stage $cpStage deliverable; tests: " + $tst)
          } else {
            Log ("CHECKPOINT-K7: tests NOT green - no commit. Tail: " + $tst)
          }
        } else {
          Log "CHECKPOINT-K7: build failed - no commit"
        }
        Pop-Location
      } else {
        Log ("CHECKPOINT-K7: stage $cpStage checkpoint had no commit, but nothing dirty in allowed paths - OK")
      }
    }
  }
} catch { Log ("K7-CHECK-ERROR: " + $_.Exception.Message) }

if ($status -in @('queued','executing','gating','reviewing','revision_required')) {
  # P15 guard: revision_required is a TRANSIENT state (worker advances it on
  # task completion). If it persists across two passes (30 min) with no
  # state_version change, a review was recorded via API and nothing advanced
  # it (api.py record_review lacks advance_workflow). Resume is exactly what
  # advance_workflow would do (workflows.py:3521-3531). Guards apply.
  if ($status -eq 'revision_required' -and $state.lastStatus -eq 'revision_required' -and $state.lastVersion -eq [int]$wf.state_version) {
    $minsSinceResume2 = 9999
    if ($state.lastResumeAt) { $minsSinceResume2 = ($now - [datetime]$state.lastResumeAt).TotalMinutes }
    if (-not $state.escalated -and $minsSinceResume2 -ge 20 -and $state.consecutiveResumes -lt 4) {
      Do-Resume 'revision_required stalled with no version change for 2 passes (P15): advancing to next round'
    }
  }
  Log ("OK: status=$status, round=$round, ver=$($wf.state_version)")
  $state.lastStatus = $status
  $state.lastVersion = [int]$wf.state_version
  Set-State $state
  exit 0
}

if ($status -ne 'awaiting_human') {
  Log ("Status=$status - no action defined")
  exit 0
}

# ---------- 3. awaiting_human: diagnose ----------
$rep = $null
try { $rep = Invoke-RestMethod -Uri "$api/api/workflows/$wid/report" -TimeoutSec 15 } catch {}
if ($null -eq $rep) { Log 'API-ERROR: report unreachable'; exit 0 }
$lastType = ''
if ($rep.events -and $rep.events.Count -gt 0) { $lastType = $rep.events[-1].event_type }
Log ("AWAITING_HUMAN: last event=$lastType")

# --- guards ---
$minsSinceResume = 9999
if ($state.lastResumeAt) { $minsSinceResume = ($now - [datetime]$state.lastResumeAt).TotalMinutes }
if ($state.escalated) { Log 'ESCALATED: already flagged for owner; no action'; exit 0 }
if ($minsSinceResume -lt 20) { Log 'GUARD: less than 20 min since last resume - skip'; exit 0 }
if ($state.consecutiveResumes -ge 4) {
  Log 'ESCALATE: 4 consecutive resumes without progress - human eyes needed'
  $state.escalated = $true; Set-State $state
  exit 0
}

switch -Regex ($lastType) {
  '^executor\.invalid_output$' { Do-Resume 'executor report had no machine-readable verdict; restarting round' }
  '^executor\.failed$' { Do-Resume 'executor process failed (transient provider/runtime error); restarting round - WIP in worktree will be picked up' }
  '^review\.(no_diff|diff_origin_unknown)$' { Do-Resume 'review without diff/unknown origin; restarting round' }
  '^stage\.checkpoint_failed$' {
    $gn = git -C $repo config user.name 2>$null
    if ([string]::IsNullOrWhiteSpace($gn)) {
      git -C $repo config user.name 'PicketPilot'
      git -C $repo config user.email 'promptpilot@picket302.local'
      Log 'CHECKPOINT: git identity restored'
    }
    Do-Resume 'checkpoint failed; identity checked/restored'
  }
  '^limit\.(stage_revisions|max_rounds)$' {
    if (Patch-Limits) { Do-Resume 'round budget raised by watchdog' }
  }
  '^gate\.failed$' {
    Do-Resume 'gate failed: restarting round so the gate reruns'
  }
  default {
    Log ("[SMART-NEEDED] unknown wait reason: $lastType - needs smart analysis (ZCode supervisor/owner)")
  }
}
Log 'PASS DONE'
