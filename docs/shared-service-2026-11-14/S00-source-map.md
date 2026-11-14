# S00 — SOURCE-MAP совместного режима и архитектурный ADR

Дата: 2026-11-14. Base: `323852039e5f09d514aafd0f8bb508a14dddce36`
(`origin/main` на момент выдачи задания совпадает с HEAD).

Этап 0 из ТЗ «ПОРЯДОК РАБОТЫ И ЧЕКПОЙНТЫ». Документ не описывает
реализованную функциональность: это **карта исходного кода** и **зафиксированное
архитектурное решение**. Фактические статусы (`SHARED` / `LOCAL_ONLY` /
`NOT_IMPLEMENTED` / `PARTIAL`) выставляются по мере прохождения этапов 1–8 и
фиксируются в итоговом отчёте владельцу (S50).

---

## 1. Что означает «общий режим» в этом документе

| Граница | Значение |
|---|---|
| **SHARED** | Данные живут в общей базе на серверном ПК. Клиент обращается к ним через типизированный API; сервер исполняет доменные правила теми же репозиториями SQLite, что и локальный режим. |
| **LOCAL_ONLY** | Данные остаются на конкретном ПК. В общем режиме они **не** переезжают на другие машины и **не** попадают в общую базу. |
| **NOT_IMPLEMENTED** | Модуль в общем режиме сознательно недоступен: UI отключается или показывает предупреждение, данные не теряются и не создаются «наполовину». |
| **PARTIAL** | Модуль работает, но с осознанным ограничением; ограничение объявлено пользователю. |

Принцип: **приложение никогда не подменяет недоступный сервер местной базой.**
Если сервер недоступен — клиент сообщает об этом и не пишет данные в
местную базу (критерий приёмки из ТЗ, «сервер недоступен при запуске и
исчезает посреди операции»).

---

## 2. Ключевые опоры текущего кода (проверено на BASE_SHA)

| Опора | Файл:строка | Что там фактически |
|---|---|---|
| Единый `SqliteUnitOfWork` на всё приложение | `src/Picket302.Infrastructure/Data/SqliteUnitOfWork.cs:25` | Одно соединение открывается в конструкторе и живёт весь срок приложения. PRAGMA `journal_mode`/`busy_timeout` **не задаются** нигде в `src/` (единственный PRAGMA в проекте — `foreign_keys` в `MigrationRunner`). |
| Композиция репозиториев | `src/Picket302.App/AppServices.cs:88` (блок `Контрагенты = new CounterpartyRepository(сессия)` … `Обслуживание = new DatabaseMaintenanceRepository(сессия)`) | 12+ конкретных SQLite-репозиториев, все — над одной сессией. |
| Свойство `Сессия` у `AppServices` | `src/Picket302.App/AppServices.cs:253` | Тип `SqliteUnitOfWork`. Через него завязаны `PrintSessionController`, пользовательские отчёты (`AppServices.cs:224` — `new SqliteReportDataProvider(сессия.Connection)`), входящая корреспонденция, COM (`MainShell.cs:2236`), email-журнал (`MainShell.cs:2494`). |
| Транзакции — на уровне прикладных служб | `src/Picket302.Domain/IUnitOfWork.cs:7` | `IUnitOfWork` умеет только `Begin/Commit/Rollback`. `SaveChangesAsync` в проекте **нет**. 12 прикладных служб берут `IUnitOfWork` в конструктор и сами открывают транзакцию. |
| Перечисление режимов подключения | `src/Picket302.Infrastructure/Persistence/ConnectionManager.cs:14` | `DatabaseKind.LocalSqlite = 0`, `DatabaseKind.ServerPostgres = 1`. Значение `1` не переопределяем. |
| Диалог подключения | `src/Picket302.App/UI/Forms/FormConnectionSettings.cs:101` | Режим «Сервер» собирает `ServerPostgres` и спрашивает имя БД/пользователя/пароль, порт по умолчанию `5432` (строка 38). |
| Живое окно не открывает серверные реквизиты | `src/Picket302.App/MainShell.cs:2284` | `ОткрытьБазу()` показывает информационное сообщение и **не** переключает базу: «Открытие в работающем окне возможно только для файла базы данных на этом компьютере». То есть текущий «экран сервера» — декларация, а не работающий сетевой режим. |
| Резервное копирование | `src/Picket302.Infrastructure/Backup/BackupService.cs:75` | `VACUUM INTO` в отдельный файл (не копирование рабочего db-файла), затем ZIP + AES-256-GCM. WAL-файлы при восстановлении удаляются (`BackupService.cs`, `УдалитьСопутствующиеФайлы`). |
| Калибровка принтера | `src/Picket302.Infrastructure/Repositories/PrinterProfileRepository.cs:15` | Таблица `printer_profiles`, `UNIQUE (printer_name, envelope_format)` — то есть калибровка **уже привязана к имени принтера**, но хранится в общей базе. |
| Шаблоны | `src/Picket302.Infrastructure/Repositories/PrintTemplateRepository.cs:11` + `src/Picket302.Infrastructure/Templates/TemplatePackageRepository.cs:29` | Строка в `print_templates` хранит только `file_path`; содержимое — `.lbk`-пакет (ZIP с `manifest.json` + `blank.xml`) в файловом каталоге `Templates/Packages`. |
| Пользовательские отчёты | `src/Picket302.Application/Services/CustomReportManager.cs:35` + `src/Picket302.Infrastructure/Queries/SqliteReportDataProvider.cs:12` | Пакеты «макет + query.xml» в каталоге профиля; запрос исполняется **поверх открытого соединения текущей сессии**. |
| COM-автоматизация | `src/Picket302.ComAutomation/AutomationDispatcher.cs:622` | Отдельный процесс открывает `SqliteUnitOfWork` по пути `%AppData%\Picket302\picket302.db` и **не читает** `connection.json`. |
| USB-установщик | `src/Picket302.Infrastructure/Deployment/UsbInstaller.cs:72` | Копирование на съёмный носитель. Службы, правила брандмауэра и установки Windows **не делает**. |

