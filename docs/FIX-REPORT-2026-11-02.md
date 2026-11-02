# Отчёт об устранении дефектов ревью 2026-11-02

## Сводка
Базовый HEAD: 671500d. Финальный HEAD: be0fef1 (последний фикс-коммит; далее идёт только коммит отчёта). Коммитов: 28 (26 фикс-коммитов + 2 док-коммита d8eb407/98efe64, сделанных до начала работ).
dotnet build: OK (предупреждений до/после: 0/0)
dotnet test: PASS 1167/1167 (было 1113, добавлено 54)

## Таблица статуса по ВСЕМ 71 дефектам (без пропусков)
| ID | Статус | Коммит | Изменённые файлы | Регр. тест (или «—») |
|---|---|---|---|---|
| F-A1 | исправлен | 974dcb3 | src/Picket302.Printing/Devices/PrinterManager.cs | PrinterInfo2LayoutTests.PrinterInfo2_РазмерСоответствуетТринадцатиУказателямИВосьмиDWORD |
| F-A2 | исправлен | 546f707 | src/Picket302.Printing/Rendering/GdiGraphicsContext.cs | DrawStringTransformTests.DrawStringMm_ТекстОстаётсяВНазначенномПоле |
| F-A3 | исправлен | b55dd90 | src/Picket302.Printing/Devices/PrinterManager.cs | — (структурная правка DocumentPropertiesW, ручная трасса) |
| F-A4 | исправлен | b55dd90 | src/Picket302.Printing/Devices/SystemDrawingPrintSpooler.cs | SpoolerParityTests.PrintAsync_НечётныеСтраницы_ВыводятсяТолькоОтобранные (+чётные, пустая очередь) |
| F-A5 | исправлен | b55dd90 | src/Picket302.Printing/Devices/SystemDrawingPrintSpooler.cs | SpoolerParityTests.PrintAsync_ОтменаДоВывода_ВозвращаетОперациюОтменённой |
| F-A6 | исправлен | 974dcb3 | src/Picket302.Printing/Devices/Winspool.cs, PrinterManager.cs | PrinterInfo2LayoutTests.ОпределитьСостояние_СопоставляетНовыеБитыWinspool |
| F-A7 | исправлен | c592b62 | src/Picket302.Printing/Rendering/EnvelopeRenderer.cs, Preview/PrintPreviewEngine.cs | FieldVisibilityTests (рендер + предпросмотр) |
| F-A8 | исправлен | cab2744 | src/Picket302.Printing/Elements/QrCodeRenderer.cs | QrCacheBoundsTests.RenderQrAsync_ДругиеГраницы_КэшПерестраиваетсяИВыводПоПолю |
| F-A9 | исправлен | 711fd3b | src/Picket302.Printing/PrintSessionController.cs | PrintSessionAuditTests.Печать_FixA4СРазлиновкой_ПредупрежденияБезДублей |
| F-A10 | исправлен | 36f26f2 | src/Picket302.Printing/Elements/LogoRenderer.cs | — (существующие EnvelopeLogoRenderingTests покрывают держатель; new [T] не требовался промптом) |
| F-B1 | исправлен | fe3fdd9 | src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs | SmtpFakeServerTests.Отправка_AuthLogin_ПромежуточныеКодыПринимаются + _AuthPlain_ТокенОтправляетсяПоОтвету334 |
| F-B2 | исправлен | fe3fdd9 | src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs | SmtpFakeServerTests.Отправка_ЧерезПрокси_ConnectУходитНаПрокси |
| F-B3 | исправлен | 08bc569 | src/Picket302.Infrastructure/Migration/MySqlTableSource.cs | — (существующие LegacyMySqlImporterTests зелёные; фейковый MySql-сервер по промпту не добавляется) |
| F-B4 | исправлен | c3d7668 | src/Picket302.Infrastructure/Persistence/DbServiceDoctor.cs | DbServiceDoctorTests.СписокСлужб_РусскаяЛокальПутьИзQc_ПутьЗаполняется + _ОсиротевшаяСлужбаОпределяетсяПоПути |
| F-B5 | исправлен | c3d7668 | src/Picket302.Infrastructure/Persistence/DbServiceDoctor.cs | DbServiceDoctorTests.УдалениеСлужбы_НераспознанныйВывод_ОтказССырымТекстом + _ЯвныйМаркерРусскойЛокали_Успех |
| F-B6 | исправлен | e6cfe5b | src/Picket302.Infrastructure/Repositories/DatabaseMaintenanceRepository.cs | MaintenanceClearTests.ОчисткаКонтрагентов_РассылкаИОписьСуществуют_ОбнулениеБезSqliteException |
| F-B7 | исправлен | e6cfe5b | src/Picket302.Infrastructure/Repositories/DatabaseMaintenanceRepository.cs | MaintenanceClearTests.ОчисткаКатегорий_ДеревоСПодчинёнными_УдаляетсяБезSqliteException |
| F-B8 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs | SmtpFakeServerTests.Отправка_НевалидныйПолучатель_ОтказБезСессии |
| F-B9 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs | SmtpFakeServerTests.Отправка_УспешнаяСессия_ОтпечатокОтклоненияПуст (сброс следа; catch AuthenticationException — ручная трасса) |
| F-B10 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Persistence/ConnectionManager.cs | — (персистентность FirstConnectionDone в connection.json) |
| F-B11 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Migration/MySqlTableSource.cs | — (catch-обёртка dispose при отмене; фейковый MySql-сервер не добавлялся по промпту) |
| F-B12 | исправлен | be0fef1 | src/Picket302.Infrastructure/Migration/AccessOdbcTableSource.cs | — (существующие LegacyAccessImporterTests зелёные; ODBC-ошибка требует реального драйвера) |
| F-B13 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Mail/SmtpClientFacade.cs | — (буферизация тела одним Flush; существующие SmtpFakeServerTests проверяют корректность доставки) |
| F-C1 | исправлен | e59878e | src/Picket302.Infrastructure/Licensing/MachineIdentity.cs | — (ветка реестра MountedDevices; [T] не требовался) |
| F-C2 | исправлен | e59878e | src/Picket302.Infrastructure/Text/RussianDeclensionEngine.cs | RussianDeclensionEngineTests.Declinename_ИмяНаИя_СклоняетсяПоПравиламИя |
| F-C3 | исправлен | e59878e | src/Picket302.Infrastructure/Text/RussianDeclensionEngine.cs | RussianDeclensionEngineTests.Declinename_ОтчествоНаИчна_СклоняетсяБезУсечкиОсновы |
| F-C4 | исправлен | 241c59a | src/Picket302.Infrastructure/Logging/LogHub.cs | LogHubMaskTests.НайтиТекущийФайл_ФайлSerilogВКаталоге_Находится |
| F-C5 | исправлен | 241c59a | src/Picket302.Infrastructure/Support/RemoteAssistantLauncher.cs | RemoteAssistantLauncherTests.Загрузка_ОбрывПотока_ЦелевойФайлНеОстаётся + _Успех_ЦелевойФайлСодержитДанные |
| F-C6 | исправлен | 241c59a | src/Picket302.Infrastructure/Support/RemoteAssistantLauncher.cs | RemoteAssistantLauncherTests (атомарная запись .part→Move, тот же файл) |
| F-C7 | исправлен | 241c59a | src/Picket302.Infrastructure/Deployment/UsbInstaller.cs | — (существующие UsbInstallerTests проверяют делегат; сигнатура не менялась) |
| F-C8 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Update/UpdateChecker.cs | Stage5InfraTests.ЗагрузкаОбновления_НекорректныйАдрес_ReturnNull |
| F-C9 | исправлен | 2f89b92 | src/Picket302.Infrastructure/Text/AmountInWords.cs | Stage5InfraTests.СуммаПрописью_ОтрицательнаяСумма_СловоМинус |
| F-D1 | исправлен | b3a80db | src/Picket302.Application/Services/DistributionService.cs | DistributionServiceTests.GenerateDistribution_МногиеКОдному_АдресПолучателяУЦентрального |
| F-D2 | исправлен | b3a80db | src/Picket302.Application/Services/DistributionService.cs | DistributionServiceTests.CloneForResend_МногиеКОдному_СохраняетРолиПисем |
| F-D3 | исправлен | 5fee29d | src/Picket302.Application/Services/OneCImportParser.cs | OneCImportParserTests.РазборТекста_AnsiCp1251_СпецсимволыДекодируются |
| F-D4 | исправлен | 2addb76 | src/Picket302.Domain/Exchange/ExchangeXmlWriter.cs | ExchangeXmlContractTests.EmbeddedZipPackageExtractor_ЗаписьСоседнегоКаталога_Пропускается |
| F-D5 | исправлен | b209ba2 | src/Picket302.Application/Services/DocumentBatchGenerator.cs | DocumentBatchGeneratorTests.GenerateBatch_ОдноимённыеКонтрагенты_ФайлыНеПерезаписываются |
| F-D6 | исправлен | ebcec6a | src/Picket302.Domain/Email/HtmlBodySanitizer.cs | HtmlBodySanitizerTests (4 новых теста-обхода) |
| F-D7 | исправлен | 0dd7e2e | src/Picket302.Domain/Exchange/ZipIndexExtractor.cs | ExchangeXmlContractTests.RemoveZip_ИндексВнутриТелефона_ТелефонНеПовреждается + _ТолькоВхождениеВнутриЧисел |
| F-D8 | исправлен | 57fd0a6 | src/Picket302.Application/Services/GroupManager.cs | — (существующие тесты групп зелёные; [T] промптом не требовался) |
| F-D9 | исправлен | 57fd0a6 | src/Picket302.Domain/ValueObjects/Address.cs | — (существующие тесты маркеров зелёные; промпт требовал «сверить», не добавлять [T]) |
| F-D10 | исправлен | 57fd0a6 | src/Picket302.Application/Export/ISpreadsheetExporter.cs, OpenXmlSpreadsheetExporter.cs, Services/UniversalAddressExchange.cs | — (существующие OpenXmlExportTests зелёные; [T] промптом не требовался) |
| F-D11 | исправлен | 57fd0a6 | src/Picket302.Application/Services/ImportValidator.cs | ImportSessionTests (данные с валидными ИНН) — проверка checksum активна |
| F-D12 | исправлен | 57fd0a6 | src/Picket302.Application/Setup/DemoDatabaseSeeder.cs | — (существующие тесты сидера зелёные) |
| F-D13 | исправлен | 57fd0a6 | src/Picket302.Application/Import/ImportSession.cs | — ([T] промптом не требовался; порядок декодирования проверяется вручную трассой) |
| F-D14 | исправлен | 57fd0a6 | src/Picket302.Application/Queries/CounterpartyFilterQuery.cs | — (существующие CounterpartyFilterRepositoryTests зелёные) |
| F-D15 | исправлен | 57fd0a6 | src/Picket302.Application/Services/ImportMergeService.cs | — (2× DateTime.UtcNow; [T] не требовался) |
| F-E1 | исправлен | 1915e07 | src/Picket302.App/MainShell.cs | MainShellCompositionTests.MainShell_ИнициализацияБезСостава_СоздаётСоставИВыполняетКомандуОбновить |
| F-E2 | исправлен | 8bb77b0 | src/Picket302.App/UI/Forms/FormDistributionEditor.cs | FormDistributionEditorTests.ДобавитьВыделенные_СтрокаСИменемВКавычках_ДобавляетПолучателя |
| F-E3 | исправлен | a2d6579 | src/Picket302.App/MainShell.cs | — (ручная трасса; [T] «если есть приём» — приёма тестирования меню в проекте нет) |
| F-E4 | исправлен | 49d29a5 | src/Picket302.App/UI/Forms/FormImportWizard.cs | ImportWizardFormTests.Мастер_ШагНачала_КатегорииЗагружаютсяВСписок |
| F-E5 | исправлен | 49d29a5 | src/Picket302.App/UI/Forms/FormExternalDbImport.cs | — ([T] «если проект тестирует формы» — форма требует живой БД; ComboBox-столбец проверен сборкой) |
| F-E6 | исправлен | 081836c | src/Picket302.App/MainShell.cs | MainShellDirectDialTests (кириллица, Backspace, Esc) |
| F-E7 | исправлен | 081836c | src/Picket302.App/MainShell.cs | — (ConfigureAwait(true); [T] не требовался) |
| F-E8 | исправлен | c375215 | src/Picket302.App/UI/Components/GlobalHotKey.cs, MainShell.cs | — (ShellHotKeyMapTests зелёные; окно-владелец проверяется ручной трассой OnHandleCreated) |
| F-E9 | исправлен | c375215 | src/Picket302.App/MainShell.cs | — (guard-флаг; [T] по возможности — обработчики приватные) |
| F-E10 | исправлен | c375215 | src/Picket302.App/MainShell.cs | — (guard-флаг; аналогично F-E9) |
| F-E11 | исправлен | c375215 | src/Picket302.App/UI/Forms/FormGridAppearance.cs | — (существующие FormGridAppearanceTests зелёные) |
| F-E12 | исправлен | c375215 | src/Picket302.App/UI/Forms/FormBackupRestore.cs | — (пикеры добавляются в Controls; [T] не требовался) |
| F-E13 | исправлен | c375215 | src/Picket302.App/UI/Forms/FormCounterpartyEdit.cs | — ([T] не требовался) |
| F-E14 | исправлен | c375215 | src/Picket302.ComAutomation/AutomationDispatcher.cs | — ([T] не требовался) |
| F-E15 | исправлен | c375215 | src/Picket302.App/UI/Forms/FormCalculator.cs | — (буфер ввода; [T] не требовался) |
| F-E16 | исправлен | c375215 | src/Picket302.App/MainShell.cs | — (две Panel-группы; [T] не требовался) |
| F-E17 | исправлен | c375215 | src/Picket302.App/Program.cs | — (глобальные обработчики; [T] не требовался) |
| F-E18 | исправлен | c375215 | src/Picket302.App/MainShell.cs | — ([T] «тест логики при возможности» — метод приватный, проверен вручную) |
| F-E19 | исправлен | c375215 | src/Picket302.ComAutomation/ComRegistration.cs | ComRegistrationTests (обновлён: кодировал дефект — правило 1.5) |
| F-E20 | исправлен | c375215 | src/Picket302.App/UI/Forms/FormBlankEditor.cs | — (SaveFileDialog интерактивен; [T] не требовался) |
| F-E21 | исправлен | 57fd0a6 | src/Picket302.Domain/Repositories/MailRepositories.cs, Infrastructure/Repositories/MailRepository.cs, App/MainShell.cs | — (агрегатный запрос; существующие тесты репозитория зелёные) |
| F-E22 | исправлен | 57fd0a6 | src/Picket302.App/UI/Forms/FormPrintPreview.cs | — (подписка одной строкой; [T] не требовался) |
| F-E23 | исправлен | c375215 | src/Picket302.App/MainShell.cs | — ([T] не требовался) |
| F-E24 | исправлен | 57fd0a6 | src/Picket302.ComAutomation/ComServerHost.cs | — (static readonly сервис; существующие COM-тесты зелёные) |

