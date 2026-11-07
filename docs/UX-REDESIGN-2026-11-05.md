# UX-редизайн Picket302 — этап 2026-11-05/07

## 1. Цель этапа

Завершить большой этап визуального редизайна WinForms-интерфейса рабочего
оператора печати конвертов: адаптивная компоновка вкладки «Адресная книга»,
адаптивные диалоги `FormEnvelopePrint` и `FormPrintPreview`, типографика и
подписи, empty-state пустого дерева «Отправленные», понятное подтверждение
сохранения вида таблицы. Цель — ~70 % визуального сходства с референсом
`KonvertProNet.exe` при сохранении всех подтверждённых функций.

## 2. Версии и ревизии этапа

| Момент | Коммит | Содержание |
|---|---|---|
| Базовый коммит ТЗ | `0dc8002` (в `main` через merge `f6a1e64`) | исходный базис этапа |
| Этап редизайна | `9d76bda` (ветка `feature/redesign-ui-stage2`) | корневой SplitContainer, TableLayoutPanel-диалоги, прокручиваемый предпросмотр |
| Закрытие первой серии замечаний | `a558952` | изоляция профиля, сериализация тестов, acceptance F5/F6, empty-book, правки отчёта |
| Закрытие второй серии замечаний (гард, fail-closed, утечки, evidence) | см. `git log` ветки после `a558952` | обход гарда закрыт, скрипты fail-closed, FolderCycleSchemaTests соединение, владение профиля, CS1998, 0 предупреждений harness |

## 3. Сравнение с референсом

Референс `D:\KonvertProNet\KonvertProNet.exe` был доступен. Сравнивался
**только главный экран** (BitBlt-захват окна `TfrmMakeKonvert`, 1086×775):
`docs/ux-audit-2026-11-03/референс-2026-11-05/01-главное-адресная-книга.png`.

Подтверждено по референсу:

- схема «3+2»: три верхние колонки (отправитель/получатели/логотип+примечание)
  и две таблицы «От кого»/«Кому» внизу — соответствует текущей схеме Picket302;
- заголовки групп — обычный регистр, жирный («Отправитель:», «От кого:»),
  капслок в референсе отсутствует — капслок удалён и из Picket302;
- кнопки действий — обычный текст, хоткеи не в подписях кнопок.

Отличия от референса (намеренные): Picket302 сохраняет четыре вкладки и меню
(по ТЗ), красный/синий акценты отправителя/получателя, более светлую палитру
`EnvelopeUiTheme`.

**Диалоги печати референса не открывались** (риск отправки задания на
принтер реальному принтеру системы) — сравнение диалогов не выполнялось.
Поэтому `REFERENCE_VISUAL_VERIFIED=PARTIAL`.

## 4. Что изменено

### 4.1. `src/Picket302.App/MainShell.cs`

- **Корневой горизонтальный `SplitContainer`** вкладки «Адресная книга»:
  верх — редакторы адресов и параметры отображения (`TableLayoutPanel`,
  3 колонки 34/34/32 %, `AutoScroll` только этой панели), низ — вертикальный
  `SplitContainer` с таблицами отправителей и получателей. Фиксированная
  высота 220 px упразднена.
- **Безопасное задание `SplitterDistance`** — `НастроитьРазделитель(...)`:
  минимальные размеры панелей и расстояние применяются лениво после создания
  дескриптора и достоверного лэйаута (присвоение `Panel1MinSize`/
  `Panel2MinSize` до лэйаута бросает исключение против конструкторского
  размера 150×100 — найдено тестами).
- **Заголовки групп обычным регистром**: «Отправитель», «Получатель»,
  «Вид логотипа», «Примечание к адресу», «От кого», «Кому», «Папки писем».
- **Кнопки без хоткеев в тексте**: «F5 Печать» → «Печать текущего конверта»,
  «F6 Превью» → «Предпросмотр и печать». Хоткеи сохранены в меню
  («Текущий конверт (F5)» и т. д.), ToolTip и AccessibleDescription.
  Привязка значка к кнопке переведена с текста на явный параметр.
- **Пустое дерево «Отправленные»**: `ОбновитьПустотуДерева()` скрывает пустой
  `TreeView` вместе с горизонтальной полосой прокрутки; зеркало состояния
  `ПоказанаПодсказкаПустыхПапок`.
- **Подтверждение сохранения вида** (E2): текст `ТекстСохраненияВида()`
  называет таблицу и параметры.
