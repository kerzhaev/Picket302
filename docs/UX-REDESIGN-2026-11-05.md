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

### 8.3. Статусы ревизии lock-release и аварийной уборки дерева (шестая серия замечаний, 2026-11-07)

Закрыты fail-closed-дефекты, при которых PASS не требовал подтверждённого
освобождения блокировки и чистого дерева процессов:

1. **`Exit-SharedTestLock` — fail-closed контракт**: прежняя версия глотала
   ошибку `ReleaseMutex` (только `Write-Host`, пустой поток) и могла дать
   PASS без освобождения. Новая версия возвращает результат
   `Attempted/Released/Disposed/Error` с диагностикой (имя мьютекса, PID,
   сценарий, тип и текст исключения); `$null` — явный failure (захват не
   выполнялся); ошибка `Dispose` тоже failure; двойной вызов даёт
   `Released=$false` (ложного PASS нет). Все три драйвера добавляют
   результат в итоговый failure: PASS при неподтверждённом освобождении
   невозможен.
2. **Аварийная уборка дерева (Вариант A)**: `Stop-Acceptance` больше сам
   не убивает процесс (прежняя ветвь убивала parent без проверки дерева,
   после чего `Stop-HarnessTree` с `HasExited` досрочно возвращал
   `$false`); при таймауте — единый движок `Stop-HarnessTree`: PID и
   StartTime сохраняются ДО завершения (PID reuse); graceful CLOSE →
   `taskkill /T /F` с проверкой `$LASTEXITCODE` («не найден»/128 отличён
   от реальной ошибки, вывод сохранён); потомки собираются РЕКУРСИВНО с
   подтверждением идентичности по CreationDate (неподтверждённые не
   убиваются — fail-closed); повторный скан — отдельный шаг, остатки
   добиваются, третий скан — итог; ошибка CIM — исключение, а не пустой
   список. Результат различает graceful/killed/remaining/unconfirmed/
   verification-error; драйверы выставляют флаги `tree-cleanup-failed`,
   `remaining-descendants`, `tree-unconfirmed-descendants`,
   `tree-verification-failed` и включают их в happy-условия.
3. **`ВладениеБлокировкой.Dispose` — проверяемый teardown**: при таймауте
   Join потока-владельца итог `ВладелецНеЗавершился`, сигнальные объекты
   НЕ уничтожаются (поток ими пользуется), cleanup не считается
   успешным; итог доступен тестам (`Итог`/`СообщениеИтога`/
   `ОсвобождениеПодтверждено`), не бросает из Dispose (не затирает
   исходную ошибку); двойной Dispose идемпотентен; диагностика содержит
   имя мьютекса, процесс и PID. Детерминированные тестовые швы
   (`ТестТаймаутJoinВладельца`, `ТестДействиеПередОсвобождением`) вместо
   ожидания случайных секунд.
4. **Синтетический regression-тест дерева**
   (`tools/ui-acceptance/test-treecleanup-synthetic.ps1`, без Picket302,
   без окон, без production-профиля): скрытый родитель PowerShell +hidden
   ребёнок; timeout-путь завершает ОБА (повторный скан подтверждает);
   parent-exited/child-alive — потомок находится по сохранённому PID и
   завершается; fault-инъекции kill-fail / cim-fail / descendant-remains
   дают «cleanup НЕ подтверждён»; evidence-запись и проверка маркеров
   без UI; ни один fault не оставляет реальный процесс (финальная
   уборка по сохранённым PID, все synthetic PID проверены на отсутствие).

Fault-инъекции шестой серии (все без UI, все exit 1): lock `release-fail`
и `dispose-fail` (guard-negative), `final-delta` (guard-negative),
`tree-cleanup-fail` (guard-negative, через `Test-ProcessTreeGone`),
`kill-fail`/`cim-fail`/`descendant-remains` (синтетическое дерево),
`evidence-fail` (механизм записи доказан без UI; драйверный fault — при
следующем разрешённом UI-прогоне). `evidence-fail` и UI-сценарии
emptybook/printflow — NOT_RUN_BY_SCOPE (запрещены постановкой).

    SOURCE_VERIFIED=true                    — исходник сверен
    BUILD_DEBUG_VERIFIED=true               — 0 ошибок / 0 предупреждений
    BUILD_RELEASE_VERIFIED=true             — 0 ошибок / 0 предупреждений
    HARNESS_BUILD_VERIFIED=true             — harness 0 ошибок
    TEST_DEBUG_VERIFIED=true                — полный non-COM Debug
                                            1258/1258 (1256 + 2 теста
                                            teardown блокировки),
                                            0 fail, 0 skip
    TEST_RELEASE_VERIFIED=true              — полный non-COM Release
                                            1258/1258, 0 fail, 0 skip
    PARALLEL_TESTHOST_VERIFIED=true         — одновременный Debug+Release:
                                            оба 1258/1258, exit 0; без
                                            ложных sentinel/P302GuardTest_*
                                            (логи %TEMP%\p302-parallel-
                                            verify6\)
    LOCK_RELEASE_VERIFIED=true              — happy Released+Disposed
                                            (синтетика G1); fault
                                            release-fail и dispose-fail —
                                            exit 1 с диагностикой
                                            (mutex/pid/сценарий)
    PROCESS_TREE_CLEANUP_VERIFIED=true      — синтетика: timeout-kill
                                            завершает обоих; parent-exited/
                                            child-alive находится и
                                            завершается; faults → cleanup
                                            НЕ подтверждён
    PROCESS_TREE_POSTCHECK_VERIFIED=true    — повторный скан — отдельный
                                            шаг; guard-negative делает
                                            пост-проверку дерева после
                                            каждого случая harness;
                                            fault tree-cleanup-fail → exit 1
    FAIL_CLOSED_VERIFIED=true               — все fault-инъекции серии
                                            дают exit 1, ни один не
                                            оставляет процесс/sentinel/
                                            каталог
    PROFILE_SAFETY_VERIFIED=true            — дельта 0/0/0 во всех
                                            прогонах; sentinel создаётся/
                                            удаляется под блокировкой,
                                            после прогонов отсутствует
    TEMP_CLEANUP_VERIFIED=true              — P302GuardTest_*=0,
                                            Picket302Tests_*=0,
                                            P302TreeSim_*=0,
                                            P302LockProof_*=0, .bak/.lock=0,
                                            keep=5, процессов 0
    EVIDENCE_WRITE_VERIFIED=PARTIAL         — механизм записи и проверка
                                            маркеров доказаны без UI
                                            (invalid-имя → $false; маркер
                                            отсутствует → $false); runtime-
                                            прогон happy/failure-записи —
                                            при следующем разрешённом
                                            UI-прогоне
    UI_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE    — emptybook/printflow с UI не
                                            запускались (запрещены
                                            постановкой)
    REFERENCE_VISUAL_VERIFIED=PARTIAL       — без изменений; 70% — целевой
                                            ориентир, количественно не
                                            измерялся
    COM_VERIFIED=NOT_RUN_BY_SCOPE           — вне области
    PRINT_VERIFIED=NOT_RUN_BY_SCOPE         — вне области
    CODE_COVERAGE_MEASURED=false            — без изменений

