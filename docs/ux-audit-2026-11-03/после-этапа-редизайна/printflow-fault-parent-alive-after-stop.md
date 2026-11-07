# F5/F6 fault-injection: parent-alive-after-stop

Прогон: 2026-11-07 23:24:28, профиль: временный (guid), createDemoData=false.

[2026-11-07T20:24:28Z][pid=0] sentinel before: 798C3E09434282A0A6A0098B2EDFF14E6CBA499C84CD3C2059F33345F3F25F94|639296798684503621
[2026-11-07T20:24:28Z][pid=0] production profile files before: 5
[2026-11-07T20:24:28Z][pid=0] FAULT-INJECT: parent-alive-after-stop — synthetic harness (без UI, без печати)
[2026-11-07T20:24:28Z][pid=83160] SYNTHETIC HARNESS PID: 83160
[2026-11-07T20:24:28Z][pid=83160] условие [fault-режим: сценарий пропущен]: False
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: tree-cleanup-failed
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: remaining-descendants
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: tree-postcheck-failed
[2026-11-07T20:24:28Z][pid=83160] остановка: graceful=False killed=True pid=83160 TreeOk=False TreeConfirmed=True остатки=[83160] неподтверждённые=[] taskkill= ошибка: fault-inject: parent alive after stop
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FAIL: harness не завершён после остановки (HarnessNotExited) — дальнейшие ожидания запрещены
[2026-11-07T20:24:28Z][pid=83160] harness exit code: ; killed: True; HarnessNotExited: True
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: tree-cleanup-failed
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: remaining-descendants
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FLAG: tree-postcheck-failed
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FAIL: harness убит в finally (pid=83160) — happy-path PASS невозможен
[2026-11-07T20:24:28Z][pid=83160] SAFETY-FAIL: harness остался жив даже после аварийной остановки (pid=83160) — драйвер завершается с exit 1 без дальнейших ожиданий
[2026-11-07T20:24:28Z][pid=83160] production delta (финально): new=0 changed=0 removed=0
[2026-11-07T20:24:28Z][pid=83160] Режим: printflow
[2026-11-07T20:24:28Z][pid=83160] UTC: 2026-11-07T20:24:28Z
[2026-11-07T20:24:28Z][pid=83160] PID harness: 83160
[2026-11-07T20:24:28Z][pid=83160] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-07T20:24:28Z][pid=83160] временный профиль: не создан
[2026-11-07T20:24:28Z][pid=83160] createDemoData=false
[2026-11-07T20:24:28Z][pid=83160] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-07T20:24:28Z][pid=83160] F5: False; F6: False; неожиданных окон: False
[2026-11-07T20:24:28Z][pid=83160] harness: exit= killed=True; cleanup: False; profile safety: False
[2026-11-07T20:24:28Z][pid=83160] LOCK RELEASE: released=True disposed=True
[2026-11-07T20:24:28Z][pid=83160] HARNESS NOT EXITED: True
[2026-11-07T20:24:28Z][pid=83160] неудачи очистки: tree-cleanup-failed;remaining-descendants;tree-postcheck-failed;harness-not-exited;emergency-kill
[2026-11-07T20:24:28Z][pid=83160] FAULT: parent-alive-after-stop
[2026-11-07T20:24:28Z][pid=83160] EXPECTED FINAL EXIT: 1
[2026-11-07T20:24:28Z][pid=83160] FINAL VERDICT: FAIL
[2026-11-07T20:24:28Z][pid=83160] PASS_OF_CONTRACT: True (fault — инъекция корректно обнаружена, скрипт не вернёт 0)