- **Полосы кнопок «От кого»/«Кому» переведены на `AutoSize`** (закрытие
  замечаний review): фиксированная высота 50 px обрезала второй ряд кнопок
  «Кому» («Открыть список в Excel», «Сохранить вид таблицы», «Вид таблиц…»)
  — найдено runtime-приёмкой 2026-11-06, подтверждено скриншотами до/после.
- **TabIndex**: поиск отправителя → таблицы → кнопки; `AccessibleName`.

### 4.2. `src/Picket302.App/UI/Forms/FormEnvelopePrint.cs`

Полностью переведён на `TableLayoutPanel`/`FlowLayoutPanel` без абсолютных
координат: секции «Шаблон и принтер», «Параметры печати», «Дополнительные
параметры»; переносимое предупреждение разлиновки с флагом подавления;
сворачивание строки шаблона; `AutoSize` формы; пустой список принтеров —
понятное состояние и отключённая кнопка настроек; `AccessibleName`/
`AccessibleDescription`/`TabIndex`. Публичный контракт (`ВыбранныйШаблон`,
`ВыбранныйПринтер`, `ПоказатьСписокШаблонов`, `ПереченьШаблоновПоказывается`,
`ПредупреждениеРазлиновкиПоказывается`, `ПодавитьПредупреждениеРазлиновки`,
`ПоказатьГраницыПечати`, `СформироватьЗапрос`) сохранён.

### 4.3. `src/Picket302.App/UI/Forms/FormPrintPreview.cs`

- панель управления сверху (`FlowLayoutPanel`), без координатных кнопок;
- холст в прокручиваемой подложке (`Panel.AutoScroll` + вычисляемый размер):
  при 150–200 % документ прокручивается, а не обрезается;
- пустой список страниц — понятное состояние; баннер переносится и входит
  в размер холста;
- **исправлен дефект, найденный runtime-приёмкой**: прежний код открывал
  записывающий `Graphics.FromImage(метафайл)` и одновременно выполнял
  воспроизведение `DrawImage` — GDI+ падал с `OutOfMemoryException`
  при первой отрисовке. Записывающий контекст удалён.

### 4.4. `src/Picket302.App/UI/Components/AddressEditor.cs`

- добавлен `MinimumSize` (320×190): редактор не сжимается меньше шести строк
  адреса в адаптивных контейнерах.

## 5. Изоляция профиля и acceptance-контур (закрытие замечаний review)

### 5.1. Явные пути профиля, гард изоляции

- `tests/Picket302.Tests/Infrastructure/ИзолированныйПрофиль.cs` принимает
  корень профиля явно (`new ИзолированныйПрофиль(корень)`) и создаёт полную
  структуру каталогов (`DataDirectory`, `LogsDirectory`, `SecretsDirectory`).
- Новый `tests/Picket302.Tests/Infrastructure/ГардПрофиля.cs`:
  `ГарантироватьИзоляцию(пути)` бросает понятное исключение, если каталог
  данных совпадает с `Picket302ProfilePaths.ПоУмолчанию().DataDirectory`.
  Гард встроен в конструктор `ИзолированныйПрофиль` и в acceptance-harness.
- Переменные `APPDATA`/`LOCALAPPDATA` нигде не используются как механизм
  изоляции: путь профиля известен до создания `AppServices`.

### 5.2. Acceptance-harness (tools/ui-acceptance, не входит в sln)

`UiAcceptance.exe <emptybook|printflow> <кореньПрофиля> [keep]` — консольный
хост реального `MainShell` на Release-сборке:

- гард изоляции до создания состава (отказ на производственном пути);
- `AppServices.СоздатьAsync(пути, создатьДемоБазу: false)` — демо-засев в
  acceptance-профиле никогда не выполняется;
- режим `printflow` сеет синтетического отправителя (`IsSender=true`,
  `ТекущийОтправитель.SetGlobalSender`), получателя и конвертный шаблон;
- команды stdin (`SELECT`, `F5`, `F6`, `CLOSE`) исполняются на UI-потоке;
  кнопки нажимаются реальным `PerformClick`.

Драйверы: `run-emptybook.ps1` (пункт B) и `run-printflow.ps1` (пункт D) —
Win32-ожидание окон по заголовку, `BM_CLICK` кнопок, `SendMessageTimeout
WM_NULL` для проверки отзывчивости UI-потока, sentinel-файл и отпечатки
(SHA-256 + LastWriteTimeUtc) реального профиля до/после.

Команды сборки/прогона (выполнялись 2026-11-06):

    dotnet build Z:\Picket302\tools\ui-acceptance\UiAcceptance.csproj -c Release
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-acceptance\run-emptybook.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-acceptance\run-printflow.ps1

