# Stage 5 (verify) — итоговый отчёт независимой верификации и доводки

> Исторический отчёт ветки `piscine/picket302-stage5` на SHA `8aef10d`.
> После объединения с UI-серией актуальный статус, тесты и ограничения
> находятся в `../stage5/INTEGRATED-FINAL-REPORT.md`; число 66,7 % ниже не
> является пересчётом интегрированной версии.

Дата: 2026-11-10. Исполнитель: агент-сессия «stage5-verify».
Форма отчёта — по разделам задания. Все доказательства — в каталоге
`docs/ux-audit-2026-11-10/stage5-verify/` (00–07 + evidence), ключевые
машиночитаемые артефакты дублированы там же.

## 1. STATUS

```text
STATUS: READY_WITH_LIMITATIONS
```

Ограничения (точные):
1. Общий визуальный процент измерен по объявленной методике на полном
   охвате взвешенного набора (9 поверхностей с реальными референсами) и
   составил **66.7 % < 70 %** (06-visual-matrix.md). Дефицит дают четыре
   расхождения с референсом (R5 быстрые фильтры, R6 механизм логотипа,
   R4 опции поиска, R2 комплектность листа в стенде) — три из них
   требуют продуктового решения владельца и/или правок MainShell.cs,
   которую одновременно правит параллельная сессия.
2. COM: холодная активация и контрактные вызовы доказаны (5/5 + UNC),
   но полный acceptance-сценарий `PrintEnvelope` через COM не выполнялся:
   `AcceptanceScenarioTests` перемещает production-БД (запрещено), а БД в
   профиле отсутствует.
3. Реальный OS-DPI 125/150 % не проверялся на хосте (пользователь за ПК,
   стенда нет): `OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST`.
4. UI-клик «Печать», доводящий задание до spooler из диалога, не
   выполнялся (интерактивные порты обоих кандидатов украли бы фокус
   пользователя); сегмент «spooler→очередь→файл» доказан app-level
   пробой штатным спулером.

## 2. Git

| Параметр | Значение |
|---|---|
| Исходный SHA | `b9e280346ec84d752a07bc68c56c9d2a70c17e3b` (= origin/main на старт, проверено fetch) |
| Ветка работы | `piscine/picket302-stage5`, изолированный worktree `C:\Users\Nachfin\p302-worktrees\stage5-193008` |
| Коммиты | `6e86031` fix(ui) V-01 + тест; `3f3a21c` feat(acceptance) драйверы/команды; `ef67e36` docs(stage5-verify); последний коммит ветки — настоящий отчёт (см. `git log`) |
| origin/main | не изменялся (`b9e2803`) |
| merge / push / PR | **не выполнялись** (подтверждаю) |

Общий checkout `Z:\Picket302` возвращён на `main`; чужие незакоммиченные
изменения параллельной сессии (MainShell.cs, EnvelopeUiTheme.cs,
Program.cs, run-screens.ps1, тесты, docs/stage5/) не тронуты.

## 3. Таблица дефектов

| ID | Серьёзность | Воспроизведение | Доказанная причина | Исправление | Тест | Runtime-доказательство | Остаточный риск |
|---|---|---|---|---|---|---|---|
| V-01 | средняя (визуальный) | «Настройка вида таблиц»: метка «Высота строки, px:» на 23 px ниже своего поля; снимки 2026-11-03, 2026-11-10, мой 07-grid-appearance.png | неявные строки TableLayoutPanel разносили метку и NumericUpDown по разным полосам | явные координаты пары в контейнере вкладки (`FormGridAppearance.cs`) | `FormGridAppearanceLayoutTests` 4/4 (красный до фикса: расхождение 23 px) | `07-grid-appearance-after-fix.png` (метка и поле на одной линии) | низкий |
| D-COM-STATE | диагностика, не дефект кода | Stage4: холодный CoCreateInstance 0x80040154 за ~43 мс без запуска сервера | загрязнение состояния SCM после жёстких kill без CoRevokeClassObject (механизм описан в комментарии `ComServerHost.ShutdownEventName`); из документированного чистого состояния не воспроизводится: 8/8 циклов PASS (raw+managed+UNC) | изменений кода не требуется; регламент «kill = провал» и per-PID shutdown-событие уже в коде | приёмка 5× (`acceptance-5x.json`) | дельты журнала с -Embedding/CoRegisterClassObject per PID, DCOM-событий нет | ретроспективно конкретное загрязнение Stage4 не идентифицируется |
| G1–G4 | решения владельца | см. 06-visual-matrix.md «Расхождения, требующие решения» | дизайн-расхождения с референсом, не дефекты | — | — | — | — |

