# F5/F6 runtime-доказательство (happy path)

Прогон: 2026-11-21 14:43:47, профиль: временный (guid), createDemoData=false.

[2026-11-21T11:43:16Z][pid=0] sentinel before: CD64B693F8C437E87DCA01591772FBB256DB32559A4B5DC87604158464E11B43|639308581960206565
[2026-11-21T11:43:16Z][pid=0] production profile files before: 6
[2026-11-21T11:43:16Z][pid=0] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\8e7cc56dd2d6425c8502564af331a4b6
[2026-11-21T11:43:19Z][pid=3152] harness READY: READY 723166 3152
[2026-11-21T11:43:22Z][pid=3152] PRINTTOFILE хук установлен: Z:\Picket302\docs\ux-run-2026-11-21\harness-printconfirm-font3\rc-s17-f5-printtofile.pdf
[2026-11-21T11:43:22Z][pid=3152] SEEDTEMPLATEPRINT: шаблон C6 162x114 (печать) засеян
[2026-11-21T11:43:22Z][pid=3152] SELECT подтверждён stdout: OK SELECT
[2026-11-21T11:43:22Z][pid=3152] ## F5 «Печать текущего конверта»
[2026-11-21T11:43:22Z][pid=3152] Карточка отправления появилась: hwnd=264712 title='Карточка отправления'
[2026-11-21T11:43:23Z][pid=3152] скриншот карточки: 20-карточка-отправления-f5.png
[2026-11-21T11:43:23Z][pid=3152] Карточка: нажата ОК (BM_CLICK)
[2026-11-21T11:43:23Z][pid=3152] Диалог «Печать» появился: hwnd=330248
[2026-11-21T11:43:24Z][pid=3152] скриншот диалога печати: 21-диалог-печати-f5.png
[2026-11-21T11:43:24Z][pid=3152] Диалог «Печать»: принтер переключён на «Microsoft Print to PDF — Ready»
[2026-11-21T11:43:24Z][pid=3152] Диалог «Печать»: нажата Печать — контролируемое задание (PrintToFile)
[2026-11-21T11:43:25Z][pid=3152] итерация подтверждения печати: 1
[2026-11-21T11:43:25Z][pid=3152] Подача конвертов появилась: hwnd=395784
[2026-11-21T11:43:25Z][pid=3152] Подача конвертов: нажата Продолжить
[2026-11-21T11:43:26Z][pid=3152] Предпросмотр печати появился: hwnd=4983304
[2026-11-21T11:43:27Z][pid=3152] Предпросмотр: нажата Печать — задание уходит в спулер
[2026-11-21T11:43:35Z][pid=3152] Конверт слишком большой: не показан за 8 с
[2026-11-21T11:43:43Z][pid=3152] Проверка размера бумаги: не показана — размер прошёл
[2026-11-21T11:43:44Z][pid=3152] OK: печатный PDF-файл создан и стабилен
[2026-11-21T11:43:44Z][pid=3152] PDF magic: %PDF- size=803
[2026-11-21T11:43:44Z][pid=3152] OK: PDF magic корректен (%PDF-)
[2026-11-21T11:43:44Z][pid=3152] PDF sha256: 74942CD0CA15F5C5B50C05CDCD76012D67D2A547001F3A2BEE1B8AFE0800044B
[2026-11-21T11:43:46Z][pid=3152] spooler jobs after print: 0
[2026-11-21T11:43:46Z][pid=3152] OK: в очереди спулера не осталось заданий
[2026-11-21T11:43:46Z][pid=3152] UI отвечает после F5 (WM_NULL): True
[2026-11-21T11:43:46Z][pid=3152] FLOW F5 PASS: True
[2026-11-21T11:43:46Z][pid=3152] PRINTTOFILE off — хук снят после контролируемой печати
[2026-11-21T11:43:47Z][pid=3152] остановка: graceful=True killed=False pid=3152 TreeOk=True TreeConfirmed=True остатки=[] неподтверждённые=[] taskkill= ошибка: 
[2026-11-21T11:43:47Z][pid=3152] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-21T11:43:47Z][pid=3152] stdout: OK SELECT=True OK F5=True OK F6=False FAIL-строк=0
[2026-11-21T11:43:47Z][pid=3152] temp profile cleaned: True
[2026-11-21T11:43:47Z][pid=3152] sentinel unchanged: True
[2026-11-21T11:43:47Z][pid=3152] production delta: new=0 changed=0 removed=0
[2026-11-21T11:43:47Z][pid=3152] OK: SELECT подтверждён stdout (OK SELECT)
[2026-11-21T11:43:47Z][pid=3152] OK: нет FAIL-строк в stdout harness
[2026-11-21T11:43:47Z][pid=3152] информационно: OK F5=True OK F6=False в stdout (async void после ShowDialog)
[2026-11-21T11:43:47Z][pid=3152] F5-цепочка: True; F6-цепочка: True
[2026-11-21T11:43:47Z][pid=3152] неожиданных окон: False; UI после F5: True; UI после F6: True
[2026-11-21T11:43:47Z][pid=3152] harness: exit=0 killed=False -> True
[2026-11-21T11:43:47Z][pid=3152] cleanup: True; profile safety: True
[2026-11-21T11:43:47Z][pid=3152] дерево: ok=True остатки=0 неподтверждённые=0 ошибка=''
[2026-11-21T11:43:47Z][pid=3152] сценарий (все флаги): True
[2026-11-21T11:43:47Z][pid=3152] production delta (финально): new=0 changed=0 removed=0
[2026-11-21T11:43:47Z][pid=3152] Режим: printflow
[2026-11-21T11:43:47Z][pid=3152] UTC: 2026-11-21T11:43:47Z
[2026-11-21T11:43:47Z][pid=3152] PID harness: 3152
[2026-11-21T11:43:47Z][pid=3152] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-21T11:43:47Z][pid=3152] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\8e7cc56dd2d6425c8502564af331a4b6
[2026-11-21T11:43:47Z][pid=3152] createDemoData=false
[2026-11-21T11:43:47Z][pid=3152] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-21T11:43:47Z][pid=3152] F5: True; F6: True; неожиданных окон: False
[2026-11-21T11:43:47Z][pid=3152] harness: exit=0 killed=False; cleanup: True; profile safety: True
[2026-11-21T11:43:47Z][pid=3152] LOCK RELEASE: released=True disposed=True
[2026-11-21T11:43:47Z][pid=3152] HARNESS NOT EXITED: False
[2026-11-21T11:43:47Z][pid=3152] неудачи очистки: нет
[2026-11-21T11:43:47Z][pid=3152] FAULT: нет
[2026-11-21T11:43:47Z][pid=3152] EXPECTED FINAL EXIT: 0
[2026-11-21T11:43:47Z][pid=3152] FINAL VERDICT: PASS
