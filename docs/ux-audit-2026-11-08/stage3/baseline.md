# Phase 0 — Baseline inventory (stage 3, 2026-11-08)

Series: `docs/ux-audit-2026-11-08/stage3/`
Captured before any modification of this series.

## 1. Git state (read-only checks only)

```
git status --short --branch
## main...origin/main
?? graphify-out/

git log -1 --oneline
aee4ea8 docs(acceptance): clarify stdin BOM contract

git branch -a
* main
  remotes/origin/HEAD -> origin/main
  remotes/origin/main

git worktree list --porcelain
worktree //192.168.1.117/Projets/Picket302
HEAD aee4ea8f22efd996851e1250b8cc0b9997607ac6
branch refs/heads/main

worktree C:/Users/Nachfin/.codex/worktrees/redesign-konvert-ui/Picket302
HEAD 0dc80026bafe7641f6d7853550128ccb04b78945
detached
```

| Item | Value |
|---|---|
| Branch | `main` |
| HEAD SHA | `aee4ea8f22efd996851e1250b8cc0b9997607ac6` |
| Expected published commit | `aee4ea8` — **matches** |
| main ↔ origin/main | synchronised (`## main...origin/main`, no ahead/behind marker) |
| Remote refs | `git ls-remote origin` → exactly one ref: `refs/heads/main = aee4ea8`. No tags, no other branches. |
| Tracked-file modifications | none |
| Untracked (outside this series) | `graphify-out/` only — **pre-existing, untouched** |
| Foreign worktree | `C:/Users/Nachfin/.codex/worktrees/redesign-konvert-ui/Picket302` (detached `0dc8002`) — **not touched, not removed** |

### Pre-existing untracked state (declared, not part of this work)

| Path | Status | Action |
|---|---|---|
| `graphify-out/` | untracked dir, 528 files under `graphify-out/cache/` | **NOT touched, NOT deleted, NOT rebuilt, NOT committed** (hard constraint §3) |
| `tmp_audit/` | tracked probe projects (`probe`, `probe2`, `probe3`) | not modified |

### Structural finding about the reference

`origin` **is** `https://github.com/kerzhaev/Picket302.git` and its only ref equals our
HEAD byte-for-byte. Consequently the published repository carries **no reference
implementation distinct from the code under audit**, and it contains **zero image
files** (`find EVIDENCE -name '*.png' -o -name '*.jpg' …` → 0). The visual reference
that does exist on this host is:

| Reference artefact | Location | Usable as pixel reference? |
|---|---|---|
| Legacy AMS ENVELOPS main window screenshot | `docs/ux-audit-2026-11-03/референс-2026-11-05/01-главное-адресная-книга.png` | **Yes** — the only genuine pixel reference for the main screen |
| Legacy UI structure (54 DFM forms) | `EVIDENCE/EVIDENCE/ui_control_trees.txt` | Structural/textual only |
| Legacy caption inventory | `EVIDENCE/EVIDENCE/ui_captions_reference.txt` | Structural/textual only |
| Legacy screenshot *index* (133 names, no bytes) | `EVIDENCE/EVIDENCE/screenshot_index.txt` | Index only — images absent |
| Legacy application, runnable | `D:\KonvertProNet\KonvertProNet.exe` | Present; **not launched** — running it would drive the legacy print pipeline against a live printer (out of scope, §4) |
| Pre-redesign Picket302 screenshots | `docs/ux-audit-2026-11-03/*.png` (5 files) | Before-state of *this* product |
| Post-redesign Picket302 screenshots | `docs/ux-audit-2026-11-03/после-этапа-редизайна/` (40 files) | Previous stage's after-state |

**Consequence for Phase 2:** for most of the 26 mandatory surfaces no pixel
reference exists. Those are marked `REFERENCE_SCREEN_UNAVAILABLE` and excluded
from the weighted score (§9 forbids scoring screens without a reference).

## 2. Processes

```
Get-Process | Where-Object { $_.ProcessName -match 'Picket302|UiAcceptance|testhost|dotnet' }
→ (no rows)
```

No `Picket302`, `UiAcceptance`, `testhost` or `dotnet` process was running at
baseline. No foreign process was touched.

## 3. Production profile fingerprint (before)

`%AppData%\Picket302` resolved via `[Environment]::GetFolderPath('ApplicationData')`
(not the `APPDATA` env var, which the harness may shadow).

