using Picket302.Application.Services;
using Picket302.Application.Setup;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Persistence;
using Picket302.Infrastructure.Repositories;

namespace Picket302.Composition;

/// <summary>
/// Состав слоя данных над одной сессией базы: репозитории и прикладные
/// службы доменных правил.
/// </summary>
/// <remarks>
/// <para>
/// Состав собирается один раз на одну сессию. В одиночном режиме сессия
/// живёт вместе с приложением; в общем режиме служба создаёт отдельный
/// состав на каждый запрос — сессия открывается, отработает запрос и
/// освобождается.
/// </para>
/// <para>
/// Никакой бизнес-логики в этом классе нет: здесь только сборка уже
/// существующих репозиториев и прикладных служб. Правила предметной области
/// остаются в <c>Picket302.Application</c> и выполняются одинаково в обоих
/// режимах.
/// </para>
/// </remarks>
public sealed class DataServices
{
    /// <summary>
    /// Создаёт состав над указанной сессией базы.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    /// <param name="ворота">
    /// Ворота лицензии: ограничивают создание записей и печать партий в
    /// режиме пробного периода. Пусто — ограничения не применяются (служба
    /// общего режима: лицензия принадлежит рабочему месту, а не службе).
    /// </param>
    public DataServices(ISqliteSession сессия, ILicenseGate? ворота = null)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        Сессия = сессия;
        ВоротаЛицензии = ворота;

        Контрагенты = new CounterpartyRepository(сессия);
        ПоляКонтрагентов = new CounterpartyFieldRepository(сессия);
        Группы = new GroupRepository(сессия);
        Письма = new MailRepository(сессия);
        Рассылки = new DistributionRepository(сессия);
        Параметры = new ParameterRepository(сессия);
        Папки = new FolderRepository(сессия);
        Обслуживание = new DatabaseMaintenanceRepository(сессия);
        СчётчикиНумерации = new ParameterRegistrationNumberStore(сессия);

        ШаблоныПечати = new PrintTemplateRepository(сессия);
        ПоляШаблонов = new BlankFieldRepository(сессия);
        ЖурналПечати = new PrintJobRepository(сессия);
        ИсторииПечати = new PrintHistoryRepository(сессия);
        ПрофилиПринтеров = new PrinterProfileRepository(сессия);
        КэшКодов = new QrCacheRepository(сессия);

        Входящие = new IncomingMailRepository(сессия);
        ДвиженияВходящих = new IncomingMovementRepository(сессия);
        ПочтовыеРеестры = new PostRegisterRepository(сессия);
        ДокументыПисем = new MailDocumentRepository(сессия);
        ДвиженияДокументов = new DocumentMovementRepository(сессия);
        Описи = new InventoryRepository(сессия);
        ПозицииОписей = new InventoryItemRepository(сессия);

