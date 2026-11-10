# RC S05 — Покрытие, карта рисков и контрольная точка S01–S05

Дата: 2026-11-11 (вечер 2026-11-10 UTC). Код: базовая линия `5af9874` +
исправление тестовой инфраструктуры этого этапа (см. S05-1).
Worktree `rc-20261110`, .NET SDK 8.0.420 x64.

## S05-1. Находка: молчаливая потеря coverage сборки App при интеграции

**Симптом.** Строгий non-COM прогон `--collect:"XPlat Code Coverage"` на
интегрированном коде (5af9874 и уже 6ff1d01) даёт cobertura без пакета
`Picket302.App` — 5 сборок вместо 6. На дереве `8aef10d` (до интеграции)
тот же collector даёт все 6 сборок, включая App (воспроизведено сегодня в
изолированном probe-worktree; bisect: 8aef10d OK → 6ff1d01 BROKEN).

**Причина** (доказано coverlet.msbuild 6.0.4, который репортит ошибку,
которую collector молча глотает):

```text
Unable to instrument module: Picket302.App.dll
Coverlet.Core.Exceptions.CecilAssemblyResolutionException:
Failed to resolve assembly 'System.Windows.Forms, Version=8.0.0.0, …'
  at Mono.Cecil.MetadataBuilder.AddConstant(IConstantProvider, TypeReference)
```

Интеграционный коммит 6ff1d01 добавил в App параметры с WinForms-типами,
требующими записи констант при переписывании IL; Cecil не смог разрешить
System.Windows.Forms в каталоге тестов → **вся сборка App исключена из
coverage без единого сообщения** collector-режима. Следствие: любые
coverage-заявления об App, сделанные после интеграции без этой проверки,
были бы ложными (после интеграции до настоящего этапа coverage не измерялся
— `INTEGRATED-FINAL-REPORT` честно ставил `CODE_COVERAGE_MEASURED=false`).

**Фикс** (тестовая инфраструктура, продукт не тронут):
`tests/Picket302.Tests/Picket302.Tests.csproj` →
`<PreserveCompilationContext>true</PreserveCompilationContext>`
(рекомендация самого coverlet для WinForms-сборок).

**Проверка фикса:** probe-worktree на 6ff1d01 — App вернулся в cobertura;
затем на RC-ветке — полный прогон ниже содержит все 6 сборок.

## Измерение (Debug, strict non-COM, coverlet XPlat → cobertura)

```text
dotnet test Picket302.sln -c Debug
  --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"
  --collect:"XPlat Code Coverage"
→ passed 1384 / failed 0 / skipped 0 (тот же набор, что в S01)
```

Артефакт `coverage.cobertura.xml` (9 941 176 байт,
SHA-256 `85df2a642e6e6f83…`, путь
`tests/Picket302.Tests/TestResults/f82156aa-…/`) — в репозиторий не
коммитится из-за объёма (как и в серии stage5-verify), сводка ниже.

| Сборка | line | branch |
|---|---:|---:|
| Picket302.App | **52,02 %** | 32,02 % |
| Picket302.Application | 74,29 % | 60,23 % |
| Picket302.ComAutomation | 5,35 % | 3,93 % |
| Picket302.Domain | 73,20 % | 64,41 % |
| Picket302.Infrastructure | 73,95 % | 60,70 % |
| Picket302.Printing | 76,92 % | 63,96 % |
| **TOTAL** | **64,78 %** (33 452/51 644) | **52,51 %** (7 581/14 436) |

Сопоставление с серией stage5-verify (b9e2803: 64,57/52,59) — в пределах
интеграционной дельты; App 52,02 против 50,34 тогда. Числа посчитаны
collector'ом, никаких «на глаз».

## Контрольная точка: строгие прогоны (правило 9)

| Прогон | Итог |
|---|---|
| `dotnet build Picket302.sln -c Debug` | 0 ошибок / 0 предупреждений |
| `dotnet build Picket302.sln -c Release` | 0 ошибок / 0 предупреждений |
| strict Debug (в прогоне coverage) | **1384/0/0** |
| strict Release `--no-build` | **1384/0/0** (36 с) |
| `git diff --check` | чисто |

COM/printing/integration-наборы на этом этапе не затронуты (правки —
только тестовая инфраструктура); COM-волна S11–S15 проведёт живые прогоны.

## Карта рисков: непроверенные пути против coverage

### Классы с нулевым покрытием (279 всего; ключевые по критичности)

