using Picket302.Application.Services;
using Picket302.Application.Setup;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Domain.Services;
using Picket302.Infrastructure;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Fonts;
using Picket302.Infrastructure.Integration;
using Picket302.Infrastructure.Licensing;
using System.Drawing.Printing;
using Picket302.Infrastructure.Localization;
using Picket302.Infrastructure.Logging;
using Picket302.Infrastructure.Persistence;
using Picket302.Infrastructure.Queries;
using Picket302.Infrastructure.Registry;
using Picket302.Infrastructure.Repositories;
using Picket302.Infrastructure.Storage;
using Picket302.Infrastructure.Templates;
using Picket302.Infrastructure.Text;
using Picket302.Printing;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Preview;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;
using Picket302.Printing.Warnings;

namespace Picket302.App;

/// <summary>
/// Состав приложения: сессия базы данных, репозитории и службы доменного слоя.
/// </summary>
/// <remarks>
/// Состав собирается один раз при запуске и передаётся окнам приложения.
/// Путь к базе по умолчанию — профиль пользователя
/// (<c>%AppData%\Picket302\picket302.db</c>).
/// </remarks>
public sealed class AppServices : IAsyncDisposable
{
    /// <summary>Каталог профиля приложения.</summary>
    public const string КаталогПриложения = "Picket302";

    /// <summary>Имя файла базы данных по умолчанию.</summary>
    public const string ИмяФайлаБазы = "picket302.db";

    private readonly SqliteUnitOfWork _сессия;
    private readonly Picket302ProfilePaths _профиль;

    /// <summary>
    /// Спулер печати: экземпляр удерживается для проброса точки
    /// автоматических проверок (PrintToFile приёмочных драйверов) —
    /// в работе хук не задаётся.
    /// </summary>
    private readonly SystemDrawingPrintSpooler СпулерПечати = new();

    /// <summary>
    /// Точка автоматических проверок печати (S17, приёмка UI→спулер):
    /// пробрасывает штатный хук <see cref="SystemDrawingPrintSpooler.
    /// НастройкаДокументаДляПроверок"/> — включает PrintToFile штатного
    /// драйвера без интерактивного порта. Только для драйверов приёмки;
    /// в работе не задаётся.
    /// </summary>
    public Action<PrintDocument>? НастройкаДокументаПечатиДляПроверок
    {
        set => СпулерПечати.НастройкаДокументаДляПроверок = value;
    }