## 4. Визуальная матрица

Полностью: `06-visual-matrix.md`. Итог: 9 взвешенных пар (R1 71, R2 59,
R3 75, R4 63, R5 55, R6 56, R7 72, R8 80, R9 69; веса 5/3/1/1/1/2/2/1/3)
→ **OVERALL = 66.7 %**, охват взвешенного набора 9/9 пар с реальными
референсами; ещё ~26 поверхностей — NOT_SCORED (нет пиксельного
референса), структурно проверены (включая впервые снятые вкладки
«Конверты»/«Рассылки»). Пиксельная оценка одного главного окна
(18/20=90, stage3) в общий процент не входит.

## 5. COM

- Ключи: ProgID `PEPro.Application`, CLSID `{5715286E-F98F-4C1C-AC86-AB943AFAE004}`, AppID=CLSID, LocalServer32 (кавычки + ServerExecutable), Interface/TypeLib 2.2 — регистрация создавалась и удалялась только моими циклами в HKCU.
- Клиенты: raw `CoCreateInstance` CLSCTX_LOCAL_SERVER/CLSCTX_ALL (x64), managed `Activator.CreateInstance(GetTypeFromProgID/CLSID)` (x64), IDispatch-вызовы GetAPIVersion/GetProductVersion/DatabaseIsLoaded (HRESULT 0).
- Холодные циклы: run2 (104 мс), run3 managed (87 мс), unc-true (133 мс, LocalServer32 = UNC), acc1–acc5 (приёмка): каждый — новый PID (19504, 5688, 12636, 64236, 56992), свежая дельта `-Embedding` (cwd=`C:\WINDOWS\system32` — запуск SCM), `CoRegisterClassObject=0x00000000`, успешный CreateInstance, безопасные вызовы, штатный `CoRevokeClassObject` (дельта журнала), GRACEFUL_EXIT_0, 0 процессов, ключи удалены, temp удалён.
- Дельта событий Microsoft-Windows-DistributedCOM после каждого цикла: пуста. Постороннее событие 10010 (18:32:41) принадлежит CLSID `{F576B2F9-…}` — не приложению.
- SCM-трасса: ProcMon недоступен; трасса запуска SCM доказана дельтой журнала (`-Embedding`, cwd system32) + появлением нового PID; ETW не потребовался (отказов не было).
- Флаги: DIRECT_COM_SERVER_VERIFIED=true; COLD_COM_ACTIVATION_VERIFIED=true; COM_VERIFIED=PARTIAL (граница — PrintEnvelope через COM, см. §1.2).

## 6. Печать

- Точные имена: `Microsoft Print to PDF` (проба PrintToFile), `PDF-XChange 5.0 for ABBYY` (по умолчанию, виртуальный; не использовался для вывода из-за диалогового порта), физических принтеров нет.
- Engine-level: PDF 89 347 B, `%PDF-1.7`, sha256 A4B9A70F…; очереди до/после 0; FG не менялся.
- App-level (штатный `SystemDrawingPrintSpooler`, 2 страницы): задание в очереди `Microsoft Print to PDF` id3 doc=[PICKET302-STAGE5-SPOOL-PROBE] Spooling→Printing,Retained→завершено; PDF 1 199 B `%PDF-1.7` sha256 F81C0D11…; остальные очереди 0; FG не менялся.
- UI-цепочки до spooler: 8 драйверов PASS (spooler jobs 0→0 в каждом).
- Физическая печать: не выполнялась и не заявляется
  (PHYSICAL_PRINT_VERIFIED=false).