### 8.4. Статусы ревизии liveness драйверов (седьмая серия замечаний, 2026-11-07)

Закрыт liveness/fail-closed дефект: после `Stop-Acceptance` оба драйвера
выполняли бесконечный `$h.Proc.WaitForExit()` — при `TreeOk=false`
(ошибка taskkill/CIM, живой потомок) родитель мог остаться живым, и
драйвер зависал вместо exit 1, теряя финальный fingerprint, evidence и
освобождение блокировки.

1. **`Wait-ProcessExitBounded`** — единственный допустимый способ
   ожидания процесса в драйверах: явный именованный таймаут,
   структурированный результат (Exited/ExitCode-null-при-недоступности/
   TimedOut/StartTime для PID-reuse диагностики); недоступный ExitCode
   уничтоженного процесса не превращается в исключение.
2. **`Invoke-HarnessStop`** — единая ограниченная остановка для обоих
   драйверов: null/неполный результат/исключение остановки — fail-флаги
   (`teardown-failed`/`stop-result-incomplete`/`stop-exception`) без
   бесконечных ожиданий; `TreeOk=false` — немедленный
   `tree-cleanup-failed`, затем МАКСИМУМ ОДНА ограниченная повторная
   попытка аварийного teardown (5000 мс) и повторный bounded post-scan
   (отдельный шаг); живые/неподтверждённые PID — в диагностику
   (`remaining-descendants`/`unconfirmed-descendants`/
   `tree-postcheck-failed`).
3. **Драйверы**: бесконечные `WaitForExit()` удалены (статическая
   проверка `grep -nP '\.WaitForExit\(\s*\)' tools/ui-acceptance` — 0
   совпадений в run-* файлах); ExitCode читается только у фактически
   завершившегося процесса; `harness-not-exited` — fail-флаг, входящий в
   happy-условия; finally продолжает ограниченную финализацию при живом
   harness (аварийный `Invoke-HarnessStop`, диагностика, exit 1);
   guard-negative тоже переведён на bounded-ожидание (15000 мс после
   закрытия stdout).
4. **`Stop-HarnessTree`**: добавлены поля `TaskKillExitCode`,
   `HarnessNotExited`, `StartTime`; fault-шов
   `P302_DRIVER_FAULT=parent-alive-after-stop` (только driver-level
   liveness-тесты: TreeOk=false, процесс не убивается — внешняя уборка
   за тестом).
5. **Driver-level suite** (`test-driver-boundedwait.ps1`, без UI/печати/
   COM): внешний launcher с watchdog `$proc.WaitForExit(120000)` для
   КАЖДОГО драйвера; оба драйвера с fault завершились сами за ~1 с с
   exit 1; проверены: маркеры fault/SAFETY-FAIL/HarnessNotExited/
   RemainingPids в stdout, отсутствие `PASS: True`,
   `LOCK RELEASE: released=True disposed=True` в evidence, отсутствие
   `lock-release-failed`, удаление sentinel, отсутствие synthetic PID.
   Инженерные ловушки, найденные при постройке suite: scriptblock-события
   .NET в PS 5.1 не видят локальные переменные (нет замыкания сессии);
   `Start-Process -PassThru` не отдаёт ExitCode; синхронный ReadToEnd и
   pipe-запись блокируются, пока потомки держат write-дескрипторы —
   применены файловые редиректы через `cmd /c` (труб нет — дедлок
   невозможен), а synthetic harness не наследует stdout/stderr драйвера.

Прогоны (2026-11-07, всё без UI):

    DRIVER_BOUNDED_WAIT_VERIFIED=true    — emptybook fault-прогон 1040 мс,
                                         printflow в тех же секундах;
                                         watchdog 120000 мс не потребовался
    DRIVER_FAILURE_EXIT_VERIFIED=true    — оба драйвера exit 1 при fault;
                                         PASS: True отсутствует
    DRIVER_NO_HANG_VERIFIED=true         — внешний watchdog подтвердил
                                         отсутствие зависания; синтетика
                                         не осталась
    LOCK_RELEASE_VERIFIED=true           — fault-прогоны: LOCK RELEASE
                                         released=True disposed=True
    PROCESS_TREE_CLEANUP_VERIFIED=true   — синтетика дерева (шестая серия)
                                         зелёная после изменений
    PROCESS_TREE_POSTCHECK_VERIFIED=true — пост-скан — отдельный шаг в
                                         Invoke-HarnessStop
    FAIL_CLOSED_VERIFIED=true            — guard-negative: happy 0, faults
                                         (wrong-expectation, final-delta,
                                         tree-cleanup-fail, lock
                                         release-fail, dispose-fail) —
                                         все exit 1
    PROFILE_SAFETY_VERIFIED=true         — дельта 0/0/0; sentinel под
                                         блокировкой, после прогонов
                                         отсутствует
    TEMP_CLEANUP_VERIFIED=true           — все префиксы 0, keep=5
    EVIDENCE_WRITE_VERIFIED=PARTIAL      — fault-evidence записаны и
                                         проверены маркерами; runtime
                                         happy-запись — при следующем
                                         разрешённом UI-прогоне
    UI_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE — emptybook/printflow happy UI не
                                         запускались (запрещены
                                         постановкой)
    BUILD_DEBUG_VERIFIED=true            — 0/0
    BUILD_RELEASE_VERIFIED=true          — 0/0
    HARNESS_BUILD_VERIFIED=true          — 0/0
    TEST_DEBUG_VERIFIED=true             — 1258/1258, 0 fail, 0 skip
    TEST_RELEASE_VERIFIED=true           — 1258/1258, 0 fail, 0 skip
    REFERENCE_VISUAL_VERIFIED=PARTIAL    — без изменений; 70% — целевой
                                         ориентир, не измерялся
    COM_VERIFIED / PRINT_VERIFIED        — NOT_RUN_BY_SCOPE
    CODE_COVERAGE_MEASURED=false         — без изменений

### 8.5. Статусы ревизии race-safety уборки дерева (восьмая серия замечаний, 2026-11-08)

