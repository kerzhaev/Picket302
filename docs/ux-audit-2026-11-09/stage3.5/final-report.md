# Stage3.5 — доступность редактора бланков

Дата проверки: 2026-11-09  
Статус: `READY_WITH_LIMITATIONS`  
Рабочая ветка: `main`  
Исходный кодовый SHA: `a6c087d`  
Кодовый коммит серии: `5c0dbcb`  

## 1. Причина серии

Визуальная матрица уже содержит рабочую поверхность «Редактор бланков», но
его основные рабочие зоны не имели собственного семантического имени для
UI Automation и средств чтения с экрана. Это нарушало правило A1 дизайн-
контракта, хотя ручная логика редактирования макета работала.

Проблемные зоны:

- холст бланка;
- палитра элементов;
- инспектор свойств;
- скрипт `onBeforePrint`;
- список предупреждений разметки;
- вкладки «События» и «Предупреждения»;
- действия меню сохранения, удаления и применения режима разметки.

## 2. Исправление

В `src/Picket302.App/UI/Forms/FormBlankEditor.cs` добавлены явные
`AccessibleName` и `AccessibleDescription` для рабочих поверхностей и
ключевых пунктов меню. Описания называют назначение элемента, а не только
его положение или визуальный цвет.

В `tests/Picket302.Tests/App/AdaptiveLayoutTests.cs` добавлен тест
`РедакторБланков_РабочиеПоверхностиИмеютДоступныеИмена`. Он создаёт реальную
форму без показа окна и проверяет пять рабочих контролов. Это не подменяет
runtime-приёмку, но фиксирует регрессию семантических имён на уровне
актуального WinForms-кода.

Бизнес-правила бланков, сериализация, холст, обработчики мыши, меню и
сохранение не переписывались.

## 3. Верификация

| Проверка | Результат |
|---|---:|
| Целевые тесты редакторов Debug | `2/2`, 0 fail, 0 skip |
| Полный строгий non-COM Debug | `1332/1332`, 0 fail, 0 skip |
| Полный строгий non-COM Release | `1332/1332`, 0 fail, 0 skip |
| Coverlet strict non-COM Release | `1332/1332`; line `65.01%`; branch `53.56%` |
| Coverlet: покрытые строки | `26427/40653` |
| Coverlet: покрытые ветви | `7550/14096` |
| Coverage-каталог этой серии | удалён, `exists=false` |
| `git diff --check` до коммита | PASS |

Фильтр строгого набора:

```text
FullyQualifiedName!~Acceptance
&FullyQualifiedName!~ComAutomation
&FullyQualifiedName!~Printing.PrintEngineTests
```

Coverage собирался в отдельный каталог `%TEMP%` и удалён после чтения
`coverage.cobertura.xml`. Статус изменения production-профиля отдельно в
этой серии не объявляется новым fingerprint-доказательством.

## 4. Флаги

```text
SOURCE_VERIFIED=true
BUILD_DEBUG_VERIFIED=true
BUILD_RELEASE_VERIFIED=true
TEST_DEBUG_VERIFIED=true
TEST_RELEASE_VERIFIED=true
BLANK_EDITOR_ACCESSIBILITY_VERIFIED=true
CODE_COVERAGE_MEASURED=true
COVERAGE_LINE=65.01%
COVERAGE_BRANCH=53.56%
REFERENCE_VISUAL_VERIFIED=PARTIAL
COM_VERIFIED=DEFERRED_BY_OWNER
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
```

## 5. Оставшиеся gates

- Пиксельный референс KonvertProNet по-прежнему есть только для главного
  экрана; его оценка `90/100` не распространяется автоматически на всё
  приложение.
- Реальные Windows 125% и 150% на этом хосте не переключались.
- Холодная COM-активация и SCM в эту серию не входили.
- Физическая печать и реальный spooler не проверялись.
- `graphify-out/` не изменялся и не включён в коммит.

Следующий кодовый кандидат — оставшиеся вспомогательные формы, если их
интерактивные поверхности будут включены в runtime accessibility-контракт.
Системные DPI/COM/PRINT-проверки остаются отдельными стендовыми gates.