        СлужбаКонтрагентов = new CounterpartyService(Контрагенты, сессия, ВоротаЛицензии);
        СлужбаГрупп = new GroupService(Группы, сессия);
        СлужбаПисем = new MailService(
            Письма,
            Контрагенты,
            ИсторииПечати,
            сессия,
            ВоротаЛицензии);
        СлужбаРассылок = new DistributionService(
            Рассылки,
            Письма,
            Контрагенты,
            сессия,
            ВоротаЛицензии);
        СлужбаПолей = new CustomFieldManager(ПоляКонтрагентов, сессия);
        СлужбаКаскада = new CascadeAddressUpdater(Письма);
        СлужбаПакетногоРедактирования = new BatchEditService(Контрагенты, сессия, Параметры);
        СлужбаМенеджера = new CounterpartyManager(Контрагенты, сессия);
        СлужбаКатегорий = new CategoryTreeService(Группы, Контрагенты, сессия);
        СлужбаАгентов = new AgentService(Контрагенты, сессия);
        СлужбаНумерации = new RegistrationNumberGenerator(СчётчикиНумерации);
        СлужбаСидов = new DemoDatabaseSeeder(
            Группы,
            Контрагенты,
            ПоляКонтрагентов,
            Обслуживание,
            сессия);
        СлужбаПапок = new FolderService(Папки, сессия);
        СлужбаМенеджераГрупп = new GroupManager(Группы, Контрагенты, сессия);
        ДанныеПечати = new PrintDataSource(Письма, Контрагенты);
    }

    /// <summary>Сессия базы данных.</summary>
    public ISqliteSession Сессия { get; }

    /// <summary>Ворота лицензии; пусто — ограничения не применяются.</summary>
    public ILicenseGate? ВоротаЛицензии { get; }

    /// <summary>Репозиторий контрагентов.</summary>
    public ICounterpartyRepository Контрагенты { get; }

    /// <summary>Репозиторий пользовательских полей контрагентов.</summary>
    public ICounterpartyFieldRepository ПоляКонтрагентов { get; }

    /// <summary>Репозиторий групп (категорий).</summary>
    public IGroupRepository Группы { get; }

    /// <summary>Репозиторий отправлений.</summary>
    public IMailRepository Письма { get; }

    /// <summary>Репозиторий рассылок.</summary>
    public IDistributionRepository Рассылки { get; }

    /// <summary>Репозиторий параметров.</summary>
    public IParameterRepository Параметры { get; }

    /// <summary>Репозиторий папок.</summary>
    public IFolderRepository Папки { get; }

    /// <summary>Репозиторий обслуживания базы.</summary>
    public IDatabaseMaintenanceRepository Обслуживание { get; }

    /// <summary>Хранилище счётчика регистрационных номеров.</summary>
    public IRegistrationNumberStore СчётчикиНумерации { get; }

    /// <summary>Репозиторий шаблонов печати.</summary>
    public IPrintTemplateRepository ШаблоныПечати { get; }

    /// <summary>Репозиторий полей бланка.</summary>
    public IBlankFieldRepository ПоляШаблонов { get; }

    /// <summary>Репозиторий реестра печатных сессий.</summary>
    public IPrintJobRepository ЖурналПечати { get; }

    /// <summary>Репозиторий журнала печати.</summary>
    public IPrintHistoryRepository ИсторииПечати { get; }

    /// <summary>Репозиторий принтерных профилей.</summary>
    public IPrinterProfileRepository ПрофилиПринтеров { get; }

    /// <summary>Репозиторий кэша QR-кодов.</summary>
    public IQrCacheRepository КэшКодов { get; }

    /// <summary>Репозиторий входящей корреспонденции.</summary>
    public IIncomingMailRepository Входящие { get; }

    /// <summary>Репозиторий движения входящих документов.</summary>
    public IIncomingMovementRepository ДвиженияВходящих { get; }

    /// <summary>Репозиторий почтовых реестров.</summary>
    public IPostRegisterRepository ПочтовыеРеестры { get; }

    /// <summary>Репозиторий документов письма.</summary>
    public IMailDocumentRepository ДокументыПисем { get; }

    /// <summary>Репозиторий движения документов.</summary>
    public IDocumentMovementRepository ДвиженияДокументов { get; }

    /// <summary>Репозиторий описей.</summary>
    public IInventoryRepository Описи { get; }

    /// <summary>Репозиторий позиций описей.</summary>
    public IInventoryItemRepository ПозицииОписей { get; }

    /// <summary>Служба контрагентов.</summary>
    public CounterpartyService СлужбаКонтрагентов { get; }

    /// <summary>Служба групп.</summary>
    public GroupService СлужбаГрупп { get; }

    /// <summary>Служба отправлений.</summary>
    public MailService СлужбаПисем { get; }

    /// <summary>Служба рассылок.</summary>
    public DistributionService СлужбаРассылок { get; }

    /// <summary>Служба пользовательских полей.</summary>
    public CustomFieldManager СлужбаПолей { get; }

    /// <summary>Служба каскадного обновления адресов.</summary>
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

    /// <summary>Исполнитель демо-данных и очистки базы.</summary>
    public DemoDatabaseSeeder СлужбаСидов { get; }

    /// <summary>Служба папок.</summary>
    public FolderService СлужбаПапок { get; }

    /// <summary>Менеджер групп.</summary>
    public GroupManager СлужбаМенеджераГрупп { get; }

    /// <summary>Источник данных отправлений для печати.</summary>
    public PrintDataSource ДанныеПечати { get; }
}