Закрыт race-condition в синтетической уборке дерева: первый запуск
`test-treecleanup-synthetic.ps1` иногда завершался exit 1 с
`TREE-CLEANUP-SYNTHETIC EXCEPTION: ERROR: The process "28840" not found.`

**Причина гонки.** PowerShell 5.1 при `$ErrorActionPreference='Stop'`
переводит stderr native-команды в terminating error. `taskkill` пишет в
stderr «ERROR: The process "N" not found.» именно в гонке, когда
процесс уже завершился между scan и taskkill — и исключение
прерывало `Stop-HarnessTree` ДО финального post-scan: терялись
`RemainingPids`, `UnconfirmedPids` и структурированный результат.

**Исправление (общий production-like helper, не только тест C):**

1. `Invoke-TaskKillSafe` — единая безопасная обёртка над
   `taskkill /T /F /PID`: локальный `ErrorActionPreference='Continue'`
   (stderr native-команды не превращается в terminating error) плюс
   try/catch (исключение наружу не выходит), bounded wait, структурированный
   результат `Attempted/Pid/Operation/ExitCode/Output/Exception/
   NotFound/AlreadyExited/TimedOut/Success`. Not-found (код 128 или
   «not found» в выводе) — это **признак гонки, а не успех**: окончательную
   классификацию даёт post-scan.
2. `Stop-HarnessTree`: прямые вызовы `taskkill` заменены на helper
   (родитель и потомки); добавлены поля `DescendantKillResults`,
   `DescendantKillErrors`, `AlreadyExitedPids`, `TaskKillRacePids`,
   `TaskKillTimedOutPids` (существующие `TaskKillExitCode`, `KillOutput`,
   `RemainingPids`, `UnconfirmedPids`, `TreeError`, `TreeConfirmed`,
   `HarnessNotExited` сохранены). Гонка классифицируется:
   процесс исчез и post-scan подтвердил отсутствие → benign race с
   отметкой в диагностике (`TreeError` НЕ ставится); PID жив →
   `RemainingPids` + `TreeOk=false`; идентичность не подтверждена →
   `UnconfirmedPids` + `TreeOk=false`.
3. **Пост-скан всегда выполняется** (отдельный шаг) и дополнен **прямой
   проверкой каждого PID потомка первого скана**: после смерти родителя
   потомки переподчиняются, и rescan по `ParentProcessId` их может не
   найти — «дерево чисто» тогда означало бы «процесс не найден», а не
   «процесс завершён». Идентичность сверяется по `CreationDate` (PID
   reuse не приводит к убийству чужого процесса).
4. `test-treecleanup-synthetic.ps1`: `ЗавершитьПоПид` — через
   `Invoke-TaskKillSafe` (гонка диагностируется как already-exited race,
   а не роняет suite); финальная уборка в `finally` изолирована
   per-PID (ошибка одного не прерывает остальные).
5. `test-driver-boundedwait.ps1`: все `taskkill` — через helper;
   **evidence изолирован**: `P302_EVIDENCE_ROOT` направляет fault-evidence
   во временный каталог, отслеживаемые файлы репозитория тестом не
   перезаписываются; добавлена проверка side-effect hygiene (git
   status до/после).

**Детерминированные race-швы** (не полагаются на случайное окно между
scan и taskkill):

- `P302_TREE_FAULT=descendant-disappears-before-taskkill` — родитель
  завершается БЕЗ `/T` (иначе `/T` убил бы всё дерево, и потомок не
  дожил бы до стадии очистки), затем потомок исчезает сам перед
  taskkill → taskkill честно возвращает not-found → post-scan
  подтверждает отсутствие → `TreeOk=true`, в диагностике остаётся
  отметка race (`TaskKillRacePids`).
- `P302_TREE_FAULT=descendant-not-found-but-alive` — тот же сценарий, но
  PID живой: `RemainingPids` непуст, `TreeError` содержит причину,
  `TreeOk=false`.

Инъекции `kill-fail`, `cim-fail`, `descendant-remains` остаются
fail-closed (проверено в synthetic suite).

Прогоны (2026-11-08, всё без UI/COM/печати):

    TREE_CLEANUP_RACE_SAFE_VERIFIED=true      — Stop-HarnessTree больше
                                               не бросает необработанное
                                               исключение; гонка
                                               диагностируется
                                               структурированно
    TREE_CLEANUP_REPEATABILITY_VERIFIED=true  — synthetic suite 10 прогонов
                                               подряд: 10/10 PASS, 0 FAIL
    SYNTHETIC_TREE_SUITE_VERIFIED=true        — synthetic suite PASS (48
                                               проверок), exit 0
    EVIDENCE_SIDE_EFFECT_FREE_VERIFIED=true   — driver-level suite не
                                               изменяет tracked-файлы
                                               (git status до/после
                                               совпадает); evidence во
                                               временном каталоге
    DRIVER_NO_HANG_VERIFIED=true              — без изменений (седьмая
                                               серия): watchdog не истёк
    BUILD_DEBUG_VERIFIED / BUILD_RELEASE_VERIFIED / HARNESS_BUILD_VERIFIED
                                           = true — 0 ошибок, 0 предупреждений
    TEST_DEBUG_VERIFIED / TEST_RELEASE_VERIFIED = true — 1258/1258,
                                               0 fail, 0 skip
    LOCK_RELEASE_VERIFIED=true               — fault-прогоны: released=True
    PROCESS_TREE_CLEANUP_VERIFIED=true       — tree suite PASS
    PROCESS_TREE_POSTCHECK_VERIFIED=true     — post-scan всегда; прямая
                                               проверка PID потомков
    FAIL_CLOSED_VERIFIED=true                — kill-fail, cim-fail,
                                               descendant-remains,
                                               descendant-not-found-but-
                                               alive — все failure
    PROFILE_SAFETY_VERIFIED=true             — дельта 0/0/0, sentinel
                                               отсутствует
    TEMP_CLEANUP_VERIFIED=true               — все префиксы 0, keep=5,
                                               .bak/.lock=0, процессов 0
    EVIDENCE_WRITE_VERIFIED=PARTIAL          — fault-evidence записаны и
                                               проверены; runtime happy-
                                               запись — при разрешённом
                                               UI-прогоне
    UI_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE     — UI не запускался
    REFERENCE_VISUAL_VERIFIED=PARTIAL         — без изменений; 70% —
                                               целевой ориентир
    COM_VERIFIED / PRINT_VERIFIED = NOT_RUN_BY_SCOPE
    CODE_COVERAGE_MEASURED=false              — без изменений

### 8.6. Статусы ревизии bounded timeout самой taskkill (девятая серия замечаний, 2026-11-08)