## 7. Функциональная матрица (сводка; детали — evidence драйверов и 1374 теста)

| Сценарий | Действие | Результат UI | БД/файл | Тест/драйвер | Статус |
|---|---|---|---|---|---|
| Пустая книга | запуск | главное окно, COUNTS=0 | изолированный профиль | run-emptybook | PASS |
| Снимки 10 поверхностей | SHOW 6 диалогов + main 2 размера | окна открыты/сняты | — | run-screens | PASS |
| S1–S8 (sent, F5/F6/F7, шаблон не выбран, блокировка бумаги, клипарт, сброс вида, бланки) | цепочки с fault-injection | guard'ы и блокировки сработали | mails создаётся только в успешной цепочке | run-screens2 | PASS |
| Получатель без адреса | F5/F6 | MessageBox «Печать», предпросмотр не открыт | MAILCOUNT=0 | run-noaddress | PASS |
| Нет принтера | F5 | MessageBox «Принтер по умолчанию не установлен» | данные не потеряны | run-noprinter | PASS |
| Предпросмотр F6 | цепочка до preview | PREVIEWSCAN непустой | spooler 0 | run-previewflow | PASS |
| Печать F5/F6 до диалога | цепочка | диалоги открыты/закрыты | spooler 0 | run-printflow | PASS |
| DPI 125/150/100 | SETDPI (WM_DPICHANGED) | LAYOUTCHECK вне=0, нулевых=0 | — | run-dpi | PASS |
| Вкладки 1/2 (seeded+empty) | SETTAB | сняты впервые | — | run-stage5-tabs | PASS |
| Меню «Печать», «Фильтры», Поиск, Архивация | PRINTMENU/FILTERMENU/SHOW | сняты | — | run-stage5-menu/extra | PASS |
| Полный лист предпросмотра | F6-цепочка + resize | лист целиком | spooler 0 | run-stage5-preview | PASS |
| CRUD/импорт/экспорт/отчёты/ошибки подключения | unit/integration | — | SQLite-проверки в тестах | 1374 non-COM | PASS |

## 8. DPI

Симуляция 96/120/144/96 через реальный WM_DPICHANGED: PASS (07-dpi.md).
Реальный OS-DPI: NOT_RUN_ON_HOST (причина в 07-dpi.md).

## 9. Команды, результаты, покрытие

```text
dotnet build Picket302.sln -c Debug            → 0 ошибок / 0 предупреждений
dotnet build Picket302.sln -c Release          → 0 ошибок / 0 предупреждений
dotnet build tools/ui-acceptance/… -c Release  → 0 ошибок
dotnet test … -c Debug   --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests" → 1374/1374 PASS
dotnet test … -c Release --no-build --filter (тот же)                            → 1374/1374 PASS
SKIP/исключены: AcceptanceScenarioTests (весь класс, небезопасная изоляция БД),
  Picket302.Tests.ComAutomation.* (фильтр non-COM), Printing.PrintEngineTests
  (исторический консервативный scope)
coverage: dotnet test … --collect:"XPlat Code Coverage" → CODE_COVERAGE_MEASURED=true:
  line 64.57 % (32632/50533), branch 52.59 % (7479/14221); по проектам — 05-builds-tests-coverage.md
```

## 10. Проверка чистоты

