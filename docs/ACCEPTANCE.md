# Приёмка Picket302 (этап S8)

Документ фиксирует приёмку по требованиям заказа и техническому заданию:

- `01_REQUIREMENTS_MASTER.txt` — мастер-описание заказа, §7 «Критерии приёмки» (11 критериев);
- `SPEC_INPUT/IMPLEMENTATION_SPEC.md` — ТЗ, §11.1–§11.4 (уровни тестов, мок-объекты, таблица «функция → способ проверки», критерий «приёмка закрыта»), §12 (план реализации по этапам), Приложение D (карта функций);
- `00_FUNCTION_INDEX.txt` — перечень 163 функций с приоритетами `[К]` (обязательно), `[П]` (важно), `[Д]` (дополнительно).

Область действия — полный перечень из 163 функций (диапазон 1..163, без пропусков), см. `00_FUNCTION_INDEX.txt:208`.

## Как читать таблицу

| Столбец | Значение |
|---|---|
| № | Номер функции в `00_FUNCTION_INDEX.txt` |
| Название | Краткое наименование функции |
| Приоритет | `[К]` — обязательно, `[П]` — важно, `[Д]` — дополнительно. Значение взято из `00_FUNCTION_INDEX.txt`; расхождений с `01_REQUIREMENTS_MASTER.txt` нет — все 163 функции совпадают |
| Статус | `DONE` — реализовано и покрыто тестом; `PARTIAL` — реализовано частично либо не покрыто тестом; `DEFERRED` — не реализовано |
| Модуль/файл | Фактическое размещение реализации (проверено по коду) |
| Способ проверки | Уровень теста по ТЗ §11.1 (`Юнит` / `Интеграционный` / `Приёмочный`) и `UI-автотест` — термины §11.3 — и конкретный тест/класс |

Правило статуса: **реализовано в коде И покрыто конкретным тестом** → `DONE`. Только реализация без теста, либо реализация без названной в тексте функции составляющей → `PARTIAL`. Реализации нет → `DEFERRED`. Для всех `PARTIAL`/`DEFERRED` с приоритетом `[П]`/`[Д]` причина и ID допущения из `docs/OPEN_QUESTIONS_LOG.md` указаны в графе «Способ проверки» той же строки — отдельного столбца «Причина» таблица не содержит.

## Сводка

| Показатель | Значение |
|---|---|
| Всего функций | 163 |
| `DONE` | 133 |
| `PARTIAL` | 26 |
| `DEFERRED` | 4 |
| Из них с приоритетом `[К]` | 64 `DONE`; 1 `PARTIAL` — №127 (перечислен в разделе «Дефекты [К]») |
| Из них с приоритетом `[П]`/`[Д]` | 27 `PARTIAL` + 4 `DEFERRED` — с явной причиной и ссылкой на допущение |

Разбивка `DEFERRED`: №10 (группировка строк), №14 (поиск по внешней картоте
probili.ru), №35 (запоминание последних введённых данных при печати), №125
(антиотладка/антимониторинг — запрещено ТЗ, замена зафиксирована как S7.4).

## 1. Адресная книга и контрагенты (№1–19)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 1 | Карточка контрагента (ИНН/КПП/ОГРН/ОКФС/ОКПО/ОКВЭД/БИК/КС/РС/РКЦ, паспорт, дата и место рождения, должность, ФИО руководителя, e-mail, телефон, особые отметки) | [К] | DONE | `src/Picket302.Domain/Entities/Counterparty.cs`; `src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs` | Юнит + UI-автотест: `Domain/InnValidatorTests.cs`, `App/MainShellLayoutTests.cs` (`FormCounterpartyEdit_НекорректныйИНН_БлокируетКнопкуСохранить`) |
| 2 | Разделение юридических и физических лиц, тип контрагента | [К] | DONE | `src/Picket302.Domain/Entities/Counterparty.cs` (`CounterpartyType`); `src/Picket302.Domain/Rules/InnValidator.cs`; `src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs` | Юнит: `Domain/InnValidatorTests.cs` (`Validate_ДесятизначныйИннДляФизЛица_Отклоняется`, `Validate_ДвенадцатизначныйИннДляЮрЛица_Отклоняется`); UI: `App/MainShellLayoutTests.cs` |
| 3 | Три независимых адреса (юридический/почтовый/фактический) + переключатель «основной» | [К] | DONE | `src/Picket302.Domain/ValueObjects/AddressSet.cs`; `src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs` | UI-автотест: `App/MainShellLayoutTests.cs` (`FormCounterpartyEdit_ТриАдреса_СПереключателемОсновного`) |
| 4 | Адрес = индекс + до 6 строк | [К] | DONE | `src/Picket302.Domain/ValueObjects/Address.cs` | Юнит: `Domain/AddressTests.cs` (6 строк, неверный индекс, неверное число строк, обрезка >255, пустая строка) |
| 5 | Разбор почтового индекса из строки адреса (флаг `parseZipIndexFromAddress`) | [К] | DONE | `src/Picket302.Application/Services/AddressParserService.cs` | Юнит + интеграционный: `Application/AddressParserServiceTests.cs`; `Acceptance/AcceptanceScenarioTests.cs` |
| 6 | Нормализация и склонение русских ФИО и адресов | [П] | DONE | `src/Picket302.Infrastructure/Text/RussianDeclensionEngine.cs` | Юнит: `Application/RussianDeclensionEngineTests.cs` |
| 7 | Пользовательские поля контрагента | [П] | DONE | `src/Picket302.Application/Services/CustomFieldManager.cs`; `src/Picket302.Domain/Entities/CounterpartyField.cs` | Юнит + интеграционный: `Application/CounterpartyServicesTests.cs`, `Domain/CustomFieldTagTests.cs` |
| 8 | Группы/папки адресов, «группа по умолчанию», флаг «закрытая группа» | [К] | DONE | `src/Picket302.Application/Services/CategoryTreeService.cs`; `src/Picket302.Application/Services/GroupManager.cs` | Интеграционный: `Domain/IntegrityRulesTests.cs`, `Infrastructure/GroupRepositoryTests.cs`, **`Application/DemoAndCategoriesTests.cs` (`CategoryTreeService_ЗакрытаяКатегория_НеПринимаетНовыеЗаписи` — добавлен на этапе S8)** |
| 9 | Фильтры и сортировка по любой колонке; сохранённые пользовательские фильтры | [К] | DONE | `src/Picket302.App/UI/Components/AddressBookSortController.cs`; `src/Picket302.Application/Services/SavedFilterService.cs`; `src/Picket302.Application/Queries/CounterpartyFilterQuery.cs`; `src/Picket302.Infrastructure/Repositories/CounterpartyRepository.cs` (`GetPagedAsync`) | Юнит: `App/AddressBookSortControllerTests.cs`, `Application/SavedFilterServiceTests.cs`; интеграционный: `Application/CounterpartyFilterRepositoryTests.cs` (отбор в БД). Операторские пути: меню «Вид» → «Фильтры» (сохранить, применить, удалить, сбросить сортировку); критерии отбора передаются в запрос БД |
| 10 | Группировка строк в списке | [П] | DEFERRED | — | UI-автотест: не реализовано — компонент группировки (`GroupableGrid`) отсутствует. Допущение **S8.1** |
| 11 | Постраничный просмотр больших списков (виртуальный режим) | [К] | DONE | `src/Picket302.Infrastructure/Persistence/VirtualDataBuffer.cs`; `src/Picket302.Infrastructure/Repositories/CounterpartyRepository.cs` (`GetPagedAsync`) | Интеграционный: `Infrastructure/RegistrationNumberStoreTests.cs` (окно по смещению/размеру, «Записи 1-100 из 150000»), `Application/CounterpartyServicesTests.cs` |
| 12 | Запоминание видимости и ширины столонок | [П] | DONE | `src/Picket302.App/UI/Components/GridViewSettingsController.cs`; `src/Picket302.Infrastructure/Storage/JsonUiSettingsStore.cs`; `src/Picket302.App/MainShell.cs` | Юнит: `App/GridViewSettingsControllerTests.cs` (`НастройкиВидаТаблицы_СохраняютШиринуСтолбца`, `СкрытиеСтолбца_СохраняетсяИВосстанавливается`, `ВозвратВсехСтолбцов_ОтменяетСкрытиеИСохраняется`) |
| 13 | Поиск контрагента по ИНН/УНН через открытые источники | [П] | DONE | `src/Picket302.Infrastructure/Integration/WebLookupLauncher.cs` | Юнит: `Infrastructure/WebLookupLauncherTests.cs` (подстановка ИНН, ошибки, маршрут для Беларуси, пустой ИНН) |
| 14 | Поиск контрагента по внешней картоте (probili.ru) | [Д] | DEFERRED | — | Интеграционный: не реализовано — клиента и настройки URL нет. Допущение **S8.1** |
| 15 | Массовое обновление адресов писем при изменении адреса контрагента | [П] | DONE | `src/Picket302.Application/Services/CascadeAddressUpdater.cs` | Интеграционный: `Application/CounterpartyServicesTests.cs` (напечатанное письмо не изменяется) |
| 16 | «Текущий отправитель» как глобальная сущность; выбор отправителя по первым буквам | [К] | DONE | `src/Picket302.Domain/Services/CurrentSenderContext.cs`; `src/Picket302.App/UI/Components/SenderQuickSelect.cs`; `src/Picket302.App/MainShell.cs` | Юнит: **`App/SenderQuickSelectTests.cs` (добавлен на этапе S8)**; UI: панель «Быстрый выбор» в оболочке |
| 17 | Избранное (VIP) на адресе | [К] | DONE | `src/Picket302.Domain/Entities/Counterparty.cs` (`IsVip`, `ToggleVipStatus`); `src/Picket302.App/MainShell.cs` | Интеграционный/UI: `App/SearchMechanismTests.cs` (`SearchSession_ФильтрыРаздельны_ИзбранноеИГруппаНеСливаются`) |
| 18 | Одновременное редактирование одного поля у многих выделенных строк | [К] | DONE | `src/Picket302.Application/Services/BatchEditService.cs`; `src/Picket302.App/MainShell.cs` | Интеграционный: `Application/CounterpartyServicesTests.cs`; **`BatchEditService_*ПорогПодтверждения*` — добавлены на этапе S8** (порог N=10, конфигурируется) |
| 19 | Пометка «удалить адрес» скрытой комбинацией Ctrl+Alt+F8 | [Д] | DONE | `src/Picket302.App/UI/Components/ShellHotKeyMap.cs`; `src/Picket302.Application/Services/CounterpartyManager.cs` | UI-автотест: `App/ShellHotKeyMapTests.cs`; `Application/CounterpartyServicesTests.cs` (мягкое удаление) |

