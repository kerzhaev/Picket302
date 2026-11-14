using Picket302.App.Shared;
using Picket302.Application.Services;
using Picket302.Composition;
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
using Picket302.Infrastructure.Localization;
using Picket302.Infrastructure.Logging;
using Picket302.Infrastructure.Persistence;
using Picket302.Infrastructure.Queries;
using Picket302.Infrastructure.Registry;
using Picket302.Infrastructure.Repositories;
using Picket302.Infrastructure.Storage;
using Picket302.Infrastructure.Text;
using Picket302.Infrastructure.Templates;
using Picket302.Printing;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Preview;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;
using Picket302.Printing.Warnings;
using System.Drawing.Printing;

namespace Picket302.App;

/// <summary>
/// Состав приложения: данные программы и её локальные части.
/// </summary>
/// <remarks>
/// <para>
/// Состав собирается один раз при запуске и передаётся окнам приложения.
/// Путь к базе по умолчанию — профиль пользователя
/// (<c>%AppData%\Picket302\picket302.db</c>).
/// </para>
/// <para>
/// <b>Два режима.</b> В одиночном режиме данные лежат в локальной базе, и
/// программа работает с ними через прикладные службы. В совместном режиме
/// общие данные лежат в базе серверной службы, и программа обращается к ним
/// через <see cref="Данные"/>. Перехода на местную базу при недоступности
/// службы нет: в совместном режиме в составе приложения нет ни одного
/// репозитория, обращающегося к локальному файлу общей базы.
/// </para>
/// <para>
/// Локальные данные (выбранный принтер, калибровка, параметры рабочего
/// места, оформление окна, лицензия, журналы, секреты) остаются на
/// компьютере пользователя в обоих режимах.
/// </para>
/// </remarks>
public sealed class AppServices : IAsyncDisposable
{
    /// <summary>Каталог приложения.</summary>
    public const string КаталогПриложения = "Picket302";

    /// <summary>Имя файла базы данных по умолчанию.</summary>
    public const string ИмяФайлаБазы = "picket302.db";

    private readonly SqliteUnitOfWork? _сессия;
    private readonly SharedTransport? _транспорт;
    private readonly SharedUsageCounter? _счётчикОбъёма;
    private readonly Picket302ProfilePaths _профиль;

    /// <summary>
    /// Спулер печати: экземпляр удерживается для проброса точки
    /// автоматических проверок (PrintToFile приёмочных драйверов) —
    /// в работе хук не задаётся.
    /// </summary>
    private readonly SystemDrawingPrintSpooler СпулерПечати = new();

    /// <summary>
    /// Создаёт состав приложения в одиночном режиме.
    /// </summary>
    private AppServices(
        SqliteUnitOfWork сессия,
        string путьБазы,
        Picket302ProfilePaths профиль,
        PrinterManager? службаПринтеров,
        bool реестрОтправителяИзолирован)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
        _профиль = профиль;

        ПутьБазы = путьБазы;

        ХранилищеНастроек = new JsonUiSettingsStore(профиль.UiSettingsPath);
        НастройкиОтображения = new UiSettingsStorage(ХранилищеНастроек);

        // Журнал приложения с независимыми каналами (ТЗ §10.3; §8.11,
        // функция 111). Каталог журналов — <данные профиля>\logs.
        Журнал = new LogHub(профиль.LogsDirectory);

        // Лицензирование (ТЗ §8.10, функции 119–126): автомат состояний
        // вычисляется при старте, проверка ведётся в одной точке. Ворота
        // создаются до состава слоя данных: прикладные службы ограничивают
        // создание записей лимитами пробного периода.
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

        var состав = new DataServices(сессия, ВоротаЛицензии);
        СоставДанных = состав;
        Данные = new LocalDataGateway(состав);

        Параметры = состав.Параметры;
        ПрофилиПринтеров = состав.ПрофилиПринтеров;

        // Реестровое хранилище текущего отправителя подменяется в изолированном
        // режиме (stage3.1, контейнирование): составное хранилище пишет
        // SenderId и в HKCU\Software\PEPro\CurrentSender — приёмочные прогоны
        // не должны писать в реальную машину за пределами профиля.
        var реестрОтправителя = реестрОтправителяИзолирован
            ? new Picket302.Infrastructure.Registry.NullCurrentSenderStore()
            : (Picket302.Domain.Services.ICurrentSenderStore)new WindowsCurrentSenderRegistryStore();

        var параметрыОтправителя = new LocalCurrentSenderStore(состав.Параметры);