---

## 3. SOURCE-MAP: сценарии `AppServices` / `MainShell`

Колонка «Тест приёмки» — идентификатор проверки, которая будет добавлена в
`tests/Picket302.Tests` на соответствующем этапе. Статус — проектный
(PLAN); фактический статус выставляется в S50.

### 3.1. Адресная книга (контрагенты)

| № | Сценарий | Текущий код | Граница в общем режиме | Статус | Тест приёмки |
|---|---|---|---|---|---|
| 1 | Список контрагентов постранично с фильтром | `AppServices.cs:88` (`ICounterpartyRepository`), UI: `MainShell.cs:2135` | SHARED | PLAN | `Shared_CounterpartyPage_A_И_B_ВидятОдниДанные` |
| 2 | Карточка контрагента (чтение) | `AppServices.cs:88`, `ICounterpartyRepository.GetByIdAsync` | SHARED | PLAN | `Shared_CounterpartyRead_СовпадаетУОбоих` |
| 3 | Создание/изменение контрагента | `AppServices.cs:134` `CounterpartyService.SaveAsync` (транзакция `CounterpartyService.cs:73/86/90`) | SHARED — **серверная команда**: сохранение + проверка лицензии + `row_version` в одной транзакции | PLAN | `Shared_CounterpartySave_A_ВиденУ_B` |
| 4 | Мягкое удаление / восстановление | `AppServices.cs:134`, `CounterpartyService.SoftDeleteAsync` | SHARED | PLAN | `Shared_CounterpartySoftDelete_ВидноУ_B` |
| 5 | Пакетное удаление/восстановление | `AppServices.cs:147` `CounterpartyManager` | SHARED — серверная команда | PLAN | `Shared_BatchDelete_ОдинКонфликтНеТеряется` |
| 6 | Пакетное изменение поля | `AppServices.cs:146` `BatchEditService` (транзакция `BatchEditService.cs:163/171/177`) | SHARED — серверная команча | PLAN | `Shared_BatchEdit_ПрименяетсяНаСервере` |
| 7 | Пользовательские поля контрагента | `AppServices.cs:144` `CustomFieldManager` | SHARED | PLAN | `Shared_CustomField_ВидноУ_B` |
| 8 | Поиск по ИНН/УНН (merge-ключ) | `ICounterpartyRepository.FindByMergeKeyAsync` | SHARED | PLAN | `Shared_FindByMergeKey_ИспользуетсяИмпортом` |
| 9 | Различные значения поля (для фильтров) | `ICounterpartyRepository.GetDistinctFieldValuesAsync` | SHARED | PLAN | `Shared_DistinctValues_СовпадаютУОбоих` |
| 10 | Каскадное обновление адреса в непечатанных письмах | `AppServices.cs:145` `CascadeAddressUpdater` | SHARED — серверная команда | PLAN | `Shared_CascadeAddress_ПрименяетсяНаСервере` |
| 11 | Дерево категорий | `AppServices.cs:148` `CategoryTreeService` | SHARED | PLAN | `Shared_CategoryTree_ВидноУ_B` |
| 12 | Создание/переименование/удаление категории, перенос контрагента | `CategoryTreeService` (транзакции `91/96/100`, `198–220`) | SHARED — серверные команды | PLAN | `Shared_CategoryMutations_ВидныУ_B` |
| 13 | Отправители (`IsSender`) | `ICounterpartyRepository.GetSendersAsync` | SHARED — список общий | PLAN | `Shared_Senders_СписокОбщий` |
| 14 | **Текущий выбранный отправитель** | `AppServices.cs:131` `CurrentSenderContext` (`реестр HKCU` + `ParameterCurrentSenderStore` в общей базе) | **LOCAL_ONLY** — выбор рабочего места остаётся на ПК; в общей базе хранится только признак «отправитель» у контрагента | PLAN | `Shared_CurrentSender_НеПереезжаетНаДругойПК` |