### 5.3. Защитные тесты профиля (ProfileIsolationTests, 7 тестов)

- `СоставПриложенияВИзолированномПрофиле_НеИзменяетРеальныйПрофиль` —
  отпечатки всех файлов реального профиля до/после;
- `РеквизитыПодключения_СоздаютсяТолькоВоВременномПрофиле`;
- `ПустойПрофильБезДемоБазы_АдреснаяКнигаОстаётсяПустой` —
  `создатьДемоБазу: false` даёт пустую адресную книгу;
- `ДемоБазаСоздаётсяТолькоПриЯвномЗапросе`;
- `ИзолированныйПрофиль_ОтказываетНаПроизводственномПути` — гард;
- `SentinelФайлРеальногоПрофиля_НеИзменяетсяПрогоном` — sentinel-файл
  (SHA-256 + LastWriteTimeUtc) и отсутствие новых файлов реального профиля;
- `ЖурналСбоевТестовогоКонтура_ПишетВоВременныйПрофиль`.

Файлы профиля пользователя, проверявшиеся до/после (полный рекурсивный
обход `КаталогДанныхПоУмолчанию()`): `picket302.db`, `connection.json`,
`servers.ini`, `print_audit.log`, `ui-settings.json`, `mail-settings.json`,
`license.dat`, `com-automation.log`, `logs/*`, `Secrets/*` — 5 существующих
файлов, дельта 0/0/0 во всех acceptance-прогонах.

## 6. Runtime-визуальная приёмка

Дата прогона: 2026-11-06. База для скриншотов — Release-сборка коммита
этапа. Профиль acceptance — временный, `createDemoData=false`, sentinel
реального профиля неизменен в каждом прогоне.

| Экран | Состояние | Размер | Screenshot | Итог |
|---|---|---|---|---|
| Адресная книга | **пустая (createDemoData=false)** | 1280×720 | `01-адресная-книга-пустая-1280x720.png` | PASS («Всего: 0», обе подсказки) |
| Конверты | пустой список | 1280×720 | `02-…png` | PASS (empty-state) |
| Рассылки | пустой список | 1280×720 | `03-…png` | PASS (empty-state) |
| Отправленные | пустое дерево | 1280×720 | `04-…png` | PASS (дерево скрыто) |
| Адресная книга | демо-данные | 1280×720 | `05-…-1280x720.png` | PASS |
| Адресная книга | демо-данные | 1366×768 | `05-…-1366x768.png` | PASS |
| Адресная книга | демо-данные | 1920×1080 | `05-…-1920x1080.png` | PASS |
| FormEnvelopePrint | обычный | авто | `10-…png` | PASS |
| FormEnvelopePrint | без списка шаблонов | авто | `11-…png` | PASS |
| FormEnvelopePrint | предупреждение разлиновки | авто | `12-…png` | PASS |
| FormEnvelopePrint | без принтеров | авто | `16-…png` | PASS |
| FormEnvelopePrint | длинные имена | авто | `17-…png` | PASS |
| FormPrintPreview | 100 % | авто | `13-…png` | PASS |
| FormPrintPreview | 200 % | авто | `14-…png` | PASS (прокрутка) |
| FormPrintPreview | большая страница | авто | `18-…png` | PASS |
| FormPrintPreview | баннер-предупреждение | авто | `19-…png` | PASS |
| Очистка базы | диалог | авто | `15-…png` | PASS |
| Карточка отправления (F5) | модальный шаг | авто | `20-…png` | PASS |
| Диалог печати (F5) | перед отменой | авто | `21-…png` | PASS |
| Предпросмотр (F6) | перед закрытием | авто | `22-…png` | PASS |

Примечание к скриншоту «пустая адресная книга»: прежний файл с этим именем
содержал демо-данные (очистка базы в раннем проходе не подтвердилась) и был
заменён настоящим пустым состоянием из acceptance-прогона
`run-emptybook.ps1`; ошибочный кадр отозван.

### 6.1. Доказательство F5/F6 (файл `f5-f6-runtime-evidence.md`)

Цепочка F5 «Печать текущего конверта»: главное окно → кнопка → «Карточка
отправления» (hwnd+время зафиксированы) → «ОК» → «Печать» → «Отмена» →
диалог закрыт, главный поток отзывчив (`SendMessageTimeout WM_NULL`).

Цепочка F6 «Предпросмотр и печать»: главное окно → кнопка → «Карточка
отправления» → «ОК» → MessageBox «Отправленных сформировано: 1» → «ОК» →
«Печать» → «Печать» → «Подача конвертов» → «Продолжить» → «Предпросмотр
печати» → «Закрыть» (DialogResult ≠ OK, задание на принтер не создаётся) →
главный поток отзывчив.

