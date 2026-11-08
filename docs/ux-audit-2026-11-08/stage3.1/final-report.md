ОТЧЁТ
STAGE3.1

1. Статус

**READY_WITH_LIMITATIONS**

Ограничения (не дефекты кода): физическая печать, OS-DPI 125/150 %, ZIPcode-
штамп, COM — вне зоны/блокеры владельца (см. §10–11); no-printer —
CONTRACT_ONLY (подменяемый провайдер); один открытый кодовый дефект LAY3-01
(P2, переполнение строки TLP, DPI-независим — см. defects.md); источник
ENV3-02 не установлен, контейнирование доказано.

2. Исходный SHA

`799f321` (main, = итог stage3; origin/main = aee4ea8)

3. Итоговый SHA

см. коммиты серии (push/merge не выполнялись — правила §3).

4. Ветка и Git-состояние

main, ahead of origin/main by 3 (stage3) + коммиты этой серии;
untracked: graphify-out/ (предсуществующий, не тронут);
чужой worktree C:/Users/Nachfin/.codex/worktrees/redesign-konvert-ui —
не тронут.

5. Список изменённых файлов

| Файл | Что | Зачем |
|---|---|---|
| `src/Picket302.Printing/Devices/PrinterManager.cs` | `GetDefaultPrinter` → virtual | подмена в сценарии «нет принтера» |
| `src/Picket302.Infrastructure/Registry/CurrentSenderStores.cs` | NEW `NullCurrentSenderStore` | контейнирование записи SenderId в реальный реестр (DRV31-01) |
| `src/Picket302.App/AppServices.cs` | параметры `службаПринтеров`, `реестрОтправителяИзолирован` в `СоздатьAsync`/приватный ctor | то же + подмена принтера |
| `tools/ui-acceptance/Program.cs` | режимы `screens2`/`noprinter`; команды SETTAB, SENTINFO, SEEDTEMPLATE, CLIPSELECT, GRIDRESET, SETDPI, LAYOUTCHECK, F7; `GUARD PASS`-журнал; изоляция реестра; WM_DPICHANGED-симуляция; без шаблона в демо-засеве | §6–8 ТЗ stage3.1 |
| `tools/ui-acceptance/UiAcceptance.csproj` | `ApplicationHighDpiMode=PerMonitorV2` | реальный конвейер DPI-рескала (DRV31-02) |
| `tools/ui-acceptance/run-noprinter.ps1` | NEW | §7.4 |
| `tools/ui-acceptance/run-screens2.ps1` | NEW | §7.1–7.3, 7.5, 7.8–7.10 |
| `tools/ui-acceptance/run-dpi.ps1` | NEW | Фаза 2 |
| `tests/Picket302.Tests/App/ContainmentIsolationTests.cs` | NEW, 5 кейсов | §12, ENV3-02 |
| `docs/ux-audit-2026-11-08/stage3.1/*` | документация серии | §15 |

6. Что исправлено

- DRV31-01: acceptance больше не пишет SenderId в реальный реестр
  (NullCurrentSenderStore; флаг производственного поведения сохранён).
- DRV31-02: harness на PerMonitorV2 — DPI-рескал реальный.
- DRV31-03: три дефекта новых драйверов (evidence-каталог до скриншотов;
  script-scope чтения MessageBox; окно чтения stdout).
- Демо-засев screens2 без шаблона — сценарий «нет шаблона» теперь честный.

7. ENV3-02

- источник: НЕ найден (HISTORICAL_SOURCE_UNIDENTIFIED=true); наиболее
  вероятный класс — параллельная тестовая сессия владельца (сотни новых
  temp-каталогов с mtime в ходе серии); конкретный процесс не пойман
  (FileSystemWatcher не ставился — событие ≠ доказательство PID).
- проверки: read-only аудит процессов/времени/путей; поиск кодовых путей
  (§6.2 ТЗ) — явный профиль везде, найден теоретический обход «Открыть
  базу…» (MainShell:2055, вне acceptance-путей) и РЕАЛЬНЫЙ обход реестра
  (исправлен, см. §6).
