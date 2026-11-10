# No-printer runtime-доказательство (happy path)

Прогон: 2026-11-10 20:40:49, профиль: временный (guid), нет принтера.

[2026-11-10T17:40:39Z] sentinel before: D2EEA00F477963921B47E71F6D2FFEFA2BA745FFD5E217B1B9B8D5A5BAEE7F6F|639299292398208707
[2026-11-10T17:40:39Z] production profile files before: 4
[2026-11-10T17:40:40Z] spooler jobs before: 0
[2026-11-10T17:40:40Z] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\310ce6a06b164f99b0f2a3d82667a859
[2026-11-10T17:40:41Z] harness READY: READY 1051626 124004
[2026-11-10T17:40:41Z] guard line: GUARD PASS pid=124004 root=C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\310ce6a06b164f99b0f2a3d82667a859\Picket302 process=C:\Users\Nachfin\p302-worktrees\stage5-193008\tools\ui-acceptance\bin\x64\Release\net8.0-windows\UiAcceptance.exe
[2026-11-10T17:40:41Z] profile line: PROFILE C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\310ce6a06b164f99b0f2a3d82667a859\Picket302
[2026-11-10T17:40:43Z] OK: гард профиля отчитался (pid + корень в stdout)
[2026-11-10T17:40:43Z] получатель выбран (строка 1 адресной книги)
[2026-11-10T17:40:43Z] Карточка отправления появилась: hwnd=3803894
[2026-11-10T17:40:43Z] Карточка: нажата ОК
[2026-11-10T17:40:44Z] текст сообщения: 'Принтер по умолчанию не установлен'
[2026-11-10T17:40:45Z] скриншот сообщения: C:\Users\Nachfin\AppData\Local\Temp\picket302-stage5-20261110-184530\runtime-evidence\60-no-printer-warning.png (3978 байт)
[2026-11-10T17:40:45Z] OK: сообщение называет отсутствие принтера по умолчанию
[2026-11-10T17:40:45Z] OK: скриншот сообщения создан
[2026-11-10T17:40:45Z] OK: кнопка ОК найдена
[2026-11-10T17:40:47Z] OK: сообщение закрыто (окно исчезло)
[2026-11-10T17:40:49Z] OK: подача конвертов не открылась
[2026-11-10T17:40:49Z] OK: предпросмотр не открылся
[2026-11-10T17:40:49Z] UI отвечает после сообщения (WM_NULL): True
[2026-11-10T17:40:49Z] отправлений после F5: 1
[2026-11-10T17:40:49Z] OK: отправление создано (данные не потеряны)
[2026-11-10T17:40:49Z] остановка: graceful=True killed=False pid=124004 TreeOk=True остатки=[]
[2026-11-10T17:40:49Z] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-10T17:40:49Z] spooler jobs after: 0
[2026-11-10T17:40:49Z] OK: заданий в спулере не появилось
[2026-11-10T17:40:49Z] sentinel unchanged: True
[2026-11-10T17:40:49Z] production delta: new=0 changed=0 removed=0
[2026-11-10T17:40:49Z] условие [гард профиля в stdout]: True
[2026-11-10T17:40:49Z] условие [сообщение показано]: True
[2026-11-10T17:40:49Z] условие [сообщение о принтере по умолчанию]: True
[2026-11-10T17:40:49Z] условие [сообщение закрывается]: True
[2026-11-10T17:40:49Z] условие [отправление создано (данные не потеряны)]: True
[2026-11-10T17:40:49Z] условие [спулер не изменился]: True
[2026-11-10T17:40:49Z] условие [временный профиль удалён]: True
[2026-11-10T17:40:49Z] условие [sentinel совпал]: True
[2026-11-10T17:40:49Z] условие [новых файлов реального профиля нет]: True
[2026-11-10T17:40:49Z] условие [изменённых файлов реального профиля нет]: True
[2026-11-10T17:40:49Z] условие [удалённых файлов реального профиля нет]: True
[2026-11-10T17:40:49Z] условие [harness exit 0]: True
[2026-11-10T17:40:49Z] условие [harness не убит]: True
[2026-11-10T17:40:49Z] условие [harness завершён (bounded)]: True
[2026-11-10T17:40:49Z] условие [нет FAIL в stdout]: True
[2026-11-10T17:40:49Z] условие [дерево процессов чисто]: True
[2026-11-10T17:40:49Z] условие [нет живых потомков harness]: True
[2026-11-10T17:40:49Z] production delta (финально): new=0 changed=0 removed=0
[2026-11-10T17:40:49Z] Режим: noprinter
[2026-11-10T17:40:49Z] UTC: 2026-11-10T17:40:49Z
[2026-11-10T17:40:49Z] PID harness: 124004
[2026-11-10T17:40:49Z] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-10T17:40:49Z] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\310ce6a06b164f99b0f2a3d82667a859
[2026-11-10T17:40:49Z] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-10T17:40:49Z] LOCK RELEASE: released=True disposed=True
[2026-11-10T17:40:49Z] HARNESS NOT EXITED: False
[2026-11-10T17:40:49Z] неудачи очистки: нет
[2026-11-10T17:40:49Z] EXPECTED FINAL EXIT: 0
[2026-11-10T17:40:49Z] FINAL VERDICT: PASS