### 3.2. Отправления, папки, рассылки

| № | Сценарий | Текущий код | Граница в общем режиме | Статус | Тест приёмки |
|---|---|---|---|---|---|
| 15 | Список писем постранично | `AppServices.cs:91`, UI `MainShell.cs` (загрузка отправленных) | SHARED | PLAN | `Shared_MailPage_ВидноУ_B` |
| 16 | Создание отправления | `AppServices.cs:136` `MailService.CreateAsync` (транзакция `MailService.cs:87/92/96`) | SHARED — серверная команда; **нумерация выдаётся на сервере в той же транзакции** | PLAN | `Shared_MailCreate_НомерВыданНаСервере` |
| 17 | Смена статуса отправления | `MailService.TransitionStatusAsync` (транзакция `133–142`) | SHARED — серверная команда | PLAN | `Shared_MailStatus_ВиденУ_B` |
| 18 | Перенос письма в папку | `MailService.MoveToFolderAsync` (транзакция `171–180`) | SHARED | PLAN | `Shared_MailMove_ВидноУ_B` |
| 19 | Обновление адреса получателя в черновике | `IMailRepository.UpdateDraftRecipientAddressAsync` | SHARED | PLAN | `Shared_DraftAddress_ОбновляетсяНаСервере` |
| 20 | Счётчики писем по папкам | `IMailRepository.ПолучитьСчётчикиПоПапкамAsync` | SHARED | PLAN | `Shared_FolderCounts_СовпадаютУОбоих` |
| 21 | Папки (список/создание/переименование/перенос/удаление) | `AppServices.cs:94` `FolderRepository`; прикладная служба `FolderService` (транзакции `61/103/147`) | SHARED | PLAN | `Shared_Folders_ВидныУ_B` |
| 22 | Формирование рассылки (создание писем по выборке) | `AppServices.cs:142` `DistributionService.GenerateDistributionAsync` (транзакция `151–193`) | SHARED — **одна серверная команда**, одна транзакция: рассылка + N писем | PLAN | `Shared_DistributionGenerate_ОднаКоманда` |
| 23 | Создание рассылки из выбранных контрагентов | `DistributionService.CreateFromSelectionAsync` (транзакция `302–370`) | SHARED | PLAN | `Shared_DistributionFromSelection_ОднаКоманда` |
| 24 | Клонирование рассылки для повторной отправки | `DistributionService.CloneForResendAsync` (транзакция `441–531`) | SHARED | PLAN | `Shared_DistributionClone_ОднаКоманда` |
| 25 | Правка сохранённой рассылки (добавление/удаление писем) | `DistributionService.UpdateDistributionAsync` | SHARED | PLAN | `Shared_DistributionUpdate_ОднаКоманда` |
| 26 | Список рассылок (фильтр) | `DistributionService.ListDistributionsAsync` | SHARED | PLAN | `Shared_DistributionList_ВиденУ_B` |
| 27 | Отправка/сохранение/пересчёт конвертов | `DistributionService.SaveAsync/MarkDispatchedAsync/RecalculateEnvelopesRequiredAsync` | SHARED | PLAN | `Shared_DistributionState_ВиденУ_B` |
| 28 | **Сквозная регистрационная нумерация** | `AppServices.cs:150` `RegistrationNumberGenerator` → `AppServices.cs:99` `ParameterRegistrationNumberStore` (`INSERT … ON CONFLICT … RETURNING`, атомарно) | SHARED — счётчик в общей базе, выдаётся **внутри** транзакции серверной команды | PLAN | `Shared_Numbering_ПятьКлиентов_НетДублей` |
| 29 | Журнал печати (`print_history`) | `AppServices.cs:141` `PrintHistoryRepository`; `AppServices.cs:194` `PrintHistoryRecorder` | SHARED — общая семантика: «подготовлено → передано в спулер клиента → ошибка» + `device_id` | PLAN | `Shared_PrintHistory_ВиднаУ_B_С_deviceId` |
| 30 | Реестр печатных сессий (`print_jobs`, неизменяемый) | `AppServices.cs:142` `PrintJobRepository` | SHARED | PLAN | `Shared_PrintJobs_ОбщийЖурнал` |
| 31 | Документы письма (`mail_documents`) | `IMailDocumentRepository` | SHARED | PLAN | `Shared_MailDocuments_ВидныУ_B` |
| 32 | Движение документов (`document_movements`) | `IDocumentMovementRepository` | SHARED | PLAN | `Shared_DocumentMovements_ОбщаяИстория` |
| 33 | Почтовые реестры (`post_registers`) | `IPostRegisterRepository` | SHARED | PLAN | `Shared_PostRegisters_ВидныУ_B` |
| 34 | Описи (`inventories`, `inventory_items`) | `IInventoryRepository`, `IInventoryItemRepository` | SHARED | PLAN | `Shared_Inventories_ВидныУ_B` |
| 35 | Журнал email-рассылок (`email_log`, V007) | `AppServices.cs:2494` → `src/Picket302.Infrastructure/Mail/EmailLogWriter.cs:35` | SHARED — журнал общий; SMTP-сессия выполняется на ПК отправителя | PLAN | `Shared_EmailLog_ВиденУ_B` |