        ТекущийОтправитель = new CurrentSenderContext(
            new CompositeCurrentSenderStore(реестрОтправителя, параметрыОтправителя));

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
        var выводКодов = new QrCodeRenderer(new QrCacheRepository(сессия));

        ШаблоныПечати = состав.ШаблоныПечати;
        ПоляШаблонов = состав.ПоляШаблонов;
        ЖурналПечати = состав.ЖурналПечати;
        ДанныеПечати = состав.ДанныеПечати;

        СлужбаПечати = new PrintSessionController(
            состав.ШаблоныПечати,
            состав.ПоляШаблонов,
            состав.ЖурналПечати,
            состав.ДанныеПечати,
            new PrintAuditWriter(каталогАудита),
            СпулерПечати,
            рендерер,
            new PaperSizeValidator(),
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer(),
            printerProfiles: состав.ПрофилиПринтеров,
            printHistory: new PrintHistoryRecorder(состав.СлужбаПисем),
            licenseGate: ВоротаЛицензии);

        ПрофильКалибровки = new PrinterOffsetProfile(состав.ПрофилиПринтеров);

        СлужбаПредпросмотра = new PrintPreviewEngine(
            рендерер,
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer());

        // Реквизиты подключения к базе и справочник серверов (ТЗ §8.8,
        // функции 101 и 102; §8.11, функция 127).
        МенеджерПодключения = СоздатьМенеджерПодключенияДляДиалога();
        РеестрСерверов = СоздатьРеестрСерверовДляДиалога();

