# Stage 5 — reference surfaces и независимая визуальная проверка (UI evidence)

Дата: 2026-11-10
Статус серии: `READY_WITH_LIMITATIONS`
Базовый SHA: `b9e280346ec84d752a07bc68c56c9d2a70c17e3b`
Ветка рабочей копии: `main` (изменения этой серии пока незакоммичены)
`main` и `origin/main` на момент базовой проверки указывали на тот же SHA.
Коммит/merge/push этой серии: `не выполнялись`.

> Текущий сводный вердикт после объединения двух серий находится в
> `INTEGRATED-FINAL-REPORT.md`. Этот документ оставлен как подробный UI
> evidence-пакет; его pre-integration metadata и исторические флаги не
> заменяют сводный отчёт.

## 1. Что сделано

### 1.1. Первичные legacy-референсы

В локальном `D:\KonvertProNet\KonvertProNet.chm` найдены изображения
встроенной справки старой программы. Зафиксировано 15 отобранных assets:

- главное окно;
- предпросмотр конверта;
- меню печати;
- inline-поиск и диалог поиска;
- фильтр групп/избранного;
- размещение, редактор и меню логотипа;
- меню, столбцы и свойства таблиц;
- архивация и меню резервного копирования;
- печатный workflow.

Хеш исходного CHM:

```text
272C8D259099CA070DE77FA74E58B97C231E5F66450B2E55A8CEB02A1C6383B6
```

Копии assets и их SHA-256 находятся в
`stage5/reference-matrix.md`. Временный каталог декомпиляции после работы
не является частью репозитория.

### 1.2. Расширен скрытый acceptance capture

В `tools/ui-acceptance/Program.cs` добавлены реальные пути открытия:

- `SHOW search` — настоящий `FormSearch` с запросом `гост`, legacy-подобными
  полями поиска и функциональными параметрами сопоставления; число совпадений
  передаётся в доступное описание поля, а не рисуется неподтверждённым
  декоративным текстом;
- `SHOW backup` — настоящий `FormBackupRestore`, получающий путь базы только
  из изолированного `AppServices`;
- `FILTERMENU` — настоящий HWND вложенного меню `Вид → Фильтры`;
- `QUICKFILTERMENU` — настоящий HWND dropdown после нажатия кнопки
  `Фильтры…` над таблицей получателей;
- `PRINTMENU` — настоящий HWND выпадающего меню `Печать`;
- `TABLEMENU` — настоящий `ContextMenuStrip` тела таблицы получателей с
  выделением строки под курсором и legacy-подобными командами.

В продолжение независимого review в `src/Picket302.App/MainShell.cs` добавлен
видимый пункт `Редактор бланков (F8)` непосредственно в меню `Печать`; в
`tests/Picket302.Tests/App/ResponsiveUiContractTests.cs` добавлен регрессионный
контракт. Это не меняет обработчик горячей клавиши и не запускает печать.

В том же файле добавлены быстрые фильтры `Избранные`, `Все категории` и
динамические пункты из реального дерева категорий в `Вид → Фильтры`. Существующие
команды сохранения/применения/удаления пользовательских фильтров сохранены и
разделены визуальным разделителем.

В `MainShell` добавлена кнопка `Фильтры…` над таблицей получателей; она
открывает тот же production-dropdown, что и `Вид → Фильтры`, поэтому фильтр
доступен в legacy-подобном placement без удаления существующих команд меню.
Контрактные тесты проверяют наличие кнопки, а runtime harness проверяет её
реальным нажатием.

В `tools/ui-acceptance/run-screens.ps1` happy-path расширен с 15 до 16
поверхностей. Для успеха теперь обязательно наличие всех 16 PNG:

- 2 размера главного окна;
- 10 рабочих форм;
- меню `Вид → Фильтры`;
- меню `Печать`;
- быстрый dropdown после кнопки `Фильтры…`;
- контекстное меню строки таблицы адресной книги.

Постановочные формы и подменные bitmap для этих кадров не использовались.