**Дефект (несоответствие заявления и реализации).** Восьмая серия
объявила у `Invoke-TaskKillSafe` параметр `$timeoutMs`, но внутри
выполняла native-команду СИНХРОННО:

    $вывод = & taskkill /T /F /PID $идентификатор 2>&1

Параметр в сигнатуре **не ограничивал время исполнения самой
taskkill**: ограничен был только внешний watchdog всего
acceptance-драйвера — это другое, не связанное свойство. Зависшая
taskkill висела бы бесконечно внутри `Stop-HarnessTree`.

**Исправление — настоящий bounded runner.**

1. Новый `Invoke-BoundedCommand` (`acceptance-lib.ps1`): native-команда
   запускается **отдельным процессом** через
   `System.Diagnostics.ProcessStartInfo` (`UseShellExecute=false`,
   `CreateNoWindow=true`, `RedirectStandardOutput/Error=true`) и
   ожидается `WaitForExit($TimeoutMs)`. При таймауте процесс
   принудительно завершается (`Kill`), после чего выполняется **только
   ограниченное** ожидание (`WaitForExit(2000)`) — бесконечный
   `WaitForExit()` в наборе отсутствует.
2. **Pipe-deadlock исключён конструктивно:** stdout/stderr читаются
   **асинхронно** (`ReadToEndAsync`) с самого старта процесса, поэтому
   буфер трубы не переполняется и дочерний процесс не блокируется на
   записи. Синхронный `ReadToEnd()` до `WaitForExit()` запрещён (и
   запрещён статической проверкой). Сбор вывода тоже ограничен по
   времени — потомок, удерживающий конец трубы, не блокирует helper.
3. `Invoke-TaskKillSafe` переведён на runner: `$timeoutMs` передаётся
   в `WaitForExit($TimeoutMs)`. На таймауте — `TimedOut=true`,
   `Success=false`, `ExitCode=null` (процесс не завершился штатно),
   `Exception` сохраняется, `Pid`/`Operation`/`Output` сохраняются;
   таймаут **не** трактуется как not-found.
4. Классификация результатов сохранена без ослабления: код 0 →
   `Success=true`; код 128 / «not found» / «не найден» →
   `NotFound=true` + `AlreadyExited=true` (это признак гонки —
   окончательное решение даёт post-scan); прочие ненулевые коды →
   `TreeError` + `TreeOk=false`; таймаут → `TaskKillTimedOutPids` +
   `TreeError` + `TreeOk=false`; исключение PowerShell/native →
   `Exception` заполняется, наружу не выходит.
5. `Stop-HarnessTree`: родитель, потомки и повторная аварийная
   остановка — через тот же bounded helper; финальный post-scan
   выполняется **всегда** (в том числе после timeout/error/not-found);
   отсутствие исключения **не** означает PASS; benign race даёт
   `TreeOk=true` только после подтверждённого post-scan.
   Существующие поля не удалены: `TaskKillExitCode`, `KillOutput`,
   `RemainingPids`, `UnconfirmedPids`, `TreeError`, `TreeConfirmed`,
   `HarnessNotExited`, `DescendantKillResults`, `DescendantKillErrors`,
   `AlreadyExitedPids`, `TaskKillRacePids`, `TaskKillTimedOutPids`.
6. Детерминированный шов `P302_TASKKILL_FAULT=hang`: системный
   taskkill подвешивать не нужно — runner подменяется синтетическим
   процессом, который спит дольше таймаута; в fault-режиме внутренний
   бюджет `Get-TaskKillTimeoutMs` = 800 мс.
7. Новый статический guard
   `tools/ui-acceptance/test-taskkill-bounded-static.ps1` (AST-разбор
   `acceptance-lib.ps1`): требует, чтобы `Invoke-TaskKillSafe` вызывал
   bounded runner **и передавал ему `$timeoutMs`** (ловит состояние
   «параметр объявлен, но не используется»), чтобы runner использовал
   `ProcessStartInfo` + `WaitForExit($TimeoutMs)` + `ReadToEndAsync`,
   чтобы синхронного `& taskkill` не осталось и чтобы во всём
   `tools/ui-acceptance` не было безлимитного `WaitForExit()`.

**Два независимых свойства (не смешиваются и не подменяют друг
друга):**

- `DRIVER_NO_HANG_VERIFIED` — внешний watchdog ограничивает **весь**
  acceptance-драйвер;
- `TASKKILL_INTERNAL_TIMEOUT_VERIFIED` — внутренний таймаут
  ограничивает **отдельный native-процесс** taskkill.

**Измеренные факты (2026-11-08, всё без UI/COM/печати).**

- Helper-level (timeoutMs = 800): фактическое ожидание `WaitMs` = 801 мс,
  полный `ElapsedMs` = 822 мс (внешний замер 828 мс) — уложился в
  таймаут + запас на запуск/сбор диагностики; `TimedOut=true`,
  `Success=false`, `ExitCode=null`, `Exception` присутствует, PID и
  Operation сохранены, зависший синтетический runner после теста **не
  жив**.
- Stop-HarnessTree-level: необработанного исключения нет; post-scan
  выполнен (`TreeConfirmed=true`); `TreeOk=false`; `TreeError` содержит
  «за таймаут»; `TaskKillTimedOutPids` заполнен; `RemainingPids` /
  `UnconfirmedPids` не потеряны (живой потомок виден в `RemainingPids`).
- Driver-level: `P302_TASKKILL_FAULT=hang` + `P302_DRIVER_FAULT=taskkill-hang`
  — оба драйвера завершаются с exit 1, watchdog не истёк, evidence
  содержит «TASKKILL TIMEOUT» и «за таймаут», `TreeOk=False`,
  `FINAL VERDICT: FAIL`, lock released/disposed = True, sentinel удалён.