**Классификация прежнего наблюдения о «зависании UI-потока F5/F6»**: ошибка
протокола автоматизации. Ранние прогоны не обрабатывали модальную цепочку
(MessageBox «Отправленных сформировано», форма «Подача конвертов») и теряли
выбор получателя (клик по ячейке вместо заголовка строки), после чего
интерпретировали молчаливые ветви выходов и таймауты UIA-клиента как
зависание. При обработке всех модальных окон зависание не воспроизводится;
`FormMailEdit`-deadlock не подтверждён.

### 6.2. Найденные и исправленные дефекты этапа

1. `FormPrintPreview`: `OutOfMemoryException` GDI+ при первой отрисовке —
   исправлен (см. 4.3).
2. Присвоение `Panel1MinSize`/`Panel2MinSize` до лэйаута — исправлено
   ленивым применением (см. 4.1).
3. Обрезка второго ряда кнопок «Кому» фиксированной высотой 50 px —
   исправлено `AutoSize` (см. 4.1).

## 7. Тесты

### 7.1. Состав

Обновлены `MainShellLayoutTests` (регистр заголовков) и `PrintDialogsTests`
(рекурсивный обход контейнеров, пустые принтеры, кнопки-акцепты,
layout-контейнеры, длинные имена).

Добавлены `ResponsiveUiContractTests` — **15 тестов** (в отчёте этапа
ошибочно указывалось 13 при 14 фактических; добавлен тест полосы кнопок
«Кому» после правки `AutoSize`): корневой разделитель, верх/низ компоновки,
кнопки без хоткеев в тексте, кнопки в клиентской области при минимальном
размере (перевод Bounds в координаты формы через `LayoutAssertions`),
полоса кнопок «Кому» обеими строками видимы, пустое дерево, текст
подтверждения вида, диалог печати (смена шаблонов, предупреждение, запрос,
координаты), предпросмотр (масштаб, холст, прокрутка, пустой документ,
доступность).

Добавлены 5 тестов в `ProfileIsolationTests` (итого 7 — см. 5.3).

### 7.2. Устранённая утечка временных профилей

Полный прогон слоя App оставлял 3 каталога временных профилей на запуск
(`picket302.db` внутри): `AdaptiveLayoutTests` и `DistributionPrintScenarioTests`
создавали состав напрямую (`AppServices.СоздатьAsync`), не передавая владение
профилю — `Dispose` профиля не освобождал состав, пул соединений держал файл,
каталог не удалялся. Добавлен `ИзолированныйПрофиль.ВзятьВладение(состав)`,
владение передано в обоих местах; контрольный прогон — дельта каталогов 0.
Устаревшие каталоги прежних прогонов очищены (100 шт., только GUID-каталоги
тестовой инфраструктуры).

### 7.3. Сериализация UI/профильных тестов

Совместный прогон `ResponsiveUiContractTests` + `ProfileIsolationTests`
воспроизводил редкий order-dependent сбой конструктора формы (1 сбой на
первом прогоне 2026-11-06, далее 3 подряд зелёных): конструирование
WinForms-форм в параллельных коллекциях и глобальный
`SqliteConnection.ClearAllPools()` в утилизации профиля пересекаются.

Решение: общая коллекция `[Collection("WinFormsПрофиль")]`
(`tests/Picket302.Tests/Infrastructure/WinFormsПрофильОпределение.cs`) для
25 классов слоя App, создающих формы/составы/профили. Доменные и
инфраструктурные тесты без форм продолжают выполняться параллельно —
область сериализации ограничена UI/профильными тестами.

### 7.4. Команды и фактические результаты прогонов (все 2026-11-06)

Целевые UI/профильные (Debug ×3):

    dotnet test .\tests\Picket302.Tests\Picket302.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~ResponsiveUiContractTests|FullyQualifiedName~MainShellLayoutTests|FullyQualifiedName~PrintDialogsTests|FullyQualifiedName~ProfileIsolationTests" --logger "console;verbosity=minimal"

→ 3 прогона: 58/58, 58/58, 58/58 (0 fail, 0 skip), testhost завершается
штатно, живых testhost/Picket302-процессов нет, новых временных профилей нет.

Целевые UI/профильные (Release ×3): та же команда с `-c Release` →
58/58 ×3, аналогичная чистота.

Узкий фильтр:

    dotnet test .\tests\Picket302.Tests\Picket302.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~ResponsiveUiContractTests|FullyQualifiedName~ProfileIsolationTests" --logger "console;verbosity=minimal"

