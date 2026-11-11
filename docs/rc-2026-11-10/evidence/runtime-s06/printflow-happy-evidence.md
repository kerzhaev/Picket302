# F5/F6 runtime-доказательство (happy path)

Прогон: 2026-11-11 21:02:20, профиль: временный (guid), createDemoData=false.

[2026-11-11T18:02:04Z][pid=0] sentinel before: 541AD36D7695E16129D57D1A6A1D1391B588063C775CD93E64D514796333BBBF|639300169241207148
[2026-11-11T18:02:04Z][pid=0] production profile files before: 4
[2026-11-11T18:02:04Z][pid=0] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\1eed979bf7ff4e57982d1f37c46ebc02
[2026-11-11T18:02:05Z][pid=18332] harness READY: READY 789060 18332
[2026-11-11T18:02:07Z][pid=18332] SELECT подтверждён stdout: OK SELECT
[2026-11-11T18:02:07Z][pid=18332] ## F5 «Печать текущего конверта»
[2026-11-11T18:02:07Z][pid=18332] Карточка отправления появилась: hwnd=7277362 title='Карточка отправления'
[2026-11-11T18:02:08Z][pid=18332] скриншот карточки: 20-карточка-отправления-f5.png
[2026-11-11T18:02:08Z][pid=18332] Карточка: нажата ОК (BM_CLICK)
[2026-11-11T18:02:08Z][pid=18332] Диалог «Печать» появился: hwnd=7342898
[2026-11-11T18:02:09Z][pid=18332] скриншот диалога печати: 21-диалог-печати-f5.png
[2026-11-11T18:02:10Z][pid=18332] Диалог «Печать»: нажата Отмена — задания на принтер не создаётся
[2026-11-11T18:02:12Z][pid=18332] OK: диалог «Печать» исчез после Отмены
[2026-11-11T18:02:12Z][pid=18332] UI отвечает после F5 (WM_NULL): True
[2026-11-11T18:02:12Z][pid=18332] FLOW F5 PASS: True
[2026-11-11T18:02:12Z][pid=18332] ## F6 «Предпросмотр и печать»
[2026-11-11T18:02:12Z][pid=18332] Карточка отправления появилась: hwnd=7408434
[2026-11-11T18:02:12Z][pid=18332] Карточка: нажата ОК (BM_CLICK)
[2026-11-11T18:02:12Z][pid=18332] MessageBox формирования отправлений появился: hwnd=7473970 title='Печать'
[2026-11-11T18:02:13Z][pid=18332] MessageBox подтверждён (ОК), скриншот: 24-messagebox-формирование-210212.png
[2026-11-11T18:02:13Z][pid=18332] OK: MessageBox формирования отправлений найден и подтверждён
[2026-11-11T18:02:13Z][pid=18332] Диалог «Печать» появился: hwnd=7539506
[2026-11-11T18:02:14Z][pid=18332] Диалог «Печать»: нажата Печать — ожидание окна предпросмотра
[2026-11-11T18:02:14Z][pid=18332] Окно «Подача конвертов» появилось: hwnd=7605042
[2026-11-11T18:02:15Z][pid=18332] скриншот подачи: 25-подача-конвертов-210214.png
[2026-11-11T18:02:15Z][pid=18332] Подача конвертов: нажата Продолжить
[2026-11-11T18:02:15Z][pid=18332] OK: окно «Подача конвертов» появилось и обработано
[2026-11-11T18:02:15Z][pid=18332] Окно «Предпросмотр печати» появилось: hwnd=9571122
[2026-11-11T18:02:16Z][pid=18332] скриншот предпросмотра: 22-предпросмотр-f6.png
[2026-11-11T18:02:16Z][pid=18332] Предпросмотр: нажата Закрыть (DialogResult != OK, спулер не вызывается)
[2026-11-11T18:02:19Z][pid=18332] OK: окно предпросмотра исчезло
[2026-11-11T18:02:19Z][pid=18332] UI отвечает после F6 (WM_NULL): True
[2026-11-11T18:02:19Z][pid=18332] FLOW F6 PASS: True
[2026-11-11T18:02:20Z][pid=18332] остановка: graceful=True killed=False pid=18332 TreeOk=True TreeConfirmed=True остатки=[] неподтверждённые=[] taskkill= ошибка: 
[2026-11-11T18:02:20Z][pid=18332] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-11T18:02:20Z][pid=18332] stdout: OK SELECT=True OK F5=False OK F6=False FAIL-строк=0
[2026-11-11T18:02:20Z][pid=18332] temp profile cleaned: True
[2026-11-11T18:02:20Z][pid=18332] sentinel unchanged: True
[2026-11-11T18:02:20Z][pid=18332] production delta: new=0 changed=0 removed=0
[2026-11-11T18:02:20Z][pid=18332] OK: SELECT подтверждён stdout (OK SELECT)
[2026-11-11T18:02:20Z][pid=18332] OK: нет FAIL-строк в stdout harness
[2026-11-11T18:02:20Z][pid=18332] информационно: OK F5=False OK F6=False в stdout (async void после ShowDialog)
[2026-11-11T18:02:20Z][pid=18332] F5-цепочка: True; F6-цепочка: True
[2026-11-11T18:02:20Z][pid=18332] неожиданных окон: False; UI после F5: True; UI после F6: True
[2026-11-11T18:02:20Z][pid=18332] harness: exit=0 killed=False -> True
[2026-11-11T18:02:20Z][pid=18332] cleanup: True; profile safety: True
[2026-11-11T18:02:20Z][pid=18332] дерево: ok=True остатки=0 неподтверждённые=0 ошибка=''
[2026-11-11T18:02:20Z][pid=18332] сценарий (все флаги): True
[2026-11-11T18:02:20Z][pid=18332] production delta (финально): new=0 changed=0 removed=0
[2026-11-11T18:02:20Z][pid=18332] Режим: printflow
[2026-11-11T18:02:20Z][pid=18332] UTC: 2026-11-11T18:02:20Z
[2026-11-11T18:02:20Z][pid=18332] PID harness: 18332
[2026-11-11T18:02:20Z][pid=18332] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-11T18:02:20Z][pid=18332] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\1eed979bf7ff4e57982d1f37c46ebc02
[2026-11-11T18:02:20Z][pid=18332] createDemoData=false
[2026-11-11T18:02:20Z][pid=18332] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-11T18:02:20Z][pid=18332] F5: True; F6: True; неожиданных окон: False
[2026-11-11T18:02:20Z][pid=18332] harness: exit=0 killed=False; cleanup: True; profile safety: True
[2026-11-11T18:02:20Z][pid=18332] LOCK RELEASE: released=True disposed=True
[2026-11-11T18:02:20Z][pid=18332] HARNESS NOT EXITED: False
[2026-11-11T18:02:20Z][pid=18332] неудачи очистки: нет
[2026-11-11T18:02:20Z][pid=18332] FAULT: нет
[2026-11-11T18:02:20Z][pid=18332] EXPECTED FINAL EXIT: 0
[2026-11-11T18:02:20Z][pid=18332] FINAL VERDICT: PASS
