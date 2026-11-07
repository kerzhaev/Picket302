# Emptybook fault-injection: parent-alive-after-stop

Прогон: 2026-11-07 23:29:14, профиль: временный (guid), createDemoData=false.

[2026-11-07T20:29:14Z] sentinel before: A34009A12942C255BFFE655DCACE0C302977CB8E60080F9AD52E855553580FF4|639296801541861225
[2026-11-07T20:29:14Z] production profile files before: 5
[2026-11-07T20:29:14Z] FAULT-INJECT: parent-alive-after-stop — synthetic harness (без UI)
[2026-11-07T20:29:14Z] SYNTHETIC HARNESS PID: 111740
[2026-11-07T20:29:14Z] условие [fault-режим: сценарий пропущен]: False
[2026-11-07T20:29:14Z] SAFETY-FLAG: tree-cleanup-failed
[2026-11-07T20:29:14Z] SAFETY-FLAG: remaining-descendants
[2026-11-07T20:29:14Z] SAFETY-FLAG: tree-postcheck-failed
[2026-11-07T20:29:14Z] остановка: graceful=False killed=True pid=111740 TreeOk=False TreeConfirmed=True остатки=[111740] неподтверждённые=[] taskkill= ошибка: fault-inject: parent alive after stop
[2026-11-07T20:29:14Z] SAFETY-FAIL: harness не завершён после остановки (HarnessNotExited) — дальнейшие ожидания запрещены
[2026-11-07T20:29:14Z] harness exit code: ; killed: True; HarnessNotExited: True
[2026-11-07T20:29:14Z] SAFETY-FLAG: tree-cleanup-failed
[2026-11-07T20:29:14Z] SAFETY-FLAG: remaining-descendants
[2026-11-07T20:29:14Z] SAFETY-FLAG: tree-postcheck-failed
[2026-11-07T20:29:14Z] SAFETY-FAIL: harness убит в finally (pid=111740) — happy-path PASS невозможен
[2026-11-07T20:29:14Z] SAFETY-FAIL: harness остался жив даже после аварийной остановки (pid=111740) — драйвер завершается с exit 1 без дальнейших ожиданий
[2026-11-07T20:29:14Z] production delta (финально): new=0 changed=0 removed=0
[2026-11-07T20:29:14Z] Режим: emptybook
[2026-11-07T20:29:14Z] UTC: 2026-11-07T20:29:14Z
[2026-11-07T20:29:14Z] PID harness: 111740
[2026-11-07T20:29:14Z] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-07T20:29:14Z] временный профиль: не создан
[2026-11-07T20:29:14Z] createDemoData=false
[2026-11-07T20:29:14Z] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-07T20:29:14Z] LOCK RELEASE: released=True disposed=True
[2026-11-07T20:29:14Z] HARNESS NOT EXITED: True
[2026-11-07T20:29:14Z] неудачи очистки: tree-cleanup-failed;remaining-descendants;tree-postcheck-failed;harness-not-exited;emergency-kill
[2026-11-07T20:29:14Z] FAULT: parent-alive-after-stop
[2026-11-07T20:29:14Z] EXPECTED FINAL EXIT: 1
[2026-11-07T20:29:14Z] FINAL VERDICT: FAIL
[2026-11-07T20:29:14Z] PASS_OF_CONTRACT: True (fault — инъекция корректно обнаружена, скрипт не вернёт 0)