### 3.3. Шаблоны и печать

| № | Сценарий | Текущий код | Граница в общем режиме | Статус | Тест приёмки |
|---|---|---|---|---|---|
| 36 | Шаблоны конвертов/бланков (строки `print_templates`) | `AppServices.cs:154` | SHARED | PLAN | `Shared_TemplateRows_ВидныУ_B` |
| 37 | Поля бланка (`blank_fields`) | `AppServices.cs:155` | SHARED | PLAN | `Shared_BlankFields_ВидныУ_B` |
| 38 | **Файловые `.lbk`-пакеты** | `AppServices.cs:154` + `TemplatePackageRepository.cs:29` + `LbkPackageArchiver.cs:81` | SHARED — пакеты хранятся **на сервере** с версией и SHA-256; клиент получает конкретную версию для предпросмотра/печати и держит ограниченный локальный кэш | PLAN | `Shared_Lbk_ДобавленНаA_ДоступенНаB` |
| 39 | Кэш шаблонов и его инвалидация | — (нет) | LOCAL_ONLY (кэш) — кэш не отдаёт старый шаблон после смены версии | PLAN | `Shared_LbkCache_СтараяВерсияНеОтдаётся` |
| 40 | Данные отправлений для печати | `AppServices.cs:157` `PrintDataSource` | SHARED — данные берутся из общей базы | PLAN | `Shared_PrintData_ИзОбщейБазы` |
| 41 | **Предпросмотр и вызов спулера** | `AppServices.cs:199` `PrintPreviewEngine`, `AppServices.cs:181` `PrintSessionController` (`src/Picket302.Printing/PrintSessionController.cs`), `СпулерПечати` | **LOCAL_ONLY** — визуализация и отправка задания в спулер происходят на ПК пользователя; сервер никогда не печатает | PLAN | `Shared_Print_СерверНеВызываетСпулер` |
| 42 | **Выбор принтера и калибровка** | `AppServices.cs:193` `new PrinterProfileRepository(сессия)`, `AppServices.cs:197` `PrinterOffsetProfile` | **LOCAL_ONLY** — переносится в локальный sidecar профиля; в общей базе остаётся legacy-таблица без изменений | PLAN | `Shared_PrinterProfileA_НеПоявляетсяУB` |
| 43 | Тихая печать (`SilentPrintService`, `ISilentPrintChannel`) | `src/Picket302.Application/Services/SilentPrintService.cs:65` | LOCAL_ONLY (канал печати) + SHARED (данные) | PLAN | `Shared_SilentPrint_ДанныеИзОбщейБазы` |
| 44 | Параметры печати (`ПоследнийПринтер`, `ПоследниеКопии`, `ПоследнийШаблонId`, `ПоказыватьСписокШаблонов`) | `AppServices.cs:158` `PrintOptions` → таблица `parameters` | **LOCAL_ONLY** — параметры рабочего места, в частности `ПоследнийПринтер` | PLAN | `Shared_PrintOptions_НеПереезжают` |
| 45 | Журнал аудита печати (`print_audit.log`) | `AppServices.cs:186` `PrintAuditWriter` (каталог профиля) | LOCAL_ONLY | PLAN | — |

