using System.Globalization;

using Picket302.Domain.Contracts;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Импорт базы из исходной MySQL-схемы предшественника — 8 таблиц
/// (ТЗ §8.9, функция 117: «Импорт базы из исходной MySQL-схемы (8 таблиц)»).
/// </summary>
/// <remarks>
/// <para>
/// Перенос выполняется в три шага: чтение исходной базы драйвером MySqlConnector
/// (кодировка cp1251), сопоставление с целевой моделью и запись в целевую базу
/// через репозитории слоя инфраструктуры.
/// </para>
/// <para>
/// Существующие записи сливаются по ключу функции 74 (ТЗ §8.5): ИНН, а при его
/// отсутствии — нормализованное наименование и почтовый индекс. Политика слияния
/// задаётся при создании импортера; по умолчанию действует «дополнить пустые
/// поля» — значения, заведённые вручную, не затираются.
/// </para>
/// <para>
/// Пароль подключения не выводится ни в журнал, ни в текст ошибки.
/// </para>
/// </remarks>
public sealed class LegacyMySqlImporter
{
    private readonly LegacyTargetWriter _писатель;
    private readonly LegacyDataMapper _картограф = new();

    /// <summary>
    /// Создаёт импортер исходной MySQL-схемы.
    /// </summary>
    /// <param name="session">Сессия целевой базы данных.</param>
    /// <param name="policy">
    /// Политика слияния с существующими записями (ТЗ §8.5, функция 74).
    /// </param>
    public LegacyMySqlImporter(ISqliteSession session, MergePolicy policy = MergePolicy.FillEmpty)
    {
        ArgumentNullException.ThrowIfNull(session);

        _писатель = new LegacyTargetWriter(session, policy);
    }

    /// <summary>
    /// Импортирует базу из исходной MySQL-схемы.
    /// </summary>
    /// <param name="host">Хост сервера MySQL.</param>
    /// <param name="port">Порт сервера MySQL.</param>
    /// <param name="db">Имя базы данных.</param>
    /// <param name="user">Имя пользователя.</param>
    /// <param name="password">Пароль пользователя; в журнал не выводится.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <exception cref="LegacyMigrationException">
    /// Подключение к исходной базе не удалось.
    /// </exception>
    public MigrationReport ImportFromMySqlDump(
        string host,
        int port,
        string db,
        string user,
        string password)
        => ImportFromMySqlDumpAsync(host, port, db, user, password).GetAwaiter().GetResult();

    /// <summary>
    /// Импортирует базу из исходной MySQL-схемы.
    /// </summary>
    /// <param name="host">Хост сервера MySQL.</param>
    /// <param name="port">Порт сервера MySQL.</param>
    /// <param name="db">Имя базы данных.</param>
    /// <param name="user">Имя пользователя.</param>
    /// <param name="password">Пароль пользователя; в журнал не выводится.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <exception cref="LegacyMigrationException">
    /// Подключение к исходной базе не удалось.
    /// </exception>
    public async Task<MigrationReport> ImportFromMySqlDumpAsync(
        string host,
        int port,
        string db,
        string user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        using var источник = new MySqlTableSource(host, port, db, user, password);

        var открыт = await источник.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (!открыт)
        {
            throw new LegacyMigrationException(
                $"Не удалось подключиться к исходной базе MySQL {host}:{port.ToString(CultureInfo.InvariantCulture)} "
                + $"(база «{db}», пользователь «{user}»).");
        }

        return await ImportAsync(источник, ОписаниеИсточника(db), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Импортирует данные исходной MySQL-схемы из указанного источника.
    /// </summary>
    /// <param name="source">Источник данных исходной схемы.</param>
    /// <param name="sourceDescription">
    /// Описание источника для колонки <c>counterparties.source</c>.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <remarks>
    /// Перегрузка позволяет проверить перенос без сервера MySQL: источником
    /// может быть набор данных, прочитанный в память.
    /// </remarks>
    public async Task<MigrationReport> ImportAsync(
        ILegacyTableSource source,
        string sourceDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var данные = await LegacyDataSet.ЗагрузитьAsync(source, cancellationToken).ConfigureAwait(false);
        var план = _картограф.MapMySql(данные);
        var отчёт = план.Report ?? new MigrationReportBuilder(LegacyDataMapper.ИсточникMySql);

        await _писатель
            .WriteAsync(план, отчёт, sourceDescription, cancellationToken)
            .ConfigureAwait(false);

        return отчёт.Build();
    }

    /// <summary>
    /// Формирует описание источника для колонки <c>counterparties.source</c>
    /// (ТЗ §8.12, функция 75): «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;».
    /// </summary>
    /// <param name="имя">Имя базы данных или файла.</param>
    /// <returns>Текст описания источника.</returns>
    private static string ОписаниеИсточника(string имя)
        => FormattableString.Invariant(
            $"Импорт {LegacyDataMapper.ИсточникMySql} от {DateTime.Now:yyyy-MM-dd} из {имя}");
}