Прогоны (2026-11-08, всё без UI/COM/печати):

    TASKKILL_INTERNAL_TIMEOUT_VERIFIED=true  — Invoke-TaskKillSafe имеет
                                               настоящий bounded runner
                                               (отдельный процесс +
                                               WaitForExit($timeoutMs));
                                               таймаут → TimedOut=true,
                                               Success=false, TreeOk=false;
                                               post-scan выполняется
    TASKKILL_TIMEOUT_FAULT_VERIFIED=true      — детерминированный шов
                                               P302_TASKKILL_FAULT=hang:
                                               helper-level и
                                               Stop-HarnessTree-level
                                               тесты зелёные
    TASKKILL_BOUNDED_STATIC_VERIFIED=true     — статический guard (AST):
                                               29 проверок, exit 0;
                                               ловит «таймаут объявлен,
                                               но не используется»
    DRIVER_TASKKILL_TIMEOUT_VERIFIED=true     — driver-level: оба драйвера
                                               exit 1, evidence содержит
                                               таймаут, lock освобождён,
                                               watchdog не истёк
    TREE_CLEANUP_RACE_SAFE_VERIFIED=true      — без изменений (восьмая
                                               серия): гонка
                                               диагностируется
                                               структурированно
    TREE_CLEANUP_REPEATABILITY_VERIFIED=true  — synthetic suite 10 прогонов
                                               подряд: 10/10 PASS, 0 FAIL
    SYNTHETIC_TREE_SUITE_VERIFIED=true        — synthetic suite PASS (71
                                               проверка), exit 0
    EVIDENCE_SIDE_EFFECT_FREE_VERIFIED=true   — driver-level suite не
                                               изменяет tracked-файлы
                                               (git status до/после
                                               совпадает); evidence во
                                               временном каталоге
    DRIVER_NO_HANG_VERIFIED=true              — без изменений (седьмая
                                               серия): watchdog не истёк
    BUILD_DEBUG_VERIFIED / BUILD_RELEASE_VERIFIED / HARNESS_BUILD_VERIFIED
                                           = true — 0 ошибок, 0 предупреждений
    TEST_DEBUG_VERIFIED / TEST_RELEASE_VERIFIED = true — 1258/1258,
                                               0 fail, 0 skip
    LOCK_RELEASE_VERIFIED=true               — fault-прогоны: released=True
    PROCESS_TREE_CLEANUP_VERIFIED=true       — tree suite PASS
    PROCESS_TREE_POSTCHECK_VERIFIED=true     — post-scan всегда; прямая
                                               проверка PID потомков
    FAIL_CLOSED_VERIFIED=true                — kill-fail, cim-fail,
                                               descendant-remains,
                                               descendant-not-found-but-
                                               alive, taskkill timeout —
                                               все failure
    PROFILE_SAFETY_VERIFIED=true             — дельта 0/0/0, sentinel
                                               отсутствует
    TEMP_CLEANUP_VERIFIED=true               — все префиксы 0, keep=5,
                                               .bak/.lock=0, процессов 0
    EVIDENCE_WRITE_VERIFIED=PARTIAL          — fault-evidence записаны и
                                               проверены; runtime happy-
                                               запись — при разрешённом
                                               UI-прогоне
    UI_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE     — UI не запускался
    REFERENCE_VISUAL_VERIFIED=PARTIAL         — без изменений; 70% —
                                               целевой ориентир
    COM_VERIFIED / PRINT_VERIFIED = NOT_RUN_BY_SCOPE
    CODE_COVERAGE_MEASURED=false              — без изменений

### 8.7. Статусы ревизии lock-файла, timeout-диагностики и строгого non-COM scope (десятая серия замечаний, 2026-11-08)

**Исправление №1 — lock-файл COM-приёмки (`LOCK_FILE_CLEANUP_VERIFIED`).**

Дефект: `AcceptanceScenarioTests.ЗахватитьБлокировкуПриёмки` создавал
`%TEMP%\Picket302.ComAcceptance.lock` (`FileMode.OpenOrCreate`,
`FileShare.None`) и никогда его не удалял — поток освобождал
блокировку, но нулевой файл оставался в %TEMP% после каждого прогона
с COM-сценарием.

Исправление — production-test-инфраструктура, а не тестовый костыль:
механизм вынесен в internal-хелпер
`tests/Picket302.Tests/Infrastructure/БлокировкаФайлаПриёмки.cs`
(`Захватить(путь, ожидание)`), поток открывается с
`FileOptions.DeleteOnClose` (`bufferSize: 1`). Все требуемые контракты
выполнены конструктивно:

1. сериализация — `FileShare.None` не даёт конкуренту открыть файл,
   пока дескриптор открыт; DeleteOnClose этого не ослабляет: .NET
   форсирует только `FILE_SHARE_DELETE`, а запрашиваемый Read/Write
   доступ по-прежнему запрещён режимом совместного доступа владельца;
2. удаление — ОС удаляет файл при закрытии последнего дескриптора;
3. аварийная смерть владельца — ОС закрывает дескриптор, файл
   исчезает без дополнительных действий;
4. устаревший файл прежней реализации переживается следующим
   захватом (`OpenOrCreate` открывает его как обычный файл) и
   удаляется при закрытии нового владельца.

Regression-тесты `БлокировкаФайлаПриёмкиTests` (namespace
`Picket302.Tests.Infrastructure` — намеренно НЕ «Acceptance», чтобы
строгий non-COM фильтр их включал; без COM-активации): файл
существует при открытом дескрипторе и удаляется после закрытия;
повторный захват создаёт и снова удаляет; stale-файл поглощается и
удаляется; конкурент получает fail-closed `IOException` с диагностикой
«Блокировка COM-приёмки не освобождена за N секунд» за ограниченный
бюджет и блокировку после освобождения.
Пути в тестах — уникальный guid в %TEMP%; production-профиль и
чужие TEMP-артефакты не задеваются. 3/3 в Debug и Release.

**Исправление №2 — непустая диагностика таймаута
(`TIMEOUT_EXCEPTION_VALUE_VERIFIED`).**

Дефект: `Invoke-BoundedCommand` при таймауте выставлял
`TimedOut=true` / `Success=false` / `ExitCode=null`, но оставлял
`Exception=null` — причина была только в поле `Диагностика`, и
`Invoke-TaskKillSafe` копировал null наружу; H-тест проверял лишь
`ContainsKey('Exception')` (тавтология).

Исправление: при таймауте `Exception` обязан быть непустым и
содержать причину: `команда не завершилась за N мс (метка=…,
pid=…)`. Ошибка Kill добавляется к причине таймаута, не затирая её.
Классификация не менялась: таймаут — не benign race,
`NotFound=false`, `TreeOk=false`, `TaskKillTimedOutPids` заполняется.
H-тест заменён на проверку содержимого: непусто + содержит «timeout /
таймаут / не завершилась / bounded» + содержит метку операции.
Измеренный факт (2026-11-08): `timeoutMs=800` → `WaitMs=833`,
`ElapsedMs=907`, `Exception=команда не завершилась за 800 мс
(метка=echo-timeout, pid=…)`, `TimedOut=true`, `Success=false`,
`ExitCode=null`, `NotFound=false`, зависший runner не жив.

**Исправление №3 — строгий non-COM scope
(`STRICT_NON_COM_TEST_VERIFIED`, `TEST_SCOPE_EVIDENCE_VERIFIED`).**

Дефект: фильтр
`!~ComAutomation&!~Printing.PrintEngineTests` пропускал
`Picket302.Tests.Acceptance.AcceptanceScenarioTests` (включая
COM-тест `Com_PrintEnvelope_OutOfProc_…`) — набор из «1258»
некорректно назывался «non-COM».