    private AppServices(
        SqliteUnitOfWork сессия,
        string путьБазы,
        Picket302ProfilePaths? пути = null,
        PrinterManager? службаПринтеров = null,
        bool реестрОтправителяИзолирован = false)
    {
        // Пути профиля: производственный код использует каталог по умолчанию
        // (%AppData%\Picket302), изолированные проверки — временный корень.
        _профиль = пути ?? Picket302ProfilePaths.ПоУмолчанию();
        var профиль = _профиль;

        _сессия = сессия;
        ПутьБазы = путьБазы;

        Контрагенты = new CounterpartyRepository(сессия);
        ПоляКонтрагентов = new CounterpartyFieldRepository(сессия);
        Группы = new GroupRepository(сессия);
        Письма = new MailRepository(сессия);
        Рассылки = new DistributionRepository(сессия);
        Параметры = new ParameterRepository(сессия);
        Папки = new FolderRepository(сессия);
        Обслуживание = new DatabaseMaintenanceRepository(сессия);

        ХранилищеНастроек = new JsonUiSettingsStore(профиль.UiSettingsPath);
        НастройкиОтображения = new UiSettingsStorage(ХранилищеНастроек);
        СчётчикиНумерации = new ParameterRegistrationNumberStore(сессия);

        // Журнал приложения с независимыми каналами (ТЗ §10.3; §8.11,
        // функция 111). Каталог журналов — <данные профиля>\logs.
        Журнал = new LogHub(профиль.LogsDirectory);

        // Лицензирование (ТЗ §8.10, функции 119–126): автомат состояний
        // вычисляется при старте, проверка ведётся в одной точке.
        Машина = new MachineIdentity(сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));
        ХранилищеЛицензии = new LicenseStore(
            профиль.LicensePath,
            null,
            Машина,
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));
        СчётчикиИспользования = DatabaseUsageCounter.Создать(сессия);
        АвтоматЛицензии = new LicenseStateMachine(
            ХранилищеЛицензии,
            null,
            null,
            () => СчётчикиИспользования?.Invoke() ?? new СнимокИспользования(0, 0));
        ВоротаЛицензии = new LicenseStateMachineGate(АвтоматЛицензии);
        МенеджерЛицензии = new LicenseManager(ХранилищеЛицензии, АвтоматЛицензии);
        АктивацияЛицензии = new ActivationService(ХранилищеЛицензии, АвтоматЛицензии);

        // Реестровое хранилище текущего отправителя подменяется в изолированном
        // режиме (stage3.1, контейнирование): составное хранилище пишет
        // SenderId и в HKCU\Software\PEPro\CurrentSender — приёмочные прогоны
        // не должны писать в реальную машину за пределами профиля.
        var реестрОтправителя = реестрОтправителяИзолирован
            ? new Picket302.Infrastructure.Registry.NullCurrentSenderStore()
            : (Picket302.Domain.Services.ICurrentSenderStore)new WindowsCurrentSenderRegistryStore();
        var параметрыОтправителя = new ParameterCurrentSenderStore(сессия);
        ТекущийОтправитель = new CurrentSenderContext(
            new CompositeCurrentSenderStore(реестрОтправителя, параметрыОтправителя));

        СлужбаКонтрагентов = new CounterpartyService(Контрагенты, сессия, ВоротаЛицензии);
        СлужбаГрупп = new GroupService(Группы, сессия);
        СлужбаПисем = new MailService(
            Письма,
            Контрагенты,
            new PrintHistoryRepository(сессия),
            сессия,
            ВоротаЛицензии);
        СлужбаРассылок = new DistributionService(Рассылки, Письма, Контрагенты, сессия, ВоротаЛицензии);
        СлужбаАдресов = new AddressParserService();
        СлужбаПолей = new CustomFieldManager(ПоляКонтрагентов, сессия);
        СлужбаКаскада = new CascadeAddressUpdater(Письма);
        СлужбаПакетногоРедактирования = new BatchEditService(Контрагенты, сессия, Параметры);
        СлужбаМенеджера = new CounterpartyManager(Контрагенты, сессия);
        СлужбаКатегорий = new CategoryTreeService(Группы, Контрагенты, сессия);
        СлужбаАгентов = new AgentService(Контрагенты, сессия);
        СлужбаНумерации = new RegistrationNumberGenerator(СчётчикиНумерации);
        СлужбаСидов = new DemoDatabaseSeeder(Группы, Контрагенты, ПоляКонтрагентов, Обслуживание, сессия);
        Склонение = new RussianDeclensionEngine();

        ШаблоныПечати = new PrintTemplateRepository(сессия);
        ПоляШаблонов = new BlankFieldRepository(сессия);
        ЖурналПечати = new PrintJobRepository(сессия);
        ДанныеПечати = new PrintDataSource(Письма, Контрагенты);
        ПараметрыПечати = new PrintOptions(Параметры);
        ПодсказкаПодачи = new FeedOrientationHint(Параметры);
        РежимРазлиновки = new PreprintedMode(Параметры);
        // Принтерный менеджер подменяем (stage3.1): acceptance-сценарий «нет
        // принтера» передаёт производный класс, возвращающий null, — реальный
        // системный принтер стенда при этом не изменяется.
        СлужбаПринтеров = службаПринтеров ?? new PrinterManager();

        var каталогАудита = Path.GetDirectoryName(профиль.PrintAuditPath)
            ?? Path.GetDirectoryName(Path.GetFullPath(путьБазы))
            ?? AppContext.BaseDirectory;

        var контрольШрифта = new ZipcodeFontGuard();
        var измеритель = new GdiTextMeasurer();
        КлипартБиблиотека = new ClipartLibrary();
        var рендерер = new EnvelopeRenderer(измеритель, id => { КлипартБиблиотека!.TryGet(id, out var png); return png; });

        // Кэш сгенерированных QR-кодов хранится в таблице qr_cache: без
        // репозитория сформированные коды оставались бы только в памяти
        // и таблица не наполнялась (ТЗ §3.1.8).
        var кэшКодов = new QrCacheRepository(сессия);
        var выводКодов = new QrCodeRenderer(кэшКодов);

        СлужбаПечати = new PrintSessionController(
            ШаблоныПечати,
            ПоляШаблонов,
            ЖурналПечати,
            ДанныеПечати,
            new PrintAuditWriter(каталогАудита),
            СпулерПечати,
            рендерер,
            new PaperSizeValidator(),
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer(),
            printerProfiles: new PrinterProfileRepository(сессия),
            printHistory: new PrintHistoryRecorder(СлужбаПисем),
            licenseGate: ВоротаЛицензии);

        ПрофильКалибровки = new PrinterOffsetProfile(new PrinterProfileRepository(сессия));

        СлужбаПредпросмотра = new PrintPreviewEngine(
            рендерер,
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer());

        // Журнал приложения с независимыми каналами (ТЗ §10.3; §8.11,
        // функция 111). Каталог журналов — <AppData>\Picket302\logs.

        // Реквизиты подключения к базе и справочник серверов (ТЗ §8.8,
        // функции 101 и 102; §8.11, функция 127).
        МенеджерПодключения = СоздатьМенеджерПодключенияДляДиалога();
        РеестрСерверов = СоздатьРеестрСерверовДляДиалога();

        // Менеджер локализации: переключение языка без перезапуска (ТЗ §8.11,
        // функция 112). Проблемы локализации попадают в журнал ошибок.
        МенеджерЯзыка = new LanguageManager(
            null,
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));

        // Пользовательские отчёты (ТЗ §8.4, функция 56): реестр пакетов
        // «макет + query.xml» в каталоге профиля Templates/Reports/User;
        // исполнение пользовательских запросов — поверх открытого соединения
        // текущей сессии (штатный валидатор и StrictParametric).
        МенеджерПользовательскихОтчётов = new CustomReportManager(_профиль.DataDirectory);
        ПоставщикДанныхОтчётов = new SqliteReportDataProvider(сессия.Connection);

        // Промежуточные данные реестров (ТЗ §8.4, функция 58): опция чекбокса
        // и дамп датасета во временный XML до отправки в спулер.
        ДамперДанныхОтчётов = new ReportDataDumper(Параметры);

        // Входящая корреспонденция (ТЗ §8.2, функция 29): журнал в базе,
        // сканы в каталоге профиля (scans/) — путь не теряется при сбое.
        var репозиторийВходящих = new IncomingMailRepository(сессия);
        СлужбаВходящих = new IncomingMailService(репозиторийВходящих, _профиль.DataDirectory);

        // Движение входящих документов (ТЗ §8.2, функция 30): история —
        // только дополнение (append-only).
        ТрекерВходящих = new IncomingMovementTracker(
            new IncomingMovementRepository(сессия),
            репозиторийВходящих);
    }

    /// <summary>
    /// Счётчик объёма данных для проверки лимитов пробного периода
    /// (ТЗ §8.10, функция 119). По умолчанию считает контрагентов и
    /// созданные документы по базе данных.
    /// </summary>
    public Func<СнимокИспользования>? СчётчикиИспользования { get; set; }

    /// <summary>Путь к файлу базы данных.</summary>
    public string ПутьБазы { get; }

    /// <summary>Сессия доступа к базе данных.</summary>
    public SqliteUnitOfWork Сессия => _сессия;

    /// <summary>Репозиторий контрагентов.</summary>
    public ICounterpartyRepository Контрагенты { get; }

    /// <summary>Репозиторий пользовательских полей контрагентов.</summary>
    public ICounterpartyFieldRepository ПоляКонтрагентов { get; }

    /// <summary>Репозиторий групп (категорий).</summary>
    public IGroupRepository Группы { get; }

    /// <summary>Репозиторий почтовых отправлений.</summary>
    public IMailRepository Письма { get; }

    /// <summary>Репозиторий рассылок.</summary>
    public IDistributionRepository Рассылки { get; }

    /// <summary>Репозиторий параметров.</summary>
    public IParameterRepository Параметры { get; }

    /// <summary>Репозиторий папок писем.</summary>
    public IFolderRepository Папки { get; }

    /// <summary>Репозиторий обслуживания базы (выборочная очистка).</summary>
    public IDatabaseMaintenanceRepository Обслуживание { get; }

    /// <summary>Хранилище настроек отображения.</summary>
    public UiSettingsStorage НастройкиОтображения { get; }

    /// <summary>
    /// Хранилище настроек интерфейса: используется для сохранённых
    /// пользовательских фильтров адресной книги (ТЗ §8.1, функция 9).
    /// </summary>
    public IUiSettingsStore ХранилищеНастроек { get; }

    /// <summary>Хранилище счётчика регистрационных номеров.</summary>
    public IRegistrationNumberStore СчётчикиНумерации { get; }

    /// <summary>Контекст текущего отправителя.</summary>
    public CurrentSenderContext ТекущийОтправитель { get; }

    /// <summary>Служба контрагентов.</summary>
    public CounterpartyService СлужбаКонтрагентов { get; }

    /// <summary>Служба групп.</summary>
    public GroupService СлужбаГрупп { get; }

    /// <summary>Служба почтовых отправлений.</summary>
    public MailService СлужбаПисем { get; }

    /// <summary>Служба рассылок.</summary>
    public DistributionService СлужбаРассылок { get; }

    /// <summary>Служба разбора адресов.</summary>
    public AddressParserService СлужбаАдресов { get; }

    /// <summary>Служба пользовательских полей.</summary>
    public CustomFieldManager СлужбаПолей { get; }

    /// <summary>Служба каскадного обновления адресов писем.</summary>
    public CascadeAddressUpdater СлужбаКаскада { get; }

    /// <summary>Служба пакетного редактирования.</summary>
    public BatchEditService СлужбаПакетногоРедактирования { get; }

    /// <summary>Менеджер записей адресной книги.</summary>
    public CounterpartyManager СлужбаМенеджера { get; }

    /// <summary>Служба дерева категорий.</summary>
    public CategoryTreeService СлужбаКатегорий { get; }

    /// <summary>Служба отправителей.</summary>
    public AgentService СлужбаАгентов { get; }

    /// <summary>Генератор регистрационных номеров.</summary>
    public RegistrationNumberGenerator СлужбаНумерации { get; }

    /// <summary>Исполнитель наполнения демо-базы и очистки базы.</summary>
    public DemoDatabaseSeeder СлужбаСидов { get; }

    /// <summary>Движок склонения ФИО и адресов.</summary>
    public RussianDeclensionEngine Склонение { get; }

    /// <summary>Репозиторий шаблонов конвертов и бланков.</summary>
    public IPrintTemplateRepository ШаблоныПечати { get; }

    /// <summary>Репозиторий полей бланка.</summary>
    public IBlankFieldRepository ПоляШаблонов { get; }

    /// <summary>Неизменяемый реестр сессий печати.</summary>
    public IPrintJobRepository ЖурналПечати { get; }

    /// <summary>Источник данных отправлений для печати.</summary>
    public PrintDataSource ДанныеПечати { get; }

    /// <summary>Параметры печати (опция «Показывать список шаблонов перед печатью»).</summary>
    public PrintOptions ПараметрыПечати { get; }

    /// <summary>Обучающее предупреждение о подаче конвертов узкой стороной (ТЗ §8.5, функция 147).</summary>
    public FeedOrientationHint ПодсказкаПодачи { get; }

    /// <summary>
    /// Режим печати на разлинованные (типографские) конверты: предупреждение о
    /// соответствии ГОСТ Р 51506-99 и флаг «Больше не показывать это сообщение»
    /// (ТЗ §8.6, функция 89).
    /// </summary>
    public PreprintedMode РежимРазлиновки { get; }

    /// <summary>Список принтеров системы.</summary>
    public PrinterManager СлужбаПринтеров { get; }

    /// <summary>Управление печатными сессиями (F5 / F6 / F7).</summary>
    public PrintSessionController СлужбаПечати { get; }

    /// <summary>Библиотека клипарта (S26, функция 158): редактор бланков
    /// выбирает элементы, рендерер получает PNG через резолвер.</summary>
    public ClipartLibrary КлипартБиблиотека { get; }

    /// <summary>
    /// Калибровочные смещения пар «принтер + формат» (ТЗ §8.4, функция 45):
    /// UI-точка входа диалога печати; хранится в printer_profiles.
    /// </summary>
    public PrinterOffsetProfile ПрофильКалибровки { get; }

    /// <summary>Движок предпросмотра печати.</summary>
    public PrintPreviewEngine СлужбаПредпросмотра { get; }

    /// <summary>
    /// Журнал приложения с независимыми каналами (ТЗ §10.3; §8.11, функция 111).
    /// </summary>
    public LogHub Журнал { get; }

    /// <summary>
    /// Менеджер подключения к базе данных (ТЗ §8.8, функция 102).
    /// </summary>
    public ConnectionManager МенеджерПодключения { get; }

    /// <summary>
    /// Создаёт независимый менеджер для диалога подключения в том же профиле,
    /// что и состав приложения. Производственный состав по-прежнему получает
    /// менеджер в <c>%AppData%\Picket302</c>, а изолированный состав не
    /// пересекается с профилем пользователя при открытии «Открыть базу…».
    /// </summary>
    /// <returns>Новый менеджер с путями текущего профиля.</returns>
    public ConnectionManager СоздатьМенеджерПодключенияДляДиалога() => new(
        _профиль.ConnectionSettingsPath,
        _профиль.SecretsDirectory,
        _профиль.DatabasePath);

    /// <summary>
    /// Справочник серверов БД (ТЗ §8.8, функция 101; §8.11, функция 127).
    /// </summary>
    public ServerRegistry РеестрСерверов { get; }

    /// <summary>
    /// Создаёт независимый справочник серверов для диалога подключения в том
    /// же профиле, что и состав приложения.
    /// </summary>
    /// <returns>Новый справочник серверов текущего профиля.</returns>
    public ServerRegistry СоздатьРеестрСерверовДляДиалога() => new(
        _профиль.ServerRegistryPath);

    /// <summary>
    /// Менеджер локализации интерфейса (ТЗ §8.11, функция 112).
    /// </summary>
    public LanguageManager МенеджерЯзыка { get; }

    /// <summary>
    /// Менеджер пользовательских отчётов (ТЗ §8.4, функция 56): регистрация
    /// пакетов «макет .lbk + query.xml», перечень, правка запроса с валидацией.
    /// </summary>
    public CustomReportManager МенеджерПользовательскихОтчётов { get; }

    /// <summary>
    /// Исполнитель запросов слоя данных поверх текущей базы (ТЗ §8.4,
    /// функция 56): встроенные по идентификатору и пользовательские по
    /// определению, значения — только привязанными параметрами.
    /// </summary>
    public SqliteReportDataProvider ПоставщикДанныхОтчётов { get; }

    /// <summary>
    /// Дампер промежуточных данных реестров (ТЗ §8.4, функция 58): опция
    /// «Сохранять промежуточный XML данных» и сброс датасета во временный XML
    /// до спулинга; ошибка записи переводит на генерацию в памяти.
    /// </summary>
    public ReportDataDumper ДамперДанныхОтчётов { get; }

    /// <summary>
    /// Служба входящей корреспонденции (ТЗ §8.2, функция 29): журнал
    /// регистрации, поиск, сканы в каталоге профиля пользователя.
    /// </summary>
    public IncomingMailService СлужбаВходящих { get; }

    /// <summary>
    /// Трекер движения входящих документов (ТЗ §8.2, функция 30): приём →
    /// передача → архив; история только дополнением.
    /// </summary>
    public IncomingMovementTracker ТрекерВходящих { get; }

    /// <summary>Базовый адрес сервиса трекера Почты России по умолчанию.</summary>
    public const string АдресТрекераПочтыПоУмолчанию = "https://tracking.pochta.ru/";

    /// <summary>
    /// Создаёт трекер Почты России (ТЗ §8.8, функция 110): базовый адрес и
    /// учётные данные берутся из таблицы параметров (значения пароля/токена —
    /// в защищённом виде DPAPI; пустые — запрос вернёт ошибку авторизации).
    /// </summary>
    /// <returns>Готовый трекер.</returns>
    public async Task<RussianPostTracker> СоздатьТрекерПочтыAsync(
        CancellationToken cancellationToken = default)
    {
        var адрес = await Параметры
            .GetValueAsync("RussianPost.BaseUrl", cancellationToken)
            .ConfigureAwait(false);

        var логин = await Параметры
            .GetValueAsync("RussianPost.Login", cancellationToken)
            .ConfigureAwait(false);

        var пароль = await Параметры
            .GetValueAsync("RussianPost.ProtectedPassword", cancellationToken)
            .ConfigureAwait(false);

        var токен = await Параметры
            .GetValueAsync("RussianPost.ProtectedToken", cancellationToken)
            .ConfigureAwait(false);

        return new RussianPostTracker(
            string.IsNullOrWhiteSpace(адрес) ? АдресТрекераПочтыПоУмолчанию : адрес,
            new RussianPostCredentials(логин ?? string.Empty, пароль, токен),
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));
    }

    /// <summary>
    /// Вычислитель идентификатора оборудования (ТЗ §8.10, функция 120).
    /// </summary>
    public MachineIdentity Машина { get; }

    /// <summary>
    /// Хранилище данных лицензии (ТЗ §8.10, функция 123).
    /// </summary>
    public LicenseStore ХранилищеЛицензии { get; }

    /// <summary>
    /// Автомат состояний лицензии (ТЗ §8.10, функция 119).
    /// </summary>
    public LicenseStateMachine АвтоматЛицензии { get; }

    /// <summary>
    /// Ворота лицензии: единая проверка ограничений пробного периода при
    /// создании записей и печати партий (ТЗ §8.10, функция 119).
    /// </summary>
    public ILicenseGate ВоротаЛицензии { get; }

    /// <summary>
    /// Менеджер лицензии: статус, снятие ключа, несколько управляющих
    /// (ТЗ §8.10, функция 123).
    /// </summary>
    public LicenseManager МенеджерЛицензии { get; }

    /// <summary>
    /// Служба активации лицензии: онлайн и офлайн (ТЗ §8.10, функция 122).
    /// </summary>
    public ActivationService АктивацияЛицензии { get; }

    /// <summary>
    /// Возвращает путь к базе данных по умолчанию —
    /// <c>%AppData%\Picket302\picket302.db</c>.
    /// </summary>
    public static string ПутьБазыПоУмолчанию() => Path.Combine(
        Picket302ProfilePaths.КаталогДанныхПоУмолчанию(),
        ИмяФайлаБазы);

    /// <summary>
    /// Создаёт состав приложения и применяет миграции схемы.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="создатьДемоБазу">
    /// Наполнять ли базу демонстрационными данными, если адресная книга пуста.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовый состав приложения.</returns>
    public static Task<AppServices> СоздатьAsync(
        string путьБазы,
        bool создатьДемоБазу = false,
        CancellationToken cancellationToken = default,
        PrinterManager? службаПринтеров = null,
        bool реестрОтправителяИзолирован = false)
        => СоздатьЯдроAsync(null, путьБазы, создатьДемоБазу, cancellationToken, службаПринтеров, реестрОтправителяИзолирован);

    /// <summary>
    /// Создаёт состав приложения в указанном профиле путей и применяет
    /// миграции схемы: изолированные проверки передают временный профиль.
    /// </summary>
    /// <param name="пути">Пути профиля приложения.</param>
    /// <param name="создатьДемоБазу">
    /// Наполнять ли базу демонстрационными данными, если адресная книга пуста.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовый состав приложения.</returns>
    public static Task<AppServices> СоздатьAsync(
        Picket302ProfilePaths пути,
        bool создатьДемоБазу = false,
        CancellationToken cancellationToken = default,
        PrinterManager? службаПринтеров = null,
        bool реестрОтправителяИзолирован = false)
    {
        ArgumentNullException.ThrowIfNull(пути);

        return СоздатьЯдроAsync(
            пути,
            пути.DatabasePath,
            создатьДемоБазу,
            cancellationToken,
            службаПринтеров,
            реестрОтправителяИзолирован);
    }

    /// <summary>
    /// Общая цепочка создания состава: подготовка каталога, миграции схемы
    /// и сборка служб по путям профиля.
    /// </summary>
    private static async Task<AppServices> СоздатьЯдроAsync(
        Picket302ProfilePaths? пути,
        string путьБазы,
        bool создатьДемоБазу,
        CancellationToken cancellationToken,
        PrinterManager? службаПринтеров = null,
        bool реестрОтправителяИзолирован = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);

        var каталог = Path.GetDirectoryName(Path.GetFullPath(путьБазы));

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var строкаПодключения = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(путьБазы),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Default,
        }.ToString();

        var сессия = new SqliteUnitOfWork(строкаПодключения);

        try
        {
            await new MigrationRunner().ApplyAsync(сессия.Connection, cancellationToken)
                .ConfigureAwait(false);

            var состав = new AppServices(
                сессия,
                Path.GetFullPath(путьБазы),
                пути,
                службаПринтеров,
                реестрОтправителяИзолирован);

            if (создатьДемоБазу)
            {
                await состав.НаполнитьДемоБазуЕслиПустоAsync(cancellationToken).ConfigureAwait(false);
            }

            return состав;
        }
        catch
        {
            await сессия.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Наполняет базу демонстрационными данными, если адресная книга пуста
    /// (ТЗ §8.11, функция 137).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число созданных демонстрационных записей.</returns>
    public async Task<int> НаполнитьДемоБазуЕслиПустоAsync(CancellationToken cancellationToken = default)
    {
        var страница = await Контрагенты
            .GetPagedAsync(
                new PageRequest(PageRequest.DefaultOffset, PageRequest.DefaultLimit),
                null,
                cancellationToken)
            .ConfigureAwait(false);

        if (страница.TotalCount > 0)
        {
            return 0;
        }

        return await СлужбаСидов.SeedDemoDataAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _сессия.DisposeAsync().ConfigureAwait(false);
    }
}