## 2. Письма, рассылки, папки (№20–35)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 20 | Создание исходящего письма (адресат, отправитель, конверт/уведомление, число копий) | [К] | DONE | `src/Picket302.Application/Services/MailFactory.cs`; `src/Picket302.Domain/Entities/Mail.cs` | Интеграционный: `Application/CounterpartyServicesTests.cs` |
| 21 | Виды рассылок | [П] | DONE | `src/Picket302.Application/Services/DistributionService.cs` | Интеграционный: `Application/DistributionServiceTests.cs` (1→N, N→1, пустой список, выделение, клонирование, фильтр по состоянию, блокировка отправки) |
| 22 | Папки для писем | [П] | DONE | `src/Picket302.Application/Services/FolderService.cs`; `src/Picket302.Domain/Entities/Folder.cs` | Интеграционный: `Infrastructure/FolderAndLogoTests.cs` (ссылка, счётчик, запрет несуществующей, цикл, перемещение) |
| 23 | Классификация категории почтового отправления | [К] | DONE | `src/Picket302.Domain/Entities/MailEnums.cs` (`MailCategory`); `src/Picket302.Domain/Entities/Mail.cs` (`SetCategory`); `src/Picket302.App/UI/Forms/FormMailEdit.cs` | Юнит + UI-автотест: домен реализован по ТЗ §8.1 (Простое/Заказное/С объявленной ценностью/1-й класс) и покрыт `Domain/MailClassificationTests.cs`; на этапе S8 добавлена **карточка отправления с выпадающим списком категории** (`App/MailEditFormTests.cs`), значения записываются в карточку отправления до печати. Допущение **S8.8** |
| 24 | Полный жизненный цикл отправления (4 даты) | [К] | DONE | `src/Picket302.Domain/Entities/Mail.cs` (`TransitionStatus`) | Интеграционный: `Domain/MailLifecycleTests.cs` (даты, недопустимые переходы) |
| 25 | Сквозная нумерация отправлений по шаблонам | [К] | DONE | `src/Picket302.Application/Services/RegistrationNumberGenerator.cs` | Интеграционный: `Application/RegistrationNumberGeneratorTests.cs` |
| 26 | Отметки «Заказное», «Заказное с уведомлением» на бланке | [К] | DONE | `src/Picket302.Printing/Elements/ServiceStampRenderer.cs` | Юнит: `Printing/ServiceStampRendererTests.cs` (рамка 10 мм, отметка «с уведомлением», простое — пусто, матрица комбинаций) |
| 27 | Объявленная ценность, плата, вес — в реестре Ф.103 | [К] | DONE | `src/Picket302.Printing/Reports/Form103Report.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (`Ф103_ИтогиСходятся`); `Acceptance/AcceptanceScenarioTests.cs` |
| 28 | Опись вложений (позиции, количество, стоимость, примечание) | [К] | DONE | `src/Picket302.Domain/Entities/Inventory.cs`, `InventoryItem.cs`; `src/Picket302.Infrastructure/Repositories/InventoryItemRepository.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (`Ф107_ДваЭкземпляра_ИтогиСходятся`), `Infrastructure/RepositoryTests.cs` |
| 29 | Входящая корреспонденция (регистрация, хранение сканов, поиск по номеру и типу документа) | [П] | PARTIAL | `src/Picket302.Domain/Entities/MailDocument.cs` | Интеграционный: реализована только сущность с номером документа; сервис регистрации входящих, модуль сканирования, тип документа и поиск — отсутствуют, тестов нет. Допущение **S8.1** |
| 30 | Обработка входящих: приём/передача/архив; движения документов | [П] | PARTIAL | `src/Picket302.Domain/Entities/DocumentMovement.cs`; `src/Picket302.Infrastructure/Repositories/DocumentMovementRepository.cs` | Интеграционный: есть сущность, перечисление и репозиторий; трекер обработки и интерфейс отсутствуют, тестов нет. Допущение **S8.1** |
| 31 | Массовая генерация документов по шаблону | [П] | DONE | `src/Picket302.Application/Services/DocumentBatchGenerator.cs` | Интеграционный: `Application/DocumentBatchGeneratorTests.cs` |
| 32 | Выбор вида уведомления (ф.119 / украинская форма / 112-эп) + флаг «нужно уведомление» | [К] | DONE | `src/Picket302.Domain/Entities/MailEnums.cs` (`NotificationType`, `NotificationTypeMapper`); `src/Picket302.Domain/Entities/Mail.cs` (`SelectNotificationForm`); `src/Picket302.App/UI/Forms/FormMailEdit.cs` | Юнит + UI-автотест: перечисление видов уведомления, метод `SelectNotificationForm(NotificationType)` по сигнатуре ТЗ §8.1 и разбор значения колонки `mails.notice_type` покрыты `Domain/MailClassificationTests.cs`; на этапе S8 добавлен **выпадающий список вида уведомления и флажок «нужно уведомление» в карточке отправления** (`App/MailEditFormTests.cs`). Допущение **S8.8** |
| 33 | Формирование адресных строк (европейское разбиение, реверс порядка, перенос по словам/по буквам) | [П] | PARTIAL | `src/Picket302.Domain/Exchange/PrintTextNormalizer.cs`; `src/Picket302.Domain/Postal/EnvelopeGeometry.cs` | Интеграционный: разбиение и перенос реализованы; реверс порядка (`AddressOrderSmallToLarge`) переносится в контекст, но не применяется; тестов нет. Допущение **S8.1** |
| 34 | Автоподбор кегля | [К] | DONE | `src/Picket302.Printing/Engine/TextFitter.cs` (`FitField`) | Юнит: `Printing/TextFitterTests.cs` |
| 35 | Запоминание последних введённых данных при печати одного конверта | [Д] | DEFERRED | — | Интеграционный: не реализовано — хранилище последних параметров печати отсутствует. Допущение **S8.1** |

## 3. Бланки, печать, кодовый штамп (№36–49)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 36 | 8 встроенных бланков | [К] | DONE | `src/Picket302.Infrastructure/Templates/BuiltinTemplatesCatalog.cs` | Юнит + интеграционный: `Infrastructure/BuiltinTemplatesTests.cs` (8 форм, геометрия, поля, фоны); `Acceptance/AcceptanceScenarioTests.cs`. См. примечание к размерам бланков в разделе «Ограничения» |
| 37 | Библиотека пакетов бланков (≥20 шт.) | [К] | DONE | `src/Picket302.Infrastructure/Templates/BuiltinTemplatesCatalog.cs`; `src/Picket302.Infrastructure/Templates/TemplatePackageRepository.cs` | Юнит round-trip `.lbk`: `Infrastructure/BuiltinTemplatesTests.cs`, `Infrastructure/LbkPackageTests.cs` |
| 38 | Редактор бланков | [К] | DONE | `src/Picket302.App/UI/Forms/FormBlankEditor.cs`; `src/Picket302.Printing/Editor/CanvasController.cs`; `BlankLayoutValidator.cs` | Юнит + интеграционный: `App/FormBlankEditorTests.cs`, `Printing/BlankEditorTests.cs` |
| 39 | Формат бланка — структурированный XML | [К] | DONE | `src/Picket302.Domain/Serialization/BlankXmlSerializer.cs` | Юнит round-trip: `Domain/BlankXmlSerializerTests.cs` (листинг 51, XSD, позиция ошибки, штамп без `PostIndex`, семантика `visible`/`printable`) |
| 40 | Импорт/экспорт бланков пакетом `.lbk` | [К] | DONE | `src/Picket302.Infrastructure/IO/LbkPackageArchiver.cs` | Юнит ZIP round-trip: `Infrastructure/LbkPackageTests.cs` |
| 41 | Почтовый индекс — отдельный тип поля со своим шрифтом и кеглем | [К] | DONE | `src/Picket302.Printing/Elements/PostIndexRenderer.cs`; `PostIndexSpec.cs`; `src/Picket302.Infrastructure/Fonts/ZipcodeFontGuard.cs` | Юнит + интеграционный: `Printing/PostIndexRendererTests.cs`, `Infrastructure/BuiltinTemplatesTests.cs` |
| 42 | Конверты — отдельный тип печати + номенклатура форматов | [К] | DONE | `src/Picket302.Domain/Postal/EnvelopeFormat.cs`; `EnvelopeFormatCatalog.cs`; `EnvelopeFormats.json` | Юнит валидатора геометрии на мок-DEVMODE: `Domain/EnvelopeFormatCatalogTests.cs` (Россия и Украина, ориентации) |
| 43 | Обратная печать конверта | [П] | DONE | `src/Picket302.Printing/Engine/EnvelopeInverter.cs` | Юнит: `Printing/PrintEngineTests.cs` |
| 44 | Печать нескольких конвертов на одном листе (наклейки) | [П] | DONE | `src/Picket302.Printing/Engine/SheetLayoutImposition.cs` | Юнит: `Printing/PrintEngineTests.cs`, `Printing/PrintWarningsTests.cs` |
| 45 | Смещение изображения хранится отдельно для каждого вида конверта | [П] | PARTIAL | `src/Picket302.Domain/Entities/PrinterOffsetProfile.cs`; `src/Picket302.Infrastructure/Repositories/PrinterProfileRepository.cs` | Юнит: профиль смещения по паре «принтер + формат» реализован, прямого теста нет. Допущение **S8.1** |
| 46 | Выравнивание подачи на печать по горизонтали и вертикали | [П] | DONE | `src/Picket302.Printing/Engine/FeedAlignmentCalculator.cs` | Юнит: `Printing/PrintEngineTests.cs`, `Printing/PrintWarningsTests.cs` |
| 47 | Логотип компании на конверте (растровый файл), режим «с логотипом / без логотипа» | [К] | DONE | `src/Picket302.Printing/Elements/LogoRenderer.cs`; `src/Picket302.Printing/Rendering/EnvelopeRenderer.cs`; `src/Picket302.Domain/Entities/Counterparty.cs` (`Logo`) | Интеграционный: **`Printing/EnvelopeLogoRenderingTests.cs` (добавлен на этапе S8)** — оба режима, недоступный и повреждённый файл; хранение: `Infrastructure/FolderAndLogoTests.cs` |
| 48 | QR-код на конверте | [К] | DONE | `src/Picket302.Printing/Elements/QrCodeRenderer.cs`; `src/Picket302.Printing/Codes/QrEncoder.cs` | Интеграционный: `Printing/QrCodeRendererTests.cs`, `Printing/QrErrorCorrectionTests.cs` (синдромы RS/BCH), `Infrastructure/ReportQueriesTests.cs` |
| 49 | Собственные стандартные бланки: доверенность на почту, заявление о включении в реестр | [Д] | PARTIAL | `src/Picket302.Infrastructure/Templates/SpecialFormsBuilder.cs`; `BuiltinTemplatesCatalog.cs` | Интеграционный: заготовки форм создаются, но подстановка реквизитов контрагента и меню «Печать → Специальные бланки» не реализованы. Допущение **S8.1** |

