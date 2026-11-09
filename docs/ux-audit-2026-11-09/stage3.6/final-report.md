# Stage3.6 — accessibility полей карточек и адресных редакторов

Дата проверки: 2026-11-09  
Статус: `READY_WITH_LIMITATIONS`  
Рабочая ветка: `main`  
Исходный кодовый SHA: `5a18168`  
Кодовый коммит серии: `3cb4f0d`  

## 1. Причина серии

После проверки кнопок и рабочих поверхностей оставался более глубокий
дефект: ключевые поля карточек «Отправление» и «Контрагент» имели видимые
подписи, но не все имели отдельные UIA-имена. То же относилось к индексу и
переключателю основного адреса внутри общего `AddressEditor`.

В результате программа чтения с экрана могла объявить часть полей только
по типу контрола или без контекста, особенно в карточке с несколькими
адресами.

## 2. Исправление

В `src/Picket302.App/UI/Forms/FormMailEdit.cs` добавлены доступные имена и
описания для:

- категории отправления;
- вида уведомления;
- формата конверта;
- флажка уведомления;
- количества экземпляров;
- подтверждения и отмены карточки.

В `src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs` добавлены имена для
вкладок, типа контрагента, пользовательских полей и всех полей, добавляемых
через общий метод `ДобавитьПоле`. Имя поля выводится из его видимой подписи,
поэтому банковские, паспортные и реквизитные поля не расходятся с текстом
на экране.

В `src/Picket302.App/UI/Components/AddressEditor.cs` добавлены имена для
почтового индекса, основного адреса и скрытого списка истории индексов.

В `tests/Picket302.Tests/App/AdaptiveLayoutTests.cs` добавлен регрессионный
тест `КарточкиОтправленияИКонтрагента_ИнтерактивныеПоляИмеютДоступныеИмена`.
Он проверяет публичные поля карточки отправления и обходит все интерактивные
контролы карточки контрагента.

Бизнес-правила, значения полей, валидация ИНН/КПП/БИК, сохранение и печатный
pipeline не изменялись.

## 3. Верификация

| Проверка | Результат |
|---|---:|
| Целевой тест карточек | `1/1`, 0 fail, 0 skip |
| Полный строгий non-COM Debug | `1333/1333`, 0 fail, 0 skip |
| Полный строгий non-COM Release | `1333/1333`, 0 fail, 0 skip |
| Coverlet strict non-COM Release | `1333/1333`; line `65.03%`; branch `53.57%` |
| Coverlet: покрытые строки | `26455/40684` |
| Coverlet: покрытые ветви | `7554/14100` |
| Coverage-каталог серии | удалён, `exists=false` |
| `git diff --check` до коммита | PASS |

Фильтр полного набора:

```text
FullyQualifiedName!~Acceptance
&FullyQualifiedName!~ComAutomation
&FullyQualifiedName!~Printing.PrintEngineTests
```

## 4. Флаги

```text
SOURCE_VERIFIED=true
BUILD_DEBUG_VERIFIED=true
BUILD_RELEASE_VERIFIED=true
TEST_DEBUG_VERIFIED=true
TEST_RELEASE_VERIFIED=true
MAIL_AND_COUNTERPARTY_ACCESSIBILITY_VERIFIED=true
ADDRESS_EDITOR_ACCESSIBILITY_VERIFIED=true
CODE_COVERAGE_MEASURED=true
COVERAGE_LINE=65.03%
COVERAGE_BRANCH=53.57%
REFERENCE_VISUAL_VERIFIED=PARTIAL
COM_VERIFIED=DEFERRED_BY_OWNER
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
```

## 5. Оставшиеся gates

- Пиксельный референс KonvertProNet подтверждён для главного экрана, но не
  для всех диалогов и рабочих поверхностей.
- Реальный Windows DPI 125%/150% не запускался.
- Холодная COM-активация и SCM отложены владельцем.
- Физическая печать и реальный spooler не проверялись.
- `graphify-out/` не изменялся и не включён в коммит.

Следующий безопасный кодовый кандидат — оставить accessibility-контракт
раздельным для оставшихся вспомогательных форм. Внешние DPI/COM/PRINT gates
требуют отдельного стенда и не заменяются текущим non-COM набором.