→ 22/22 (15 ResponsiveUiContractTests + 7 ProfileIsolationTests).

Слой App целиком:

    dotnet test .\tests\Picket302.Tests\Picket302.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~Picket302.Tests.App" --logger "console;verbosity=minimal"

→ 483/483.

Полный non-COM набор:

    dotnet test .\tests\Picket302.Tests\Picket302.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~App|FullyQualifiedName~Application|FullyQualifiedName~Domain|FullyQualifiedName~Infrastructure|FullyQualifiedName~Printing|FullyQualifiedName~Licensing|FullyQualifiedName~Architecture" --logger "console;verbosity=minimal"

→ **1251/1251** (0 fail, 0 skip).

Не запускались (вне области): `FullyQualifiedName~ComAutomation`,
`FullyQualifiedName~Acceptance`.

## 8. Статусы (HISTORICAL — прогоны 2026-11-07, третья серия замечаний)

> **HISTORICAL.** Таблица ниже описывает прогоны скриптов ревизии 794cb4e.
> Актуальные статусы — в разделе 8.2 (после неё эта таблица и раздел 8.1
> сохранены как история, рядом с прежним `ACCEPTANCE_EXIT_CODE_VERIFIED=true`
> читайте новое `PARTIAL` в 8.2: сценарии emptybook/printflow с UI не
> перезапускались — NOT_RUN_BY_SCOPE).

    SOURCE_VERIFIED=true              — исходник сверён, все правки в ветке
    BUILD_DEBUG_VERIFIED=true         — 0 ошибок / 0 предупреждений
    BUILD_RELEASE_VERIFIED=true       — 0 ошибок / 0 предупреждений
    BUILD_ACCEPTANCE_HARNESS_VERIFIED=true — harness 0 ошибок / 0
                                      предупреждений
    TEST_DEBUG_VERIFIED=true          — полный non-COM Debug 1251/1251
    TEST_RELEASE_VERIFIED=true        — полный non-COM Release 1251/1251;
                                      целевые Release 58/58
    TEST_STABILITY_VERIFIED=true      — полные прогоны Debug и Release:
                                      0 каталогов Picket302Tests_* после;
                                      целевые Debug/Release 58/58 ×3 ранее
    PROFILE_SAFETY_VERIFIED=true      — сегментный гард: равенство,
                                      вложенность в обе стороны, регистр,
                                      разделители, reparse-канонизация
                                      (junction-цикл fail-closed); негативные
                                      прогоны exit 3 на родителе и на самом
                                      prod-каталоге; sentinel в finally;
                                      дельта бизнес-файлов 0/0/0
    ACCEPTANCE_EXIT_CODE_VERIFIED=true — emptybook/printflow exit 0 при
                                      PASS; fault-inject (delta-removed,
                                      killed, cleanup-false, harness-exit)
                                      → exit 1; guard-negative fault → exit 1
    TEMP_CLEANUP_VERIFIED=true        — после полных прогонов Debug и
                                      Release: Picket302Tests_* = 0,
                                      Picket302Tests отсутствует; cleanup
                                      failure стал машинно видимым
                                      (ProfileCleanupException);
                                      5 keep-профилей в
                                      %TEMP%\Picket302Acceptance сохранены
                                      намеренно (не удаляются)
    NAG_RUNTIME_VERIFIED=true         — startup NAG отключён решением
                                      владельца (DEFERRED_BY_OWNER);
                                      acceptance-прогоны проходят без NAG
    EMPTY_STATE_VISUAL_VERIFIED=true  — настоящий пустой экран, скриншот 01
    F5_RUNTIME_VERIFIED=true          — полная модальная цепочка F5,
                                      UI отзывается, печати нет
    F6_RUNTIME_VERIFIED=true          — полная цепочка F6 (карточка,
                                      MessageBox, печать, подача,
                                      предпросмотр), печати нет
    UI_VISUAL_VERIFIED=true           — скриншоты таблицы п. 6
    REFERENCE_VISUAL_VERIFIED=PARTIAL — подтверждено структурное сходство
                                      главного экрана; количественная
                                      оценка 70% не измерялась; сравнение
                                      диалогов референса не выполнялось
    COM_VERIFIED=NOT_RUN_BY_SCOPE     — COM/cold activation/реестр/SCM не
                                      запускались
    PRINT_VERIFIED=NOT_RUN_BY_SCOPE   — физическая печать не выполнялась:
                                      F5 отменён, F6 закрыт на
                                      предпросмотре до передачи задания
    CODE_COVERAGE_MEASURED=false      — coverage-collector не
                                      устанавливался, артефакта нет