В ходе независимой проверки добавлен реальный `FormTableSettings` для
настройки таблицы получателей. Он открывается командами `SHOW tablecolumns` и
`SHOW tableproperties`, сохраняет изменения только после `ОК`, поддерживает
состав/видимость/названия/ширины столбцов, порядок и свойства таблицы. Для
non-activating `PrintWindow` список столбцов и четыре команды справа имеют
явный paint-слой, а интерактивные контролы сохранены; это устраняет дефект
«поведенческий тест зелёный, runtime-кадр пустой».

После этого happy-path расширен с 13 до 16 поверхностей. Перед capture
отдельно пересобраны `Picket302.App` и `UiAcceptance`; SHA-256
`Picket302.App.dll` в исходном Release-каталоге и в каталоге harness совпал
(`DBC98355533F910F13F0C669DAEA57C9119073198B84DC39E01B7D8C5FF8148D`), что
закрывает риск stale binary для этого прогона.

В `MainShell` добавлено настоящее контекстное меню строк адресной книги по
образцу `legacy-table-menu.png`: поиск (Ctrl+F), история печати (Ctrl+H),
группы конвертов (Ctrl+G), создание независимой копии строки, перенос записи
в таблицу «От кого», копирование текущей ячейки (Ctrl+C), вложенный экспорт/
импорт и настройка таблицы. Контекст заголовка столбца по-прежнему отделён и
показывает команды скрытия/возврата столбцов. Добавлены иконки в общей
векторной теме, acceptance-команда `TABLEMENU` и отдельный реальный кадр.

## 2. Проверка сборки и захвата

| Проверка | Результат |
|---|---:|
| `dotnet build Picket302.sln -c Debug --no-restore` | 0 ошибок / 0 предупреждений |
| `dotnet build Picket302.sln -c Release --no-restore` | 0 ошибок / 0 предупреждений |
| `dotnet build tools/ui-acceptance/UiAcceptance.csproj -c Release --no-restore` | 0 ошибок / 0 предупреждений |
| `dotnet test ... --filter "RecipientTableContextMenuTests|MainShellUxFixesTests|DialogAccessibilityContractTests"` | 85/85 targeted в Debug и 85/85 в Release |
| strict non-COM Debug (`!~Acceptance&!~ComAutomation&!~Printing.PrintEngineTests`) | 1384/1384 |
| strict non-COM Release (`!~Acceptance&!~ComAutomation&!~Printing.PrintEngineTests`) | 1384/1384 |
| static non-activating capture guard | PASS |
| `run-emptybook.ps1` после изменений меню | exit 0, `COUNTS всего=0` |
| `run-screens.ps1` (после пересборки App и harness) | 16/16, exit 0 |
| graceful shutdown | `True` |
| Kill на happy-path | `False` |
| process-tree cleanup | `TreeOk=True`, живых потомков нет |
| temporary profile cleanup | PASS |
| production fingerprint | `new=0 changed=0 removed=0` |
| evidence hashes | 16/16 PASS |
| reference hashes | 15/15 PASS |
| `git diff --check` | PASS |

Подробные кадры и журналы:

- `stage5/reference-matrix.md`;
- `stage5/evidence/screens-capture-manifest.md`;
- `stage5/evidence/screens-happy-evidence-12-surfaces-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-f8-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-filter-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-quick-filter-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-search-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-table-settings-2026-11-10.md`;
- `stage5/evidence/screens-happy-evidence-table-menu-2026-11-10.md`;
- `stage5/evidence/current-table-columns-printwindow.png`;
- `stage5/evidence/current-table-properties-printwindow.png`;
- `stage5/evidence/current-address-table-menu-printwindow.png`;
- `stage5/evidence/screens-failure-evidence-concurrent-com-2026-11-10.md`;
- `stage5/evidence/emptybook-happy-evidence-2026-11-10.md`;
- `stage5/evidence/capture-manifest.md`.

## 3. Подтверждённые visual gaps

Это расхождения, увиденные на реальной паре `legacy -> current`, а не
предположения по коду:

1. Legacy quick-filter menu показывает `Избранные` и группы прямо над
   таблицей. Picket302 теперь имеет реальную кнопку `Фильтры…` над таблицей,
   открывающую тот же dropdown с быстрыми `Избранные`, `Все категории` и
   реальными узлами справочника; остаются отличия точных отступов и legacy
   funnel-иконки.