- containment: NullCurrentSenderStore + GUARD PASS-журнал + 5 regression-
  тестов; SenderId ABSENT до/после всей батарейки; production delta 0/0/0
  в каждом прогоне (final fingerprint идентичен baseline).
- production-профиль текущей серией НЕ изменён.

8. Runtime matrix

| Сценарий | PS 5.1 | PS 7 | Флаг |
|---|---|---|---|
| emptybook | exit 0 | exit 0 | RUNTIME_EMPTYBOOK_VERIFIED=true (не регрессировал) |
| F5 (printflow) | exit 0 | exit 0 | RUNTIME_F5_VERIFIED=true |
| F6 (printflow) | exit 0 | exit 0 | RUNTIME_F6_VERIFIED=true |
| preview (SEEDFIELDS+PREVIEWSCAN) | exit 0 | exit 0 | RUNTIME_PREVIEW_VERIFIED=true |
| no-address | exit 0 | exit 0 | RUNTIME_NO_ADDRESS_VERIFIED=true (не регрессировал) |
| screens (stage3-поверхности) | exit 0 | exit 0 | UI_VISUAL_VERIFIED=PARTIAL (без референсов) |
| **no-printer** | exit 0 | exit 0 | RUNTIME_NO_PRINTER_VERIFIED=**CONTRACT_ONLY** |
| **screens-2 (S1–S8)** | exit 0 | exit 0 | sent-empty/sent-data/no-template/paper-warning=true |
| **dpi-симуляция** | exit 0 | exit 0 | DPI_SIMULATION_125/150=true; DPI_RUNTIME=NOT_RUN_ON_HOST |
| fault-инъекции screens2 ×8 | exit 1 (контракт) | — | PASS_OF_CONTRACT: true |
| страж evidence-корня | exit 0 | exit 0 | EVIDENCE_WRITE_VERIFIED=true |

Скриншоты серии: `40-…48-*` (screens-2), `50-…53-*` (dpi), `60-no-printer`
+ повторно снятые surfaces screens-режима; evidence:
`%TEMP%\p302-evidence-2026-11-08-stage3-1` (P302_EVIDENCE_ROOT; старые
каталоги серий не тронуты — страж).

9. Визуальная матрица

- всего поверхностей: 26; reference: 1 (главный экран, legacy-PNG) —
  pixel score 90 ONLY на ней; structural/runtime: 18 поверхностей сняты
  скриншотами этой+прошлой серий; unverified (нет ни снимка): 8.
- REFERENCE_VISUAL_VERIFIED=PARTIAL; REFERENCE_VISUAL_SCORE=90;
  REFERENCE_VISUAL_SCORE_SCOPE=single-reference-surface
  (не распространяется на приложение).

10. Печать

PRINT_VERIFIED=NOT_RUN_BY_SCOPE; POST_INDEX_TEXT_RENDER_VERIFIED=true;
POST_INDEX_STAMP_VERIFIED=NOT_RUN_ON_HOST (лицензия ZIPcode.ttf не
подтверждена владельцем); физический принтер не использовался; спулер
0→0 во всех прогонах; блокеры: согласование физического стенда,
лицензированный шрифт.

11. COM

COM_VERIFIED=DEFERRED_BY_OWNER (ничего не запускалось/регистрировалось).

12. Сборки

```
Debug:   0 ошибок / 0 предупреждений (43.7 с, полный)
Release: 0 ошибок / 0 предупреждений (28.8 с)
Harness: 0 ошибок / 0 предупреждений
Маркеры: App Debug/Release «реестрОтправителяИзолирован»=1;
         harness «ПустойМенеджерПринтеров»=1; тесты «ContainmentIsolationTests»=2;
         App.dll в bin/harness == bin/src (sha256 c29be1ce…da8411)
```

13. Тесты

