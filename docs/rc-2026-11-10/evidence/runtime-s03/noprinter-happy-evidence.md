# No-printer runtime-доказательство (happy path)

Прогон: 2026-11-10 23:47:23, профиль: временный (guid), нет принтера.

[2026-11-10T20:47:13Z] sentinel before: 9B3BF49B5E29703DF1968486024A6BA9B29774160792144D9526D0F4169B6E0C|639299404335986319
[2026-11-10T20:47:13Z] production profile files before: 4
[2026-11-10T20:47:13Z] spooler jobs before: 0
[2026-11-10T20:47:13Z] temp profile: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\2136a09496464559b4394d7a7ed044f2
[2026-11-10T20:47:14Z] harness READY: READY 2819816 44468
[2026-11-10T20:47:14Z] guard line: GUARD PASS pid=44468 root=C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\2136a09496464559b4394d7a7ed044f2\Picket302 process=C:\Users\Nachfin\p302-worktrees\rc-20261110\tools\ui-acceptance\bin\x64\Release\net8.0-windows\UiAcceptance.exe
[2026-11-10T20:47:14Z] profile line: PROFILE C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\2136a09496464559b4394d7a7ed044f2\Picket302
[2026-11-10T20:47:16Z] OK: гард профиля отчитался (pid + корень в stdout)
[2026-11-10T20:47:17Z] получатель выбран (строка 1 адресной книги)
[2026-11-10T20:47:17Z] Карточка отправления появилась: hwnd=29231944
[2026-11-10T20:47:17Z] Карточка: нажата ОК
[2026-11-10T20:47:18Z] текст сообщения: 'Принтер по умолчанию не установлен'
[2026-11-10T20:47:18Z] скриншот сообщения: C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\rc-2026-11-10\evidence\runtime-s03\60-no-printer-warning.png (3978 байт)
[2026-11-10T20:47:18Z] OK: сообщение называет отсутствие принтера по умолчанию
[2026-11-10T20:47:18Z] OK: скриншот сообщения создан
[2026-11-10T20:47:18Z] OK: кнопка ОК найдена
[2026-11-10T20:47:20Z] OK: сообщение закрыто (окно исчезло)
[2026-11-10T20:47:22Z] OK: подача конвертов не открылась
[2026-11-10T20:47:22Z] OK: предпросмотр не открылся
[2026-11-10T20:47:22Z] UI отвечает после сообщения (WM_NULL): True
[2026-11-10T20:47:22Z] отправлений после F5: 1
[2026-11-10T20:47:22Z] OK: отправление создано (данные не потеряны)
[2026-11-10T20:47:23Z] остановка: graceful=True killed=False pid=44468 TreeOk=True остатки=[]
[2026-11-10T20:47:23Z] harness exit code: 0; killed: False; HarnessNotExited: False
[2026-11-10T20:47:23Z] spooler jobs after: 0
[2026-11-10T20:47:23Z] OK: заданий в спулере не появилось
[2026-11-10T20:47:23Z] sentinel unchanged: True
[2026-11-10T20:47:23Z] production delta: new=0 changed=0 removed=0
[2026-11-10T20:47:23Z] условие [гард профиля в stdout]: True
[2026-11-10T20:47:23Z] условие [сообщение показано]: True
[2026-11-10T20:47:23Z] условие [сообщение о принтере по умолчанию]: True
[2026-11-10T20:47:23Z] условие [сообщение закрывается]: True
[2026-11-10T20:47:23Z] условие [отправление создано (данные не потеряны)]: True
[2026-11-10T20:47:23Z] условие [спулер не изменился]: True
[2026-11-10T20:47:23Z] условие [временный профиль удалён]: True
[2026-11-10T20:47:23Z] условие [sentinel совпал]: True
[2026-11-10T20:47:23Z] условие [новых файлов реального профиля нет]: True
[2026-11-10T20:47:23Z] условие [изменённых файлов реального профиля нет]: True
[2026-11-10T20:47:23Z] условие [удалённых файлов реального профиля нет]: True
[2026-11-10T20:47:23Z] условие [harness exit 0]: True
[2026-11-10T20:47:23Z] условие [harness не убит]: True
[2026-11-10T20:47:23Z] условие [harness завершён (bounded)]: True
[2026-11-10T20:47:23Z] условие [нет FAIL в stdout]: True
[2026-11-10T20:47:23Z] условие [дерево процессов чисто]: True
[2026-11-10T20:47:23Z] условие [нет живых потомков harness]: True
[2026-11-10T20:47:23Z] production delta (финально): new=0 changed=0 removed=0
[2026-11-10T20:47:23Z] Режим: noprinter
[2026-11-10T20:47:23Z] UTC: 2026-11-10T20:47:23Z
[2026-11-10T20:47:23Z] PID harness: 44468
[2026-11-10T20:47:23Z] реальный профиль: C:\Users\Nachfin\AppData\Roaming\Picket302
[2026-11-10T20:47:23Z] временный профиль: C:\Users\Nachfin\AppData\Local\Temp\Picket302Acceptance\2136a09496464559b4394d7a7ed044f2
[2026-11-10T20:47:23Z] FINAL DELTA: new=0 changed=0 removed=0
[2026-11-10T20:47:23Z] LOCK RELEASE: released=True disposed=True
[2026-11-10T20:47:23Z] HARNESS NOT EXITED: False
[2026-11-10T20:47:23Z] неудачи очистки: нет
[2026-11-10T20:47:23Z] EXPECTED FINAL EXIT: 0
[2026-11-10T20:47:23Z] FINAL VERDICT: PASS