2. Legacy print menu показывает F5/F6/F7/F8 и иконки. Picket302 после
   исправления также показывает все четыре команды и иконки из своей
   векторной темы; осталось различие плотности меню и стиля значков.
3. Legacy search dialog содержит дополнительные поля и режимы поиска;
   Picket302 теперь повторяет эту структуру реальными `ComboBox`/`CheckBox`,
   поддерживает «с любой части», «с начала» и «полное совпадение», а также
   параметры регистра и формата. Осталось сопоставить inline-поиск в таблице и
   точную legacy-типографику/плотность.
4. Legacy table settings представлены отдельными surfaces для columns и
   properties. Picket302 теперь имеет реальные вкладки `Цвета, названия`,
   `Другие свойства` и `Умолчания`; обе рабочие вкладки и контекстное меню
   строки сняты в hidden runtime. Структура и команды контекстного меню
   сопоставлены, но сохранение настроек после перезапуска и точное pixel-level
   оформление (старые bitmap-иконки, отступы и плотность) ещё не оценены.

Быстрый фильтр семантически закрыт, а его placement над таблицей, расширенный
поиск и точное pixel-level оформление по-прежнему требуют отдельной оценки.
Исправления меню печати, фильтров, контекстного меню и двух вкладок настройки
таблицы покрыты runtime-кадрами; оставшиеся различия не скрыты под общим
процентом.

## 4. Честные границы

```text
SOURCE_VERIFIED=true
BUILD_DEBUG_VERIFIED=true
BUILD_RELEASE_VERIFIED=true
HARNESS_BUILD_VERIFIED=true
UI_HIDDEN_CAPTURE_VERIFIED=true
STRICT_NONCOM_DEBUG_VERIFIED=true (1384/1384)
STRICT_NONCOM_RELEASE_VERIFIED=true (1384/1384)
PRINT_MENU_F8_ICONS_VERIFIED=true
QUICK_FILTER_MENU_VERIFIED=true
QUICK_FILTER_BUTTON_RUNTIME_VERIFIED=true
SEARCH_DIALOG_RUNTIME_VERIFIED=true
SEARCH_OPTIONS_VERIFIED=true
TABLE_SETTINGS_RUNTIME_VERIFIED=true
TABLE_COLUMNS_RUNTIME_VERIFIED=true
TABLE_PROPERTIES_RUNTIME_VERIFIED=true
TABLE_CONTEXT_MENU_RUNTIME_VERIFIED=true
APP_HARNESS_BINARY_SHA_MATCH_VERIFIED=true
REFERENCE_ASSET_HASHES_VERIFIED=true
REFERENCE_VISUAL_VERIFIED=PARTIAL
OVERALL_70_PERCENT_MEASURED=false
COM_VERIFIED=PARTIAL
COLD_COM_ACTIVATION_VERIFIED=false
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
CODE_COVERAGE_MEASURED=false
```

В этой серии не запускались холодная COM-активация, физическая печать,
виртуальный spooler acceptance и настоящий системный DPI 125/150 %. Эти
флаги не выводятся из зелёного UI-capture.

Примечание о границах тестового фильтра: первый диагностический запуск с
`!~ComAutomation&!~Printing.PrintEngineTests` включил класс
`AcceptanceScenarioTests` и закономерно получил один известный `0x80040154`.
После сверки с `docs/ACCEPTANCE.md` выполнен корректный строгий scope с
исключением всего `Acceptance`; именно его результаты `1380/1380` указаны
выше и называются non-COM.

## 5. Git и чистота

Изменены acceptance harness, меню `MainShell`, `FormTableSettings`, тесты
настройки таблицы и добавлены stage5 reference/evidence.
Изменения пока не коммитились, чтобы сначала показать независимое ревью и не
смешивать новые визуальные findings с незавершённой работой другого агента.

Существующие `graphify-out/` и старые untracked PNG/evidence не удалялись и не
добавлялись в stage5 автоматически. После финального review можно отдельно
закоммитить только перечисленные файлы.