```
Strict non-COM Debug:   1298/1298 PASS (включая 5 новых ContainmentIsolation) (exit 0)
Strict non-COM Release: 1298 PASS (exit 0)
Targeted UI: Responsive 15, CounterpartyLayout 9, NarrowLayout 6,
             GridAppearance 4, DialogAccessibility 16, ClipartPicker 4,
             ContainmentIsolation 5 (NEW) — все PASS
```

14. Coverage

```
CODE_COVERAGE_MEASURED=true
TOOL: coverlet.collector 6.0.2
Line: 62,58 %  Branch: 52,15 %
Per-package: Domain 73.2, Infrastructure 73.9, Application 74.3,
             Printing 76.9, App 42.7, ComAutomation 5.3 (исключён фильтром)
```

15. Cleanup

```
Picket302/KonvertProNet/UiAcceptance/testhost-процессы: 0
P302Acceptance: пуст; .lock/.bak/sentinel: отсутствуют
Чужие процессы/worktree: не тронуты
graphify-out/: не тронут (528 файлов)
```

16. Production profile fingerprint

```
Финальные отпечатки == baseline:
picket302.db   258048  sha256=16830691…da03c6  mtime=2026-11-08T15:51:24Z (внешнее)
connection.json  225  sha256=399006d9…62d6972d5  mtime=2026-11-08T16:17:30Z (внешнее)
Дельта серии: 0/0/0 в каждом прогоне (GUARD PASS pid+root в каждом evidence)
HKCU\Software\PEPro\CurrentSender\SenderId: ABSENT до и после (контейнирование)
```

17. Open defects

- LAY3-01 (P2, код): переполнение FlowLayoutPanel в строке TLP главного
  окна — открыт (defects.md; DPI-независим, evidence на 96/120/144).
- ENV31-01/02 (окружение, владельцу): SMB-инкрементал; параллельная
  temp-активность + источник ENV3-02 не подтверждён.

18. Следующая волна

```
P0: LAY3-01 — идентифицировать ряд TLP и исправить; OS-DPI стенд 125/150 %
    (реальный масштаб сессии) для DPI_RUNTIME_*.
P1: жёсткое запрещение production-обхода «Открыть базу…»/скрытого входа
    (решение владельца); распространить DialogAccessibilityContractTests
    на остальные диалоги (BackupRestore, мастера импорта/экспорта);
    screens-2 для печатных поверхностей при 125/150 %.
P2: физический стенд печати (print-readiness §4); структурные контракты
    вместо UNVERIFIED-поверхностей.
```

---

Что доказано: полное закрытие списка runtime-поверхностей §7 (S1–S8 + no-
printer + DPI-симуляция) настоящими прогонами с fail-closed-доказательствами
(spooler, sentinel, fingerprint, fault-инъекции, скриншоты, exit-коды) в
обеих оболочках; реальный конвейер PerMonitorV2-рескала с отслеживанием
DeviceDpi 120/144/96; контейнирование реестровой записи SenderId с
регрессионными тестами и до/после-доказательством; production-профиль не
изменён серией (fingerprint == baseline).

Что исправлено: DRV31-01 (реестр), DRV31-02 (PMv2 harness), DRV31-03
(дефекта драйверов), демо-засев без шаблона.

Что осталось внешним блокером: физический принтер, лицензия ZIPcode.ttf,
OS-DPI стенд, источник ENV3-02, COM (владелец отложил).

Что не выполнялось: push/merge/PR (запрещено), COM/cold activation,
физическая печать, изменение системного масштаба ОС.

Что должен подтвердить владелец: источник внешней записи 15:51/16:17 UTC;
решение по «Открыть базу…»-обходу; согласование физического стенда и
лицензии шрифта.

Какая следующая конкретная задача: LAY3-01 (идентифицировать переполняющий
ряд TLP вкладки конверта и исправить с LAYOUTCHECK-регрессией), затем
OS-DPI стенд 125/150 %.