- Production-профиль: все прогоны драйверов — `new=0 changed=0 removed=0` (sentinel совпал); COM-циклы — единственное изменение append `com-automation.log` (ожидаемое, задокументировано), connection.json/print_audit.log байт-в-байт, `picket302.db` не создан; итоговый fingerprint = baseline + лог.
- HKCU COM-ключи: после каждого цикла удалены (progIdGone/clsidGone/appIdGone=True); на конец сессии регистрация отсутствует.
- Процессы: своих зависших нет (все остановки graceful, TreeOk, kill не применялся); чужие процессы не завершались.
- Temp: все мои каталоги (`p302-stage5-com-*`, UNC-tmp) удалены; scratch-каталог сессии оставлен до review (путь в §11).
- Spooler: 0 заданий во всех очередях после всех проверок.
- `git diff --check` — чисто; `git status` worktree — чисто после коммитов; общий checkout: только чужие изменения и исторические untracked (не тронуты).

## 11. Файлы и evidence

- Worktree: `C:\Users\Nachfin\p302-worktrees\stage5-193008`, ветка `piscine/picket302-stage5`.
- Документация и evidence: `docs/ux-audit-2026-11-10/stage5-verify/` (00–08, evidence/runtime 45 файлов, evidence/com 15, evidence/print 4).
- Scratch (до review): `C:\Users\Nachfin\AppData\Local\Temp\picket302-stage5-20261110-184530\` (baseline.json, com-evidence/ включая полные JSON циклов, coverage.cobertura.xml, исходники проб comprobe/printprobe/spoolprobe, скрипты).

## 12. Открытые вопросы владельцу

1. Координация двух stage5-сессий: порядок слияния веток и разбор конфликта `tools/ui-acceptance/Program.cs`/`MainShell.cs` (моя ветка добавляет PRINTMENU/FILTERMENU/SHOW search|backup; параллельная — свои команды и UI-фиксы).
2. R5: вводить быстрые чек-фильтры групп/избранного (воронка над таблицей) или сохранить только сохранённые фильтры + дерево категорий.
3. R6: собственный клипарт-пикер против произвольного компонента картинки в редакторе бланков (механизм референса).
4. R4: расширять ли «Поиск» опциями референса («Поиск в», «Совпадение», «С учетом регистра»).
5. Переработка изоляции `AcceptanceScenarioTests` (перемещение production-БД) — санкционировать запуск или утвердить редизайн механизма; до этого COM-печать остаётся PARTIAL.
6. Лицензия/источник ZIPcode.ttf для кодового штампа (копии только в D:\KonvertProNet — не копировались).
7. Стенд или согласованное окно времени для реального OS-DPI 125/150 %.
8. Санкционировать ли окно риска фокуса для UI-клика «Печать» до spooler (либо права администратора на принтер с FILE-портом) — иначе граница §1.4 постоянна.

## 13. Флаги

```text
SOURCE_VERIFIED=true
BUILD_DEBUG_VERIFIED=true
BUILD_RELEASE_VERIFIED=true
TEST_DEBUG_VERIFIED=true (1374/1374 non-COM)
TEST_RELEASE_VERIFIED=true (1374/1374 non-COM)
UI_VISUAL_VERIFIED=true (8 драйверов stage4 + 4 новых, все PASS, неактивирующе)
REFERENCE_VISUAL_VERIFIED=true (9 пар с реальными референсами CHM/runtime)
OVERALL_70_PERCENT_MEASURED=false (измерено 66.7 % по объявленной методике, охват 9/9 взвешенных пар)
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST (пользователь за ПК, стенда нет)
PROFILE_SAFETY_VERIFIED=true (дельта 0/0/0 у драйверов; у COM-циклов — только append com-automation.log)
DIRECT_COM_SERVER_VERIFIED=true
COLD_COM_ACTIVATION_VERIFIED=true (5/5 приёмочных + 3 предварительных, включая UNC)
COM_VERIFIED=PARTIAL (PrintEnvelope через COM не выполнялся, §1.2)
VIRTUAL_PRINT_VERIFIED=true (engine PrintToFile + app-spooler→очередь→PDF)
PHYSICAL_PRINT_VERIFIED=false (физических принтеров нет)
CODE_COVERAGE_MEASURED=true (line 64.57 %, branch 52.59 %)
```