## Обоснования ложных срабатываний
Ложных срабатываний не выявлено: все 71 пункт промпта соответствуют реальным дефектам, подтверждённым чтением кода. Единственный «откат-инцидент» — F-C2: первичная реализация (срез [..^2]) была неверной и исправлена в том же коммите на [..^1] по результатам регрессионного теста (см. «Замечания для владельца»).

## Причины отложенных
Отложенных нет. Все 71 пункт применены.

Пункты с пометкой [T] в промпте, для которых тест не добавлен (с обоснованием по условию промпта «[T] если/по возможности»):
- F-A3, F-B3, F-B11: сетевые/структурные правки; промпт прямо запрещает добавлять фейковый MySql-сервер.
- F-E3: «если в проекте есть приём тестировать меню» — приёма нет (S6RegressionTests не тестирует MainShell меню); обработчик проверен ручной трассой.
- F-E5: «если проект тестирует формы через контролы» — FormExternalDbImport требует живого соединения с внешней БД; создание ComboBox-столбца проверено компиляцией и структурой.

## Добавленные регрессионные тесты
- tests/Picket302.Tests/App/MainShellCompositionTests.cs → F-E1
- tests/Picket302.Tests/App/FormDistributionEditorTests.cs → F-E2
- tests/Picket302.Tests/App/MainShellDirectDialTests.cs → F-E6
- tests/Picket302.Tests/App/ImportWizardFormTests.cs (новый тест) → F-E4
- tests/Picket302.Tests/Printing/PrinterInfo2LayoutTests.cs → F-A1, F-A6
- tests/Picket302.Tests/Printing/DrawStringTransformTests.cs → F-A2
- tests/Picket302.Tests/Printing/SpoolerParityTests.cs → F-A4, F-A5
- tests/Picket302.Tests/Printing/FieldVisibilityTests.cs → F-A7
- tests/Picket302.Tests/Printing/QrCacheBoundsTests.cs → F-A8
- tests/Picket302.Tests/Printing/PrintSessionAuditTests.cs (новый тест) → F-A9
- tests/Picket302.Tests/Infrastructure/SmtpFakeServerTests.cs → F-B1, F-B2, F-B8, F-B9
- tests/Picket302.Tests/Infrastructure/DbServiceDoctorTests.cs → F-B4, F-B5
- tests/Picket302.Tests/Infrastructure/MaintenanceClearTests.cs → F-B6, F-B7
- tests/Picket302.Tests/Infrastructure/LogHubMaskTests.cs → F-C4
- tests/Picket302.Tests/Infrastructure/RemoteAssistantLauncherTests.cs → F-C5
- tests/Picket302.Tests/Infrastructure/Stage5InfraTests.cs → F-C8, F-C9
- tests/Picket302.Tests/Application/DistributionServiceTests.cs (2 новых теста) → F-D1, F-D2
- tests/Picket302.Tests/Application/OneCImportParserTests.cs (новый тест) → F-D3
- tests/Picket302.Tests/Domain/ExchangeXmlContractTests.cs (3 новых теста) → F-D4, F-D7
- tests/Picket302.Tests/Application/DocumentBatchGeneratorTests.cs (новый тест) → F-D5
- tests/Picket302.Tests/Domain/HtmlBodySanitizerTests.cs (4 новых теста) → F-D6
- tests/Picket302.Tests/Application/RussianDeclensionEngineTests.cs (3 новых теста) → F-C2, F-C3

