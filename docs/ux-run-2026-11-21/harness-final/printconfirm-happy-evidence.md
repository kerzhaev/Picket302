# F5/F6 runtime-доказательство (happy path)

Прогон: 2026-11-21 15:49:32, профиль: временный (guid), createDemoData=false.

[2026-11-21T12:49:01Z][pid=0] sentinel before: 57EC991330C3B009E7E4618EC980B43AB0AB1667D1533131C8A312A8C06F80EC|639308621409378408
[2026-11-21T12:49:01Z][pid=0] production profile files before: 6
[2026-11-21T12:49:01Z][pid=0] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\d749d86f7d624687a43954bd1028574e
[2026-11-21T12:49:04Z][pid=53328] harness READY: READY 12716644 53328
[2026-11-21T12:49:07Z][pid=53328] PRINTTOFILE хук установлен: Z:\Picket302\docs\ux-run-2026-11-21\harness-final\rc-s17-f5-printtofile.pdf
[2026-11-21T12:49:07Z][pid=53328] SEEDTEMPLATEPRINT: шаблон C6 162x114 (печать) засеян
[2026-11-21T12:49:07Z][pid=53328] SELECT подтверждён stdout: OK SELECT
[2026-11-21T12:49:07Z][pid=53328] ## F5 «Печать текущего конверта»
[2026-11-21T12:49:07Z][pid=53328] Карточка отправления появилась: hwnd=13568630 title='Карточка отправления'
[2026-11-21T12:49:08Z][pid=53328] скриншот карточки: 20-карточка-отправления-f5.png
[2026-11-21T12:49:08Z][pid=53328] Карточка: нажата ОК (BM_CLICK)
[2026-11-21T12:49:08Z][pid=53328] Диалог «Печать» появился: hwnd=13634166
[2026-11-21T12:49:09Z][pid=53328] скриншот диалога печати: 21-диалог-печати-f5.png
[2026-11-21T12:49:09Z][pid=53328] Диалог «Печать»: принтер переключён на «Microsoft Print to PDF — Ready»
[2026-11-21T12:49:09Z][pid=53328] Диалог «Печать»: нажата Печать — контролируемое задание (PrintToFile)
[2026-11-21T12:49:10Z][pid=53328] итерация подтверждения печати: 1
[2026-11-21T12:49:10Z][pid=53328] Подача конвертов появилась: hwnd=13699702
[2026-11-21T12:49:10Z][pid=53328] Подача конвертов: нажата Продолжить
[2026-11-21T12:49:11Z][pid=53328] Предпросмотр печати появился: hwnd=18287222
[2026-11-21T12:49:12Z][pid=53328] Предпросмотр: нажата Печать — задание уходит в спулер
[2026-11-21T12:49:20Z][pid=53328] Конверт слишком большой: не показан за 8 с
[2026-11-21T12:49:28Z][pid=53328] Проверка размера бумаги: не показана — размер прошёл
[2026-11-21T12:49:29Z][pid=53328] OK: печатный PDF-файл создан и стабилен
[2026-11-21T12:49:29Z][pid=53328] PDF magic: %PDF- size=803
[2026-11-21T12:49:29Z][pid=53328] OK: PDF magic корректен (%PDF-)
[2026-11-21T12:49:29Z][pid=53328] PDF sha256: 157F5C867C6CA09487B45A0596F23EB17CDF4E061D9B40F710C04A0C392F7BAB
[2026-11-21T12:49:31Z][pid=53328] spooler jobs after print: 0
[2026-11-21T12:49:31Z][pid=53328] OK: в очереди спулера не осталось заданий
[2026-11-21T12:49:31Z][pid=53328] UI отвечает после F5 (WM_NULL): True
[2026-11-21T12:49:31Z][pid=53328] FLOW F5 PASS: True
[2026-11-21T12:49:31Z][pid=53328] PRINTTOFILE off — хук снят после контролируемой печати
[2026-11-21T12:49:32Z][pid=53328] остановка: graceful=True killed=False pid=53328 TreeOk=True TreeConfirmed=True остатки=[] неподтверждённые=[] taskkill= ошибка: 
[2026-11-21T12:49:32Z][pid=53328] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-21T12:49:32Z][pid=53328] stdout: OK SELECT=True OK F5=True OK F6=False FAIL-строк=0
[2026-11-21T12:49:32Z][pid=53328] temp profile cleaned: True
[2026-11-21T12:49:32Z][pid=53328] sentinel unchanged: True
[2026-11-21T12:49:32Z][pid=53328] production delta: new=0 changed=0 removed=0
[2026-11-21T12:49:32Z][pid=53328] OK: SELECT подтверждён stdout (OK SELECT)
[2026-11-21T12:49:32Z][pid=53328] OK: нет FAIL-строк в stdout harness
[2026-11-21T12:49:32Z][pid=53328] информационно: OK F5=True OK F6=False в stdout (async void после ShowDialog)
[2026-11-21T12:49:32Z][pid=53328] F5-цепочка: True; F6-цепочка: True
[2026-11-21T12:49:32Z][pid=53328] неожиданных окон: False; UI после F5: True; UI после F6: True
[2026-11-21T12:49:32Z][pid=53328] harness: exit=0 killed=False -> True
[2026-11-21T12:49:32Z][pid=53328] cleanup: True; profile safety: True
[2026-11-21T12:49:32Z][pid=53328] дерево: ok=True остатки=0 неподтверждённые=0 ошибка=''
[2026-11-21T12:49:32Z][pid=53328] сценарий (все флаги): True
[2026-11-21T12:49:32Z][pid=53328] production delta (финально): new=0 changed=0 removed=0
[2026-11-21T12:49:32Z][pid=53328] Режим: printflow
[2026-11-21T12:49:32Z][pid=53328] UTC: 2026-11-21T12:49:32Z
[2026-11-21T12:49:32Z][pid=53328] PID harness: 53328
[2026-11-21T12:49:32Z][pid=53328] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-21T12:49:32Z][pid=53328] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\d749d86f7d624687a43954bd1028574e
[2026-11-21T12:49:32Z][pid=53328] createDemoData=false
[2026-11-21T12:49:32Z][pid=53328] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-21T12:49:32Z][pid=53328] F5: True; F6: True; неожиданных окон: False
[2026-11-21T12:49:32Z][pid=53328] harness: exit=0 killed=False; cleanup: True; profile safety: True
[2026-11-21T12:49:32Z][pid=53328] LOCK RELEASE: released=True disposed=True
[2026-11-21T12:49:32Z][pid=53328] HARNESS NOT EXITED: False
[2026-11-21T12:49:32Z][pid=53328] неудачи очистки: нет
[2026-11-21T12:49:32Z][pid=53328] FAULT: нет
[2026-11-21T12:49:32Z][pid=53328] EXPECTED FINAL EXIT: 0
[2026-11-21T12:49:32Z][pid=53328] FINAL VERDICT: PASS
