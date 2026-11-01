using System.Globalization;

using Picket302.Domain.Contracts;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Импорт базы из исходной Access-схемы предшественника — 14 таблиц
/// (ТЗ §8.9, функция 118: «Импорт базы из исходной Access-схемы (14 таблиц)»).
/// </summary>
/// <remarks>
/// <para>
/// Перенос выполняется в три шага: чтение файла .mdb через ODBC-провайдер
/// Microsoft Access Database Engine, сопоставление с целевой моделью и запись в
/// целевую базу через репозитории слоя инфраструктуры.
/// </para>
/// <para>
/// Существующие записи сливаются по ключу функции 74 (ТЗ §8.5): ИНН, а при его
/// отсутствии — нормализованное наименование и почтовый индекс. Политика слияния
/// задаётся при создании импортера; по умолчанию действует «дополнить пустые
/// поля».
/// </para>
/// <para>
/// Отсутствующая таблица пропускается с предупреждением в отчёте; файл, занятый
/// другим процессом, приводит к сообщению с указанием закрыть приложение-владелец.
/// </para>
/// </remarks>
public sealed class LegacyAccessImporter
{
    private readonly LegacyTargetWriter _писатель;
    private readonly LegacyDataMapper _картограф = new();

    /// <summary>
    /// Создаёт импортер исходной Access-схемы.
    /// </summary>
    /// <param name="session">Сессия целевой базы данных.</param>
    /// <param name="policy">
    /// Политика слияния с существующими записями (ТЗ §8.5, функция 74).
    /// </param>
    public LegacyAccessImporter(ISqliteSession session, MergePolicy policy = MergePolicy.FillEmpty)
    {
        ArgumentNullException.ThrowIfNull(session);

        _писатель = new LegacyTargetWriter(session, policy);
    }

    /// <summary>
    /// Импортирует базу из файла .mdb исходной Access-схемы.
    /// </summary>
    /// <param name="mdbPath">Путь к файлу .mdb.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <exception cref="LegacyMigrationException">
    /// Файл занят другим процессом, не найден либо не установлен ODBC-драйвер
    /// Microsoft Access Database Engine.
    /// </exception>
    public MigrationReport ImportFromMdb(string mdbPath)
        => ImportFromMdbAsync(mdbPath).GetAwaiter().GetResult();

    /// <summary>
    /// Импортирует базу из файла .mdb исходной Access-схемы.
    /// </summary>
    /// <param name="mdbPath">Путь к файлу .mdb.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <exception cref="LegacyMigrationException">
    /// Файл занят другим процессом, не найден либо не установлен ODBC-драйвер
    /// Microsoft Access Database Engine.
    /// </exception>
    public async Task<MigrationReport> ImportFromMdbAsync(
        string mdbPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mdbPath);

        // Занятость файла проверяется до подключения: ODBC-драйвер не различает
        // занятость файла и отсутствие драйвера, а пользователю нужны разные
        // подсказки (ТЗ §8.9, функция 118).
        AccessOdbcTableSource.ПроверитьДоступностьФайла(mdbPath);

        using var источник = new AccessOdbcTableSource(mdbPath);

        var открыт = await источник.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (!открыт)
        {
            throw new LegacyMigrationException(AccessOdbcTableSource.СообщениеНетДрайвера);
        }

        return await ImportAsync(источник, ОписаниеИсточника(mdbPath), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Импортирует данные исходной Access-схемы из указанного источника.
    /// </summary>
    /// <param name="source">Источник данных исходной схемы.</param>
    /// <param name="sourceDescription">
    /// Описание источника для колонки <c>counterparties.source</c>.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт миграции: соответствие полей, счётчики и сводка.</returns>
    /// <remarks>
    /// Перегрузка позволяет проверить перенос без файла .mdb и установленного
    /// ODBC-драйвера: источником может быть набор данных, прочитанный в память.
    /// </remarks>
    public async Task<MigrationReport> ImportAsync(
        ILegacyTableSource source,
        string sourceDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var данные = await LegacyDataSet.ЗагрузитьAsync(source, cancellationToken).ConfigureAwait(false);
        var план = _картограф.MapAccess(данные);
        var отчёт = план.Report ?? new MigrationReportBuilder(LegacyDataMapper.ИсточникAccess);

        await _писатель
            .WriteAsync(план, отчёт, sourceDescription, cancellationToken)
            .ConfigureAwait(false);

        return отчёт.Build();
    }

    /// <summary>
    /// Формирует описание источника для колонки <c>counterparties.source</c>
    /// (ТЗ §8.12, функция 75): «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;».
    /// </summary>
    /// <param name="путь">Путь к файлу .mdb.</param>
    /// <returns>Текст описания источника.</returns>
    private static string ОписаниеИсточника(string путь)
        => FormattableString.Invariant(
            $"Импорт {LegacyDataMapper.ИсточникAccess} от {DateTime.Now:yyyy-MM-dd} из {Path.GetFileName(путь)}");
}