Исправление — строгий фильтр:

    dotnet test .\tests\Picket302.Tests\Picket302.Tests.csproj `
      -c Debug --no-build --no-restore `
      --filter 'FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests' `
      --logger 'console;verbosity=minimal'

Область доказана `--list-tests` тем же фильтром в ОБОИХ конфигурациях:
0 совпадений `AcceptanceScenarioTests` / `Com_PrintEnvelope` /
`Picket302.ComAutomation` / `Printing.PrintEngineTests`. Единственное
текстовое совпадение «acceptance» — inline-данные guard-теста
(`ProfileIsolationGuardTests.Кандидат_SiblingСОбщимПрефиксом_…`,
пример sibling-пути `"C:\prod\Root\Picket302-acceptance"`), не
тестовый класс. **Фактическое число: Debug 1259/1259, Release
1259/1259, 0 fail, 0 skip** (1258 − 2 COM-теста приёмки + 3 новых
lock-теста). Прежние заявления «1258 = non-COM» считаются
скорректированными этим разделом; числа больше не записываются
заранее — только из фактического прогона.

Прогоны (2026-11-08, всё без UI/COM/печати):

    LOCK_FILE_CLEANUP_VERIFIED=true          — DeleteOnClose: файл
                                               существует при открытом
                                               дескрипторе, удаляется при
                                               закрытии, stale-файл
                                               поглощается, конкурент
                                               fail-closed; 3/3 Debug+Release
    TIMEOUT_EXCEPTION_VALUE_VERIFIED=true    — Exception при таймауте
                                               непустой и содержит причину
                                               (метка+pid); H-тест
                                               проверяет содержимое
    STRICT_NON_COM_TEST_VERIFIED=true        — строгий фильтр
                                               !~Acceptance&!~ComAutomation&
                                               !~Printing.PrintEngineTests;
                                               Debug и Release 1259/1259
    TEST_SCOPE_EVIDENCE_VERIFIED=true        — --list-tests обоих
                                               конфигураций: 0
                                               Acceptance/Com_PrintEnvelope/
                                               ComAutomation/PrintEngineTests
    ACCEPTANCE_COM_VERIFIED=NOT_RUN_BY_SCOPE — COM-приёмка (out-of-proc)
                                               не запускалась
    TASKKILL_INTERNAL_TIMEOUT_VERIFIED=true  — без изменений (девятая
                                               серия)
    TASKKILL_TIMEOUT_FAULT_VERIFIED=true     — synthetic 10/10 PASS
                                               (73 проверки каждый)
    TASKKILL_BOUNDED_STATIC_VERIFIED=true    — статический guard 29/29
    DRIVER_TASKKILL_TIMEOUT_VERIFIED=true    — driver-level 3/3 PASS
                                               (59 проверок каждый)
    TREE_CLEANUP_RACE_SAFE_VERIFIED=true     — без изменений
    TREE_CLEANUP_REPEATABILITY_VERIFIED=true — synthetic 10 прогонов
                                               подряд: 10/10, 0 FAIL
    BUILD_DEBUG_VERIFIED / BUILD_RELEASE_VERIFIED / HARNESS_BUILD_VERIFIED
                                           = true — 0 ошибок, 0 предупреждений
    TEST_DEBUG_VERIFIED / TEST_RELEASE_VERIFIED = true — strict non-COM
                                               1259/1259, 0 fail, 0 skip
    LOCK_RELEASE_VERIFIED=true               — fault-прогоны: released=True
    PROCESS_TREE_CLEANUP_VERIFIED=true       — tree suite PASS
    PROCESS_TREE_POSTCHECK_VERIFIED=true     — post-scan всегда
    FAIL_CLOSED_VERIFIED=true                — kill-fail, cim-fail,
                                               descendant-remains,
                                               descendant-not-found-but-
                                               alive, taskkill timeout —
                                               все failure
    PROFILE_SAFETY_VERIFIED=EXTERNAL_WARNING — прогоны этой серии
                                               трогали профиль только
                                               чтением (fingerprint) и
                                               sentinel-ом (создан и
                                               удалён); НО в окне прогонов
                                               во внешнем журнале
                                               com-automation.log
                                               зафиксирован запуск
                                               Picket302.ComAutomation.exe
                                               2026-11-08 10:17:08–10:17:10
                                               из bin-каталога тестов
                                               этого репозитория (не
                                               прогоны этой серии), и
                                               %AppData%\Picket302\
                                               picket302.db в переписи
                                               отсутствует (файл
                                               .acceptance-*.bak нигде не
                                               оставлен). Владельцу:
                                               восстановить пользовательскую
                                               базу из собственной резервной
                                               копии; прогоны этой серии
                                               базу не удаляли (list-tests
                                               доказывает отсутствие
                                               Acceptance-тестов)
    TEMP_CLEANUP_VERIFIED=true               — все префиксы 0, keep=5,
                                               .bak=0, .lock=0 (включая
                                               %TEMP%\Picket302.ComAcceptance.lock),
                                               процессов 0, тестовые
                                               lock-артефакты *.test-* = 0
    EVIDENCE_WRITE_VERIFIED=PARTIAL          — fault-evidence записаны и
                                               проверены; runtime happy-
                                               запись — при разрешённом
                                               UI-прогоне
    UI_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE     — UI не запускался
    UI_VISUAL_VERIFIED=NOT_RUN_BY_SCOPE      — без изменений
    REFERENCE_VISUAL_VERIFIED=PARTIAL         — без изменений; 70% —
                                               целевой ориентир
    COM_VERIFIED / PRINT_VERIFIED = NOT_RUN_BY_SCOPE
    CODE_COVERAGE_MEASURED=false              — без изменений

### 8.8. Статусы ревизии кодировки harness, компоновки карточки контрагента, главного экрана 1000 px, политики адреса получателя и доказательства рендеринга предпросмотра (одиннадцатая серия замечаний, 2026-11-08)

> Серия выполнялась поверх 57307cc (main, merge+push уже выполнены
> владельцем); коммит серии создаётся локально без push/merge.

**Что изменено (по частям задания).**

1. **Часть A — контракт кодировки harness.** `Program.cs`: `Console.SetOut/
   SetError` поверх `OpenStandardOutput/Error` с `UTF8Encoding(false)` ДО
   первого вывода — UTF-8 без BOM независимо от OEM-кодировки консоли.
   `acceptance-lib.ps1` (`Start-Acceptance`): явные
   `StandardOutputEncoding/StandardErrorEncoding = UTF8` до Process.Start.
   `Invoke-BoundedCommand` (native-команды): явная фиксация OEM-декодирования
   через `[Console]::OutputEncoding` (taskkill пишет OEM, а не UTF-8).
   Синтетический runner (run-emptybook, run-printflow), diag-window,
   run-guard-negative — явные кодировки декодирования. Регрессия в
   run-emptybook: условие «точный текст COUNTS»
   (`COUNTS всего=0 отправителей=0 получателей=0`) и «нет U+FFFD в stdout».
   Совместимость хостов: `Add-Type` сниппет AccWin переписан на `IntPtr[]`
   вместо `List<IntPtr>` и ссылки-ПУТИ (`System.Drawing.Common.dll` +
   `System.Private.Windows.Core.dll` при наличии) — PS 7 Add-Type без
   CoreLib-ссылки не компилирует `List<T>` (проверено 7.5.5).
2. **Часть B — компоновка FormCounterpartyEdit.** Конфликт
   `AddressEditor.Dock = Top` и ручных `Location` устранён: вкладка
   «Реквизиты» строится вертикальной `TableLayoutPanel` (тип → реквизиты →
   адаптивная сетка адресов) внутри прокручиваемого хоста (`AutoScroll`);
   панель пользовательских полей закреплена к НИЗУ вкладки (вне прокрутки);
   кнопки Сохранить/Отмена закреплены на форме. Сетка адресов: 2 колонки на
   широком клиенте, 1 колонка при ширине < 2×(320+8) (метод
   `НастроитьКолонкиАдресов`, переключение по Resize). Колонки подписей
   реквизитов/банка 90→110 px, паспорта 110→120 px. Строка примечания —
   фиксированная высота 64 (AutoSize многострочного TextBox недооценивал
   высоту, поле вытекало на 4 px). Новые публичные акцессоры:
   `РедакторЮридическогоАдреса`, `РедакторФактическогоАдреса`,
   `РедакторПочтовогоАдреса`, `ТаблицаПользовательскихПолей`,
   `СтраницаРеквизитов`. Бизнес-логика (юр/физ, ИНН/КПП/БИК-валидация,
   три адреса, основной адрес, Custom-теги) не менялась.
3. **Часть C — главный экран на 1000 px.** Группа «Вид логотипа»:
   `AutoSize=true` (высота по содержимому), ряды переключателей и клипарта
   `WrapContents=true` (перенос вместо обрезания), контейнер превью
   128→148 px (заголовок «Превью конверта C5» не обрезается). Полоса кнопок
   «Кому» разделена: первичные действия (Добавить/Изменить/Удалить/Печать
   текущего конверта/Предпросмотр и печать) и дополнительные (Открыть список
   в Excel/Сохранить вид таблицы/Вид таблиц…) — две отдельные
   AutoSize-полосы, перенос предсказуем.
4. **Часть D — политика получателя без адреса (вариант A).** Адрес не
   обязателен для СОХРАНЕНИЯ получателя (поведение сохранено); при печати
   получатели без основного адреса ПРОПУСКАЮТСЯ с явным перечислением в
   сообщении — `MainShell.ТекстФормированияОтправлений(создано,
   пропущенныеБезАдреса)`: «Пропущены без основного адреса (N): …» с
   подсказкой. Не молча, не исключением, печатное задание для пропущенных
   не формируется. Зафиксировано юнит-тестами
   (`MainShellNarrowLayoutTests.ТекстФормирования_*`).
5. **Часть E — доказательство рендеринга предпросмотра.** Новые команды
   harness: `SEEDFIELDS` (реальные BlankField `[edt_str_to1]`, `[edt_str_to2]`,
   `[edtIndeks_to]` в шаблон приёмки; координаты сеются в пересечение
   печатной области принтера и бланка; индекс — текстовое поле: кодовый
   штамп требует системного шрифта ZIPcode, отсутствующего на host-машине —
   PostIndexRenderer корректно блокирует с понятным сообщением, наблюдено),
   `SELECTLAST` (выбор последней добавленной записи — получателя) и
   `PREVIEWSCAN` (пиксельный скан страницы предпросмотра: тёмные пиксели
   всего/вне печатной области/по полям; белый холст dark≈0 PASS-ом НЕ
   считается). Новый драйвер `run-previewflow.ps1`: полный F6-цикл до окна
   «Предпросмотр печати», скан, скриншот, Закрыть, проверка спулера
   (Win32_PrintJob до/после), fail-closed-финализация как в остальных
   драйверах. Найден и ИСПРАВЛЕН дефект рендеринга:
   `PrintPreviewEngine.СоздатьМетафайл` создавал метафайл с рамкой в
   МИЛЛИМЕТРАХ от опорного растра 1×1 px — пиксельное пространство записи
   (3.78 px/мм по 96 dpi) не совпадало с пиксельным пространством рамки
   (3.216 px/мм), и DrawImage воспроизводил содержимое с искажением
   ×1.1754 от начала координат (текст уходил правее/ниже полей и в
   предпросмотре, и в печати). Изолированное воспроизведение: ожидалось
   x=150..230 y=120..127 мм, фактически x=176..270 y=143..150 мм; после
   исправления (рамка в ПИКСЕЛЯХ = опорный растр 96 dpi; потребители —
   `FormPrintPreview.Отрисовать` и скан приёмки — читают
   `страница.Width/Height` в пикселях): x=149.8..229.8 y=121.5..127.5 мм.
6. **Бонус-дефект из доказательства рендеринга.**
   `PrintDataSource.СтрокаАдреса` вычисляла номер строки РАЗНОСТЬЮ ДЛИН
   имён колонок («to_str2» − «to_str1» = 0 — у всех `*_strN` одинаковая
   длина): поля str2..str6 печатали первую адресную строку. Исправлено на
   разбор последней цифры имени колонки; регрессия
   `PrintDataSourceTests.ЗначенияПолей_КаждаяСтрокаАдреса_ВСвоёмПоле`.

7. **Независимая контрольная серия после коммита `b78ebf5`.** В ходе
   проверки обнаружены и исправлены три дефекта приёмочной обвязки/UI:
   (а) Windows PowerShell 5.1 материализует первый `StandardInput` с BOM,
   поэтому harness теперь явно читает UTF-8 и отбрасывает BOM только в
   начале команды; (б) драйверы F5/F6/preview ждут актуальное окно вместе
   с требуемой кнопкой, а не устаревший HWND с тем же заголовком «Печать»;
   (в) пояснение в диалоге «Настройка вида таблиц» переносится и остаётся
   внутри вкладки при раннем и DPI-зависимом layout. Добавлен регрессионный
   тест `FormGridAppearanceTests.ПояснениеЦветов_НеВыходитЗаПределыВкладкиИПереносится`.

**Команды и фактические результаты (все прогоны 2026-11-08).**

| Проверка | Команда | Результат |
|---|---|---|
| Build Debug | `dotnet build ./Picket302.sln -c Debug --no-restore` | 0 ошибок / 0 предупреждений |
| Build Release | `dotnet build ./Picket302.sln -c Release --no-restore` | 0 ошибок / 0 предупреждений |
| Build harness | `dotnet build ./tools/ui-acceptance/UiAcceptance.csproj -c Release --no-restore` | 0 ошибок / 0 предупреждений |
| Strict non-COM Debug | `dotnet test … -c Debug --no-build --filter 'FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests'` | 1276/1276, 0 fail, 0 skip |
| Strict non-COM Release | то же `-c Release` | 1276/1276, 0 fail, 0 skip |
| Целевые (задание) | `… --filter "FullyQualifiedName~ResponsiveUiContractTests"` (Debug) | 15/15 |
| Целевые серии | FormCounterpartyLayoutTests + MainShellNarrowLayoutTests + FormGridAppearanceTests + ResponsiveUiContractTests | 34/34 |
| Acceptance emptybook | `run-emptybook.ps1` | PS 5.1: PASS, exit 0; PS 7.5.5: PASS, exit 0 |
| Acceptance printflow | `run-printflow.ps1` | PS 5.1: PASS (FLOW F5+FLOW F6), exit 0; PS 7.5.5: PASS после исправления гонки HWND, exit 0 |
| Acceptance previewflow | `run-previewflow.ps1` | PS 5.1: PASS ×3 подряд, exit 0; PS 7.5.5: PASS, exit 0 |

Факты прогона previewflow (финальный код): PREVIEW-SCAN `страниц=1
canvasW=1296 canvasH=916 dimsOk=true outsidePrintable=0`, поля
`[edt_str_to1] dark=1883`, `[edt_str_to2] dark=827`, `[edtIndeks_to]
dark=586` (порог 30; to2≠to1 — косвенное подтверждение исправления
`СтрокаАдреса` в рантайме: строка 2 короче строки 1, до исправления
оба поля дублировали строку 1); SPOOLER до=0 после=0 (закрытие
предпросмотра не создаёт задание); пустое состояние stdout без U+FFFD;
точный текст `COUNTS всего=0 отправителей=0 получателей=0`.

**Статусы серии.**

    SOURCE_VERIFIED=true                     — исходник 57307cc; список
                                               изменённых файлов — в GIT
                                               разделе серии
    BUILD_DEBUG_VERIFIED=true                — 0/0
    BUILD_RELEASE_VERIFIED=true              — 0/0
    BUILD_ACCEPTANCE_HARNESS_VERIFIED=true   — 0/0
    STDOUT_ENCODING_VERIFIED=true            — точный текст COUNTS, 0
                                               строк с U+FFFD; хосты:
                                               Windows PowerShell
                                               5.1.26100.7462 и
                                               PowerShell 7.5.5
    UI_LAYOUT_VERIFIED=true                  — FormCounterpartyEdit:
                                               контракты 34/34 (адресные
                                               блоки не пересекаются и в
                                               границах вкладки, вкладка
                                               прокручивается, подписи в
                                               одну строку ≤110 px,
                                               пользовательская таблица
                                               положительных размеров и
                                               с Custom-тегом, кнопки
                                               видны, масштабы
                                               100/125/150 % прокси
                                               Scale-ом); скриншоты:
                                               42-card-counterparty-
                                               requisites.png
                                               (PrintWindow, ClientSize
                                               760×560), главное окно
                                               40-main-window-1000x700.png
                                               и 41-main-window-1280x720.png
                                               (SetWindowPos+Capture,
                                               отзывчиво, exit 0, профиль
                                               удалён)
    RENDER_PREVIEW_VERIFIED=true             — field-bearing шаблон,
                                               текст адресата и индекс в
                                               полях, вне печатной
                                               области 0 тёмных пикселей,
                                               canvas точен (dimsOk),
                                               спулер 0; скриншот
                                               32-предпросмотр-с-полями.png
                                               (evidence-каталог серии во
                                               временных путях, вне Git)
    RUNTIME_VERIFIED=PARTIAL                 — добавление записи/SELECT
                                               (пустая книга)/F5/F6 — в
                                               рамках printflow;
                                               политика пустого адреса —
                                               юнит-уровень (тексты) +
                                               реализация в коде;
                                               полноценного runtime-
                                               сценария «получатель без
                                               адреса → F5/F6» не
                                               выполнялось (см. ограничения)
    PRINT_VERIFIED=NOT_RUN_BY_SCOPE          — физическая печать не
                                               выполнялась (диалоги
                                               закрывались; спулер 0)
    COM_VERIFIED=NOT_RUN_BY_SCOPE            — COM не запускался
    POST_INDEX_STAMP_VERIFIED=NOT_RUN_ON_HOST — кодовый штамп требует
                                               системного шрифта ZIPcode
                                               (ТЗ §6.3), на host-машине
                                               отсутствует; блокировка с
                                               понятным сообщением
                                               наблюдена, сама отрисовка
                                               штампа НЕ проверена
    TEST_DEBUG_VERIFIED=true                 — strict non-COM 1276/1276
    TEST_RELEASE_VERIFIED=true               — strict non-COM 1276/1276
    CLEANUP_VERIFIED=true                    — процессы 0; temp-
                                               профили 0 (плюс удалены
                                               5 осиротевших каталогов от
                                               2026-11-06 — до серии);
                                               .bak/.lock 0; sentinel
                                               создан и удалён без
                                               изменения; production
                                               delta 0/0/0 в каждом
                                               прогоне; graphify-out/
                                               не изменён (untracked, вне
                                               коммита); git diff --check
                                               PASS
    FAIL_FLAKE_NOTE=BOUNDED                  — один ранний прогон
                                               previewflow (до
                                               упрочнения) поймал гонку
                                               сворачивания модалки
                                               (кнопка Закрыть не
                                               найдена/WM_NULL fail в
                                               течение ~20 c); добавлены
                                               ограниченные опросы
                                               отзывчивости (до 10 c)
                                               перед Закрыть и после;
                                               далее 3 подряд PASS. Гонка
                                               не воспроизводилась после
                                               упрочнения, корневая
                                               причина занятости UI-потока
                                               не установлена (см.
                                               ограничения)
    CODE_COVERAGE_MEASURED=false             — без изменений
    LOCK_RELEASE / TREE_CLEANUP              — из соответствующих
                                               серий 8.5–8.7, не
                                               перезапускались (вне
                                               области)

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
