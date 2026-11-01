using System.Globalization;

using Picket302.Domain;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Repositories;

using ПочтовоеОтправление = Picket302.Domain.Entities.Mail;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Загрузка данных из предыдущей (бесплатной) версии Picket302 (ТЗ §8.9,
/// функция 107: «Загрузка данных из предыдущей/бесплатной версии»).
/// </summary>
/// <remarks>
/// <para>
/// Мастер указывает папку базы прежней установки. Источник читает файл базы
/// прежней установки (SQLite) в режиме «только чтение», а данные переносятся в
/// текущую схему через конвейер слияния импорта: запись ищется по ключу слияния
/// (ИНН/УНП, а при его отсутствии — по наименованию и почтовому индексу,
/// ТЗ §8.12, функция 74).
/// </para>
/// <para>
/// <b>Переносятся:</b> адресная книга (контрагенты), группы, пользовательские
/// поля, письма и история печати. История печати переносится как неизменяемые
/// снимки: тексты адресов сохраняются в том виде, в каком были записаны при
/// печати.
/// </para>
/// <para>
/// <b>Не переносятся:</b> настройки подключения прежней системы (реквизиты
/// подключения к серверу, логины и пароли). Такие настройки пропускаются, а их
/// перечень попадает в отчёт переноса.
/// </para>
/// <para>
/// Повторный запуск мастера не создаёт дубликатов: записи, уже перенесённые,
/// находятся по ключу слияния и пропускаются.
/// </para>
/// </remarks>
public sealed class LegacyUpgradeImporter
{
    /// <summary>Наименование источника данных в отчёте переноса.</summary>
    public const string ИмяИсточника = "Предыдущая версия";

    /// <summary>Имя таблицы групп прежней установки.</summary>
    public const string ТаблицаГрупп = "groups";

    /// <summary>Имя таблицы контрагентов прежней установки.</summary>
    public const string ТаблицаКонтрагентов = "counterparties";

    /// <summary>Имя таблицы пользовательских полей прежней установки.</summary>
    public const string ТаблицаПолей = "counterparty_fields";

    /// <summary>Имя таблицы писем прежней установки.</summary>
    public const string ТаблицаПисем = "mails";

    /// <summary>Имя таблицы истории печати прежней установки.</summary>
    public const string ТаблицаИсторииПечати = "print_history";

    /// <summary>Имя таблицы настроек прежней установки.</summary>
    public const string ТаблицаПараметров = "parameters";

    /// <summary>
    /// Имена файлов базы прежней установки, которые мастер ищет в указанной папке.
    /// </summary>
    public static IReadOnlyList<string> ОжидаемыеФайлыБазы { get; } =
    [
        "picket302.db",
        "picket302.sqlite",
        "picket302.sqlite3",
    ];

    /// <summary>
    /// Настройки подключения прежней системы: перенос запрещён (ТЗ §8.9,
    /// функция 107: «реквизиты подключения к серверу прежней системы
    /// не переносятся»).
    /// </summary>
    public static IReadOnlyList<string> ЗапрещённыеПараметрыПодключения { get; } =
    [
        "connectionstring",
        "datasource",
        "databasepath",
        "databasefile",
        "databasename",
        "dbpath",
        "dbhost",
        "dbport",
        "dbname",
        "dbuser",
        "dbpassword",
        "server",
        "serverhost",
        "serverport",
        "servername",
        "host",
        "port",
        "username",
        "user",
        "password",
        "login",
        "mailserver",
        "mailhost",
        "mailport",
        "mailuser",
        "mailpassword",
        "smtphost",
        "smtpport",
        "smtpuser",
        "smtppassword",
        "apiurl",
        "apikey",
        "token",
        "secret",
        "licensekey",
    ];

    /// <summary>
    /// Признаки наименования настройки, по которым настройка считается
    /// настройкой подключения.
    /// </summary>
    private static readonly string[] ПризнакиНастройкиПодключения =
    [
        "connection",
        "подключ",
        "server",
        "сервер",
        "password",
        "пароль",
        "login",
        "host",
        "хост",
        "port",
        "порт",
        "datasource",
        "источникданных",
    ];

    private readonly ISqliteSession _сессия;
    private readonly MergePolicy _политика;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IGroupRepository _группы;
    private readonly ICounterpartyFieldRepository _поля;
    private readonly IMailRepository _письма;
    private readonly IPrintHistoryRepository _историяПечати;
    private readonly IParameterRepository _параметры;

    /// <summary>
    /// Создаёт загрузчик данных прежней версии.
    /// </summary>
    /// <param name="сессия">Сессия базы данных текущей установки.</param>
    /// <param name="политика">
    /// Политика слияния с существующими записями (ТЗ §8.12, функция 74).
    /// </param>
    public LegacyUpgradeImporter(ISqliteSession сессия, MergePolicy политика = MergePolicy.FillEmpty)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
        _политика = политика;
        _контрагенты = new CounterpartyRepository(сессия);
        _группы = new GroupRepository(сессия);
        _поля = new CounterpartyFieldRepository(сессия);
        _письма = new MailRepository(сессия);
        _историяПечати = new PrintHistoryRepository(сессия);
        _параметры = new ParameterRepository(сессия);
    }

    /// <summary>
    /// Загружает данные из папки прежней установки.
    /// </summary>
    /// <param name="sourceFolder">Папка базы данных прежней установки.</param>
    /// <returns>Отчёт переноса: перенесённые, пропущенные и ошибочные записи.</returns>
    /// <exception cref="LegacyMigrationException">
    /// База прежней установки не найдена, не открывается либо её версия
    /// несовместима с текущей программой.
    /// </exception>
    public MigrationReport ImportFromLegacy(string sourceFolder)
        => ImportFromLegacyAsync(sourceFolder).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>
    /// Загружает данные из папки прежней установки.
    /// </summary>
    /// <param name="sourceFolder">Папка базы данных прежней установки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт переноса: перенесённые, пропущенные и ошибочные записи.</returns>
    /// <exception cref="LegacyMigrationException">
    /// База прежней установки не найдена, не открывается либо её версия
    /// несовместима с текущей программой.
    /// </exception>
    public async Task<MigrationReport> ImportFromLegacyAsync(
        string sourceFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);

        var папка = Path.GetFullPath(sourceFolder);

        if (!Directory.Exists(папка))
        {
            throw new LegacyMigrationException($"Папка прежней установки не найдена: {папка}.");
        }

        var файлБазы = НайтиФайлБазы(папка);

        using var источник = new LegacyUpgradeSource(файлБазы);

        if (!await источник.TryOpenAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LegacyMigrationException(
                $"Не удалось открыть базу данных прежней установки: {файлБазы}. "
                + "Проверьте, что файл не повреждён и доступен для чтения.");
        }

        var отчёт = new MigrationReportBuilder($"{ИмяИсточника} ({Path.GetFileName(файлБазы)})");

        ПроверитьСовместимость(источник, отчёт);

        var единицаРаботы = _сессия as IUnitOfWork;
        var свояТранзакция = единицаРаботы is not null && !единицаРаботы.InTransaction;

        if (свояТранзакция)
        {
            await единицаРаботы!.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var картаГрупп = await ПеренестиГруппыAsync(источник, отчёт, cancellationToken).ConfigureAwait(false);
            var картаКонтрагентов = await ПеренестиКонтрагентовAsync(источник, отчёт, картаГрупп, cancellationToken).ConfigureAwait(false);

            await ПеренестиПоляAsync(источник, отчёт, картаКонтрагентов, cancellationToken).ConfigureAwait(false);

            var картаПисем = await ПеренестиПисьмаAsync(источник, отчёт, картаКонтрагентов, cancellationToken).ConfigureAwait(false);

            await ПеренестиИсториюПечатиAsync(источник, отчёт, картаПисем, cancellationToken).ConfigureAwait(false);
            await ПеренестиПараметрыAsync(источник, отчёт, cancellationToken).ConfigureAwait(false);

            if (свояТранзакция)
            {
                await единицаРаботы!.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (свояТранзакцииОткрыта(единицаРаботы, свояТранзакция))
            {
                await единицаРаботы!.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        return отчёт.Build();
    }

    /// <summary>
    /// Ищет файл базы прежней установки в указанной папке.
    /// </summary>
    /// <param name="папка">Папка прежней установки.</param>
    /// <returns>Путь к файлу базы.</returns>
    /// <exception cref="LegacyMigrationException">Файл базы не найден.</exception>
    private static string НайтиФайлБазы(string папка)
    {
        foreach (var имя in ОжидаемыеФайлыБазы)
        {
            var кандидат = Path.Combine(папка, имя);

            if (File.Exists(кандидат))
            {
                return кандидат;
            }
        }

        var найденные = Directory
            .EnumerateFiles(папка, "*", SearchOption.TopDirectoryOnly)
            .Where(путь =>
            {
                var расширение = Path.GetExtension(путь);

                return расширение.Equals(".db", StringComparison.OrdinalIgnoreCase)
                    || расширение.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
                    || расширение.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(путь => путь, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (найденные.Length > 0)
        {
            return найденные[0];
        }

        throw new LegacyMigrationException(
            $"В папке прежней установки не найден файл базы данных. Ожидаемые файлы: "
            + $"{string.Join(", ", ОжидаемыеФайлыБазы)}. Укажите папку с базой прежней версии.");
    }

    /// <summary>
    /// Проверяет совместимость версии схемы прежней установки с текущей программой.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <exception cref="LegacyMigrationException">Версия несовместима.</exception>
    private static void ПроверитьСовместимость(LegacyUpgradeSource источник, MigrationReportBuilder отчёт)
    {
        var поддерживаемые = ПоддерживаемыеВерсии();
        var версия = источник.ВерсияСхемы;

        if (string.IsNullOrWhiteSpace(версия))
        {
            отчёт.Предупреждение(
                "В базе прежней версии не зафиксирована версия схемы — перенос выполняется без проверки совместимости.");

            return;
        }

        if (!int.TryParse(версия, NumberStyles.Integer, CultureInfo.InvariantCulture, out var номер))
        {
            throw new LegacyMigrationException(
                $"Не удалось распознать версию схемы прежней установки: «{версия}». "
                + $"Поддерживаются версии: {string.Join(", ", поддерживаемые)}.");
        }

        if (!поддерживаемые.Contains(номер))
        {
            throw new LegacyMigrationException(
                $"База данных прежней версии несовместима: версия схемы {номер:000}. "
                + $"Поддерживаются версии: {string.Join(", ", поддерживаемые.Select(ном => $"{ном:000}"))}.");
        }
    }

    /// <summary>
    /// Возвращает номера версий схемы, поддерживаемые текущей программой.
    /// </summary>
    /// <returns>Отсортированный список номеров версий.</returns>
    private static IReadOnlyList<int> ПоддерживаемыеВерсии()
        => MigrationCatalog
            .Load(typeof(LegacyUpgradeImporter).Assembly)
            .Select(миграция => int.TryParse(миграция.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var номер)
                ? номер
                : 0)
            .Where(номер => номер > 0)
            .Distinct()
            .OrderBy(номер => номер)
            .ToArray();

    /// <summary>
    /// Переносит группы прежней установки.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Соответствие «идентификатор прежней установки — новый идентификатор».</returns>
    private async Task<Dictionary<long, long>> ПеренестиГруппыAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        CancellationToken cancellationToken)
    {
        var карта = new Dictionary<long, long>();
        var строки = await источник.GetRowsAsync(ТаблицаГрупп, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            отчёт.Предупреждение($"В базе прежней установки нет таблицы «{ТаблицаГрупп}» — группы не переносятся.");

            return карта;
        }

        var существующие = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var поИмени = существующие
            .GroupBy(группа => группа.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(группа => группа.Key, группа => группа.First(), StringComparer.OrdinalIgnoreCase);

        var естьГруппаПоУмолчанию = существующие.Any(группа => группа.IsDefault);

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаГрупп, ТаблицаГрупп);

            var имя = строка.GetString("name");

            if (string.IsNullOrWhiteSpace(имя))
            {
                отчёт.Ошибок(ТаблицаГрупп, ТаблицаГрупп);
                continue;
            }

            if (поИмени.TryGetValue(имя.Trim(), out var существующая))
            {
                var изменена = await ПрименитьПолитикуКГруппеAsync(существующая, строка, cancellationToken).ConfigureAwait(false);

                карта[строка.GetInt("id") ?? 0] = существующая.Id;

                if (изменена)
                {
                    отчёт.Перенесено(ТаблицаГрупп, ТаблицаГрупп);
                }
                else
                {
                    отчёт.Пропущено(ТаблицаГрупп, ТаблицаГрупп);
                }

                continue;
            }

            var группа = new Group
            {
                Name = имя.Trim(),
                Note = строка.GetString("note"),
                IsClosed = строка.GetBool("is_closed"),
                SortOrder = строка.GetInt("sort_order") ?? 0,
                IsDefault = !естьГруппаПоУмолчанию && строка.GetBool("is_default"),
            };

            естьГруппаПоУмолчанию |= группа.IsDefault;

            var идентификатор = await _группы.AddAsync(группа, cancellationToken).ConfigureAwait(false);

            карта[строка.GetInt("id") ?? 0] = идентификатор;
            поИмени[группа.Name] = группа;

            отчёт.Перенесено(ТаблицаГрупп, ТаблицаГрупп);
            отчёт.ПеренесеноВЦель(ТаблицаГрупп);
        }

        return карта;
    }

    /// <summary>
    /// Применяет политику слияния к существующей группе.
    /// </summary>
    /// <param name="существующая">Существующая группа.</param>
    /// <param name="строка">Строка прежней установки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если группа изменена.</returns>
    private async Task<bool> ПрименитьПолитикуКГруппеAsync(
        Group существующая,
        LegacyRow строка,
        CancellationToken cancellationToken)
    {
        if (_политика == MergePolicy.SkipExisting)
        {
            return false;
        }

        var примечание = строка.GetString("note");
        var изменено = false;

        if (примечание is not null
            && (_политика == MergePolicy.Overwrite || string.IsNullOrWhiteSpace(существующая.Note)))
        {
            существующая.Note = примечание;
            изменено = true;
        }

        if (_политика == MergePolicy.Overwrite && строка.GetBool("is_closed") && !существующая.IsClosed)
        {
            существующая.IsClosed = true;
            изменено = true;
        }

        if (изменено)
        {
            await _группы.UpdateAsync(существующая, cancellationToken).ConfigureAwait(false);
        }

        return изменено;
    }

    /// <summary>
    /// Переносит контрагентов прежней установки.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="картаГрупп">Соответствие идентификаторов групп.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Соответствие «идентификатор прежней установки — новый идентификатор».</returns>
    private async Task<Dictionary<long, long>> ПеренестиКонтрагентовAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        Dictionary<long, long> картаГрупп,
        CancellationToken cancellationToken)
    {
        var карта = new Dictionary<long, long>();
        var строки = await источник.GetRowsAsync(ТаблицаКонтрагентов, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            отчёт.Предупреждение(
                $"В базе прежней установки нет таблицы «{ТаблицаКонтрагентов}» — адресная книга не переносится.");

            return карта;
        }

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаКонтрагентов, ТаблицаКонтрагентов);

            var имя = строка.GetString("name");

            if (string.IsNullOrWhiteSpace(имя))
            {
                отчёт.Ошибок(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
                continue;
            }

            var инн = строка.GetString("inn");
            var почтовыйИндекс = строка.GetString("post_zip");

            var существующий = await _контрагенты
                .FindByMergeKeyAsync(инн, имя.Trim(), почтовыйИндекс, cancellationToken)
                .ConfigureAwait(false);

            if (существующий is not null)
            {
                var изменён = await ПрименитьПолитикуККонтрагентуAsync(существующий, строка, cancellationToken).ConfigureAwait(false);

                карта[строка.GetInt("id") ?? 0] = существующий.Id;

                if (изменён)
                {
                    отчёт.Перенесено(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
                }
                else
                {
                    отчёт.Пропущено(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
                }

                continue;
            }

            var контрагент = СоздатьКонтрагента(строка, картаГрупп, отчёт);

            if (контрагент is null)
            {
                отчёт.Ошибок(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
                continue;
            }

            var идентификатор = await _контрагенты.AddAsync(контрагент, cancellationToken).ConfigureAwait(false);

            карта[строка.GetInt("id") ?? 0] = идентификатор;
            отчёт.Перенесено(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
            отчёт.ПеренесеноВЦель(ТаблицаКонтрагентов);
        }

        return карта;
    }

    /// <summary>
    /// Создаёт контрагента по строке прежней установки.
    /// </summary>
    /// <param name="строка">Строка прежней установки.</param>
    /// <param name="картаГрупп">Соответствие идентификаторов групп.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <returns>Контрагент либо <see langword="null"/>, если запись не переносится.</returns>
    private static Counterparty? СоздатьКонтрагента(
        LegacyRow строка,
        Dictionary<long, long> картаГрупп,
        MigrationReportBuilder отчёт)
    {
        var имя = строка.GetString("name");

        if (string.IsNullOrWhiteSpace(имя))
        {
            return null;
        }

        var контрагент = new Counterparty
        {
            Type = (CounterpartyType)(строка.GetInt("type") ?? 0),
            Name = имя.Trim(),
            ShortName = строка.GetString("short_name"),
            Inn = строка.GetString("inn"),
            Kpp = строка.GetString("kpp"),
            Ogrn = строка.GetString("ogrn"),
            Okfs = строка.GetString("okfs"),
            Okpo = строка.GetString("okpo"),
            Okved = строка.GetString("okved"),
            Bik = строка.GetString("bik"),
            Ks = строка.GetString("ks"),
            Rs = строка.GetString("rs"),
            Rkc = строка.GetString("rkc"),
            BankName = строка.GetString("bank_name"),
            PassportSeries = строка.GetString("passport_series"),
            PassportNumber = строка.GetString("passport_number"),
            PassportIssuedBy = строка.GetString("passport_issued_by"),
            PassportIssuedDate = строка.GetDateTime("passport_issued_date"),
            BirthDate = строка.GetDateTime("birth_date"),
            BirthPlace = строка.GetString("birth_place"),
            Position = строка.GetString("position"),
            DirectorFio = строка.GetString("director_fio"),
            Email = строка.GetString("email"),
            Phone = строка.GetString("phone"),
            SpecialNotes = строка.GetString("special_notes"),
            IsVip = строка.GetBool("is_vip"),
            IsSender = строка.GetBool("is_sender"),
            IsActive = строка.GetBool("is_active") || !строка.Values.ContainsKey("is_active"),
            Source = СобратьИсточник(строка),
            CreatedAt = строка.GetDateTime("created_at") ?? DateTime.UtcNow,
        };

        контрагент.AddressMainType = (AddressMainType)(строка.GetInt("address_main_type") ?? 1);

        var юридический = СоздатьАдрес(строка, "legal");
        var почтовый = СоздатьАдрес(строка, "post");
        var фактический = СоздатьАдрес(строка, "fact");

        if (юридический is not null)
        {
            контрагент.LegalAddress = юридический;
        }

        if (почтовый is not null)
        {
            контрагент.PostalAddress = почтовый;
        }

        if (фактический is not null)
        {
            контрагент.ActualAddress = фактический;
        }

        var идентификаторГруппы = строка.GetInt("group_id");

        if (идентификаторГруппы is not null && картаГрупп.TryGetValue(идентификаторГруппы.Value, out var новыйИдентификатор))
        {
            контрагент.GroupId = новыйИдентификатор;
        }
        else if (идентификаторГруппы is not null)
        {
            отчёт.ТребуетПроверки(ТаблицаКонтрагентов, ТаблицаКонтрагентов);
        }

        return контрагент;
    }

    /// <summary>
    /// Формирует отметку о происхождении записи.
    /// </summary>
    /// <param name="строка">Строка прежней установки.</param>
    /// <returns>Отметка о происхождении записи.</returns>
    private static string СобратьИсточник(LegacyRow строка)
    {
        var прежний = строка.GetString("source");

        return прежний is null ? ИмяИсточника : $"{ИмяИсточника}: {прежний.Trim()}";
    }

    /// <summary>
    /// Применяет политику слияния к существующему контрагенту.
    /// </summary>
    /// <param name="существующий">Существующий контрагент.</param>
    /// <param name="строка">Строка прежней установки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если контрагент изменён.</returns>
    private async Task<bool> ПрименитьПолитикуККонтрагентуAsync(
        Counterparty существующий,
        LegacyRow строка,
        CancellationToken cancellationToken)
    {
        if (_политика == MergePolicy.SkipExisting)
        {
            return false;
        }

        var изменено = false;

        var поля = new (Func<string?> Получить, Action<string?> Установить, string Столбец)[]
        {
            (() => существующий.ShortName, значение => существующий.ShortName = значение, "short_name"),
            (() => существующий.Kpp, значение => существующий.Kpp = значение, "kpp"),
            (() => существующий.Ogrn, значение => существующий.Ogrn = значение, "ogrn"),
            (() => существующий.Okfs, значение => существующий.Okfs = значение, "okfs"),
            (() => существующий.Okpo, значение => существующий.Okpo = значение, "okpo"),
            (() => существующий.Okved, значение => существующий.Okved = значение, "okved"),
            (() => существующий.Bik, значение => существующий.Bik = значение, "bik"),
            (() => существующий.Ks, значение => существующий.Ks = значение, "ks"),
            (() => существующий.Rs, значение => существующий.Rs = значение, "rs"),
            (() => существующий.Rkc, значение => существующий.Rkc = значение, "rkc"),
            (() => существующий.BankName, значение => существующий.BankName = значение, "bank_name"),
            (() => существующий.Position, значение => существующий.Position = значение, "position"),
            (() => существующий.DirectorFio, значение => существующий.DirectorFio = значение, "director_fio"),
            (() => существующий.Email, значение => существующий.Email = значение, "email"),
            (() => существующий.Phone, значение => существующий.Phone = значение, "phone"),
            (() => существующий.SpecialNotes, значение => существующий.SpecialNotes = значение, "special_notes"),
        };

        foreach (var (получить, установить, столбец) in поля)
        {
            var значение = строка.GetString(столбец);

            if (значение is null)
            {
                continue;
            }

            if (_политика == MergePolicy.Overwrite || string.IsNullOrWhiteSpace(получить()))
            {
                установить(значение);
                изменено = true;
            }
        }

        if (изменено)
        {
            await _контрагенты.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
        }

        return изменено;
    }

    /// <summary>
    /// Создаёт адрес по данным прежней установки.
    /// </summary>
    /// <param name="строка">Строка прежней установки.</param>
    /// <param name="префикс">Префикс столбцов адреса (<c>legal</c>, <c>post</c>, <c>fact</c>).</param>
    /// <returns>Адрес либо <see langword="null"/>, если адрес в прежней версии не задан.</returns>
    private static Address? СоздатьАдрес(LegacyRow строка, string префикс)
    {
        var индекс = строка.GetString($"{префикс}_zip");
        var строки = new string[Address.LineCount];

        for (var номер = 1; номер <= Address.LineCount; номер++)
        {
            строки[номер - 1] = строка.GetString($"{префикс}_str{номер}") ?? string.Empty;
        }

        var исходная = строка.GetString($"{префикс}_raw");

        if (индекс is null)
        {
            if (исходная is null)
            {
                return null;
            }

            var разобранный = Address.Parse(исходная);

            return разобранный.IsSuccess ? разобранный.Value : null;
        }

        var адрес = Address.Create(индекс, строки, исходная);

        return адрес.IsSuccess ? адрес.Value : null;
    }

    /// <summary>
    /// Переносит пользовательские поля контрагентов прежней установки.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="картаКонтрагентов">Соответствие идентификаторов контрагентов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ПеренестиПоляAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        Dictionary<long, long> картаКонтрагентов,
        CancellationToken cancellationToken)
    {
        var строки = await источник.GetRowsAsync(ТаблицаПолей, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            return;
        }

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаПолей, ТаблицаПолей);

            var прежнийИдентификатор = строка.GetInt("counterparty_id");
            var имяПоля = строка.GetString("field_name");

            if (прежнийИдентификатор is null
                || !картаКонтрагентов.TryGetValue(прежнийИдентификатор.Value, out var новыйИдентификатор)
                || string.IsNullOrWhiteSpace(имяПоля))
            {
                отчёт.Пропущено(ТаблицаПолей, ТаблицаПолей);
                continue;
            }

            var существующее = (await _поля
                .GetByCounterpartyAsync(новыйИдентификатор, cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault(поле => string.Equals(поле.FieldName, имяПоля.Trim(), StringComparison.OrdinalIgnoreCase));

            var значение = строка.GetString("field_value");

            if (существующее is not null)
            {
                if (_политика == MergePolicy.SkipExisting
                    || (_политика == MergePolicy.FillEmpty && !string.IsNullOrWhiteSpace(существующее.FieldValue)))
                {
                    отчёт.Пропущено(ТаблицаПолей, ТаблицаПолей);
                    continue;
                }

                существующее.FieldValue = значение;
                существующее.UpdatedAt = DateTime.UtcNow;

                await _поля.UpdateAsync(существующее, cancellationToken).ConfigureAwait(false);
                отчёт.Перенесено(ТаблицаПолей, ТаблицаПолей);

                continue;
            }

            await _поля.AddAsync(
                new CounterpartyField
                {
                    CounterpartyId = новыйИдентификатор,
                    FieldName = имяПоля.Trim(),
                    FieldValue = значение,
                    FieldType = (CounterpartyFieldType)(строка.GetInt("field_type") ?? 0),
                    UpdatedAt = DateTime.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);

            отчёт.Перенесено(ТаблицаПолей, ТаблицаПолей);
            отчёт.ПеренесеноВЦель(ТаблицаПолей);
        }
    }

    /// <summary>
    /// Переносит письма прежней установки.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="картаКонтрагентов">Соответствие идентификаторов контрагентов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Соответствие «идентификатор прежней установки — новый идентификатор».</returns>
    private async Task<Dictionary<long, long>> ПеренестиПисьмаAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        Dictionary<long, long> картаКонтрагентов,
        CancellationToken cancellationToken)
    {
        var карта = new Dictionary<long, long>();
        var строки = await источник.GetRowsAsync(ТаблицаПисем, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            отчёт.Предупреждение($"В базе прежней установки нет таблицы «{ТаблицаПисем}» — письма не переносятся.");

            return карта;
        }

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаПисем, ТаблицаПисем);

            var прежнийПолучатель = строка.GetInt("counterparty_id");
            var прежнийОтправитель = строка.GetInt("sender_id");

            if (прежнийПолучатель is null
                || прежнийОтправитель is null
                || !картаКонтрагентов.TryGetValue(прежнийПолучатель.Value, out var получатель)
                || !картаКонтрагентов.TryGetValue(прежнийОтправитель.Value, out var отправитель))
            {
                отчёт.Пропущено(ТаблицаПисем, ТаблицаПисем);
                continue;
            }

            var адрес = СоздатьАдресПисьма(строка);

            if (адрес is null)
            {
                отчёт.Пропущено(ТаблицаПисем, ТаблицаПисем);
                отчёт.ТребуетПроверки(ТаблицаПисем, ТаблицаПисем);
                continue;
            }

            var письмо = new ПочтовоеОтправление
            {
                CounterpartyId = получатель,
                SenderId = отправитель,
                EnvelopeType = строка.GetString("envelope_type"),
                NoticeType = строка.GetString("notice_type"),
                CopyCount = Math.Max(ПочтовоеОтправление.MinimumCopyCount, строка.GetInt("copy_count") ?? ПочтовоеОтправление.MinimumCopyCount),
                IsVip = строка.GetBool("is_vip"),
                Note = строка.GetString("note"),
                DeclaredValue = строка.GetDecimal("declared_value") ?? 0m,
                Weight = строка.GetDecimal("weight") ?? 0m,
                Fee = строка.GetDecimal("fee") ?? 0m,
                Status = (MailStatus)(строка.GetInt("status") ?? 0),
                NeedNotification = строка.GetBool("need_notification"),
                NeedReturnAddress = строка.GetBool("need_return_address") || !строка.Values.ContainsKey("need_return_address"),
                MailCategory = (MailCategory)(строка.GetInt("mail_category") ?? 0),
                DateCreated = строка.GetDateTime("date_created") ?? DateTime.UtcNow,
                DatePrinted = строка.GetDateTime("date_printed"),
                DateDispatched = строка.GetDateTime("date_dispatched"),
                DateConfirmation = строка.GetDateTime("date_confirmation"),
                DateCanceled = строка.GetDateTime("date_canceled"),
                IncomingNumber = строка.GetString("incoming_number"),
                OutgoingNumber = строка.GetString("outgoing_number"),
                RussianPostId = строка.GetString("russian_post_id"),
                RussianPostBatch = строка.GetString("russian_post_batch"),
                RecipientAddress = адрес,
            };

            var существующие = await _письма
                .GetByCounterpartyAsync(получатель, cancellationToken)
                .ConfigureAwait(false);

            if (существующие.Any(имеющееся => КлючПисьма(имеющееся) == КлючПисьма(письмо)))
            {
                карта[строка.GetInt("id") ?? 0] = существующие
                    .First(имеющееся => КлючПисьма(имеющееся) == КлючПисьма(письмо))
                    .Id;

                отчёт.Пропущено(ТаблицаПисем, ТаблицаПисем);
                continue;
            }

            var идентификатор = await _письма.AddAsync(письмо, cancellationToken).ConfigureAwait(false);

            карта[строка.GetInt("id") ?? 0] = идентификатор;
            отчёт.Перенесено(ТаблицаПисем, ТаблицаПисем);
            отчёт.ПеренесеноВЦель(ТаблицаПисем);
        }

        return карта;
    }

    /// <summary>
    /// Создаёт адрес получателя письма.
    /// </summary>
    /// <param name="строка">Строка прежней установки.</param>
    /// <returns>Адрес либо <see langword="null"/>, если адрес не задан.</returns>
    private static Address? СоздатьАдресПисьма(LegacyRow строка)
    {
        var индекс = строка.GetString("to_zip");

        if (индекс is null)
        {
            return null;
        }

        var строки = new string[Address.LineCount];

        for (var номер = 1; номер <= Address.LineCount; номер++)
        {
            строки[номер - 1] = строка.GetString($"to_str{номер}") ?? string.Empty;
        }

        var адрес = Address.Create(индекс, строки);

        return адрес.IsSuccess ? адрес.Value : null;
    }

    /// <summary>
    /// Формирует естественный ключ письма: получатель, момент создания, число
    /// экземпляров и почтовый индекс. Ключ применяется для пропуска уже
    /// перенесённых писем при повторном запуске мастера.
    /// </summary>
    /// <param name="письмо">Письмо.</param>
    /// <returns>Ключ письма.</returns>
    private static string КлючПисьма(ПочтовоеОтправление письмо)
    {
        var индекс = письмо.RecipientAddress?.Zip ?? string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{письмо.CounterpartyId}|{письмо.DateCreated:yyyy-MM-dd HH:mm:ss}|{письмо.CopyCount}|{индекс}");
    }

    /// <summary>
    /// Переносит историю печати прежней установки как неизменяемые снимки.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="картаПисем">Соответствие идентификаторов писем.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ПеренестиИсториюПечатиAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        Dictionary<long, long> картаПисем,
        CancellationToken cancellationToken)
    {
        var строки = await источник.GetRowsAsync(ТаблицаИсторииПечати, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            return;
        }

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаИсторииПечати, ТаблицаИсторииПечати);

            var прежнееПисьмо = строка.GetInt("mail_id");
            long? новоеПисьмо = null;

            if (прежнееПисьмо is not null)
            {
                картаПисем.TryGetValue(прежнееПисьмо.Value, out var идентификатор);
                новоеПисьмо = идентификатор == 0 ? null : идентификатор;
            }

            var запись = new PrintHistory
            {
                PrintedAt = строка.GetDateTime("printed_at") ?? DateTime.UtcNow,
                MailId = новоеПисьмо,
                RecipientAddressSnapshot = строка.GetString("recipient_address_snapshot") ?? string.Empty,
                SenderAddressSnapshot = строка.GetString("sender_address_snapshot") ?? string.Empty,
                CopyCount = строка.GetInt("copy_count") ?? 0,
                IsVip = строка.GetBool("is_vip"),
                Note = строка.GetString("note"),
                TemplateDescription = строка.GetString("template_description") ?? string.Empty,
            };

            if (новоеПисьмо is not null)
            {
                var существующие = await _историяПечати
                    .GetByMailAsync(новоеПисьмо.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (существующие.Any(имеющаяся =>
                    string.Equals(
                        имеющаяся.RecipientAddressSnapshot,
                        запись.RecipientAddressSnapshot,
                        StringComparison.Ordinal)
                    && string.Equals(
                        имеющаяся.TemplateDescription,
                        запись.TemplateDescription,
                        StringComparison.Ordinal)
                    && имеющаяся.PrintedAt == запись.PrintedAt))
                {
                    отчёт.Пропущено(ТаблицаИсторииПечати, ТаблицаИсторииПечати);
                    continue;
                }
            }

            await _историяПечати.AddAsync(запись, cancellationToken).ConfigureAwait(false);

            отчёт.Перенесено(ТаблицаИсторииПечати, ТаблицаИсторииПечати);
            отчёт.ПеренесеноВЦель(ТаблицаИсторииПечати);
        }
    }

    /// <summary>
    /// Переносит настройки прежней установки. Настройки подключения прежней
    /// системы не переносятся: их перечень попадает в отчёт.
    /// </summary>
    /// <param name="источник">Источник прежней установки.</param>
    /// <param name="отчёт">Отчёт переноса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ПеренестиПараметрыAsync(
        LegacyUpgradeSource источник,
        MigrationReportBuilder отчёт,
        CancellationToken cancellationToken)
    {
        var строки = await источник.GetRowsAsync(ТаблицаПараметров, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            return;
        }

        var неПеренесённые = new List<string>();

        foreach (var строка in строки)
        {
            отчёт.Прочитано(ТаблицаПараметров, ТаблицаПараметров);

            var имя = строка.GetString("name");

            if (string.IsNullOrWhiteSpace(имя))
            {
                отчёт.Ошибок(ТаблицаПараметров, ТаблицаПараметров);
                continue;
            }

            if (ЯвляетсяНастройкойПодключения(имя))
            {
                неПеренесённые.Add(имя.Trim());
                отчёт.Пропущено(ТаблицаПараметров, ТаблицаПараметров);
                отчёт.Соответствие(ТаблицаПараметров, имя.Trim(), ТаблицаПараметров, null, "настройка подключения прежней системы — не переносится");

                continue;
            }

            var значение = строка.GetString("value");
            var существующий = await _параметры.GetByNameAsync(имя.Trim(), cancellationToken).ConfigureAwait(false);

            if (существующий is not null)
            {
                if (_политика == MergePolicy.SkipExisting
                    || (_политика == MergePolicy.FillEmpty && !string.IsNullOrWhiteSpace(существующий.Value)))
                {
                    отчёт.Пропущено(ТаблицаПараметров, ТаблицаПараметров);
                    continue;
                }

                существующий.Value = значение;
                существующий.UpdatedAt = DateTime.UtcNow;

                await _параметры.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
                отчёт.Перенесено(ТаблицаПараметров, ТаблицаПараметров);

                continue;
            }

            await _параметры.AddAsync(
                new Parameter
                {
                    Name = имя.Trim(),
                    Value = значение,
                    UpdatedAt = DateTime.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);

            отчёт.Перенесено(ТаблицаПараметров, ТаблицаПараметров);
            отчёт.ПеренесеноВЦель(ТаблицаПараметров);
        }

        if (неПеренесённые.Count > 0)
        {
            отчёт.Предупреждение(
                "Настройки подключения прежней версии не переносятся: "
                + $"{string.Join(", ", неПеренесённые)}.");
        }
    }

    /// <summary>
    /// Определяет, является ли настройка реквизитами подключения прежней системы.
    /// </summary>
    /// <param name="имя">Имя настройки.</param>
    /// <returns><see langword="true"/>, если настройку переносить запрещено.</returns>
    public static bool ЯвляетсяНастройкойПодключения(string имя)
    {
        if (string.IsNullOrWhiteSpace(имя))
        {
            return false;
        }

        var нормализованное = НормализоватьИмя(имя);

        if (ЗапрещённыеПараметрыПодключения.Contains(нормализованное, StringComparer.Ordinal))
        {
            return true;
        }

        return ПризнакиНастройкиПодключения.Any(признак =>
            нормализованное.Contains(признак, StringComparison.Ordinal));
    }

    /// <summary>
    /// Нормализует имя настройки для сопоставления: нижний регистр без
    /// пробелов, подчёркиваний и дефисов.
    /// </summary>
    /// <param name="имя">Имя настройки.</param>
    /// <returns>Нормализованное имя.</returns>
    private static string НормализоватьИмя(string имя)
        => new string(имя
            .Where(символ => char.IsLetterOrDigit(символ))
            .Select(char.ToLowerInvariant)
            .ToArray());

    /// <summary>
    /// Определяет, открыта ли собственная транзакция переноса.
    /// </summary>
    /// <param name="единицаРаботы">Единица работы; может быть <see langword="null"/>.</param>
    /// <param name="свояТранзакция">Признак того, что транзакция открыта загрузчиком.</param>
    /// <returns><see langword="true"/>, если транзакцию требуется откатить.</returns>
    private static bool свояТранзакцииОткрыта(IUnitOfWork? единицаРаботы, bool свояТранзакция)
        => свояТранзакция && единицаРаботы is not null && единицаРаботы.InTransaction;
}