### 3.4. Служебные модули, найденные в коде (в том числе ранее неизвестные)

| № | Модуль | Текущий код | Граница в общем режиме | Статус | Тест приёмки |
|---|---|---|---|---|---|
| 46 | **QR-коды** (`qr_cache`, V001) | `AppServices.cs:178` `QrCacheRepository` → `QrCodeRenderer` | SHARED (кэш кодов — производный от данных, общий) | PLAN | `Shared_QrCache_Общий` |
| 47 | **Входящая корреспонденция и сканы** | `AppServices.cs:232–239` `IncomingMailRepository` + `IncomingMailService` (`src/Picket302.Application/Services/IncomingMailService.cs:19`), сканы в каталоге профиля | **NOT_IMPLEMENTED** в общем режиме: журнал в общей базе ссылался бы на файл, которого нет на чужом ПК. Модуль отключается с понятным сообщением, данные не теряются | PLAN | `Shared_IncomingMail_ОтключеноСMessage` |
| 48 | **Движение входящих документов** | `AppServices.cs:237` `IncomingMovementTracker` | NOT_IMPLEMENTED (следствие №47) | PLAN | — |
| 49 | **Пользовательские отчёты** (пакеты + `query.xml`) | `AppServices.cs:223–224` `CustomReportManager` + `SqliteReportDataProvider(сессия.Connection)` | **NOT_IMPLEMENTED** в общем режиме: запрос исполняется по открытому соединению SQLite; произвольный SQL по общей базе клиенту не выдаётся | PLAN | `Shared_UserReports_ОтключеноСMessage` |
| 50 | **Дампер промежуточных данных отчётов** | `AppServices.cs:228` `ReportDataDumper` (пишет temp-XML) | LOCAL_ONLY (следствие №49) | PLAN | — |
| 51 | **COM-автоматизация** | `src/Picket302.ComAutomation/AutomationDispatcher.cs:622` — открывает `%AppData%\Picket302\picket302.db` мимо `connection.json` | **NOT_IMPLEMENTED** в общем режиме: сервер автоматизации обязан **отказать**, а не открыть локальную базу | PLAN | `Shared_Com_ОтказВместоЛокальнойБазы` |
| 52 | **Вложения email-рассылок** | `src/Picket302.Application/Services/AttachmentService.cs:21`, каталог `%AppData%\Picket302\Attachments`, лимит 25 МБ | **LOCAL_ONLY** (файлы на ПК отправителя) + SHARED (получатели берутся из общей базы, журнал `email_log` общий). Новая система вложений не вводится | PLAN | `Shared_Attachment_ФайлыНеОбщие` |
| 53 | **SMTP-отправка** | `src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs` | LOCAL_ONLY (сеть и учётные данные клиента) + SHARED (журнал) | PLAN | `Shared_Smtp_ЖурналОбщий` |
| 54 | **Настройки почты и прокси** (`mail-settings.json`) | `src/Picket302.Infrastructure/Mail/SmtpOptions.cs` | LOCAL_ONLY | PLAN | — |
| 55 | **Учётные данные Почты России** (`RussianPost.*` в `parameters`) | `AppServices.cs:461` | LOCAL_ONLY (учётные данные конкретного сотрудника) | PLAN | — |
| 56 | **Лицензия и идентичность машины** | `AppServices.cs:107–121`, `MachineIdentity`, `LicenseStore` (`license.dat`) | LOCAL_ONLY (привязана к ПК) | PLAN | — |
| 57 | **Счётчик использования для лимита trial** | `src/Picket302.Infrastructure/Licensing/DatabaseUsageCounter.cs:25` (считает строки по сессии) | SHARED (считает по общей базе) — реализуется серверным снимком, клиент получает его по API | PLAN | `Shared_TrialCounter_СчитаетсяПоОбщейБазе` |
| 58 | **Оформление окна, вид таблиц, сохранённые фильтры** | `AppServices.cs:97–98` `JsonUiSettingsStore` (`ui-settings.json`), `SavedFilterService` | LOCAL_ONLY | PLAN | — |
| 59 | **Демо-данные и очистка базы** | `AppServices.cs:151` `DemoDatabaseSeeder` (`GetScopeCountsAsync`, `ClearDatabaseAsync`) | SHARED — серверные команды (очистка общей базы — решение оператора, с обычным подтверждением) | PLAN | `Shared_ClearScope_СчитаетсяНаСервере` |
| 60 | **Импорт адресной книги** (CSV/RTF/1С) | `src/Picket302.Application/Import/ImportSession.cs`, `ImportMergeService`, `ImportDeduplicator`; UI `ImportWizardController` | SHARED — разбор файла остаётся на клиенте, **слияние записей выполняется серверной командой** на тех же `ImportMergeService`/`ImportDeduplicator` | PLAN | `Shared_ImportMerge_ВыполняетсяНаСервере` |
| 61 | **Импорт из внешней БД** (ODBC/MySQL/SQL Server) | `src/Picket302.Infrastructure/Integration/ExternalDbImporter.cs` | LOCAL_ONLY (разбор внешнего источника) + SHARED (запись результата — серверной командой) | PLAN | — |
| 62 | **Экспорт адресной книги** (CSV/RTF/XLSX) | `src/Picket302.Application/Services/UniversalAddressExchange.cs` | LOCAL_ONLY (файл на ПК) + SHARED (данные из общей базы) | PLAN | — |
| 63 | **Резервное копирование/восстановление из клиента** | `AppServices.cs:210` `МенеджерПодключения`, `BackupService`, `RestoreService`, `ScheduledBackupService` | **NOT_IMPLEMENTED** в клиенте общего режима: копии общей базы делает сервер. Клиент получает понятное сообщение | PLAN | `Shared_ClientBackup_ОтключеноСMessage` |
| 64 | **Реестр серверов** (`servers.ini`) | `AppServices.cs:212` `ServerRegistry` (`src/Picket302.Infrastructure/Persistence/ServerRegistry.cs:109`) | LOCAL_ONLY (файл профиля) — переиспользуется как список известных серверов Picket302 | PLAN | — |
| 65 | **Диагностика службы БД** (`DbServiceDoctor`) | `src/Picket302.Infrastructure/Persistence/DbServiceDoctor.cs:62` | LOCAL_ONLY — относится к прежней службе БД, в общем режиме не применяется | PLAN | — |
| 66 | **Установка на USB** | `src/Picket302.Infrastructure/Deployment/UsbInstaller.cs:72` | LOCAL_ONLY — переносимая однопользовательская поставка, к общему режиму не относится | PLAN | — |
| 67 | **Обновление программы** (`UpdateChecker`) | `src/Picket302.Infrastructure/Update/` | LOCAL_ONLY — загрузка и проверка целостности на ПК клиента | PLAN | — |
| 68 | **Защищённое хранилище секретов** (DPAPI) | `src/Picket302.Infrastructure/Integration/DpapiSecretStore.cs` | LOCAL_ONLY — секрет устройства хранится на ПК клиента в DPAPI | PLAN | `Shared_DeviceSecret_ВDPAPI_НеВJson` |
| 69 | **Журналы приложения** (`logs/`) | `AppServices.cs:103` `LogHub` | LOCAL_ONLY | PLAN | — |
| 70 | **Изоляция профиля** | `src/Picket302.Infrastructure/ProfileIsolationGuard.cs:36` | LOCAL_ONLY — механизм проверок остаётся в силе для серверных и клиентских контуров | PLAN | `Shared_ProfileIsolation_РаботаетНаСервере` |
| 71 | **Политика резервного копирования** | `src/Picket302.Infrastructure/Backup/BackupPolicyService.cs` | LOCAL_ONLY на клиенте; сервер использует свой экземпляр поверх общей базы | PLAN | — |
| 72 | **QR/иерархия папок/групп — триггеры целостности** | `V001`–`V011` (`trg_folders_no_cycle_*`, `trg_groups_no_cycle_*`, `ux_groups_single_default`, `trg_print_jobs_no_update/no_delete`) | SHARED — работают на серверной базе без изменений | PLAN | — |

