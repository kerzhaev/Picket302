using Picket302.Application.Services;
using Picket302.Application.Setup;
using Picket302.Domain.Common;
using Picket302.Domain.Repositories;
using Picket302.Domain.Services;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Persistence;
using Picket302.Infrastructure.Registry;
using Picket302.Infrastructure.Repositories;
using Picket302.Infrastructure.Storage;
using Picket302.Infrastructure.Text;

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

    private AppServices(SqliteUnitOfWork сессия, string путьБазы)
    {
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

        НастройкиОтображения = new UiSettingsStorage(new JsonUiSettingsStore());
        СчётчикиНумерации = new ParameterRegistrationNumberStore(сессия);

        var реестрОтправителя = new WindowsCurrentSenderRegistryStore();
        var параметрыОтправителя = new ParameterCurrentSenderStore(сессия);
        ТекущийОтправитель = new CurrentSenderContext(
            new CompositeCurrentSenderStore(реестрОтправителя, параметрыОтправителя));

        СлужбаКонтрагентов = new CounterpartyService(Контрагенты, сессия);
        СлужбаГрупп = new GroupService(Группы, сессия);
        СлужбаПисем = new MailService(
            Письма,
            Контрагенты,
            new PrintHistoryRepository(сессия),
            сессия);
        СлужбаРассылок = new DistributionService(Рассылки, Письма, Контрагенты, сессия);
        СлужбаАдресов = new AddressParserService();
        СлужбаПолей = new CustomFieldManager(ПоляКонтрагентов, сессия);
        СлужбаКаскада = new CascadeAddressUpdater(Письма);
        СлужбаПакетногоРедактирования = new BatchEditService(Контрагенты, сессия);
        СлужбаМенеджера = new CounterpartyManager(Контрагенты, сессия);
        СлужбаКатегорий = new CategoryTreeService(Группы, Контрагенты, сессия);
        СлужбаАгентов = new AgentService(Контрагенты, сессия);
        СлужбаНумерации = new RegistrationNumberGenerator(СчётчикиНумерации);
        СлужбаСидов = new DemoDatabaseSeeder(Группы, Контрагенты, ПоляКонтрагентов, Обслуживание, сессия);
        Склонение = new RussianDeclensionEngine();
    }

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

    /// <summary>
    /// Возвращает путь к базе данных по умолчанию —
    /// <c>%AppData%\Picket302\picket302.db</c>.
    /// </summary>
    public static string ПутьБазыПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        КаталогПриложения,
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
    public static async Task<AppServices> СоздатьAsync(
        string путьБазы,
        bool создатьДемоБазу = false,
        CancellationToken cancellationToken = default)
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

            var состав = new AppServices(сессия, Path.GetFullPath(путьБазы));

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