| File | SHA-256 | Size (bytes) | LastWriteTimeUtc |
|---|---|---|---|
| `connection.json` | `399006D91EC787C5F4B6185DF7477EB8ABE8D549640E681A1D2B45162D6972D5` | 225 | 2026-11-05T21:39:28.7005910Z |
| `picket302.db` | **ABSENT** | – | – |
| `com-automation.log` | `3DB33E054F3E172FDCC52CED9252ABA49E72C682E514B05C4FA91B11BAA1BA4E` | 521722 | 2026-11-08T07:17:10.5093039Z |
| `print_audit.log` | `215769F4A382BE47F7379D57CAAEC78070A25B0D06A35C3EE13BA2B3D8A46658` | 48074 | 2026-11-07T22:53:29.4979076Z |

Directory also contains `logs/` and `Picket302/` subdirectories.

**External warning (carried over, not caused by this series):** `picket302.db` is
absent. The eleventh review round recorded the same absence and proved it was
caused by an *external* `Picket302.ComAutomation.exe` launch at 2026-11-08
10:17:08–10:17:10 from this repo's test `bin`, not by any acceptance run of that
round (the strict filter excludes `Acceptance`/`ComAutomation`). The production
profile is therefore **not** in a state this series can restore — that is the
owner's action. This series does not modify it.

## 4. Temporary profiles and leftovers

`%TEMP%` contained **many** pre-existing directories from earlier rounds and
other test suites (`Picket302BackupTests_*`, `Picket302Tests_*`,
`Picket302Acceptance`, `picket302-s6`, `picket302-ui`, `p302-parallel-verify*`,
`p302-probe`, `Picket302.S6*`, …). They are **not** this series' creations and
were **not deleted** (hard constraint: "any untracked files outside your task —
do not delete"; deletion of broad directories is forbidden).

This series therefore uses a **single dedicated evidence root**
`%TEMP%\p302-evidence-2026-11-08-stage3` for all runtime artefacts and asserts
*zero net growth* of profile directories attributable to it, rather than
attempting to sweep pre-existing leftovers.

Repo-wide scan for `.bak` / `.lock`: **none** (`find . -name '*.bak' -o -name '*.lock'` → empty).

## 5. Build & test baseline (measured, not assumed)

| Command | Configuration | Result |
|---|---|---|
| `dotnet build ./Picket302.sln -c Debug --no-restore --nologo` | Debug | **0 errors, 0 warnings**, 29.24 s |
| `dotnet build ./Picket302.sln -c Release --no-restore --nologo` | Release | **0 errors, 0 warnings**, 28.54 s |
| `dotnet build ./tools/ui-acceptance/UiAcceptance.csproj -c Release --no-restore --nologo` | Harness | **0 errors, 0 warnings**, 6.27 s |
| `dotnet test ./Picket302.sln -c Debug --no-build --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"` | Debug | **1276 passed / 0 failed / 0 skipped**, exit 0, 16 s |
| `dotnet test ./Picket302.sln -c Release --no-build --filter "…same…"` | Release | **1276 passed / 0 failed / 0 skipped**, exit 0, 16 s |

Counts are the ones actually reported by the runner; nothing is pre-written.

## 6. Toolchain / environment facts

| Fact | Value |
|---|---|
| Windows PowerShell | 5.1.26100.7462 |
| PowerShell 7 | 7.5.5 available (`pwsh.exe`) |
| .NET SDK | 8.0 (`net8.0-windows`, `x64` forced by `Directory.Build.props`) |
| ZIPcode font | **not installed**; `D:\KonvertProNet\ZIPcode.ttf` (8008 B) exists on disk but is neither shipped with the app nor registered (`HKCU …\Fonts` has no `ZIPcode` entry, `InstalledFontCollection` has no `ZIPcode` family) |
| Printers | default printer present; `Win32_PrintJob` used as spooler oracle |

## 7. Environment constraints recorded for the report

1. `D:\KonvertProNet` — **read-only**. Not modified, not executed.
2. `%AppData%\Picket302` — **read-only**. Fingerprinted before/after every runtime scenario.
3. COM / cold activation — **DEFERRED_BY_OWNER**. Nothing registered, nothing launched.
4. Physical printer — **out of scope**. `PRINT_VERIFIED` may not be set to `true`.
5. `graphify-out/` — **read-only**.
6. `ZIPcode.ttf` — present only under `D:\KonvertProNet`, licence unconfirmed → **not copied into the repo, not registered**. `POST_INDEX_STAMP_VERIFIED` therefore remains `NOT_RUN_ON_HOST`.

## 8. Result of Phase 0

Baseline established and reproducible. No blocking condition found in the
environment; the working tree is clean and the production profile is protected
by a pre/post fingerprint contract carried into every runtime scenario.