---

## 4. Матрица «общий / локальный / недоступно» — сводка

| Область | Общий режим | Итог |
|---|---|---|
| Контрагенты, группы/категории, пользовательские поля, папки | Общее | SHARED |
| Отправления, рассылки, нумерация, статусы, реестры, описи, документы | Общее | SHARED |
| Шаблоны (строки БД) и `.lbk`-пакеты | Общее (с версиями и хешами) | SHARED |
| Журнал печати, реестр печатных сессий, журнал email | Общее (с `device_id`) | SHARED |
| Принтер, калибровка принтера, параметры печати | Локально | LOCAL_ONLY |
| Предпросмотр и отправка в спулер | Локально | LOCAL_ONLY |
| Оформление, сетки, фильтры, лицензия, секреты, журналы, вложения, сканы | Локально | LOCAL_ONLY |
| Пользовательские отчёты (SQL по общей базе) | Недоступно с понятным сообщением | NOT_IMPLEMENTED |
| COM-автоматизация | Недоступно с понятным сообщением | NOT_IMPLEMENTED |
| Входящая корреспонденция и сканы | Недоступно с понятным сообщением | NOT_IMPLEMENTED |
| Резервное копирование общей базы из клиента | Недоступно; делает сервер | NOT_IMPLEMENTED |