### 8.1. Статусы ревизии fail-closed (HISTORICAL — четвёртая серия замечаний, 2026-11-07)

> **HISTORICAL (частично заменена 8.2).** В четвёртой серии
> `ACCEPTANCE_EXIT_CODE_VERIFIED=true` для emptybook/printflow был преждевременным:
> fault-прогоны скриптов делались на их тогдашней ревизии, а после неё UI-прогоны
> не выполнялись — актуальное значение `PARTIAL`, см. 8.2.

Исправлены дефекты acceptance/тестовой инфраструктуры, при которых PASS мог
не означать выполнение проверки:

1. **Reparse-тесты гарда без тихих пропусков** (`ProfileIsolationGuardTests`):
   невозможность создать junction/symlink — FAIL (`Assert.True`), а не
   `return`; создание подтверждается низкоуровневым атрибутом reparse point
   через `GetFileAttributesW` (`File.GetAttributes` на dangling-ссылке
   бросает `FileNotFoundException` и молча пропускал ветвь); cleanup ссылок
   и базы проверяется на фактическое отсутствие, ошибка cleanup — красный
   тест; добавлено постусловие отсутствия `P302GuardTest_*` в %TEMP%
   (`ZTempCleanupPostconditionTests`, общая коллекция с гард-тестами).
2. **`ИзолированныйПрофиль.Dispose` — все шаги независимы**: провал
   утилизации оболочки/состава не отменяет очистку пула и удаление
   каталога; ловятся любые исключения (не только узкий список), ошибка
   `ClearAllPools` входит в итоговый провал; `ProfileCleanupException`
   несёт причины всех шагов (вложенный `AggregateException` + `Data`),
   флаг «каталог остался» и число попыток. Добавлены тесты инъекции
   отказа утилизации состава и очистки пула (внутренние крючки
   `ТестУтилизацияСостава`/`ТестОчисткаПула`).
3. **PowerShell-скрипты fail-closed**: `$ErrorActionPreference='Stop'`;
   fingerprint без `SilentlyContinue` (ошибка перечисления/чтения = FAIL,
   `-Force` включает скрытые файлы); sentinel создаётся внутри
   try/finally и удаляется с `-ErrorAction Stop`; **единый путь записи
   evidence** (после cleanup и фиксации финальных статусов, один раз;
   неудача записи = exit 1); fault-инъекции пишутся в отдельные файлы
   (`emptybook-fault-<fault>.md`, `printflow-fault-<fault>.md`) с
   `PASS_OF_CONTRACT: true` и никогда не перезаписывают happy-файл
   (`emptybook-happy-evidence.md`, `printflow-happy-evidence.md`);
   обычный провал — `*-failure-evidence.md`.

Прогоны и результаты (2026-11-07, без UI-сценариев — запрещены задачей):

    SOURCE_VERIFIED=true               — исходник сверен
    BUILD_DEBUG_VERIFIED=true          — 0 ошибок / 0 предупреждений
    BUILD_RELEASE_VERIFIED=true        — 0 ошибок / 0 предупреждений
    BUILD_ACCEPTANCE_HARNESS_VERIFIED=true — harness 0 ошибок
    TEST_DEBUG_VERIFIED=true           — полный non-COM Debug 1254/1254
                                       (1251 + 3 новых теста: 2 хука
                                       Dispose + постусловие P302GuardTest)
    TEST_RELEASE_VERIFIED=true         — полный non-COM Release 1254/1254
    PROFILE_SAFETY_VERIFIED=true       — гард 19/19 (0 fail, 0 skip);
                                       reparse-ветви измерены фактически
                                       (mklink /J и симлинк созданы и
                                       проверены атрибутом reparse point)
    SYMLINK_GUARD_VERIFIED=true        — junction-равенство, junction-
                                       вложенность, dangling-симлинк,
                                       зацикленная цепочка — все ветви
                                       реально выполнены, silent return
                                       исключён
    ACCEPTANCE_EXIT_CODE_VERIFIED=PARTIAL — run-guard-negative свежий
                                       прогон exit 0; fault
                                       wrong-expectation exit 1 (без UI);
                                       emptybook/printflow скрипты
                                       переработаны, но с UI не
                                       перезапускались — их exit-коды для
                                       новой ревизии NOT_RUN_BY_SCOPE
    EVIDENCE_WRITE_VERIFIED=PARTIAL    — контракт единой записи сверен по
                                       исходнику; runtime-проверка отказа
                                       записи — при следующем разрешённом
                                       UI-прогоне
    TEMP_CLEANUP_VERIFIED=true         — до: P302GuardTest_*=0,
                                       Picket302Tests_*=0; после: 0/0;
                                       keep-профилей 5 сохранены;
                                       .bak/.lock = 0; процессов 0
    NAG_RUNTIME_VERIFIED=true          — NAG отключён (DEFERRED_BY_OWNER),
                                       без изменений
    UI_VISUAL_VERIFIED=true            — скриншоты п. 6 (код UI не менялся)
    REFERENCE_VISUAL_VERIFIED=PARTIAL  — без изменений; 70% — целевой
                                       ориентир, количественно не измерялся
    COM_VERIFIED=NOT_RUN_BY_SCOPE      — вне области
    PRINT_VERIFIED=NOT_RUN_BY_SCOPE    — вне области
    CODE_COVERAGE_MEASURED=false       — без изменений

