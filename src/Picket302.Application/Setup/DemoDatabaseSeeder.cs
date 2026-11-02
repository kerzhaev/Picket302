using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Setup;

/// <summary>
/// Демо-база при первом запуске и очистка базы
/// (ТЗ §8.11, функция 137; §8.12, функция 153: «Демо-база и очистка базы»).
/// </summary>
/// <remarks>
/// Демо-данные — отдельная копия данных: записи не помечаются как
/// демонстрационные, а удаляются обычными средствами очистки.
/// </remarks>
public sealed class DemoDatabaseSeeder
{
    private readonly IGroupRepository _группы;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly ICounterpartyFieldRepository _поля;
    private readonly IDatabaseMaintenanceRepository _обслуживание;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт исполнитель наполнения демо-базы.</summary>
    /// <param name="группы">Репозиторий групп.</param>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="поля">Репозиторий пользовательских полей.</param>
    /// <param name="обслуживание">Репозиторий обслуживания базы.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public DemoDatabaseSeeder(
        IGroupRepository группы,
        ICounterpartyRepository контрагенты,
        ICounterpartyFieldRepository поля,
        IDatabaseMaintenanceRepository обслуживание,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(группы);
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(поля);
        ArgumentNullException.ThrowIfNull(обслуживание);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _группы = группы;
        _контрагенты = контрагенты;
        _поля = поля;
        _обслуживание = обслуживание;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Наполняет базу демонстрационным каталогом контрагентов и категорий
    /// (ТЗ §8.11, функция 137).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число созданных контрагентов.</returns>
    public async Task<int> SeedDemoDataAsync(CancellationToken cancellationToken = default)
    {
        var группы = await СоздатьКатегорииAsync(cancellationToken).ConfigureAwait(false);

        var созданных = 0;

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var (контрагент, категория, поле, значение) in ДемоЗаписи())
            {
                контрагент.GroupId = группы[категория].Id;
                контрагент.CreatedAt = DateTime.UtcNow;
                контрагент.UpdatedAt = DateTime.UtcNow;

                await _контрагенты.AddAsync(контрагент, cancellationToken).ConfigureAwait(false);

                if (поле is not null && значение is not null)
                {
                    await _поля.AddAsync(
                        new CounterpartyField
                        {
                            CounterpartyId = контрагент.Id,
                            FieldName = поле,
                            FieldValue = значение,
                            FieldType = CounterpartyFieldType.Text,
                        },
                        cancellationToken).ConfigureAwait(false);
                }

                созданных++;
            }

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return созданных;
    }

    /// <summary>
    /// Очищает базу по выбранной области
    /// (ТЗ §8.11, функция 137; §8.12, функция 153).
    /// </summary>
    /// <param name="scope">Область очистки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число затронутых записей либо текст нарушения.</returns>
    public async Task<Result<int>> ClearDatabaseAsync(
        ClearScope scope,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(scope))
        {
            return Result<int>.Failure("Неизвестная область очистки базы.");
        }

        var число = await _обслуживание.CountScopeAsync(scope, cancellationToken).ConfigureAwait(false);

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var затронуто = scope switch
            {
                ClearScope.CounterpartyCategories =>
                    await _обслуживание.ClearCounterpartyCategoriesAsync(cancellationToken).ConfigureAwait(false),
                ClearScope.Distributions =>
                    await _обслуживание.ClearDistributionsAsync(cancellationToken).ConfigureAwait(false),
                ClearScope.Counterparties =>
                    await _обслуживание.ClearCounterpartiesAsync(cancellationToken).ConfigureAwait(false),
                ClearScope.SentMails =>
                    await _обслуживание.ClearSentMailsAsync(cancellationToken).ConfigureAwait(false),
                _ => 0,
            };

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<int>.Success(затронуто);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Возвращает число записей в каждой области очистки — значения показываются
    /// оператору до подтверждения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей по областям.</returns>
    public async Task<IReadOnlyDictionary<ClearScope, int>> GetScopeCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var значения = new Dictionary<ClearScope, int>();

        foreach (var область in Enum.GetValues<ClearScope>())
        {
            значения[область] = await _обслуживание
                .CountScopeAsync(область, cancellationToken)
                .ConfigureAwait(false);
        }

        return значения;
    }

    /// <summary>
    /// Создаёт категории демо-базы, включая группу по умолчанию «Неразобранные».
    /// </summary>
    private async Task<Dictionary<string, Group>> СоздатьКатегорииAsync(CancellationToken cancellationToken)
    {
        var существующие = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var группы = существующие.ToDictionary(
            группа => группа.Name,
            группа => группа,
            StringComparer.OrdinalIgnoreCase);

        if (!группы.ContainsKey(Group.DefaultGroupName))
        {
            var группаПоУмолчанию = Group.CreateDefault();
            await _группы.AddAsync(группаПоУмолчанию, cancellationToken).ConfigureAwait(false);
            группы[Group.DefaultGroupName] = группаПоУмолчанию;
        }

        foreach (var имя in new[] { "Поставщики", "Получатели", "Архив" })
        {
            if (группы.ContainsKey(имя))
            {
                continue;
            }

            var группа = new Group { Name = имя, SortOrder = группы.Count };
            await _группы.AddAsync(группа, cancellationToken).ConfigureAwait(false);
            группы[имя] = группа;
        }

        return группы;
    }

    /// <summary>
    /// Демонстрационные записи адресной книги.
    /// </summary>
    private static IEnumerable<(Counterparty Запись, string Категория, string? Поле, string? Значение)>
        ДемоЗаписи()
    {
        yield return (СоздатьЮрЛицо(
            "ООО «Ромашка»",
            "7707083893",
            "774001001",
            "101000",
            "ООО «Ромашка»",
            "г. Москва",
            "ул. Тверская, д. 15",
            "125009",
            "+7 (495) 123-45-67",
            "info@romashka.example"),
            Group.DefaultGroupName, "КодКлиента", "К-104");

        yield return (СоздатьЮрЛицо(
            "АО «Северный Ветер»",
            "7740000005",
            "780001002",
            "191000",
            "АО «Северный Ветер»",
            "г. Санкт-Петербург",
            "Невский проспект, д. 28",
            "191186",
            "+7 (812) 555-11-22",
            "office@sever.nv.example"),
            "Поставщики", null, null);

        yield return (СоздатьЮрЛицо(
            "ООО «Вектор Трейд»",
            "7707000008",
            "773001003",
            "125167",
            "ООО «Вектор Трейд»",
            "г. Москва",
            "Ленинградский проспект, д. 36",
            "125167",
            "+7 (495) 987-65-43",
            "sales@vector.example"),
            "Поставщики", "Договор", "Д-2024-017");

        yield return (СоздатьЮрЛицо(
            "ИП Смирнов А. П.",
            "500100732259",
            null,
            "630005",
            "ИП Смирнов А. П.",
            "г. Новосибирск",
            "ул. Ленина, д. 3, кв. 12",
            "630005",
            "+7 (383) 300-12-45",
            "smirnov@example.com",
            физлицо: true),
            "Получатели", null, null);

        yield return (СоздатьЮрЛицо(
            "ООО «Балтика-Логистик»",
            "7707000015",
            "774001004",
            "199106",
            "ООО «Балтика-Логистик»",
            "г. Санкт-Петербург",
            "ул. Кораблестроителей, д. 20",
            "199106",
            "+7 (812) 444-33-22",
            "log@baltika.example"),
            "Получатели", null, null);

        yield return (СоздатьЮрЛицо(
            "ФГУП «Почта Тест»",
            "7707000022",
            "774001005",
            "101000",
            "ФГУП «Почта Тест»",
            "г. Москва",
            "ул. Почтовая, д. 1",
            "101000",
            "+7 (495) 000-00-00",
            "post@pochta-test.example"),
            "Архив", null, null);
    }

    /// <summary>
    /// Создаёт демонстрационную запись контрагента с тремя срезами адреса.
    /// </summary>
    private static Counterparty СоздатьЮрЛицо(
        string наименование,
        string инн,
        string? кпп,
        string индекс,
        string строка1,
        string строка2,
        string строка3,
        string почтовыйИндекс,
        string телефон,
        string почта,
        bool физлицо = false)
    {
        var юридический = Address.Create(индекс,
        [
            наименование,
            строка2,
            строка3,
            string.Empty,
            строка1,
            string.Empty,
        ]).Value!;

        var почтовый = Address.Create(почтовыйИндекс,
        [
            наименование,
            строка2,
            строка3,
            string.Empty,
            строка1,
            string.Empty,
        ]).Value!;

        var контрагент = new Counterparty
        {
            // ИП — физическое лицо с 12-значным ИНН (F-D12).
            Type = физлицо ? CounterpartyType.NaturalPerson : CounterpartyType.LegalEntity,
            Name = наименование,
            ShortName = наименование,
            Inn = инн,
            Kpp = кпп,
            Bik = "044525225",
            Rs = "40702810000000000001",
            BankName = "ПАО СБЕРБАНК",
            Phone = телефон,
            Email = почта,
            AddressMainType = AddressMainType.Postal,
        };

        контрагент.SetAddress(AddressMainType.Legal, юридический);
        контрагент.SetAddress(AddressMainType.Postal, почтовый);
        контрагент.SetAddress(AddressMainType.Actual, почтовый);

        return контрагент;
    }
}
