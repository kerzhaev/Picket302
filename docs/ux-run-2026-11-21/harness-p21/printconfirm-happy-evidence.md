# F5/F6 runtime-доказательство (happy path)

Прогон: 2026-11-21 16:43:56, профиль: временный (guid), createDemoData=false.

[2026-11-21T13:43:25Z][pid=0] sentinel before: 86414B03A1B002BD691239C6D661B47262B3328E8203D95D3072F3591352AF53|639308654057789253
[2026-11-21T13:43:25Z][pid=0] production profile files before: 6
[2026-11-21T13:43:25Z][pid=0] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\a0b0e48f1b544841bdff1ec81bc434b3
[2026-11-21T13:43:29Z][pid=34764] harness READY: READY 458826 34764
[2026-11-21T13:43:31Z][pid=34764] PRINTTOFILE хук установлен: Z:\Picket302\docs\ux-run-2026-11-21\harness-p21\rc-s17-f5-printtofile.pdf
[2026-11-21T13:43:31Z][pid=34764] SEEDTEMPLATEPRINT: шаблон C6 162x114 (печать) засеян
[2026-11-21T13:43:31Z][pid=34764] SELECT подтверждён stdout: OK SELECT
[2026-11-21T13:43:31Z][pid=34764] ## F5 «Печать текущего конверта»
[2026-11-21T13:43:31Z][pid=34764] Карточка отправления появилась: hwnd=396102 title='Карточка отправления'
[2026-11-21T13:43:32Z][pid=34764] скриншот карточки: 20-карточка-отправления-f5.png
[2026-11-21T13:43:32Z][pid=34764] Карточка: нажата ОК (BM_CLICK)
[2026-11-21T13:43:32Z][pid=34764] Диалог «Печать» появился: hwnd=461638
[2026-11-21T13:43:33Z][pid=34764] скриншот диалога печати: 21-диалог-печати-f5.png
[2026-11-21T13:43:34Z][pid=34764] Диалог «Печать»: принтер переключён на «Microsoft Print to PDF — Ready»
[2026-11-21T13:43:34Z][pid=34764] Диалог «Печать»: нажата Печать — контролируемое задание (PrintToFile)
[2026-11-21T13:43:35Z][pid=34764] итерация подтверждения печати: 1
[2026-11-21T13:43:35Z][pid=34764] Подача конвертов появилась: hwnd=527174
[2026-11-21T13:43:35Z][pid=34764] Подача конвертов: нажата Продолжить
[2026-11-21T13:43:35Z][pid=34764] Предпросмотр печати появился: hwnd=5114694
[2026-11-21T13:43:36Z][pid=34764] Предпросмотр: нажата Печать — задание уходит в спулер
[2026-11-21T13:43:44Z][pid=34764] Конверт слишком большой: не показан за 8 с
[2026-11-21T13:43:52Z][pid=34764] Проверка размера бумаги: не показана — размер прошёл
[2026-11-21T13:43:53Z][pid=34764] OK: печатный PDF-файл создан и стабилен
[2026-11-21T13:43:53Z][pid=34764] PDF magic: %PDF- size=803
[2026-11-21T13:43:53Z][pid=34764] OK: PDF magic корректен (%PDF-)
[2026-11-21T13:43:53Z][pid=34764] PDF sha256: 335874EC73FD4A73C83C7B580555D62851D005FA15F29EC1A1B67A9CB991D7F9
[2026-11-21T13:43:55Z][pid=34764] spooler jobs after print: 0
[2026-11-21T13:43:55Z][pid=34764] OK: в очереди спулера не осталось заданий
[2026-11-21T13:43:55Z][pid=34764] UI отвечает после F5 (WM_NULL): True
[2026-11-21T13:43:55Z][pid=34764] FLOW F5 PASS: True
[2026-11-21T13:43:55Z][pid=34764] PRINTTOFILE off — хук снят после контролируемой печати
[2026-11-21T13:43:56Z][pid=34764] остановка: graceful=True killed=False pid=34764 TreeOk=True TreeConfirmed=True остатки=[] неподтверждённые=[] taskkill= ошибка: 
[2026-11-21T13:43:56Z][pid=34764] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-21T13:43:56Z][pid=34764] stdout: OK SELECT=True OK F5=True OK F6=False FAIL-строк=0
[2026-11-21T13:43:56Z][pid=34764] temp profile cleaned: True
[2026-11-21T13:43:56Z][pid=34764] sentinel unchanged: True
[2026-11-21T13:43:56Z][pid=34764] production delta: new=0 changed=0 removed=0
[2026-11-21T13:43:56Z][pid=34764] OK: SELECT подтверждён stdout (OK SELECT)
[2026-11-21T13:43:56Z][pid=34764] OK: нет FAIL-строк в stdout harness
[2026-11-21T13:43:56Z][pid=34764] информационно: OK F5=True OK F6=False в stdout (async void после ShowDialog)
[2026-11-21T13:43:56Z][pid=34764] F5-цепочка: True; F6-цепочка: True
[2026-11-21T13:43:56Z][pid=34764] неожиданных окон: False; UI после F5: True; UI после F6: True
[2026-11-21T13:43:56Z][pid=34764] harness: exit=0 killed=False -> True
[2026-11-21T13:43:56Z][pid=34764] cleanup: True; profile safety: True
[2026-11-21T13:43:56Z][pid=34764] дерево: ok=True остатки=0 неподтверждённые=0 ошибка=''
[2026-11-21T13:43:56Z][pid=34764] сценарий (все флаги): True
[2026-11-21T13:43:56Z][pid=34764] production delta (финально): new=0 changed=0 removed=0
[2026-11-21T13:43:56Z][pid=34764] Режим: printflow
[2026-11-21T13:43:56Z][pid=34764] UTC: 2026-11-21T13:43:56Z
[2026-11-21T13:43:56Z][pid=34764] PID harness: 34764
[2026-11-21T13:43:56Z][pid=34764] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-21T13:43:56Z][pid=34764] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\a0b0e48f1b544841bdff1ec81bc434b3
[2026-11-21T13:43:56Z][pid=34764] createDemoData=false
[2026-11-21T13:43:56Z][pid=34764] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-21T13:43:56Z][pid=34764] F5: True; F6: True; неожиданных окон: False
[2026-11-21T13:43:56Z][pid=34764] harness: exit=0 killed=False; cleanup: True; profile safety: True
[2026-11-21T13:43:56Z][pid=34764] LOCK RELEASE: released=True disposed=True
[2026-11-21T13:43:56Z][pid=34764] HARNESS NOT EXITED: False
[2026-11-21T13:43:56Z][pid=34764] неудачи очистки: нет
[2026-11-21T13:43:56Z][pid=34764] FAULT: нет
[2026-11-21T13:43:56Z][pid=34764] EXPECTED FINAL EXIT: 0
[2026-11-21T13:43:56Z][pid=34764] FINAL VERDICT: PASS