**Явно не заявляется:** «вся программа работает через сервер». Перечисленные
четыре модуля недоступны в общем режиме и будут отключены в UI с сообщением.

---

## 5. ADR: почему «служба + SQLite на серверном ПК», а не общая папка SQLite и не PostgreSQL

### 5.1. Контекст

Требование: 1–5 рабочих мест в одном офисе, общая база адресов, отправлений,
рассылок и шаблонов; существующий одиночный режим сохраняется полностью;
PostgreSQL в этой волне не устанавливается и не реализуется.

### 5.2. Рассмотренные варианты

**Вариант A — общая папка SQLite по SMB/UNC.** Клиент открывает файл базы
сервера как сетевой диск.

* Отвергнут.* SQLite не поддерживает сетевой файловый доступ: блокировки
  опираются на локальные семафоры файловой системы, а SMB их не обеспечивает.
  Итог — повреждение базы при параллельной работе пяти клиентов. Кроме того,
  запрет открывать файл БД по UNC прямо задан ТЗ.

**Вариант B — PostgreSQL на сервере.**

* Отвергнут в этой волне* по прямому указанию ТЗ («PostgreSQL не устанавливать
  и не реализовывать»). Дополнительно: в коде нет ни драйвера, ни PG-миграций,
  ни `PgUnitOfWork` — это отдельный объём (оценка S41: 3–5 волн). Плюс
  «стоечное» оборудование и администрирование не соответствует профилю
  «обычный компьютер сотрудника».

**Вариант C — серверная служба Picket302, SQLite-файл на локальном диске
серверного ПК.** *(выбран)*

* Плюсы: используется существующий SQLite-стека проекта (`Microsoft.Data.Sqlite`
  + Dapper + `MigrationRunner`), те же доменные правила и те же репозитории
  работают без изменений; нет внешних СУБД и лицензий; поставляется как
  обычная служба Windows на одном офисном компьютере; существующий
  `BackupService` (`VACUUM INTO`) пригоден без изменений.
* Минусы: сервисный код — новый исполняемый проект; нужен собственный
  протокол; SQLite рассчитан на небольшое число писателей (5 клиентов в
  пределах требования — с замером в S51).

### 5.3. Решение

1. Общая база — **служба Windows** на одном офисном ПК, порт по умолчанию
   `30242/TCP` (не 5432 — чтобы не пересекаться с PostgreSQL).
2. Только служба открывает SQLite-файл, лежащий на **локальном** диске
   серверного ПК. Клиенты обращаются к типизированному API; SMB/UNC к файлу
   базы и прямой SQL-порт не используются.
3. Сервер исполняет **сценарии целиком** теми же прикладными службами и
   SQLite-репозиториями, что и локальный режим; бизнес-логика не дублируется
   в контроллерах. Клиент не получает ни произвольный SQL, ни путь к файлу БД,
   ни `SqliteConnection`, ни внутренние исключения.
4. Транзакционная граница — **одна серверная команда = одна короткая
   транзакция**. Удалённый `IUnitOfWork` не реализуется.