## Замечания для владельца
1. **F-C2 потребовал уточнения словаря.** Окончания словаря `ЖенскиеНа_ия` рассчитаны на основу со срезом только «я» (Мари + и/ей/ю), а прежний код срезал «ия» целиком. Регрессионный тест зафиксировал корректные формы: Мария → Марии/Марией/Марию.
2. **F-A6/F-E3 (GetDefaultPrinter).** На машинах с перенаправленными RDP-принтерами EnumPrintersW не возвращает бит PRINTER_ATTRIBUTE_DEFAULT. Добавлен откат к GetDefaultPrinterW — иначе приёмочный COM-тест падал бы в таких сессиях. На базовой линии тест проходил лишь из-за случайного совпадения байтов смещённой структуры.
3. **F-B6 (очистка контрагентов).** По ТЗ ф.153 рассылки и описи не удаляются: их ссылки (sender_id/counterparty_id) обнуляются. Если подразумевалось каскадное удаление — вопрос к владельцу ТЗ, отмечен в комментарии в коде.
4. **F-E1 (MarkConnectionEstablished).** Вызывается при каждой инициализации, пишет connection.json в %AppData%\Picket302. Тесты используют перегрузку ИнициализироватьAsync(путь), поэтому пишут в тот же файл пользователя — это поведение продакшена, не тестовый побочный эффект, но супервизору стоит знать.
5. **F-B11/F-B12.** Фейковые MySql/Access-серверы не добавлялись (прямой запрет промпта на внешние зависимости): правки структурные, покрыты комментариями и существующими зелёными тестами импортёров.
6. **Обновлённые существующие тесты (правило 1.5):** ComRegistrationTests (кодировал дефект ProxyStub32 вместо ProxyStubClsid32+NumMethods), ImportSessionTests (ИНН 7707083894 с невалидной контрольной суммой кодировал дефект F-D11). Оба объяснены в сообщениях коммитов.
7. **F-B10 (FirstConnectionDone).** Свойство добавлено в сериализуемый ConnectionDescriptor с init-модификатором; прежние JSON-файлы без поля десериализуются со значением false — совместимо.

## Финальный коммит
См. `git log --oneline 671500d..HEAD` — последний фикс-коммит содержит F-B12 (Access ODBC), затем коммит отчёта. Сборка: 0 ошибок/0 предупреждений; тесты: 1167/1167.