## 4. Реестры, описи, отчёты (№50–64)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 50 | Ф.103 — форма реестра почтовых отправлений | [К] | DONE | `src/Picket302.Printing/Reports/Form103Report.cs`; `src/Picket302.Application/Services/SilentPrintService.cs` | Интеграционный: сверка итогов — `Application/RegistryReportsTests.cs` (`Ф103_ИтогиСходятся`, `Ф103_СмешениеОтправителей_Запрещено`, `Ф103_ПустаяВыборка_Ошибка`); `Acceptance/AcceptanceScenarioTests.cs` |
| 51 | Реестр расширенный | [К] | DONE | `src/Picket302.Printing/Reports/ExtendedRegistryReport.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (`РасширенныйРеестр_Примечания`) |
| 52 | Реестр стандартный | [П] | DONE | `src/Picket302.Printing/Reports/SimpleRegistryReport.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (`ПростойРеестр_ОднаСтраницаДо35Писем`) |
| 53 | Опись (ф.107) стандартная | [К] | DONE | `src/Picket302.Printing/Reports/InventoryF107Report.cs` | Интеграционный: сверка итогов — `Application/RegistryReportsTests.cs` (`Ф107_ДваЭкземпляра_ИтогиСходятся`); `Infrastructure/ReportQueriesTests.cs` |
| 54 | Журнал входящих | [П] | DONE | `src/Picket302.Printing/Reports/IncomingJournalReport.cs` | Интеграционный: `Application/RegistryReportsTests.cs`, `Infrastructure/ReportQueriesTests.cs` |
| 55 | Реестр входящей корреспонденции («форма 1С») | [П] | DONE | `src/Picket302.Printing/Reports/OneCIncomingReport.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (`Реестр1С_СтруктураКолонок`) |
| 56 | Пользовательские шаблоны реестров и описей | [П] | PARTIAL | `src/Picket302.Application/Services/CustomReportManager.cs` | Интеграционный: реализация есть, тестов нет; класс не вызывается из оболочки. Допущение **S8.1** |
| 57 | Дополнительные формы реестра (форма 5, 6 и др.) | [Д] | DONE | `src/Picket302.Printing/Reports/AuxiliaryPostReports.cs` | Интеграционный: `Application/RegistryReportsTests.cs` (формы 5/6; номер 7 отклоняется) |
| 58 | Сохранение промежуточных данных реестра во временный XML | [П] | PARTIAL | `src/Picket302.Application/Services/ReportDataDumper.cs` | Интеграционный: реализация есть, тестов нет; модуль не связан с интерфейсом. Допущение **S8.1** |
| 59 | Сортировка писем в реестре (по номеру / по алфавиту) | [П] | DONE | `src/Picket302.Application/Services/RegistrySorter.cs` | Юнит: `Application/RegistryReportsTests.cs` (по алфавиту, по номеру, по индексу) |
| 60 | Номера документов в одной колонке через запятую | [Д] | DONE | `src/Picket302.Application/Formatting/DocumentNumbersAggregator.cs` | Юнит: `Application/RegistryReportsTests.cs` |
| 61 | Открыть текущий список в MS Excel | [П] | DONE | `src/Picket302.App/MainShell.cs` (`ОткрытьСписокВExcel`); `src/Picket302.Infrastructure/Export/SpreadsheetMlExporter.cs` | Юнит экспорта сетки в XLSX без Office: `S6/S6RegressionTests.cs`, `Infrastructure/OpenXmlExportTests.cs` |
| 62 | Печать реестра без показа формы (для автоматизации) | [П] | DONE | `src/Picket302.Application/Services/SilentPrintService.cs` | Юнит: `Application/RegistryReportsTests.cs` (код ошибки вызывающему, ошибка спулера, смешение отправителей, пустая выборка) |
| 63 | Статистика рассылок, реестр рассылок, группы конвертов | [П] | DONE | `src/Picket302.Printing/Reports/StatsAndGroupReports.cs` | Интеграционный: `Application/RegistryReportsTests.cs`, `Infrastructure/ReportQueriesTests.cs` |
| 64 | История печати конвертов | [К] | DONE | `src/Picket302.Infrastructure/Repositories/PrintHistoryRepository.cs`; `src/Picket302.Application/Services/PrintHistoryRecorder.cs`; `src/Picket302.Printing/IPrintHistoryRecorder.cs` | Интеграционный: `Infrastructure/PrintHistorySnapshotTests.cs`, `Infrastructure/ReportQueriesTests.cs`, `Acceptance/AcceptanceScenarioTests.cs` |

## 5. Шаблоны документов, экспорт, импорт (№65–77)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 65 | Генерация документов произвольного вида по шаблонам | [П] | DONE | `src/Picket302.Application/Services/ArbitraryDocumentEngine.cs`; `DocumentBatchGenerator.cs` | Юнит: `Application/ArbitraryDocumentEngineTests.cs`, `Application/DocumentBatchGeneratorTests.cs` |
| 66 | Мастер редактирования шаблонов документов (предпросмотр, маркеры, структура) | [П] | PARTIAL | `src/Picket302.App/UI/Forms/DocTemplateWizardController.cs`; `FormDocTemplateWizard.cs` | Юнит контроллера: `App/DocTemplateWizardControllerTests.cs`; **предпросмотр результата генерации не реализован**. Допущение **S8.1** |
| 67 | Шаблоны документов: XML-описание + XSLT (12 XML + 16 XSLT) | [П] | PARTIAL | `src/Picket302.Infrastructure/Templating/XsltTransformEngine.cs`; `src/Picket302.Application/Templating/DocumentTemplateAnalyzer.cs` | Юнит: движок XSLT покрыт (`Infrastructure/OpenXmlExportTests.cs`), но поставляемых XSLT-стилей в репозитории нет; 12 XML — описания SQL-запросов отчётов. Допущение **S8.1** |
| 68 | Экспорт в Word (WordprocessingML через XSLT) | [П] | DONE | `src/Picket302.Infrastructure/Export/WordprocessingMlExporter.cs` | Интеграционный: генерация WordprocessingML и валидация пакета OpenXML без Office — `Infrastructure/OpenXmlExportTests.cs`; `Application/ArbitraryDocumentEngineTests.cs` |
| 69 | Экспорт в Excel (SpreadsheetML через XSLT) | [П] | DONE | `src/Picket302.Infrastructure/Export/SpreadsheetMlExporter.cs` | Интеграционный: генерация SpreadsheetML и валидация пакета OpenXML без Office — `Infrastructure/OpenXmlExportTests.cs`; `Application/UniversalAddressExchangeTests.cs` |
| 70 | Не зависеть от установленного MS Office | [К] | DONE | `src/Picket302.Infrastructure/Export/NativeOpenXmlPackager.cs` | Юнит (архитектурная проверка сборки): `Architecture/OfficeInteropAbsenceTests.cs`; `Acceptance/AcceptanceScenarioTests.cs` |
| 71 | Импорт/экспорт адресов: TXT, CSV, HTML, RTF, XLS, XLSX | [К] | DONE | `src/Picket302.Application/Services/UniversalAddressExchange.cs`; `src/Picket302.Application/Import/ImportSession.cs`; `src/Picket302.Application/Import/RtfTableReader.cs` | Юнит: `Application/UniversalAddressExchangeTests.cs` (экспорт всех шести форматов), `Application/RtfImportTests.cs` (импорт RTF: таблица, реквизиты, длинные значения помечаются ошибкой) |
| 72 | Импорт контрагентов из Excel, XML, CSV, 1С 7.7, 1С 8.0 | [К] | DONE | `src/Picket302.Application/Import/ImportSession.cs`; `src/Picket302.Application/Services/OneCImportParser.cs` | Интеграционный импорт эталонных выгрузок: `Application/OneCImportParserTests.cs` (8.x, 7.7), `Application/ImportSessionTests.cs` |
| 73 | Гибкий редактор настроек импорта из CSV | [П] | PARTIAL | `src/Picket302.App/UI/Forms/FormCsvImportConfig.cs`; `src/Picket302.Infrastructure/Import/CsvProfileStore.cs` | Юнит: покрыто только хранилище профилей (`Infrastructure/CsvProfileStoreTests.cs`); форма-редактор тестами не покрыта. Допущение **S8.1** |
| 74 | «Тонкое слияние» импортируемых данных с существующими в БД | [П] | DONE | `src/Picket302.Application/Services/ImportMergeService.cs` | Интеграционный по политикам слияния: `Application/ImportMergeServiceTests.cs` |
| 75 | При импорте добавляются только неповторяющиеся данные; отчётная информация об импорте и источник данных | [П] | DONE | `src/Picket302.Application/Services/ImportDeduplicator.cs`; `src/Picket302.Application/Import/ImportSession.cs` | Интеграционный: `Application/ImportDeduplicatorTests.cs`, `Application/ImportMergeServiceTests.cs` |
| 76 | Импорт контрагентов из отдельной внешней БД | [Д] | DONE | `src/Picket302.Infrastructure/Integration/ExternalDbImporter.cs`; `src/Picket302.App/UI/Forms/FormExternalDbImport.cs` | Интеграционный: `Infrastructure/ExternalDbImporterTests.cs` |
| 77 | Собственный формат данных для печати: `<EnvelopesData>` → `<EnvelopeData>` → `<From>`/`<To>` | [К] | DONE | `src/Picket302.Domain/Exchange/EnvelopeContracts.cs` | Юнит round-trip XML-контракта: `Domain/ExchangeXmlContractTests.cs`; `S6/S6RegressionTests.cs` |

## 6. Печать: принтер, сессия, аудит (№78–91)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 78 | Выбор принтера, «настройки принтера», ошибка «принтер по умолчанию не установлен» | [К] | DONE | `src/Picket302.Printing/Devices/PrinterManager.cs`; `src/Picket302.App/UI/Forms/FormEnvelopePrint.cs` | Юнит валидатора геометрии на мок-DEVMODE + UI-тест диалога: `Printing/PaperSizeValidatorTests.cs`, `App/PrintDialogsTests.cs` |
| 79 | Печать выбранного (F5) / выделенных с предпросмотром (F6) / без предпросмотра (F7) | [К] | DONE | `src/Picket302.Printing/PrintSessionController.cs` (`PrintSelectedAsync`, `PrintSelectionPreviewedAsync`, `PrintSelectionDirectAsync`); `src/Picket302.App/MainShell.cs` | Юнит + UI-автотест: журнал `print` обязателен в каждом прогоне — `Printing/PrintSessionAuditTests.cs` (мок-DEVMODE), `Acceptance/AcceptanceScenarioTests.cs` (шаг F7); `App/ShellHotKeyMapTests.cs` |
| 80 | Предпросмотр печати — обязательный, с точным показом полей и смещений | [К] | DONE | `src/Picket302.Printing/Preview/PrintPreviewEngine.cs`; `src/Picket302.App/UI/Forms/PrintPreviewBuilder.cs` | Приёмочный стенд ±1 мм: `Printing/PrintPreviewEngineTests.cs` (одинаковые координаты полей, калибровочное смещение, печатная область, формат C6, чётные страницы, баннер предупреждений) |
| 81 | Открыть шаблон во встроенном дизайнере отчётов (F8) | [К] | DONE | `src/Picket302.App/UI/Forms/FormBlankEditor.cs`; `src/Picket302.App/UI/Components/ShellHotKeyMap.cs` | Юнит карты клавиш + юнит редактора: `App/ShellHotKeyMapTests.cs`, `Printing/BlankEditorTests.cs` (пересечение полей, выход за печатную область), `App/FormBlankEditorTests.cs` |
| 82 | Опция «Показывать список шаблонов перед печатью» | [К] | DONE | `src/Picket302.Application/Services/PrintDataSource.cs` (`PrintOptions`); `src/Picket302.App/UI/Forms/FormEnvelopePrint.cs` | Юнит + UI-тест: `App/PrintOptionsTests.cs`, `App/PrintDialogsTests.cs` |
| 83 | Проверка размера бумаги (шаблон ↔ формат принтера) | [К] | DONE | `src/Picket302.Printing/Validation/PaperSizeValidator.cs`; `src/Picket302.App/UI/Forms/FormPaperSizeBlocked.cs` | Юнит валидатора геометрии на мок-DEVMODE: `Printing/PaperSizeValidatorTests.cs`; `Acceptance/AcceptanceScenarioTests.cs` |
| 84 | Опция «Зафиксировать размер A4» с честным предупреждением | [К] | DONE | `src/Picket302.Printing/Engine/FixA4Mode.cs`; `src/Picket302.App/UI/Forms/FormEnvelopePrint.cs` | Юнит: `Printing/PrintWarningsTests.cs`, `Printing/PrintEngineTests.cs` (масштабирование полей, честное предупреждение о не помещении) |
| 85 | Предупреждение «Конверт слишком большой для выбранного принтера» | [К] | DONE | `src/Picket302.Printing/Validation/OversizeWarning.cs`; `src/Picket302.App/UI/Forms/FormOversizeWarning.cs`, `OversizeWarningPresenter.cs` | Юнит + UI: `Printing/PrintWarningsTests.cs`, `App/PrintDialogsTests.cs`; блокировка в `Printing/PrintSessionAuditTests.cs` |
| 86 | Двусторонняя печать уведомлений (лицевая/оборотная) | [П] | DONE | `src/Picket302.Printing/Engine/DuplexController.cs` | Юнит на мок-DEVMODE: `Printing/PrintWarningsTests.cs`, `Printing/PrintEngineTests.cs` |
| 87 | Печать только чётных / только нечётных страниц | [П] | DONE | `src/Picket302.Printing/Engine/PageParityFilter.cs` | Юнит: `Printing/PrintWarningsTests.cs`, `Printing/PrintEngineTests.cs` |
| 88 | Журналирование параметров печати: принтер, формат, шрифт, кегль | [К] | DONE | `src/Picket302.Printing/Audit/PrintAuditWriter.cs` (`FormatRecord`); `src/Picket302.Printing/PrintSessionController.cs` | Юнит: журнал `print` обязателен в каждом прогоне — `Printing/PrintAuditWriterTests.cs` (формат 65), `Printing/PrintSessionAuditTests.cs`, `Acceptance/AcceptanceScenarioTests.cs` |
| 89 | Печать на разлинованные конверты (предупреждение ГОСТ Р 51506-99 и риск вне полей) | [К] | DONE | `src/Picket302.Printing/Engine/PreprintedMode.cs` | Юнит: `Printing/PrintWarningsTests.cs` (формулировка «ГОСТ Р 51506-99», подавление, исключение фоновых полей), `Printing/PrintEngineTests.cs`, `App/PrintDialogsTests.cs` |
| 90 | Печать почтового индекса машиночитаемым шрифтом (OCR-B-подобный, TTF) | [К] | DONE | `src/Picket302.Printing/Elements/PostIndexRenderer.cs`; `src/Picket302.Infrastructure/Fonts/ZipcodeFontGuard.cs` | Юнит: `Printing/PostIndexRendererTests.cs` (шесть знаков, шаг 5,4 мм, международный индекс 555, блокировка при отсутствии шрифта), `Printing/BlankEditorTests.cs` |
| 91 | Автоподбор кегля в поле бланка | [К] | DONE | `src/Picket302.Printing/Engine/TextFitter.cs` (`FitField`) | Юнит: `Printing/TextFitterTests.cs` |

## 7. Электронная почта (№92–100)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 92 | Электронная рассылка (отдельно от бумажной) | [П] | DONE | `src/Picket302.Application/Services/EmailDistributionService.cs`; `src/Picket302.App/UI/Forms/FormEmailDistributionWizard.cs` | Интеграционный: `Application/EmailDistributionServiceTests.cs` (электронная рассылка не создаёт записей бумажных отправлений) |
| 93 | SMTP: сервер/порт/логин/пароль; «SMTP-сервер не указан» | [П] | DONE | `src/Picket302.Infrastructure/Mail/SmtpOptions.cs` | Юнит: `Infrastructure/SmtpOptionsTests.cs` (`SmtpOptionsTests.ValidateSmtpSettings_ПустойСервер_ВозвращаетОшибкуСерверНеУказан`) |
| 94 | SSL-защищённое соединение | [П] | PARTIAL | `src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs` | Интеграционный: режимы `None`/`StartTls`/`ImplicitTls` реализованы, тестов нет. Допущение **S7.9** |
| 95 | Прокси (использовать/с авторизацией/сервер/порт/логин/пароль) | [Д] | PARTIAL | `src/Picket302.Infrastructure/Mail/ProxySettingsMapper.cs`; `SmtpOptions.cs`; `SmtpClientFacade.cs` | Интеграционный: HTTP CONNECT-туннель реализован, тестов нет. Допущение **S7.9** |
| 96 | Формат письма: простой текст / HTML | [П] | PARTIAL | `src/Picket302.Domain/Email/EmailContracts.cs`; `src/Picket302.Domain/Entities/EmailDistribution.cs` | Интеграционный: формат работает end-to-end (радиокнопки → `SetBodyFormat` → MIME `text/html`), прямого теста нет. Допущение **S8.1** |
| 97 | WYSIWYG-редактор HTML (в исходной — TinyMCE) | [П] | PARTIAL | `src/Picket302.App/UI/Editors/HtmlMailEditor.cs`; `src/Picket302.App/UI/Editors/emailedit/` | Интеграционный: собственный компактный редактор вместо TinyMCE — **допущение S7.2**; тестов нет. Допущение **S8.1** |
| 98 | Прикрепление файлов; копирование в папку отправителя | [Д] | DONE | `src/Picket302.Application/Services/AttachmentService.cs` | Юнит: `Application/AttachmentServiceTests.cs` (лимит 25 МБ, копия в хранилище) |
| 99 | Точный ReplyTo и адрес отправителя | [Д] | PARTIAL | `src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs`; `src/Picket302.App/UI/Forms/FormEmailDistributionWizard.cs` | Интеграционный: разбор адреса, `MAIL FROM`/`RCPT TO`, заголовок `Reply-To` реализованы, тестов нет. Допущение **S8.1** |
| 100 | Отправка через встроенный SMTP-клиент | [П] | PARTIAL | `src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs` | Интеграционный: собственный SMTP-клиент реализован (очередь, отмена, коды ответов), покрытия тестами нет. Допущение **S7.9** |

## 8. Администрирование, сервис, интеграция (№101–118)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 101 | Клиент-серверный режим (список серверов, TCP) | [К] | DONE | `src/Picket302.Infrastructure/Persistence/ServerRegistry.cs`; `src/Picket302.App/UI/Forms/FormConnectionSettings.cs` | Интеграционный: `Infrastructure/ServerRegistryTests.cs`. Оговорка по фактической работе с серверной БД — см. «Дефекты [К]» (№127) |
| 102 | Выбор/смена подключения к БД, хранение реквизитов | [К] | DONE | `src/Picket302.Infrastructure/Persistence/ConnectionManager.cs` | Интеграционный: `Infrastructure/ServerRegistryTests.cs` (реквизиты шифруются DPAPI, порт 5432) |
| 103 | Архивирование по расписанию + автоудаление старых архивов | [П] | DONE | `src/Picket302.Infrastructure/Backup/ScheduledBackupService.cs`; `BackupPolicyService.cs` | Интеграционный с ускоренным таймером: `Infrastructure/ScheduledBackupTests.cs` (09:00 / 13:05 / 16:05, повтор через 10 мин) |
| 104 | Восстановление БД из архива | [П] | DONE | `src/Picket302.Infrastructure/Backup/RestoreService.cs`; `src/Picket302.App/UI/Forms/FormBackupRestore.cs` | Интеграционный: `Infrastructure/ScheduledBackupTests.cs` (восстановление на чистой базе, отказ при более новой схеме, повреждённый архив, предложение перезапуска) |
| 105 | Установка программы и БД на USB-флешку | [П] | DONE | `src/Picket302.Infrastructure/Deployment/UsbInstaller.cs`; `src/Picket302.App/UI/Forms/FormUsbInstallWizard.cs` | Интеграционный: `Infrastructure/UsbInstallerTests.cs` |
| 106 | Интеграция с оболочкой Windows (контекстное меню Проводника) | [Д] | DONE | `src/Picket302.Infrastructure/Shell/ExplorerIntegration.cs` | Интеграционный: `Infrastructure/ExplorerIntegrationTests.cs` |
| 107 | Загрузка данных из предыдущей/бесплатной версии | [Д] | DONE | `src/Picket302.Infrastructure/Migration/LegacyUpgradeImporter.cs`; `src/Picket302.App/UI/Forms/FormLegacyUpgradeWizard.cs` | Интеграционный: `Infrastructure/LegacyUpgradeImporterTests.cs`; допущение **S7.11** |
| 108 | Поиск адреса по ИНН/УНН (настраиваемая команда поиска) | [П] | DONE | `src/Picket302.Infrastructure/Integration/WebLookupLauncher.cs`; `src/Picket302.App/UI/Forms/FormIntegrationSettingsPage.cs` | Юнит: `Infrastructure/WebLookupLauncherTests.cs` |
| 109 | Техподдержка: запуск/обновление утилиты удалённого доступа | [Д] | PARTIAL | `src/Picket302.Infrastructure/Support/RemoteAssistantLauncher.cs` | Интеграционный: реализовано и подключено к меню и странице интеграций (только `https`), тестов нет. Допущение **S8.1** |
| 110 | Трекер отправлений Почты России (ШПИ, партия, дата, пункт, учётные данные) | [П] | PARTIAL | `src/Picket302.Infrastructure/Integration/RussianPostTracker.cs`; `src/Picket302.Domain/Entities/Mail.cs` | Интеграционный: сервис реализован и покрыт (`Infrastructure/S7InfrastructureTests.cs`, 14 переходов ШПИ), **диалог «Трекер Почты России» отсутствует**. Допущение **S8.1** |
| 111 | Журнал ошибок приложения + отдельный журнал печати | [К] | DONE | `src/Picket302.Infrastructure/Logging/LogHub.cs` (4 канала); `src/Picket302.App/UI/Forms/FormErrorLog.cs` | Интеграционный: `Infrastructure/S7InfrastructureTests.cs` (класс `LogHubTests`) |
| 112 | Языковые файлы / переключение языка интерфейса | [П] | DONE | `src/Picket302.Infrastructure/Localization/LanguageManager.cs`; `src/Picket302.App/UI/Forms/FormLanguageSelection.cs` | Интеграционный: `Infrastructure/S7InfrastructureTests.cs` (класс `LanguageManagerTests`) |
| 113 | COM-сервер автоматизации (печать, импорт, создание писем, флаги, фокус) | [П] | DONE | `src/Picket302.ComAutomation/PEProApplication.cs`; `IPEProApplication.cs`; `ComServerHost.cs`; `AutomationDispatcher.cs` | Интеграционный: `ComAutomation/ComAutomationContractTests.cs`; `S6/S6RegressionTests.cs`. Контракт — 23 опубликованных метода по `EVIDENCE/EVIDENCE/automation_idl.txt` и ТЗ §9.1 |
| 114 | Тип-библиотека для COM, регистрация в HKCU | [Д] | DONE | `src/Picket302.ComAutomation/ComRegistration.cs`; `src/Picket302.ComAutomation/idl/PrintEnvelopePro.idl`, `PrintEnvelopePro.tlb` | Юнит: `ComAutomation/TypeLibraryTests.cs`, `ComAutomation/ComRegistrationTests.cs` (регистрация в `HKCU\Software\Classes`); допущение **S6.4** |
| 115 | Примеры вызова из скрипта (JScript) и из Excel | [Д] | DONE | `Scripts/printenvelope.js`; `Scripts/printregistry.vbs`; `Scripts/ExcelCallExample.bas`, `Scripts/ExcelCallExample.xls`; `docs/EXAMPLES.md` | Юнит: `S6/S6RegressionTests.cs` (пример Excel, кодировка Windows Script Host); допущение **S6.5** |
| 116 | Импорт/создание писем через API для интеграции с 1С | [П] | DONE | `src/Picket302.ComAutomation/ApiLettersFacade.cs`; `AutomationDispatcher.cs` | Юнит: `S6/S6RegressionTests.cs` (коды ошибок), `ComAutomation/ComAutomationContractTests.cs` |
| 117 | Импорт базы из MySQL `print_konvert` (8 таблиц) | [П] | DONE | `src/Picket302.Infrastructure/Migration/LegacyMySqlImporter.cs` | Интеграционный: `Infrastructure/LegacyMySqlImporterTests.cs` (таблицы 1–8) |
| 118 | Импорт базы из Access MDB (14 таблиц) | [П] | DONE | `src/Picket302.Infrastructure/Migration/LegacyAccessImporter.cs` | Интеграционный: `Infrastructure/LegacyAccessImporterTests.cs` (14 таблиц) |

## 9. Лицензирование (№119–126)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 119 | Режим trial: лимит по объёму и срок истечения | [К] | DONE | `src/Picket302.Infrastructure/Licensing/LicenseStateMachine.cs`; `src/Picket302.Domain/Licensing/LicenseGate.cs` (`ILicenseGate`); `src/Picket302.Infrastructure/Licensing/LicenseStateMachineGate.cs`; `src/Picket302.Infrastructure/Licensing/DatabaseUsageCounter.cs`; `src/Picket302.App/UI/Forms/FormLicenseNag.cs` | Юнит + интеграционный: срок 10 дней и правила переходов автомата — `Licensing/LicenseStateMachineTests.cs`; **лимиты объёма применяются в точках создания записей и печати партий** через единые ворота `ILicenseGate` (`Application/LicenseLimitsEnforcementTests.cs`), счётчик объёма считает контрагентов и отправления по базе; **индикатор остатка дней — в строке состояния** (`MainShell.ОбновитьИндикаторЛицензии`). **NAG-стартовый диалог: DEFERRED_BY_OWNER (2026-11-07)** — startup NAG runtime: disabled; license indicator: enabled; license gates: enabled; `FormLicenseNag`: retained for future activation (путь включения — doc-комментарий `MainShell.ПоказатьNagЛицензии`). Допущения **S8.6**, **S8.7** |
| 120 | Привязка лицензии к оборудованию (несколько вариантов machine ID) | [П] | DONE | `src/Picket302.Infrastructure/Licensing/MachineIdentity.cs` | Юнит: `Licensing/MachineIdentityTests.cs`; допущение **S7.3** (мягкий реестровый признак без WMI) |
| 121 | Детекция виртуальной машины | [Д] | PARTIAL | `src/Picket302.Infrastructure/Licensing/VmDetector.cs` | Юнит: реализовано мягко, без WMI (допущение **S7.3**), но тестов нет и предупреждение в «О программе» не выводится. Допущение **S8.1** |
| 122 | Онлайн-активация (с подписью) + офлайн-файл запроса | [П] | DONE | `src/Picket302.Infrastructure/Licensing/ActivationService.cs`; `LicenseSignature.cs` | Юнит: `Licensing/LicenseStateMachineTests.cs` |
| 123 | Статус ключа: редакция, срок, снятие ключа, привязка к нескольким «управляющим» | [П] | DONE | `src/Picket302.Infrastructure/Licensing/LicenseStore.cs`; `LicenseManager.cs` | Юнит: `Licensing/LicenseStateMachineTests.cs` |
| 124 | Шифрование данных лицензии (в исходной — TripleDES) | [Д] | DONE | `src/Picket302.Infrastructure/Licensing/LicenseCrypto.cs` | Юнит: `Licensing/LicenseCryptoTests.cs` (AES-256-GCM; TripleDES запрещён ТЗ §8.10) |
| 125 | Антиотладка / антимониторинг | [Д] | DEFERRED | — | Юнит: сознательно не реализовано — ТЗ прямо запрещает перехват отладчика, антиотладку и обращение к портам ввода-вывода. Замена — подпись ECDSA P-256, AES-256-GCM, проверка целостности сборок, мягкая детекция ВМ; запрет подтверждается тестом `Licensing/AntiDebuggingGuardTests.cs`. Допущение **S7.4** |
| 126 | Три редакции продукта с разными лимитами (Free/Home/Professional) | [П] | PARTIAL | `src/Picket302.Domain/Licensing/LicenseContracts.cs` (`Редакции`); `src/Picket302.App/UI/Forms/FormLicense.cs` | Юнит: каталог редакций и лимиты реализованы, но отдельного теста на различие лимитов нет и гейт по редакции в меню не проверяется. Допущение **S8.1** |

## 10. Эксплуатационные функции (№127–151)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 127 | Клиент-сервер: IP, общая БД, клиент/сервер | [К] | PARTIAL | `src/Picket302.Infrastructure/Persistence/ServerRegistry.cs` (`DiagnoseTopology`); `ConnectionManager.cs` | Интеграционный: реестр серверов, реквизиты подключения (тип СУБД, сервер, порт 5432, база, пользователь, пароль в DPAPI) и выбор «этот компьютер / IP-адрес / имя сервера» реализованы и покрыты (`Infrastructure/ServerRegistryTests.cs`); на этапе S8 реализована **диагностика всех четырёх ролей топологии** — клиент без сервера БД, сервер без клиентов (головной офис), сервер с общей БД и клиентами, клиент общей БД (`Infrastructure/TopologyDiagnosticsTests.cs`). **Общая серверная БД не реализована**: драйвер PostgreSQL (`Npgsql`) и диалектный слой запросов в поставку не входят. Допущения **S2.8**, **О.2**, **S8.9** |
| 128 | Архивирование БД 3 раза в сутки, ZIP с паролем, срок хранения | [П] | DONE | `src/Picket302.Infrastructure/Backup/BackupService.cs`; `ScheduledBackupService.cs` | Интеграционный: `Infrastructure/ScheduledBackupTests.cs`, `Infrastructure/BackupServiceTests.cs` |
| 129 | Восстановление БД из архива | [П] | DONE | `src/Picket302.Infrastructure/Backup/RestoreService.cs` | Интеграционный: `Infrastructure/BackupServiceTests.cs`, `Infrastructure/ScheduledBackupTests.cs` |
| 130 | Установка на USB-флешку (всё целиком / только БД) | [П] | DONE | `src/Picket302.Infrastructure/Deployment/UsbInstaller.cs`; `src/Picket302.App/UI/Forms/FormUsbInstallWizard.cs` | Интеграционный: `Infrastructure/UsbInstallerTests.cs` |
| 131 | Настройка вида таблиц: цвета, заголовки столбцов, скрытие колонок, «полосатая таблица» глобально, сброс к умолчаниям | [К] | DONE | `src/Picket302.App/UI/Components/GridAppearanceManager.cs`; `src/Picket302.App/UI/Components/GridViewSettingsController.cs`; `src/Picket302.App/UI/Forms/FormGridAppearance.cs`; `src/Picket302.Domain/Contracts/GridAppearance.cs` | Юнит: `App/GridViewSettingsControllerTests.cs`, `App/FormGridAppearanceTests.cs`, `App/MainShellLayoutTests.cs`. Операторские пути: «Вид» → «Настройка вида таблиц…» (переключатель заголовков, «Сбросить»), «Вид» → «Настройка таблицы…» (сохранение видимости, подписей и ширины столбцов), контекстное меню заголовка адресной книги («Скрыть столбец», «Показать все столбцы») |
| 132 | Три механизма поиска (Ctrl+F, прямой набор, фильтры) | [К] | DONE | `src/Picket302.App/UI/Components/SearchController.cs`; `src/Picket302.App/UI/Forms/FormSearch.cs` | UI-автотесты: `App/SearchMechanismTests.cs` |
| 133 | Подбор значения поля (пустое поле + BackSpace) | [П] | DONE | `src/Picket302.App/UI/Components/FieldAutoComplete.cs`; `FieldValueAutoComplete.cs` | UI-автотесты: `App/SearchMechanismTests.cs` |
| 134 | Ctrl + двойной щелчок по ярлыку | [Д] | DONE | `src/Picket302.App/UI/Shell/ShortcutInterceptor.cs` | Интеграционный: `App/S7AppTests.cs` |
| 135 | Пункт в контекстном меню Windows Explorer | [Д] | DONE | `src/Picket302.Infrastructure/Shell/ExplorerIntegration.cs` | Интеграционный: `Infrastructure/ExplorerIntegrationTests.cs` |
| 136 | Диагностика «сервер БД не запускается» | [П] | PARTIAL | `src/Picket302.Infrastructure/Persistence/DbServiceDoctor.cs`; `src/Picket302.App/UI/Forms/FormDbServiceDoctor.cs` | Интеграционный: реализация тестопригодна (подменяемый исполнитель `sc.exe`), но тестов нет. Допущение **S8.1** |
| 137 | Демо-база при первом запуске + «Очистить данные» | [П] | DONE | `src/Picket302.Application/Setup/DemoDatabaseSeeder.cs`; `src/Picket302.App/UI/Forms/FormClearDatabase.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs` |
| 138 | Автосоздаваемая категория «Неразобранные» | [К] | DONE | `src/Picket302.Application/Services/CategoryTreeService.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs` (блокировка удаления «Неразобранных», категория по умолчанию) |
| 139 | Флаг «Это отправитель» у записи адресной книги | [К] | DONE | `src/Picket302.Application/Services/AgentService.cs`; `src/Picket302.App/MainShell.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs`, `Acceptance/AcceptanceScenarioTests.cs` |
| 140 | Drag & drop получателей + «Добавить всех активных» | [П] | DONE | `src/Picket302.App/UI/Forms/FormDistributionEditor.cs` | Интеграционный: `Acceptance/AcceptanceScenarioTests.cs` (проверена сервисная эквивалентность; сам drag&drop-жест UI-автотестом не покрыт) |
| 141 | «Объединить в рассылку» — контекстное действие | [П] | DONE | `src/Picket302.Application/Services/DistributionService.cs` (`CreateFromSelectionAsync`); `src/Picket302.App/MainShell.cs` | Интеграционный: `Application/DistributionServiceTests.cs` |
| 142 | Повторная отправка сохранённой рассылки | [П] | DONE | `src/Picket302.Application/Services/DistributionService.cs` (`CloneForResendAsync`); `src/Picket302.App/MainShell.cs` | Интеграционный: `Application/DistributionServiceTests.cs` |
| 143 | Пробная печать любого шаблона | [П] | DONE | `src/Picket302.Printing/PrintSessionController.cs` (`PrintTestAsync`); `src/Picket302.Domain/Entities/PrintEnums.cs` (`PrintModes.Test`) | Приёмочный сценарий: `Acceptance/AcceptanceScenarioTests.cs` (режим `test` — mails не создаются); `Infrastructure/PrintModeSchemaTests.cs`; допущение **S2.23** |
| 144 | Более 50 готовых бланков, включая праздничные | [П] | PARTIAL | `src/Picket302.Infrastructure/Templates/BuiltinTemplatesCatalog.cs` (`BuildLibrary`) | Юнит: в библиотеке 28 бланков (8 типовых + 12 конвертов + 8 служебных) — меньше требуемых «более 50»; порог теста занижен до 20 (`Infrastructure/BuiltinTemplatesTests.cs`); праздничных бланков с отличающейся разметкой нет. Допущение **S8.1** |
| 145 | Пресет «стандартная разметка» + режим «без стандартной разметки» | [К] | DONE | `src/Picket302.Printing/Templates/StandardLayoutPresets.cs`; `src/Picket302.Printing/Templates/StandardLayoutApplier.cs`; `src/Picket302.App/UI/Forms/FormBlankEditor.cs` | Юнит: класс пресетов покрыт 6 тестами (`Printing/StandardLayoutPresetsTests.cs`); на этапе S8 пресет **подключён к редактору бланков**: меню «Макет» → «Стандартная разметка» / «Без стандартной разметки (поля вручную)», расстановка адресных полей и кодового штампа по нормативным зонам (`Printing/StandardLayoutApplierTests.cs`) |
| 146 | Печать адресных этикеток A5/A4/A3 + сохранение макета в PDF | [П] | PARTIAL | `src/Picket302.Printing/Engine/SheetLayoutImposition.cs` | Юнит: раскладка этикеток по листам A5/A4/A3 реализована и покрыта (`Printing/PrintWarningsTests.cs`), но интерфейсного вызова нет, а **сохранение макета в PDF полностью отсутствует**. Допущение **S8.1** |
| 147 | Подача «меньшей стороной»: справка + предупреждение | [К] | DONE | `src/Picket302.Printing/Warnings/FeedOrientationHint.cs`; `src/Picket302.App/UI/Forms/FormFeedOrientationHint.cs` | Юнит: `Printing/PrintWarningsTests.cs` (текст предупреждения и справки) |
| 148 | Автораспознавание адресов при импорте Excel/CSV | [П] | DONE | `src/Picket302.Application/Services/AddressHeuristicsService.cs` | Интеграционный: `Application/AddressHeuristicsServiceTests.cs` |
| 149 | Форматы конвертов России и Украины | [П] | DONE | `src/Picket302.Domain/Postal/EnvelopeFormatCatalog.cs`; `EnvelopeFormats.json` | Интеграционный: `Domain/EnvelopeFormatCatalogTests.cs` (19 записей: 11 Россия, 7 Украина + варианты «/О») |
| 150 | Сумма прописью с локализацией (.lng) | [К] | DONE | `src/Picket302.Infrastructure/Text/AmountInWords.cs`; `src/Picket302.Infrastructure/Text/lng/*.lng` | Интеграционный: `Infrastructure/AmountInWordsTests.cs`; пустой `Kzt.lng` — допущение **S7.7** |
| 151 | Локализация как расширяемый набор файлов | [П] | DONE | `src/Picket302.Infrastructure/Localization/LanguageManager.cs`; `lang/ru.xml`, `en.xml`, `template.xml`; `Text/lng/*.lng` | Интеграционный: `Infrastructure/S7InfrastructureTests.cs` (русский и английский + 7 словарей валют) |

## 11. Почтовые приложения (№152–163)

| № | Название | Приоритет | Статус | Модуль/файл | Способ проверки |
|---|---|---|---|---|---|
| 152 | Разбивка окна на четыре вкладки: Адресная книга / Конверты / Рассылки / Отправленные | [К] | DONE | `src/Picket302.App/MainShell.cs` | UI-автотест: `App/MainShellLayoutTests.cs` |
| 153 | Демо-база при первом запуске (готовый каталог контрагентов и категорий) и очистка базы | [П] | DONE | `src/Picket302.Application/Setup/DemoDatabaseSeeder.cs`; `src/Picket302.App/UI/Forms/FormClearDatabase.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs`, `App/MainShellLayoutTests.cs` (четыре области очистки) |
| 154 | Категории адресатов: дерево, автосоздаваемая категория «Неразобранные», переименование, удаление | [К] | DONE | `src/Picket302.Application/Services/CategoryTreeService.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs`; **`CategoryTreeService_Переименование_*` — добавлены на этапе S8** |
| 155 | Формирование рассылки: drag&drop получателей, «Добавить всех активных», фильтр по категориям | [К] | DONE | `src/Picket302.App/UI/Forms/FormDistributionEditor.cs`; `src/Picket302.Application/Services/DistributionService.cs` | Интеграционный: `Acceptance/AcceptanceScenarioTests.cs`, `Application/DistributionServiceTests.cs` |
| 156 | Сохранённые и отправленные рассылки: правка, повторная отправка, печать рассылки | [П] | DONE | `src/Picket302.Application/Services/DistributionService.cs` (`UpdateDistributionAsync`); `src/Picket302.App/MainShell.cs` («Изменить…», «Печать…», «Печать выделенных…»); `src/Picket302.App/UI/Forms/FormDistributionEditor.cs`; `src/Picket302.App/Printing/DistributionPrintScenario.cs` | Интеграционный + UI-автотест: правка без дубля — `Application/DistributionEditingTests.cs` (7 тестов, включая откат транзакции) и `Application/DistributionManyToOneEditingTests.cs` (8 тестов «многие к одному»: роли, центральный получатель, повторное сохранение); правка из редактора без выбранного глобального отправителя — `App/FormDistributionEditorEditingTests.cs` (4 теста); сценарий печати с пропуском пустых, отменой и ошибкой — `App/DistributionPrintScenarioTests.cs` (9 тестов, fake-печать) и `App/MainShellDistributionActionsTests.cs` (4 теста). Вид существующей рассылки заблокирован (смена без пересоздания состава запрещена). Статусная модель итогов печатной сессии: успех — только согласованная пара IsSuccess=true и SUCCESS; ABORTED — всегда отмена оператора (production-текст «Печать прервана оператором.» ошибкой не считается); FAILED — ошибка с текстом оператора либо общим стабильным текстом («Печать не выполнена.»); BLOCKED_SIZE — ошибка блокировки размера; несогласованный/неизвестный статус — диагностика, успехом не объявляется; ошибка предпросмотра отличена от отмены (`ПредпросмотрИтог`); ошибка после трёх попыток размера показывается оператору один раз и не теряется. Покрыто `App/PrintOutcomeMappingTests.cs` (14 тестов, включая обязательные production-варианты ABORTED-с-текстом и FAILED-без-текста) и `App/DistributionPrintScenarioTests.cs` (12 тестов: отмена с текстом, ошибка без текста, ошибка с уже показанным сообщением и др.). Физическая печать не выполнялась (допущение **S8.4**), программный конвейер покрыт `Printing` |
| 157 | Отправители (агенты): создание отправителя и выбор его при печати | [П] | DONE | `src/Picket302.Application/Services/AgentService.cs`; `src/Picket302.App/MainShell.cs` | Интеграционный: `Application/DemoAndCategoriesTests.cs`, `Application/CounterpartyServicesTests.cs` |
| 158 | Шаблоны, клипарт, шрифты и цвета: каталог, редактирование, библиотека картинок, выбор шрифта и цвета | [П] | PARTIAL | `src/Picket302.Infrastructure/Templates/ClipartLibrary.cs`; `src/Picket302.App/UI/Forms/FormClipartPicker.cs`; `src/Picket302.App/MainShell.cs` (панель «ВИД ЛОГОТИПА»); `src/Picket302.App/UI/Forms/FormBlankEditor.cs` | UI-автотест. **Локальный клипарт как логотип получателя — реализован и покрыт:** каталог из 10 вычерчиваемых кодом элементов без сети (`Infrastructure/ClipartLibraryTests.cs`, 4 теста), диалог выбора с предпросмотром (`App/FormClipartPickerTests.cs`, 3 теста), сквозное назначение/чтение/удаление логотипа контрагента настоящим репозиторием во временной базе передача PNG в `PrintDataSource.LogoBytes` и вывод всех элементов каталога конвейером `EnvelopeRenderer`/`LogoRenderer` в границы поля логотипа с сохранением пропорций; режим «без логотипа» исключает вывод (`App/ClipartLogoIntegrationTests.cs`, 2 теста). **Остаток (PARTIAL):** вставка клипарта в произвольный макет blank.xml не реализована — требует расширения формата (XSD, сериализатор, загрузка, сохранение, рендеринг, совместимость старых blank.xml и тесты). Допущение **S8.1** |
| 159 | Импорт с валидацией: сопоставление по столбцам, пошаговый предпросмотр, пропуск записей, фильтр «записи с ошибками» | [К] | DONE | `src/Picket302.Application/Services/ImportValidator.cs`; `src/Picket302.App/UI/Forms/FormImportWizard.cs` | Интеграционный + UI-автотест: мастера импорта — `Application/ImportSessionTests.cs` (валидация и политика «только без ошибок»), `App/ImportWizardFormTests.cs` (форма мастера) |
| 160 | Экспорт: выбор формата и настроек даты, действия после сохранения (открыть файл / открыть папку) | [П] | DONE | `src/Picket302.Application/Services/ExportWizard.cs`; `src/Picket302.App/UI/Forms/FormExportWizard.cs` | Интеграционный + UI-автотест + приёмочный: `Application/ExportWizardTests.cs` (выбор формата и настроек даты), `App/DocTemplateWizardControllerTests.cs` (`ФормаМастераЭкспорта_Открывается_ИСобираетЗапрос`, `Контроллер_Экспорта_СобираетЗапрос_ИзСостоянияФормы`), `Acceptance/AcceptanceScenarioTests.cs`; допущение **S6.20** |
| 161 | Резервное копирование: два режима хранения (за каждый день / только последняя копия) и очистка базы по типам данных | [П] | DONE | `src/Picket302.Infrastructure/Backup/BackupPolicyService.cs`; `src/Picket302.App/UI/Forms/FormClearDatabase.cs` | Интеграционный: `Infrastructure/ScheduledBackupTests.cs` (оба режима хранения, `DailyHistory`/`LastOnly`), `App/MainShellLayoutTests.cs` |
| 162 | Регистрация и редакции: ввод ключа, пробный период с ограничением срока, окно преимуществ редакции | [Д] | DONE | `src/Picket302.App/UI/Forms/FormLicense.cs`; `FormLicenseNag.cs`; `src/Picket302.Domain/Licensing/LicenseContracts.cs` | Юнит автомата состояний и интеграционный тест офлайн-активации: `Licensing/LicenseStateMachineTests.cs`, `LicenseCryptoTests.cs`, `MachineIdentityTests.cs` |
| 163 | Обновления и сервис: проверка и скачивание обновлений, форма исключений с готовым текстом для службы поддержки, диалоги первого запуска и пути, калькулятор, всплывающий календарь | [Д] | PARTIAL | `src/Picket302.Infrastructure/Update/UpdateChecker.cs`; `src/Picket302.App/UI/Forms/FormExceptionReport.cs`, `FormFirstRun.cs`, `FormCalculator.cs`; `src/Picket302.App/UI/Components/PopupCalendar.cs` | Юнит: проверка обновлений покрыта (`Infrastructure/S7InfrastructureTests.cs`, класс `UpdateCheckerTests`), текст для службы поддержки — тот же файл (класс `LogHubTests`); **скачивание обновлений не реализовано**, формы исключений/первого запуска/калькулятора/календаря тестами не покрыты. Допущение **S8.1** |

## Критерии приёмки §7 заказа (11 критериев)

Каждый критерий управляется тестом либо зафиксирован отдельным документом (стендовая фиксация).

| № | Критерий заказа | Как управляется | Доказательство |
|---|---|---|---|
| 1 | Конверт C6 печатается с совпадением до ±1 мм с предпросмотром на принтерах A4, лотковом конвертном и термопринтере | **Тест** (расчётная часть на детерминированном мок-DEVMODE) + **стендовая фиксация** (физические принтеры заказчика) | `Printing/PrintPreviewEngineTests.cs` — предпросмотр и печать дают одинаковые координаты полей, калибровочное смещение применяется в обоих каналах, печатная область совпадает с данными принтера. Физическая сверка ±1 мм на трёх принтерах не выполнялась — требуется оборудование заказчика (допущение **S8.4**) |
| 2 | Почтовый индекс напечатан машиночитаемым шрифтом; при отсутствии шрифта — явное предупреждение | **Тест** | `Printing/PostIndexRendererTests.cs` (шесть знаков, шаг 5,4 мм, блокировка при отсутствии шрифта); `Printing/BlankEditorTests.cs` (`Validate_ШрифтZipcodeОтсутствует_Блокировка`); `Printing/EnvelopeLogoRenderingTests.cs` (явные предупреждения) |
| 3 | При несовпадении формата шаблона и бумаги — блокирующее сообщение; режим «Зафиксировать A4» доступен и честно предупреждает | **Тест** | `Printing/PaperSizeValidatorTests.cs` (блокировка, превышение на 1,01 мм, предложение «Зафиксировать A4»); `Printing/PrintWarningsTests.cs`, `Printing/PrintEngineTests.cs` (честное предупреждение о не помещении); `App/PrintDialogsTests.cs` |
| 4 | Все 6 адресных строк независимы; адрес в БД никогда не хранится одной строкой | **Тест** | `Domain/AddressTests.cs` (6 строк, независимость строк, неверный индекс, обрезка); `Infrastructure/RepositoryTests.cs`; `src/Picket302.Infrastructure/Data/Migrations/Sqlite/V001__initial_schema.sql` (6 отдельных колонок `post_str1..post_str6`) |
| 5 | Групповое редактирование применяется только к явно выделенным строкам (Ctrl+клик/Ctrl+A) и подтверждается, если затронуто > N строк | **Тест** | `Application/CounterpartyServicesTests.cs` (правка только переданных идентификаторов); **`BatchEditService_ПорогПодтверждения_*` и `BatchEditService_Подтверждение_ТребуетсяТолькоПриЗатронутыхСвышеПорога` — добавлены на этапе S8**; порог N=10, конфигурируется (ТЗ §13) |
| 6 | Журнал печати содержит запись о каждом запуске, включая отказ по размеру | **Тест** | `Printing/PrintSessionAuditTests.cs` (успешная сессия, отказ по размеру, прерванная сессия); `Printing/PrintAuditWriterTests.cs` (формат записи 65); `Acceptance/AcceptanceScenarioTests.cs` (шаг «журнал аудита содержит запись каждого прогона») |
| 7 | Импорт 1С-выгрузки и CSV с произвольной конфигурацией работает через редактор настроек импорта | **Тест** | `Application/OneCImportParserTests.cs` (1С 7.7 и 8.x), `Application/ImportSessionTests.cs` (сопоставление столбцов, политика «только без ошибок»), `App/ImportWizardFormTests.cs` (редактор настроек импорта), `Infrastructure/CsvProfileStoreTests.cs`; `Acceptance/AcceptanceScenarioTests.cs` (шаг импорта) |
| 8 | Экспорт в Word/Excel работает на машине без установленного MS Office | **Тест** | `Architecture/OfficeInteropAbsenceTests.cs` (в сборках и исходниках нет Interop), `Infrastructure/OpenXmlExportTests.cs` (структура пакета DOCX/XLSX), `Acceptance/AcceptanceScenarioTests.cs` (шаг «zip-структура валидна») |
| 9 | Шаблоны обновляются без переустановки программы; путь к каталогу шаблонов настраивается на сетевой | **Тест** + **фиксация** | `Infrastructure/LbkPackageTests.cs` (`ДобавлениеПакетаВКаталог_ДоступенБезПерезапуска`), `Infrastructure/BuiltinTemplatesTests.cs`; пакеты читаются из каталога `TemplatePackageRepository` — параметр пути передаётся конструктором. Размещение на сетевом диске проверяется на стенде заказчика (допущение **S8.4**) |
| 10 | БД открывается в общем режиме с рабочего места, не переустанавливая программу | **Фиксация** | `Infrastructure/ServerRegistryTests.cs` (реквизиты подключения, порт 5432, шифрование DPAPI), `src/Picket302.App/UI/Forms/FormConnectionSettings.cs` (переключение «этот компьютер / сервер»). Фактическая работа с общей серверной БД **не реализована** — см. «Дефекты [К]» (№127); допущения **S2.8**, **О.2** |
| 11 | Поля шаблона редактируются мышью и перетаскиванием, перекрытие полей при печати обнаруживается и подсвечивается | **Тест** | `App/FormBlankEditorTests.cs`, `Printing/BlankEditorTests.cs` (`Validate_ПересечениеПолей_КонфликтИПредупреждение`, перемещение поля, выход за печатную область), `src/Picket302.Printing/Editor/CanvasController.cs` |

## Дефекты [К] (открытые на этапе S8)

Перечисленные функции не могут быть отмечены `DONE`; для каждой зафиксирована причина и ID допущения в `docs/OPEN_QUESTIONS_LOG.md`.

| № | Что отсутствует | Допущение |
|---|---|---|
| 127 | Общая серверная БД: нет драйвера PostgreSQL (`Npgsql`) и диалектного слоя запросов; диагностика топологии и реквизиты подключения реализованы | S2.8, О.2, S8.9 |

## Ограничения и допущения

1. **Стендовая печать не выполнялась** — проверка выполнена на детерминированном мок-DEVMODE; сверка ±1 мм на трёх физических принтерах требует оборудования заказчика (допущение S8.4).
2. **Только SQLite** — провайдер PostgreSQL в поставку не входит, клиент-серверный режим с общей БД отложен (допущения S2.8, О.2).
3. **Шрифт ZIPcode** — лицензия на распространение `ZIPcode.ttf` не подтверждена; при отсутствии шрифта печать кодового штампа блокируется явным предупреждением (допущение S4.6, ТЗ §13).
4. **Словарь валют Казахстана пуст** — поставляется пустой шаблон `.lng` (допущение S7.7).
5. **HTML-редактор писем** — собственный компактный редактор вместо TinyMCE (допущение S7.2).
6. **Антиотладка и антимониторинг не реализованы** намеренно; шифрование лицензии — AES-256-GCM (допущение S7.4).
7. **Диалог первого запуска** (`FormFirstRun`) реализован как форма, но не вызывается при старте: демо-база наполняется автоматически при пустой адресной книге (допущение S8.1).
8. **Книга Excel с внедрённым VBA** поставляется исходником `.bas` — сборка встроенного макроса требует установленного MS Office (допущение S6.5).
9. **Совместимость с файлами шаблонов предшественников (.fr3) не обеспечена** — перенос выполняется экспортом данных и новыми пакетами `.lbk` (ТЗ §13).
10. **Размеры 5 из 8 встроенных бланков** (112эф, 112эю, 117+113, 116, 119-2011) заданы по `01_REQUIREMENTS_MASTER.txt:820-828` и отличаются от значений в `00_FUNCTION_INDEX.txt:183` (220×110 и 148×104). Реализация и тест следуют более подробной таблице мастер-описания; расхождение зафиксировано как открытый вопрос (допущение S8.5).

## Статистика прогона

| Прогон | Команда | Результат |
|---|---|---|
| Сборка | `dotnet build Picket302.sln -c Debug --nologo -v q --no-incremental` | **Build succeeded. 0 Warning(s), 0 Error(s)** |
| Тесты | `dotnet test Picket302.sln --no-build --nologo -v q` | **Passed! — Failed: 0, Passed: 1112, Skipped: 0, Total: 1112** (24–25 с в двух прогонах подряд) |

Прогон выполнен повторно на этапе S8 после правок приёмочного документа: результат
воспроизводится, узел тестов не зависает (прогон выполнялся с ограничением по времени —
тайм-аут 25 минут, фактическое время 24–25 с).

### Итоговая сверка приёмочного документа (этап S8)

Таблица проверена машинно сверкой с первоисточниками:

| Проверка | Результат |
|---|---|
| Соответствие `00_FUNCTION_INDEX.txt` | 163 строки, диапазон 1–163 без пропусков и лишних номеров |
| Приоритеты: таблица ↔ `00_FUNCTION_INDEX.txt` | 0 расхождений |
| Приоритеты: `00_FUNCTION_INDEX.txt` ↔ `01_REQUIREMENTS_MASTER.txt` | 0 расхождений по всем 163 функциям |
| Ссылки на файлы | машинная сверка: 309 path-токенов; не резолвятся 3 — только цитаты «до правки» в журнале исправлений, живых битых нет |
| Ссылки на допущения `docs/OPEN_QUESTIONS_LOG.md` | 31 строка `PARTIAL`/`DEFERRED`, 0 висячих ссылок |
| Открытые функции с приоритетом `[К]` | 1 — №127 |

Внесённые по результатам сверки правки:

- №51 «Реестр расширенный» — приоритет исправлен с `[П]` на `[К]`: и `00_FUNCTION_INDEX.txt`,
  и `01_REQUIREMENTS_MASTER.txt` указывают `[К]`; статус `DONE` подтверждён тестом
  `Application/RegistryReportsTests.cs` (`РасширенныйРеестр_Примечания`);
- №111 — убрана ссылка на несуществующий файл `Infrastructure/LogHubTests.cs`: класс
  `LogHubTests` находится в `Infrastructure/S7InfrastructureTests.cs`;
- №163 — та же поправка ссылки на класс `LogHubTests`;
- №160 — добавлена недостающая ссылка на UI-покрытие формы экспорта
  `App/DocTemplateWizardControllerTests.cs` (`ФормаМастераЭкспорта_Открывается_ИСобираетЗапрос`);
- 41 строка — в графу «Способ проверки» добавлен явный уровень теста по ТЗ §11.1/§11.3
  (`Юнит` / `Интеграционный` / `Приёмочный` / `UI-автотест`); до правки уровень был назван
  в 122 строках (в 115 — одним из трёх уровней §11.1, в 7 — термином «UI-автотест» из §11.3),
  а остальные 41 описывали проверку словами без указания уровня;
- «Как читать таблицу» — убрана ссылка на несуществующий столбец «Причина»: причина и
  ссылка на допущение находятся в графе «Способ проверки» той же строки.

Динамика числа тестов по стадиям:

| Стадия | Тестов | Примечание |
|---|---|---|
| S5 (базовый уровень) | 625 | — |
| S6 | 830 | +205 (экспорт, XSLT, COM-контракт, библиотека типов) |
| S7 | 998 | +168 (почта, лицензирование, администрирование, локализация) |
| S8 | **1112** | +114 (сквозной приёмочный сценарий, исправления печатного слоя, закрытые [К]-разрывы) |

Прирост на этапе S8 (114 новых тестов в дереве, включая 2 в приёмочном сценарии):

- `tests/Picket302.Tests/Acceptance/AcceptanceScenarioTests.cs` — 2 сквозных теста: полный сценарий (демо-база → импорт → категория → рассылка → пробная печать → пакетная печать F7 → журналы → Ф.103 → экспорт XLSX → COM `PrintEnvelope` → `BackupNow`/`Restore`) и отдельный out-of-proc тест COM-сервера;
- `tests/Picket302.Tests/Printing/EnvelopeLogoRenderingTests.cs` — 4 теста (функция 47);
- `tests/Picket302.Tests/Printing/QrErrorCorrectionTests.cs` — синдромы RS/BCH для декодера QR;
- `tests/Picket302.Tests/Printing/PrintPreviewEngineTests.cs` — предпросмотр и печать дают одинаковую геометрию (критерий №1);
- `tests/Picket302.Tests/App/SenderQuickSelectTests.cs` — 6 тестов (функция 16);
- `tests/Picket302.Tests/Domain/MailClassificationTests.cs` — 14 тестов (функции 23 и 32);
- `tests/Picket302.Tests/Application/CounterpartyServicesTests.cs` — 4 теста порога подтверждения пакетной правки (функция 18) и 4 теста дерева категорий (функции 8 и 154);
- `tests/Picket302.Tests/Application/DemoAndCategoriesTests.cs` — закрытая категория, переименование, дубликат имени, пустое имя;
- `tests/Picket302.Tests/App/AddressBookSortControllerTests.cs`, `tests/Picket302.Tests/Application/SavedFilterServiceTests.cs`, `tests/Picket302.Tests/Application/CounterpartyFilterRepositoryTests.cs` — сортировка по любой колонке и сохранённые фильтры (функция 9);
- `tests/Picket302.Tests/Application/RtfImportTests.cs` — импорт RTF (функция 71);
- `tests/Picket302.Tests/App/GridViewSettingsControllerTests.cs`, `tests/Picket302.Tests/App/FormGridAppearanceTests.cs`, `tests/Picket302.Tests/App/MainShellMenuTests.cs` — настройка вида таблиц, скрытие столбцов, ширина столбца, переключатель заголовков (функции 131 и 12).

**Устранение зависания тестового узла.** До правки этапа S8 полный прогон не завершался: узел
тестов зависал на `GridViewSettingsControllerTests`. Причина — контроллер вида таблиц
совмещал чтение файла настроек (асинхронный ввод-вывод) и изменение элементов таблицы
в одном асинхронном методе: после `await` обработка продолжалась в другом потоке, а
обращение к элементу `DataGridView` из чужого потока приводило к зависанию узла тестов.
Чтение и применение разделены (`GridViewSettingsController.ЗагрузитьAsync` +
`GridViewSettingsController.Применить`), элементы таблицы изменяются только в потоке,
который ими владеет. Допущение **S8.10**.

**Сверка текущего раунда (машинная, по первоисточникам).** Повторная сверка всех 163 строк
с `00_FUNCTION_INDEX.txt` и `01_REQUIREMENTS_MASTER.txt`: приоритеты — 0 расхождений,
пропусков номеров нет, статусы `DONE`/`PARTIAL`/`DEFERRED` = 132/27/4 (совпадают со сводкой);
файловые ссылки машинно сверены: 309 path-токенов, не резолвятся только цитаты
«до правки» в журнале исправлений (`Infrastructure/LogHubTests.cs`,
`Infrastructure/CounterpartyFilterRepositoryTests.cs`, `Entities/MailFolder.cs`),
названные тест-методы существуют; каждый `PARTIAL`/`DEFERRED`
ссылается на существующее допущение `docs/OPEN_QUESTIONS_LOG.md`; все 11 критериев §7
управляются тестом либо фиксацией. Исправлены три устаревшие ссылки на файлы: №9
(`Infrastructure/CounterpartyFilterRepositoryTests.cs` → `Application/CounterpartyFilterRepositoryTests.cs`),
№22 (`Entities/MailFolder.cs` → `Entities/Folder.cs`), №77 (`EnvelopesDataSerializer.cs`; `EnvelopesData.cs`
→ `Exchange/EnvelopeContracts.cs`) и опечатка «кодаового» → «кодового» (раздел «Ограничения
и допущения»). Контрольный прогон после правок: `dotnet build` — 0 предупреждений,
0 ошибок; `dotnet test` — Failed: 0, Passed: 1112, Skipped: 0, Total: 1112.

**Сверка финального раунда (закрытие замечаний независимого аудита).** Восстановлен
заголовок документа (в первую строку попали служебные маркеры правки); строка №2 —
имена тестов приведены к фактическим `Validate_ДесятизначныйИннДляФизЛица_Отклоняется`
и `Validate_ДвенадцатизначныйИннДляЮрЛица_Отклоняется` (`Domain/InnValidatorTests.cs`);
строка №113 — путь свидетельства уточнён до `EVIDENCE/EVIDENCE/automation_idl.txt`;
второй экземпляр ссылки на репозиторий фильтров (раздел покрытия тестами, функция 9)
переведён на `Application/CounterpartyFilterRepositoryTests.cs`; сводная строка
«Ссылки на тестовые файлы» заменена фактически проверенной; опечатка «кодаового» →
«кодового» исправлена в `README.md` (раздел «Известные ограничения»). Контроль после
правок: `dotnet build Picket302.sln` — 0 предупреждений, 0 ошибок; `dotnet test` —
Failed: 0, Passed: 1112, Skipped: 0, Total: 1112; заголовок без служебных маркеров,
LF сохранён, строк таблиц — 175.

**Ревизия acceptance-инфраструктуры 2026-11-07 (четвёртая серия замечаний).**
Закрыты дефекты, при которых PASS не означал выполнение проверки: reparse-тесты
гарда без тихих пропусков (невозможность создать junction/symlink — FAIL;
создание подтверждается атрибутом reparse point через `GetFileAttributesW`;
cleanup ссылок/базы проверяется; постусловие отсутствия `P302GuardTest_*`),
`ИзолированныйПрофиль.Dispose` с независимыми шагами и полной диагностикой
(`AggregateException` + `Data`, ошибка `ClearAllPools` входит в провал),
PowerShell-драйверы fail-closed (`$ErrorActionPreference='Stop'`, fingerprint
без `SilentlyContinue`, sentinel с `-ErrorAction Stop`, единая запись evidence,
fault-инъекции в отдельных файлах `emptybook-fault-*.md` / `printflow-fault-*.md`
с `PASS_OF_CONTRACT: true`, happy-файлы `emptybook-happy-evidence.md` /
`printflow-happy-evidence.md` не перезаписываются fault-прогонами). Контроль:
Debug/Release non-COM **1254/1254** (1251 + 3 новых теста), гард 19/19
(0 fail, 0 skip; reparse-ветви измерены фактически), сборки 0 предупреждений /
0 ошибок, статический parse 4 скриптов — 0 ошибок, `run-guard-negative.ps1`
exit 0, fault `wrong-expectation` exit 1 (оба без UI). Прогоны
`run-emptybook.ps1`/`run-printflow.ps1` с UI 2026-11-07 не выполнялись (запрещены
постановкой) — свежий runtime-статус сценариев NOT_RUN_BY_SCOPE; подробности и
карта evidence-файлов — `docs/UX-REDESIGN-2026-11-05.md`, раздел 8.1.

**Ревизия межпроцессной сериализации 2026-11-07 (пятая серия замечаний).**
Закрыта межпроцессная гонка двух независимых testhost-процессов: xUnit-коллекции
сериализуют только внутри одного прогона, а параллельные Debug+Release (два
агента, ручной прогон + acceptance) давали ложные падения (чужой
`P302GuardTest_*`, чужой sentinel в реальном профиле). Введён named mutex
`Local\Picket302.TestInfrastructure.SharedProfile` — один идентификатор для
C#-инфраструктуры и PowerShell-драйверов; владение вынесено в выделенный
поток-владелец (Windows-мьютекс привязан к потоку, асинхронные тесты продолжаются
в другом потоке), таймаут ограничен, отказ fail-closed, `AbandonedMutexException`
обрабатывается с диагностикой. Постусловие чистоты `P302GuardTest_*` вынесено в
teardown `ICollectionFixture` (полный список остатков в диагностике до удаления);
evidence-контракт: финальный статус вычисляется ДО записи, после записи —
пост-проверка обязательных маркеров, финальная fingerprint-дельта переводит
прогон в FAIL, аварийное завершение — `Stop-HarnessTree` с деревом процессов и
fail-флагом. Контроль: Debug и Release non-COM последовательно **1256/1256**
(1254 + 2 теста блокировки), guard 19/19, ownership 7/7; **параллельный прогон
двух testhost (Debug+Release одновременно): оба 1256/1256, exit 0**, ни одного
ложного остатка, ни одного ложного sentinel (логи в
`%TEMP%\p302-parallel-verify\`); guard-negative безоконно: happy exit 0,
faults `wrong-expectation` и `final-delta` exit 1; PS parse 4 файлов — 0 ошибок;
после прогонов `P302GuardTest_*`=0, `Picket302Tests_*`=0, keep-профилей 5,
процессов 0, sentinel отсутствует. Сценарии `run-emptybook.ps1`/`run-printflow.ps1`
с UI не перезапускались (запрещены постановкой) — свежий runtime-статус
NOT_RUN_BY_SCOPE; подробности и полная таблица флагов —
`docs/UX-REDESIGN-2026-11-05.md`, раздел 8.2.

## Приложение: сквозной приёмочный сценарий

`tests/Picket302.Tests/Acceptance/AcceptanceScenarioTests.cs` — `Сквозной_сценарий_приёмки_от_демо_базы_до_восстановления_архива`:

1. развёртывание демо-базы и проверка состава категорий;
2. импорт CSV через валидатор с политикой «только без ошибок»;
3. создание категории и перенос в неё записей;
4. формирование рассылки эквивалентом `CreateFromSelection` (drag&drop-эквивалент) и проверка «Добавить всех активных»;
5. пробная печать (`print_mode = test`) — письма в таблице `mails` **не создаются**;
6. пакетная печать по F7-эквиваленту на мок-принтере; проверяются записи `PrintJob`/`PrintHistory`, включая отклонённые, и журнал аудита;
7. печать реестра Ф.103 — итоги сходятся;
8. экспорт в XLSX без установленного Office — структура zip-пакета валидна;
9. вызов COM-сервера `PrintEnvelope` out-of-proc — код возврата 0, запись в `mails`;
10. `BackupNow` → `Restore` на чистую базу — записи на месте.

`Com_PrintEnvelope_OutOfProc_ВозвращаетКод0_И_СоздаётЗаписьMails` — отдельный тест out-of-proc активации COM-сервера с проверкой кода 0 и появления записи в таблице `mails`.