Карта evidence-файлов: happy-path — `emptybook-happy-evidence.md` /
`printflow-happy-evidence.md` (появятся при следующем разрешённом
UI-прогоне); fault-инъекции — `emptybook-fault-*.md` /
`printflow-fault-*.md` (`PASS_OF_CONTRACT: true`); прежние основные файлы
`emptybook-runtime-evidence.md` / `f5-f6-runtime-evidence.md` теперь
указатели — их последние записи были fault-прогонами, перезаписавшими
happy-evidence (дефект устранён; записи перемещены в fault-файлы).

### 8.2. Статусы ревизии межпроцессной сериализации (пятая серия замечаний, 2026-11-07)

Закрыты дефекты, при которых PASS двух независимых testhost-процессов и
финализация evidence не были гарантированы:

1. **Межпроцессная сериализация общих ресурсов** (`БлокировкаОбщихРесурсов`):
   xUnit-коллекции сериализуют только внутри одного testhost-процесса; при
   параллельном Debug+Release (два агента, ручной прогон + acceptance)
   воспроизводились ложные падения — постусловие видело чужой
   `P302GuardTest_*`, ProfileIsolationTests видел чужой sentinel. Введён
   named mutex `Local\Picket302.TestInfrastructure.SharedProfile` (один
   идентификатор для C#-инфраструктуры и PowerShell-драйверов). Особенность
   реализации: Windows-мьютекс привязан к потоку (`ReleaseMutex` только из
   потока `WaitOne`), а асинхронные тесты продолжаются в другом потоке —
   владение вынесено в выделенный фоновый поток-владелец, вызывающий код
   сигнализирует освобождение. Таймаут ограничен (2 мин), отказ —
   fail-closed исключение; `AbandonedMutexException` (владелец умер) —
   диагностика в stderr, блокировка выдаётся следующему; аварийное
   завершение процесса освобождает мьютекс механизмами ОС. Блокировка
   применена: guard reparse-тесты, постусловие (фикстура + Z-тест),
   4 production-теста `ProfileIsolationTests`, все 3 PowerShell-драйвера.
2. **Гарантированное постусловие жизненного цикла** (`ПостусловиеЧистотыGuard`,
   `ICollectionFixture` коллекции): teardown фикстуры выполняется после
   ВСЕХ тестов коллекции — имя класса «Z» не является гарантией порядка и
   осталось лишь дополнительной проверкой. Остатки `P302GuardTest_*`
   фиксируются ПОЛНЫМ списком в диагностику до удаления, ошибка teardown —
   красный прогон.
3. **Evidence-контракт**: финальный статус вычисляется ДО записи evidence
   (Режим/UTC/PID/профили/FINAL DELTA/FAULT/EXPECTED FINAL EXIT/
   FINAL VERDICT/PASS_OF_CONTRACT — все в журнале, попадающем в файл);
   после записи выполняется пост-проверка (файл существует, не пуст,
   содержит обязательные маркеры текущего прогона) — проверка без
   «cmdlet не бросил = успех»; финальная fingerprint-дельта переводит
   прогон в FAIL (fail-флаги, а не только лог); аварийное завершение в
   finally — `Stop-HarnessTree` (CLOSE → Kill → `taskkill /T /F` +
   проверка дочерних процессов), `$убит=true` + fail-флаг + PID.
4. **Новые fault-сценарии**: `final-delta` (все 3 драйвера; для
   guard-negative доказан безоконно: exit 1, SAFETY-FAIL removed=1) и
   `evidence-fail` (невозможность записи evidence → exit 1; требует UI —
   NOT_RUN_BY_SCOPE).

Прогоны и результаты (2026-11-07, UI-сценарии emptybook/printflow не
запускались — запрещены постановкой):

    SOURCE_VERIFIED=true                    — исходник сверен
    BUILD_DEBUG_VERIFIED=true               — 0 ошибок / 0 предупреждений
    BUILD_RELEASE_VERIFIED=true             — 0 ошибок / 0 предупреждений
    BUILD_ACCEPTANCE_HARNESS_VERIFIED=true  — harness 0 ошибок
    TEST_DEBUG_VERIFIED=true                — полный non-COM Debug
                                            1256/1256 (1254 + 2 теста
                                            блокировки), 0 fail, 0 skip
    TEST_RELEASE_VERIFIED=true              — полный non-COM Release
                                            1256/1256, 0 fail, 0 skip
    GUARD_VERIFIED=true                     — guard reparse 19/19,
                                            0 skip (целевой прогон)
    OWNERSHIP_VERIFIED=true                 — ownership 7/7
    INTERPROCESS_TEST_LOCK_VERIFIED=true    — второй процесс не получает
                                            блокировку (таймаут, exit 1),
                                            после освобождения получает
                                            (exit 0); смерть владельца без
                                            ReleaseMutex (abandoned) —
                                            диагностика, владение
                                            следующему (БлокировкаОбщих
                                            РесурсовTests, child=PowerShell
                                            с тем же именем мьютекса)
    PARALLEL_TESTHOST_VERIFIED=true         — ОДНОВРЕМЕННЫЙ запуск Debug и
                                            Release testhost: оба
                                            1256/1256, exit 0; ни одного
                                            ложного P302GuardTest_*; ни
                                            одного ложного sentinel; логи:
                                            %TEMP%\p302-parallel-verify\
                                            debug.log, release.log
    POSTCONDITION_LIFECYCLE_VERIFIED=true   — teardown ICollectionFixture
                                            после всей коллекции; полный
                                            список остатков в диагностике
                                            до удаления
    FINAL_EVIDENCE_STATUS_VERIFIED=PARTIAL  — контракт (финал ДО записи +
                                            пост-проверка маркеров) сверен
                                            по исходнику и статически;
                                            runtime-прогон записи — при
                                            следующем разрешённом UI-прогоне
    FINAL_FINGERPRINT_FAIL_CLOSED_VERIFIED=true — ненулевая финальная
                                            дельта переводит прогон в FAIL:
                                            fault final-delta на
                                            guard-negative (безоконно) —
                                            removed=1, SAFETY-FAIL, exit 1
    PROFILE_SAFETY_VERIFIED=true            — production delta 0/0/0 во
                                            всех прогонах; sentinel
                                            создаётся/удаляется под
                                            блокировкой; после прогонов
                                            sentinel отсутствует
    ACCEPTANCE_EXIT_CODE_VERIFIED=PARTIAL   — guard-negative: happy exit 0,
                                            wrong-expectation exit 1,
                                            final-delta exit 1 (свежие
                                            прогоны); emptybook/printflow
                                            переработаны, но с UI не
                                            перезапускались — их exit-коды
                                            NOT_RUN_BY_SCOPE
    EVIDENCE_WRITE_VERIFIED=PARTIAL         — контракт записи сверен;
                                            runtime happy/failure-запись
                                            не проверялась заново (нужен UI)
    TEMP_CLEANUP_VERIFIED=true              — после последовательных и
                                            параллельных прогонов:
                                            P302GuardTest_*=0,
                                            Picket302Tests_*=0, keep=5,
                                            процессов 0, .bak/.lock=0
    UI_VISUAL_VERIFIED=true                 — без изменений (код UI не
                                            менялся)
    REFERENCE_VISUAL_VERIFIED=PARTIAL       — без изменений; 70% — целевой
                                            ориентир, количественно не
                                            измерялся
    COM_VERIFIED=NOT_RUN_BY_SCOPE           — вне области
    PRINT_VERIFIED=NOT_RUN_BY_SCOPE         — вне области
    CODE_COVERAGE_MEASURED=false            — без изменений

## 9. Ограничения и что не проверялось

- COM-активация, реестр, SCM — вне области (не запускались).
- Физическая печать и отправка заданий на принтер — не выполнялись ни в
  одном прогоне.
- SMTP-отправка, импорт из 1С, внешние БД — не затрагивались.
- Печать рассылок в runtime — не выполнялась (покрыта автотестами
  `DistributionPrintScenarioTests`).
- Диалоги референса не сравнивались (риск печати) — отсюда PARTIAL.
- Скриншоты «Конверты/Рассылки/Отправленные» с демо-данными — пустые
  состояния этих вкладок эквивалентны адресной книге и покрыты тестами;
  при необходимости добавить — отдельный acceptance-режим harness.
