ОТЧЁТ

1. Статус

**READY_WITH_LIMITATIONS**

Ограничения (не дефекты): визуальное сходство доказано пиксельно только для
главного экрана (у остальных 24/26 поверхностей нет референса — origin и есть
аудируемый репозиторий); физическая печать, COM и OSDPI 125/150 % — вне зоны;
производственный профиль изменён ВНЕШНИМ процессом в ходе серии (ENV3-02).

2. Цель этой серии

Завершить визуальную доводку по этапу stage3: дизайн-контракт и матрица 26
поверхностей с формальной оценкой ≥70 %; исправление подтверждённых
визуальных дефектов с regression-тестами; обязательные runtime-сценарии
(включая отсутствовавший «получатель без адреса» §11.4); печать/индекс/ZIPcode
с честными флагами; аудит acceptance-инфраструктуры; сборки, тесты, code
coverage; финальный отчёт с раздельными статусами.

3. Исходное состояние

```
Исходный SHA:            aee4ea8f22efd996851e1250b8cc0b9997607ac6 (= origin/main)
Итоговый SHA:            см. коммиты серии (main не тронут, без push/merge/PR)
Ветка:                   main
main/origin состояние:   синхронизированы на старте; origin не изменялся
Dirty/untracked:         graphify-out/ (предсуществующий, НЕ тронут)
graphify-out изменён:    нет
```

4. Изменённые файлы

| Файл | Что изменено | Почему | Как проверено |
|---|---|---|---|
| `src/Picket302.App/UI/Forms/FormClipartPicker.cs` | Отвязанные копии значков + владение + `Dispose` + AccessibleName кнопок | UX3-01 P0: падение при открытии (уничтоженный Image в ImageList) | Regression-тест Show + 3× runtime screens + скриншот |
| `src/Picket302.App/UI/Forms/FormMailEdit.cs` | AccessibleName кнопок ОК/Отмена | UX3-02 | DialogAccessibilityContractTests |
| `src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs` | AccessibleName + заголовки адресов без капители | UX3-02, UX3-03 | тесты + скриншот |
| `src/Picket302.App/UI/Forms/FormGridAppearance.cs` | AccessibleName 3 кнопок + кнопок-образцов цвета | UX3-02 | тесты |
| `tests/Picket302.Tests/App/FormClipartPickerTests.cs` | Regression-тест показа формы | UX3-01 | 4/4 Debug+Release |
| `tests/Picket302.Tests/App/DialogAccessibilityContractTests.cs` | NEW: 16 кейсов (4 диалога × 4 контракта) | UX3-02/03 | 16/16 обе конфигурации |
| `tools/ui-acceptance/Program.cs` | Режим `noaddress` (SEED/SELECTNOADDR/MAILCOUNT), режим `screens` (SHOW/HIDE), ThreadException fail-closed | §11.4, матрица, ACC3-03 | драйверы noaddress/screens |
| `tools/ui-acceptance/acceptance-lib.ps1` | `Read-HarnessLines`: перенос незавершённого `ReadLineAsync` | ACC3-01 | 10/10 прогонов драйверов |
| `tools/ui-acceptance/run-emptybook.ps1`, `run-printflow.ps1`, `run-previewflow.ps1` | Корень evidence из `P302_EVIDENCE_ROOT` | ACC3-02 | страж + чистый git status |
| `tools/ui-acceptance/run-noaddress.ps1` | NEW: сценарий §11.4 + 4 fault-режима | §11.4 | PASS ×2 shells, fault ×4 exit 1 |
| `tools/ui-acceptance/run-screens.ps1` | NEW: снимки матрицы | §8 | PASS ×3 + ×2 shells |
| `tools/ui-acceptance/test-evidence-root-guard.ps1` | NEW: статический страж | ACC3-02 | PASS обе оболочки |
| `Directory.Packages.props`, `tests/Picket302.Tests/Picket302.Tests.csproj` | coverlet.collector | §16 coverage | замер выполнен |
| `docs/ux-audit-2026-11-08/stage3/*` | Документы серии | §17 | этот каталог |

