# Picket302 Stage 5 — единый интеграционный вердикт

Дата: 2026-11-10
Статус исходников: `READY_FOR_MERGE_REVIEW_WITH_LIMITATIONS`
Интеграционная ветка: `piscine/picket302-stage5-integrated`
Исходный `origin/main`: `b9e280346ec84d752a07bc68c56c9d2a70c17e3b`
Коммиты интеграции: `6ff1d01`, `5a2daf0`, `6c5f662`
Merge в `main` и push: выполняются после этого контрольного отчёта.

Этот файл является единственным актуальным сводным вердиктом по Stage 5.
Отчёты `stage5/final-report.md` и `stage5-verify/08-final-report.md`
сохраняют подробные исходные журналы двух серий; их числа, относящиеся к
веткам до интеграции, не заменяют результаты ниже.

## 1. Что объединено

В интеграцию вошли две серии работы:

- UI и визуальное соответствие legacy-референсам: меню печати F5/F6/F7/F8,
  быстрые фильтры и кнопка `Фильтры…`, расширенный диалог поиска, настройки
  столбцов и свойств таблицы, контекстное меню строки адресной книги,
  векторные значки, исправления типографики и адаптивной компоновки;
- независимая проверка второго агента: исправление V-01 в `FormGridAppearance`,
  регрессионные layout-тесты, acceptance-драйверы вкладок и reference-поверхностей,
  холодные COM-циклы, контролируемый virtual-print probe, DPI-симуляция,
  evidence и методика визуальной матрицы.

При разрешении конфликта `tools/ui-acceptance/Program.cs` сохранены все
рабочие команды harness:

```text
SHOW search
SHOW backup
SHOW tablecolumns
SHOW tableproperties
PRINTMENU
FILTERMENU
QUICKFILTERMENU
TABLEMENU
```

Дублирующиеся реализации `ПоказатьМенюПечати` и
`ПоказатьМенюФильтров` удалены. Оставлены версии с поддержкой
`ПоследнееМеню`, штатного `HIDE` и `WS_EX_NOACTIVATE`.

## 2. Текущая верификация интегрированного tip

### Сборка и тесты

```text
BUILD_DEBUG_VERIFIED=true      0 ошибок / 0 предупреждений
BUILD_RELEASE_VERIFIED=true    0 ошибок / 0 предупреждений
HARNESS_BUILD_VERIFIED=true    0 ошибок / 0 предупреждений
TEST_DEBUG_VERIFIED=true       1384/1384, 0 FAIL, 0 SKIP
TEST_RELEASE_VERIFIED=true     1384/1384, 0 FAIL, 0 SKIP
```

Строгий non-COM фильтр:

```text
FullyQualifiedName!~Acceptance
& FullyQualifiedName!~ComAutomation
& FullyQualifiedName!~Printing.PrintEngineTests
```

Класс `FormGridAppearanceLayoutTests` из ветки второго агента входит в
актуальный счётчик. Проверены также новые контракты поиска, меню таблицы,
настройки таблицы и адаптивной компоновки.

### Скрытый runtime