        // Менеджер локализации интерфейса (ТЗ §8.11, функция 112): проблемы
        // локализации попадают в журнал ошибок.
        МенеджерЯзыка = new LanguageManager(
            null,
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));

        // Пользовательские отчёты (ТЗ §8.4, функция 56) исполняются поверх
        // открытого соединения текущей сессии: в совместном режиме такой
        // возможности нет, и модуль отключается.
        МенеджерПользовательскихОтчётов = new CustomReportManager(_профиль.DataDirectory);
        ПоставщикДанныхОтчётов = new SqliteReportDataProvider(сессия.Connection);

        // Дампер промежуточных данных реестров (ТЗ §8.4, функция 58): опция
        // «Сохранять промежуточный XML данных» и сброс датасета во временный
        // XML до спулинга; ошибка записи переводит на генерацию в памяти.
        ДамперДанныхОтчётов = new ReportDataDumper(состав.Параметры);

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
    /// Создаёт состав приложения в совместном режиме: общие данные находятся
    /// в базе серверной службы, локальные остаются на этом компьютере.
    /// </summary>
    private AppServices(
        Picket302ProfilePaths профиль,
        IDataGateway данные,
        SharedTransport транспорт,
        SharedUsageCounter счётчикОбъёма,
        SharedClientProfile профильКлиента,
        PrinterManager? службаПринтеров,
        bool реестрОтправителяИзолирован)
    {
        ArgumentNullException.ThrowIfNull(данные);
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(счётчикОбъёма);
        ArgumentNullException.ThrowIfNull(профильКлиента);

        _профиль = профиль;
        _транспорт = транспорт;
        _счётчикОбъёма = счётчикОбъёма;
        ПрофильКлиента = профильКлиента;

        Данные = данные;

        // Путь к локальной базе в совместном режиме не используется: общей
        // базой владеет служба, а локальных данных программы достаточно.
        ПутьБазы = string.Empty;

        ХранилищеНастроек = new JsonUiSettingsStore(профиль.UiSettingsPath);
        НастройкиОтображения = new UiSettingsStorage(ХранилищеНастроек);

        Журнал = new LogHub(профиль.LogsDirectory);
        var журнал = (string сообщение) => Журнал.LogError(new InvalidOperationException(сообщение));

        Машина = new MachineIdentity(сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));
        ХранилищеЛицензии = new LicenseStore(
            профиль.LicensePath,
            null,
            Машина,
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));

        СчётчикиИспользования = счётчикОбъёма.Получить;

        АвтоматЛицензии = new LicenseStateMachine(
            ХранилищеЛицензии,
            null,
            null,
            () => СчётчикиИспользования?.Invoke() ?? new СнимокИспользования(0, 0));

        ВоротаЛицензии = new LicenseStateMachineGate(АвтоматЛицензии);
        МенеджерЛицензии = new LicenseManager(ХранилищеЛицензии, АвтоматЛицензии);
        АктивацияЛицензии = new ActivationService(ХранилищеЛицензии, АвтоматЛицензии);

        // Параметры рабочего места и калибровка принтеров остаются локальными:
        // они относятся к этому компьютеру, а не к общей базе.
        var локальныеПараметры = new LocalParameterStore(профиль.DataDirectory);
        var локальныеПринтеры = new LocalPrinterProfileStore(профиль.DataDirectory);

        // Первый переход в общий режим: калибровка этого рабочего места
        // переносится из прежней локальной базы в sidecar. Чтение выполняется
        // соединением только для чтения — исходная база не изменяется; уже
        // существующие профили sidecar не дублируются.
        if (File.Exists(профиль.DatabasePath))
        {
            try
            {
                var прежняя = Picket302.Infrastructure.Repositories.ПрофилиПринтеровЧтение
                    .Прочитать(профиль.DatabasePath);

                var перенесено = локальныеПринтеры.ПеренестиИз(прежняя);

                if (перенесено > 0)
                {
                    журнал($"Калибровка принтеров перенесена из локальной базы: профилей {перенесено}.");
                }
            }
            catch (Exception ошибка)
            {
                журнал($"Не удалось перенести калибровку принтеров: {ошибка.Message}");
            }
        }

        Параметры = локальныеПараметры;
        ПрофилиПринтеров = локальныеПринтеры;

        var реестрОтправителя = реестрОтправителяИзолирован
            ? new Picket302.Infrastructure.Registry.NullCurrentSenderStore()
            : (Picket302.Domain.Services.ICurrentSenderStore)new WindowsCurrentSenderRegistryStore();

        ТекущийОтправитель = new CurrentSenderContext(
            new CompositeCurrentSenderStore(
                реестрОтправителя,
                new LocalCurrentSenderStore(локальныеПараметры)));

        СлужбаПринтеров = службаПринтеров ?? new PrinterManager();

        var каталогАудита = Path.GetDirectoryName(профиль.PrintAuditPath)
            ?? AppContext.BaseDirectory;

        var контрольШрифта = new ZipcodeFontGuard();
        var измеритель = new GdiTextMeasurer();
        КлипартБиблиотека = new ClipartLibrary();
        var рендерер = new EnvelopeRenderer(измеритель, id => { КлипартБиблиотека!.TryGet(id, out var png); return png; });

        // Шаблоны, поля бланка, реестр печатных сессий и данные для печати
        // приходят из общей базы; визуализация и спулер остаются на этом
        // компьютере.
        var шаблоны = new RemotePrintTemplateRepository(транспорт);
        var поля = new RemoteBlankFieldRepository(транспорт);
        var журналПечати = new RemotePrintJobRepository(транспорт);
        var данныеПечати = new RemotePrintDataSource(транспорт);

        ШаблоныПечати = шаблоны;
        ПоляШаблонов = поля;
        ЖурналПечати = журналПечати;
        ДанныеПечати = данныеПечати;

        var выводКодов = new QrCodeRenderer(new InMemoryQrCacheRepository());

        СлужбаПечати = new PrintSessionController(
            шаблоны,
            поля,
            журналПечати,
            данныеПечати,
            new PrintAuditWriter(каталогАудита),
            СпулерПечати,
            рендерер,
            new PaperSizeValidator(),
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer(),
            printerProfiles: локальныеПринтеры,
            printHistory: new PrintHistoryRecorder(new RemotePrintHistoryWriter(транспорт)),
            licenseGate: ВоротаЛицензии);

        ПрофильКалибровки = new PrinterOffsetProfile(локальныеПринтеры);

        СлужбаПредпросмотра = new PrintPreviewEngine(
            рендерер,
            new PostIndexRenderer(контрольШрифта),
            выводКодов,
            new LogoRenderer());

        МенеджерПодключения = СоздатьМенеджерПодключенияДляДиалога();
        РеестрСерверов = СоздатьРеестрСерверовДляДиалога();

        МенеджерЯзыка = new LanguageManager(
            null,
            сообщение => Журнал.LogError(new InvalidOperationException(сообщение)));

        // Пользовательские отчёты и входящая корреспонденция в совместном
        // режиме недоступны: они исполняются поверх локального соединения.
        МенеджерПользовательскихОтчётов = null;
        ПоставщикДанныхОтчётов = null;
        ДамперДанныхОтчётов = new ReportDataDumper(локальныеПараметры);

        СлужбаВходящих = null;
        ТрекерВходящих = null;
    }

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

    /// <summary>
    /// Счётчик объёма данных для проверки лимитов пробного периода
    /// (ТЗ §8.10, функция 119). По умолчанию считает контрагентов и
    /// созданные документы по базе данных.
    /// </summary>
    public Func<СнимокИспользования>? СчётчикиИспользования { get; set; }

    /// <summary>Путь к файлу базы данных; пусто в совместном режиме.</summary>
    public string ПутьБазы { get; }

    /// <summary>
    /// Доступ к данным программы: одиночная база или общая база службы.
    /// </summary>
    public IDataGateway Данные { get; }

    /// <summary>
    /// Признак совместного режима: данные хранятся в базе серверной службы.
    /// </summary>
    public bool ОбщийРежим => Данные.ОбщийРежим;

    /// <summary>
    /// Профиль подключения к общей базе (адрес, устройство, отпечаток
    /// сервера); пусто в одиночном режиме.
    /// </summary>
    public SharedClientProfile? ПрофильКлиента { get; }

    /// <summary>
    /// Состав слоя данных локального режима: репозитории и прикладные службы
    /// над локальной сессией; пусто в совместном режиме. Автоматические
    /// проверки и тесты используют состав для прямого обращения к
    /// репозиториям локальной базы; интерфейс программы обращается к данным
    /// только через <see cref="Данные"/>.
    /// </summary>
    public DataServices? СоставДанных { get; }

    /// <summary>
    /// Сессия локальной базы; пусто в совместном режиме. Модули, работающие
    /// только с локальными данными (мастер перехода с предыдущей версии,
    /// пользовательские отчёты), используют её после проверки
    /// <see cref="ОбщийРежим"/: в общем режиме локальной сессии нет, и
    /// обращение к ней невозможно.
    /// </summary>
    public SqliteUnitOfWork? ЛокальнаяСессия => _сессия;

    /// <summary>Хранилище настроек интерфейса.</summary>
    public IUiSettingsStore ХранилищеНастроек { get; }

    /// <summary>Хранилище настроек отображения.</summary>
    public UiSettingsStorage НастройкиОтображения { get; }

    /// <summary>Параметры рабочего места (локальные).</summary>
    public IParameterRepository Параметры { get; }

    /// <summary>Калибровочные профили принтеров (локальные).</summary>
    public IPrinterProfileRepository ПрофилиПринтеров { get; }

    /// <summary>Репозиторий шаблонов печати (общие в совместном режиме).</summary>
    public IPrintTemplateRepository ШаблоныПечати { get; }

    /// <summary>Репозиторий полей бланка (общие в совместном режиме).</summary>
    public IBlankFieldRepository ПоляШаблонов { get; }

    /// <summary>Репозиторий реестра печатных сессий.</summary>
    public IPrintJobRepository ЖурналПечати { get; }

    /// <summary>Источник данных отправлений для печати.</summary>
    public IPrintDataSource ДанныеПечати { get; }

    /// <summary>
    /// Журнал приложения с независимыми каналами (ТЗ §10.3; §8.11,
    /// функция 111).
    /// </summary>
    public LogHub Журнал { get; }

    /// <summary>
    /// Менеджер подключения к базе данных (ТЗ §8.8, функция 102).
    /// </summary>
    public ConnectionManager МенеджерПодключения { get; }

    /// <summary>
    /// Справочник серверов БД (ТЗ §8.8, функция 101; §8.11, функция 127).
    /// </summary>
    public ServerRegistry РеестрСерверов { get; }

    /// <summary>
    /// Менеджер локализации интерфейса (ТЗ §8.11, функция 112).
    /// </summary>
    public LanguageManager МенеджерЯзыка { get; }

    /// <summary>
    /// Контекст текущего отправителя.
    /// </summary>
    public CurrentSenderContext ТекущийОтправитель { get; }

    /// <summary>Список принтеров системы.</summary>
    public PrinterManager СлужбаПринтеров { get; }

    /// <summary>Управление печатными сессиями (F5 / F6 / F7).</summary>
    public PrintSessionController СлужбаПечати { get; }

    /// <summary>Библиотека клипарта (S26, функция 158).</summary>
    public ClipartLibrary КлипартБиблиотека { get; }

    /// <summary>
    /// Калибровочные смещения пар «принтер + формат» (ТЗ §8.4, функция 45):
    /// UI-точка входа диалога печати; в совместном режиме хранится локально.
    /// </summary>
    public PrinterOffsetProfile ПрофильКалибровки { get; }

    /// <summary>Движок предпросмотра печати.</summary>
    public PrintPreviewEngine СлужбаПредпросмотра { get; }

    /// <summary>
    /// Параметры печати (опция «Показывать список шаблонов перед печатью»).
    /// </summary>
    public PrintOptions ПараметрыПечати => new(Параметры);

    /// <summary>
    /// Обучающее предупреждение о подаче конвертов узкой стороной (ТЗ §8.6,
    /// функция 147).
    /// </summary>
    public FeedOrientationHint ПодсказкаПодачи => new(Параметры);

    /// <summary>
    /// Режим печати на разлинованные (типографские) конверты (ТЗ §8.6,
    /// функция 89).
    /// </summary>
    public PreprintedMode РежимРазлиновки => new(Параметры);

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
    /// Менеджер пользовательских отчётов (ТЗ §8.4, функция 56); пусто в
    /// совместном режиме — модуль там недоступен.
    /// </summary>
    public CustomReportManager? МенеджерПользовательскихОтчётов { get; }

    /// <summary>
    /// Исполнитель запросов слоя данных поверх текущей базы (ТЗ §8.4,
    /// функция 56); пусто в совместном режиме.
    /// </summary>
    public SqliteReportDataProvider? ПоставщикДанныхОтчётов { get; }

    /// <summary>
    /// Дампер промежуточных данных реестров (ТЗ §8.4, функция 58).
    /// </summary>
    public ReportDataDumper ДамперДанныхОтчётов { get; }

    /// <summary>
    /// Служба входящей корреспонденции (ТЗ §8.2, функция 29); пусто в
    /// совместном режиме — модуль там недоступен.
    /// </summary>
    public IncomingMailService? СлужбаВходящих { get; }

    /// <summary>
    /// Трекер движения входящих документов (ТЗ §8.2, функция 30); пусто в
    /// совместном режиме.
    /// </summary>
    public IncomingMovementTracker? ТрекерВходящих { get; }

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
    /// Создаёт независимый справочник серверов для диалога подключения в том
    /// же профиле, что и состав приложения.
    /// </summary>
    /// <returns>Новый справочник серверов текущего профиля.</returns>
    public ServerRegistry СоздатьРеестрСерверовДляДиалога() => new(
        _профиль.ServerRegistryPath);

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
    /// <param name="службаПринтеров">Служба принтеров.</param>
    /// <param name="реестрОтправителяИзолирован">Признак изоляции реестра отправителя.</param>
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
    /// <param name="службаПринтеров">Служба принтеров.</param>
    /// <param name="реестрОтправителяИзолирован">Признак изоляции реестра отправителя.</param>
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
    /// Создаёт состав приложения в совместном режиме.
    /// </summary>
    /// <param name="пути">Пути профиля приложения.</param>
    /// <param name="реквизиты">Реквизиты подключения к общей базе.</param>
    /// <param name="секрет">Секрет устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="службаПринтеров">Служба принтеров.</param>
    /// <param name="реестрОтправителяИзолирован">Признак изоляции реестра отправителя.</param>
    /// <returns>Готовый состав приложения.</returns>
    public static async Task<AppServices> СоздатьОбщийAsync(
        Picket302ProfilePaths пути,
        SharedClientSettings реквизиты,
        string секрет,
        CancellationToken cancellationToken = default,
        PrinterManager? службаПринтеров = null,
        bool реестрОтправителяИзолирован = false)
    {
        ArgumentNullException.ThrowIfNull(пути);
        ArgumentNullException.ThrowIfNull(реквизиты);

        var профильКлиента = new SharedClientProfile(пути.DataDirectory);

        var транспорт = new SharedTransport(
            реквизиты,
            секрет,
            сообщение => Console.Error.WriteLine(сообщение));

        var удалённый = new RemoteDataGateway(транспорт);
        var счётчик = new SharedUsageCounter(удалённый);

        await счётчик.ОбновитьAsync(cancellationToken).ConfigureAwait(false);

        return new AppServices(
            пути,
            удалённый,
            транспорт,
            счётчик,
            профильКлиента,
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

            var профиль = пути ?? Picket302ProfilePaths.ПоУмолчанию();

            var состав = new AppServices(
                сессия,
                Path.GetFullPath(путьБазы),
                профиль,
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
        var страница = await Данные
            .ПолучитьКонтрагентовAsync(
                new PageRequest(PageRequest.DefaultOffset, PageRequest.DefaultLimit),
                null,
                cancellationToken)
            .ConfigureAwait(false);

        if (страница.TotalCount > 0)
        {
            return 0;
        }

        return await Данные.НаполнитьДемоДаннымиAsync(cancellationToken).ConfigureAwait(false);
    }

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

    /// <summary>Базовый адрес сервиса трекера Почты России по умолчанию.</summary>
    public const string АдресТрекераПочтыПоУмолчанию = "https://tracking.pochta.ru/";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _счётчикОбъёма?.Dispose();
        _транспорт?.Dispose();

        if (_сессия is not null)
        {
            await _сессия.DisposeAsync().ConfigureAwait(false);
        }
    }
}