5. Визуальная матрица

```
Количество экранов:                 26
Количество экранов с референсом:     2 (главный экран в двух размерах; единственный пиксельный референс — legacy-снимок)
Количество фактически снятых:       18 снимков реального приложения (screens/драйверы)
Средневзвешенная оценка:            90 (по референсным; остальные UNVERIFIED — §9)
REFERENCE_VISUAL_VERIFIED:          PARTIAL (1 поверхность из 26)
REFERENCE_VISUAL_SCORE:             90
```

6. Runtime

```
EMPTYBOOK:     PASS (PS5.1 + PS7), exit 0, COUNTS всего=0 точно, спулер 0→0
F5:            PASS (printflow) — карточка → ОК → печать → Отмена → диалог исчез, WM_NULL ok
F6:            PASS (printflow) — карточка → MessageBox → печать → подача → предпросмотр → Закрыть
NO_ADDRESS:    PASS ×2 shells + 4 fault-режима exit 1; RUNTIME_NO_ADDRESS_VERIFIED=true (NEW)
PREVIEW:       PASS; PREVIEW-SCAN dark=3296, outsidePrintable=0, to1=1883/to2=827/индекс=586
NO_PRINTER:    диалог покрыт кодом+тестами (PrintDialogsTests 16/16); runtime-снимок NOT_CAPTURED
LONG_VALUES:   зонд обрезки — 0 обрезаний; засев длинных значений в снимках
DPI 125/150:   NOT_RUN_ON_HOST (честно; масштаб хоста 100 %)
CLEANUP:       0 процессов, 0 temp-профилей, sentinel/lock отсутствуют
```

7. Печать

```
PRINT_VERIFIED:                  NOT_RUN_BY_SCOPE
POST_INDEX_TEXT_RENDER_VERIFIED: true  (PREVIEW-FIELD [edtIndeks_to] dark=586)
POST_INDEX_STAMP_VERIFIED:       NOT_RUN_ON_HOST (лицензированного ZIPcode.ttf нет; файл D:\KonvertProNet\ZIPcode.ttf не копировался — лицензия не подтверждена)
PHYSICAL_PRINTER_USED:           false
SPOOLER_EVIDENCE:                0→0 в каждом прогоне (Win32_PrintJob)
Ограничения:                     физический стенд — чек-лист в print-readiness.md §5
```

8. COM

```
COM_VERIFIED=DEFERRED_BY_OWNER
Причина: решение владельца (регистрация/cold activation вне зоны серии)
Ничего не запускалось:   верно (ни регистрации, ни cold activation)
```

9. Сборки

```
Debug:    0 ошибок / 0 предупреждений  (30 с)
Release:  0 ошибок / 0 предупреждений  (на явно пересобранном проекте)
Harness:  0 ошибок / 0 предупреждений
```

10. Тесты

```
Strict non-COM Debug:   1293/1293 PASS (exit 0, 30 c)
Strict non-COM Release: 1293/1293 PASS (exit 0, 25 c)  [фильтр !~Acceptance&!~ComAutomation&!~Printing.PrintEngineTests]
Targeted UI:            Responsive 15, FormCounterpartyLayout 9, MainShellNarrow 6, FormGridAppearance 4, DialogAccessibility 16, MailEdit 5, PrintDialogs 16, Search 17, FormClipartPicker 4 — все PASS
Runtime scripts:        5 драйверов × 2 shells = 10/10 exit 0
Failed:                 0
Skipped:                0
```

Примечание: файла `FormPrintPreviewTests` из задания в репозитории нет;
контракты предпросмотра покрыты в `ResponsiveUiContractTests` (4 теста).

11. Code coverage