| Класс (сборка) | Строк | Риск | Закрывает |
|---|---:|---|---|
| `ComAutomation.ForegroundWindowService/ComServerHost/ComAutomationEnvironment/AutomationReportBuilder/ReportRenderer` | 257+190+157+109+91 | COM-сервер целиком вне unit-набора (тесты исключены фильтром) | S11–S15 живые циклы |
| `App.UI.Forms.FormExternalDbImport` | 385 | импорт внешней БД не выполнен ни разу | S41+ (№127) |
| `App.UI.Forms.FormEmailDistributionWizard` | 308 | мастер рассылки не проверен | S40 (№100) |
| `App.UI.Editors.HtmlMailEditor` | 308 | HTML-редактор | S39 (№97) |
| `App.UI.Forms.FormLicense` (+FormLicenseNag 57) | 190 | гейты редакций | S46 (№126) |
| `App.UI.Forms.FormIntegrationSettings(+Page)` | 360 | страница интеграций | S35/S48 |
| `App.UI.Forms.FormLegacyUpgradeWizard` | 130 | миграция legacy | S49 |
| `App.UI.Forms.FormFirstRun` | 113 | первый запуск | S48/S49 |
| `App.MainShell` async-машины печати (ДополнитьПечатьAsync/ПечатьВыделенные/ПечатьТекущийКонверт/ПоказатьДиалогПечати) | 97+70+55+54 | операторский путь печати вне unit-набора | S16–S17 живой конвейер |
| `App.UI.Forms.FormDbServiceDoctor` | 91 | диагностика БД | S35 (№136) |
| `Infrastructure.Licensing.AssemblyIntegrityChecker` / `VmDetector` | 82/72 | целостность/детекция ВМ | S46/S47 |
| `Application.Queries.CounterpartyFilterQuery` | 144 | фильтр-запрос: 0 строк при покрытом репозитории — рассогласование, проверить на S07 | S07 |
| `Application.Services.ReportDataDumper` | 51 | промежуточный XML | S28 (№58) |
| `Domain.Rules.GroupHierarchy` | 55 | группировка | S07 (№10) |
| `Application.Import.HtmlTableReader` | 60 | буферный обмен | S31+ |

### Существенно низкое покрытие (<20 %)

`ComAutomation.AutomationDispatcher` 5/221 и `PEProApplication` 23/157,
`ApiLettersFacade` 26/177, `ComRegistration` 28/186 (COM-волна);
`Infrastructure.Fonts.ZipcodeFontGuard` 4/58 (отказ шрифта — проверить на
S16, отсутствие ZIPcode.ttf должно блокировать); `Printing.Devices.PrinterManager`
67/363 (device-зависимые ветки — S18/S19); `Migration.AccessOdbcTableSource`
9/96 и `FormClearDatabase` 7/99.

### Пути, вообще не запускавшиеся runtime (по флагам S01)

COM-холод на текущем SHA (S11–S14), полный COM acceptance `PrintEnvelope`
(S14), физическая печать (S18/S19, BLOCKED_EXTERNAL без стенда), реальные
Windows DPI 125/150 (S09/S10, NOT_RUN_ON_HOST), чистая машина/установка
(S15/S49), общая БД (S41+), скачивание обновлений (S47), probili.ru
(S48, решение владельца).

## Флаги этапа

```text
CODE_COVERAGE_MEASURED=true   (64,78 line / 52,51 branch, Debug non-COM,
                               все 6 сборок, после фикса S05-1)
SOURCE_VERIFIED=true; BUILD_DEBUG/RELEASE_VERIFIED=true (0/0);
TEST_DEBUG/RELEASE_VERIFIED=true (1384/1384 оба)
UI_RUNTIME_VERIFIED=true       (S03, свежий прогон 11 драйверов)
REFERENCE_VISUAL_VERIFIED=true; OVERALL_70_PERCENT_MEASURED=PARTIAL (S04)
COM_*/PHYSICAL_PRINT/DPI/CLEAN_INSTALL — без изменений к S01
```

## Критерий этапа

Выполнен: coverage измерен на текущем SHA с сохранёнными командами и
артефактом (SHA-256), проценты по всем шести сборкам посчитаны collector'ом;
критические непокрытые пути перечислены и привязаны к закрывающим этапам;
никакой процент не оценён «на глаз». Дополнительно найден и закрыт
регресс S05-1 (молчаливая потеря App из coverage с интеграции).