5. Служба работает под учётной записью с минимальными правами; каталог данных
   — под `ProgramData`, права ограничены; логи и секреты отделены от данных.
6. Доступ из интернета — вне объёма; публичные порты не открываются.
   Протокол спроектирован так, чтобы позже работал через VPN.

### 5.4. Последствия

* Появляется новый исполняемый проект (служба) и новый проект установки.
* Появляется контрактный слой (DTO, версия протокола, коды ошибок).
* Требуется механизм маркировки версий общих сущностей (`row_version`),
  механизм идемпотентности по идентификатору операции и локальный sidecar
  для принтерных профилей.
* Режим `ServerPostgres = 1` в `connection.json` **не переопределяется**: он
  продолжает означать прежнее, а новый режим получает отдельное стабильное
  значение.

---

## 6. Решения, требующие явной фиксации

| № | Решение | Основание |
|---|---|---|
| Р1 | Новое значение `DatabaseKind` — `SharedService = 2`; `LocalSqlite = 0` и `ServerPostgres = 1` не меняются | ТЗ: «Новый режим должен иметь отдельное стабильное значение и миграционные правила» |
| Р2 | `ServerPostgres = 1` при сохранённом значении показывает честное сообщение о недоступности PostgreSQL и **не** открывает локальную базу | ТЗ: «Existing ServerPostgres… должен показывать честное сообщение, а не тихо открывать локальную базу» |
| Р3 | Печать: сервер отдаёт данные и версию шаблона; визуализация и спулер — на ПК клиента | ТЗ, требование F |
| Р4 | Нумерация отправлений выдаётся на сервере внутри транзакции команды | ТЗ, требование E |
| Р5 | Пользовательские отчёты, COM, входящая корреспонденция — NOT_IMPLEMENTED в общем режиме (UI отключается с сообщением) | ТЗ, требование I |
| Р6 | Принтерные профили переезжают в локальный sidecar; исходная БД не изменяется при переходе | ТЗ, требование H |
| Р7 | Клиент в общем режиме не запускает резервное копирование общей базы | ТЗ, чекпоинт 7 |
| Р8 | Сопряжение — по одноразовому коду, показываемому локально; после сопряжения у клиента свой секрет устройства в DPAPI, на сервере — только отпечаток | ТЗ, раздел «ЗАЩИТА ПОДКЛЮЧЕНИЯ БЕЗ АККАУНТОВ» |
| Р9 | Закрепление доверия: клиент сверяет отпечаток сервера при сопряжении и закрепляет его для последующих соединений; `trust-all` и открытый HTTP запрещены | ТЗ, тот же раздел |
| Р10 | Первичный импорт: только одна исходная локальная база, выбранная человеком; staging на том же томе, проверки, переключение только после полной проверки | ТЗ, раздел «ПЕРЕНОС ДАННЫХ» |

---

## 7. Что считается «результат готов, когда два разных клиента выполняют
сквозные операции над одними данными»

Наименьший приёмочный набор (реализуется на этапах 2–6, проверяется
автотестами и затем на двух реальных ПК, если стенд доступен):

1. Клиент A создаёт отправителя, получателя, письмо и шаблон; клиент B видит
   те же данные и ту же версию шаблона.
2. A и B одновременно правят одну запись: один получает явный конфликт,
   ни одно изменение не теряется молча.
3. Пять клиентов одновременно создают отправления — номера уникальны.
4. Искусственный обрыв ответа после успешно выполненной команды; повтор
   запроса не создаёт второе письмо/рассылку.
5. Сервер недоступен при запуске и исчезает посреди операции — клиент не
   пишет в местную базу и ясно сообщает состояние.
6. Неправильное/отозванное устройство не читает и не меняет данные.
7. A выбирает свой принтер и калибровку, B имеет другую; ни одна настройка
   не переезжает на чужой ПК.
8. `.lbk`, добавленный на A, доступен на B; смена версии не возвращает старый
   кэш; злонамеренный архив и путь отклоняются.
9. Предпросмотр на клиенте использует общие данные; серверный процесс не
   вызывает спулер.
10. Прежние локальные тесты и целевые UI/COM/Printing-тесты не регрессируют.

---

## 8. Границы этого документа

Документ описывает **исходное состояние** кода на BASE_SHA и **принятое
архитектурное решение**. Он не является отчётом о выполненной работе.
Фактические результаты проверок, числа тестов, коды возврата сборки и
вердикт независимого ревью — в S50 (итоговый отчёт владельцу).