```
CODE_COVERAGE_MEASURED: true
Инструмент:             coverlet.collector 6.0.2 (XPlat Code Coverage) + отчёт из coverage.cobertura.xml
Line:                   62,45 % (31309/50114)
Branch:                 52,05 %
Фильтры:                strict non-COM (без Acceptance/ComAutomation/PrintEngineTests)
Исключено из смысла:    Picket302.ComAutomation 5,4 % (исключён фильтром; остаток — транзитивный)
Ограничения:            UI-поверхности, физический принтер, COM/SCM и внешние устройства обычным coverage не покрываются; App 42,6 % — WinForms-код без headless-рендера
```

12. Cleanup

```
Picket302 processes:      0
UiAcceptance processes:   0 (собственный зависший PID 119900 найден по cmdline и завершён — ACC3-03)
Testhost processes:       0 моих; 1 чужой (HyperlinguaMvp, B:\) НЕ тронут
Temp profiles:            0 оставленных серией
.bak:                     0
.lock:                    0
Production fingerprint:   delta 0/0/0 в каждом прогоне; ВНЕШНИЕ изменения db/connection.json — ENV3-02
Git diff --check:         чисто
graphify-out/:            не тронут
```

13. Открытые дефекты

Кодовых открытых дефектов НЕТ. Средовые пункты:

- **ENV3-01** (MSBuild-инкрементал на SMB Z:\ пропускал пересборку App; устаревший бинарник исполнялся). Следующая команда владельцу: вести репозиторий на локальном диске либо собирать критические проекты явно (`dotnet build .\src\Picket302.App\Picket302.App.csproj -c Release`) с проверкой бинарного маркера.
- **ENV3-02** (`picket302.db` восстановлен внешне 15:51:24 UTC, `connection.json` изменён 16:17:30 UTC). Команда владельцу: подтвердить источник изменений (параллельная сессия/COM-активация) и при необходимости восстановить профиль из своей резервной копии.

14. Следующая волна

```
P0:
 - Снятие NOT_CAPTURED-поверхностей матрицы: вкладка «Отправленные» (+пустое),
   отсутствие шаблона/принтера, предупреждение размера бумаги — драйвером
   screens-2 на основе существующего SHOW-механизма.
P1:
 - OSDPI 125/150 %: стенд с масштабом или ProgrammaticScaling в harness;
   расширить DialogAccessibilityContractTests на остальные диалоги
   (FormBackupRestore, мастера импорта/экспорта).
 - Перенос репозитория на локальный диск (ENV3-01) или CI-сборка.
P2:
 - Формализация оценки поверхностей без референса: структурные контракты
   (control-tree vs ui_control_trees.txt) в тесты.
 - Физический стенд печати по чек-листу print-readiness.md §5.
```

15. Честное резюме

Доказано этой серией: P0-дефект открытия выбора клипарта найден runtime-ом,
исправлен в исходниках и закрыт regression-тестом показа формы плюс тремя
последовательными runtime-прогонами со скриншотом (значки реально
отрисованы); доступность девяти кнопок и капитель исправлены с постоянным
контрактным тестом 16/16; отсутствовавший обязательный сценарий «получатель
без адреса» построен и доказан настоящим runtime-ом в обеих оболочках, включая
поимённое сообщение, отсутствие частичной записи и спулера 0→0, с четырьмя
fault-инъекциями, каждая из которых честно валит прогон; в acceptance-инфре
исправлены три реальных дефекта (разрушение канала stdout после таймаута,
перезапись evidence прошлой серии с защитным стражем, модальный диалог
исключения в harness); сборки 0/0, тесты 1293/1293 в обеих конфигурациях,
покрытие измерено инструментом (62,45 % / 52,05 %), а не заявлено. НЕ доказано
и не заявлено: пиксельное сходство 25 поверхностей без референса (origin — это
сам репозиторий, изображений в нём нет), физическая печать, машиночитаемый
ZIPcode-штамп (лицензированного шрифта нет), COM, OSDPI 125/150 %. Изменения
производственного профиля в ходе серии — внешние (ENV3-02), каждый прогон
серии доказал дельту 0/0/0.