Команда:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-acceptance\run-screens.ps1
```

Последний интеграционный прогон: `2026-11-10T19:24:06Z`, harness PID
`97872`.

```text
UI_HIDDEN_CAPTURE_VERIFIED=true
CAPTURE_COUNT=16/16
SCREENS_PASS=true
GRACEFUL=true
KILLED=false
TREE_OK=true
HARNESS_NOT_EXITED=false
TEMP_PROFILE_REMOVED=true
PRODUCTION_DELTA=new=0 changed=0 removed=0
LOCK_RELEASE=released=True disposed=True
```

Сняты реальные окна и меню приложения:

- два размера главного окна;
- карточка контрагента и карточка отправления;
- настройка вида таблиц;
- список столбцов и свойства таблицы;
- редактор бланка, клипарт и редактор рассылки;
- поиск и резервирование;
- меню печати, меню фильтров, быстрый dropdown фильтров;
- контекстное меню таблицы адресной книги.

Свежий сырой журнал сохранён в
`stage5/evidence/screens-happy-evidence-integrated-2026-11-10.md`.
Свежие кадры сохранены в `stage5/evidence/`. После прогона временный профиль
удалён, реальный пользовательский профиль совпал с sentinel до запуска.

### Защита от stale binary

После отдельной сборки App и harness SHA-256 копий `Picket302.App.dll`
совпадает:

```text
APP/HARNESS SHA256
9AC17A8395B2AE4D05C064017D35E7A5DA79FBB45AE803B3DF12B5FC515D4446
```

Сначала проверка намеренно обнаружила рассинхрон копий после сборки solution;
harness был пересобран отдельной командой, после чего `MATCH=True`.

## 3. Что доказано второй серией и переносится в общий результат

Ветка `piscine/picket302-stage5` доказала на своём исходном SHA `8aef10d`
следующее:

- исправление V-01 и регрессионные тесты компоновки;
- 5/5 холодных COM-активаций с отдельными PID, `-Embedding`,
  `CoRegisterClassObject=S_OK`, штатным `CoRevokeClassObject`, кодом выхода 0
  и очисткой регистрации;
- UNC-цикл cold activation;
- engine-level PDF и app-level virtual spooler probe с корректным PDF;
- DPI-симуляцию `96 → 120 → 144 → 96` через `WM_DPICHANGED`;
- acceptance-драйверы вкладок, печати, preview, emptybook и сценариев ошибок.

В интеграционном diff после `8aef10d` нет изменений в
`src/Picket302.ComAutomation`, `src/Picket302.Printing` и
`src/Picket302.Application`. Поэтому продуктовый COM/printing-код этих
доказательств не изменился при интеграции. Полный `PrintEnvelope` acceptance
через COM на текущем tip отдельно не повторялся.

## 4. Визуальный вердикт

Исторический пересчёт второй серии на её исходной ветке дал `66,7 %`.
После интеграции этот процент не переиспользуется: появились новые поля
поиска, быстрые фильтры, F8, контекстное меню и отдельные рабочие вкладки
настройки таблицы, поэтому текущую матрицу нужно считать заново по свежим
кадрам. На текущем этапе честный флаг:

```text
UI_VISUAL_VERIFIED=true
REFERENCE_ASSETS_VERIFIED=true
REFERENCE_VISUAL_VERIFIED=PARTIAL
OVERALL_70_PERCENT_MEASURED=false
```

Это означает, что визуальные поверхности сняты и сопоставлены структурно,
но число `70 %` не объявляется до независимого пересчёта всей матрицы после
интеграции. Оставшиеся визуальные различия: точная плотность и отступы
legacy-меню, inline-поиск внутри таблицы, механизм произвольного логотипа,
полная комплектность шаблона preview и pixel-level стили старого Delphi UI.

## 5. COM, печать и DPI

```text
DIRECT_COM_SERVER_VERIFIED=true
COLD_COM_ACTIVATION_VERIFIED=EVIDENCE_FROM_8AEF10D
COM_VERIFIED=PARTIAL
VIRTUAL_PRINT_VERIFIED=EVIDENCE_FROM_8AEF10D
UI_PRINT_TO_SPOOLER_VERIFIED=false
PHYSICAL_PRINT_VERIFIED=false
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
```

Причина `PARTIAL`: полный acceptance-вызов `PrintEnvelope` через COM с
записью в БД на интегрированном tip не запускался; физического принтера на
хосте нет; UI-клик до интерактивного порта намеренно не выполнялся, чтобы не
перехватывать окно пользователя.

## 6. Coverage и чистота

```text
CODE_COVERAGE_MEASURED=false
```

Исторические значения coverage второй серии сохранены в её evidence, но
после интеграции заново не измерялись и в текущий флаг не подставляются.

`git diff --check` после объединения и исправления evidence проходит.
`graphify-out/`, старые untracked PNG и чужие рабочие процессы не входят в
интеграционный коммит. Production-профиль, `connection.json`, база и
пользовательские файлы не менялись по runtime sentinel.

## 7. Итоговый статус перед merge/push

```text
SOURCE_VERIFIED=true
BUILD_VERIFIED=true
TEST_VERIFIED=true (Debug/Release 1384/1384)
HARNESS_VERIFIED=true
UI_RUNTIME_VERIFIED=true (16/16)
PROFILE_SAFETY_VERIFIED=true
COM_VERIFIED=PARTIAL
PRINT_VERIFIED=PARTIAL
REFERENCE_VISUAL_VERIFIED=PARTIAL
OVERALL_70_PERCENT_MEASURED=false
CODE_COVERAGE_MEASURED=false
READY_FOR_MERGE_REVIEW_WITH_LIMITATIONS=true
```

Интеграционная ветка содержит код обеих серий, проверена сборкой, тестами и
скрытым runtime и готова к merge в `main`. Ограничения перечислены выше и
не скрыты под статусом готовности.
