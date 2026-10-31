using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Data;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка режима печати <c>print_mode = test</c> (ТЗ §8.1, функция 143)
/// и сохранности данных при пересборке таблицы реестра печати.
/// </summary>
/// <remarks>
/// <para>
/// ТЗ §8.1, функция 143 «Пробная печать шаблона» требует запись журнала
/// <c>print_jobs</c> с <c>print_mode = test</c>; перечисление §3.1.6 этот
/// режим не содержит. Схема приводится в соответствие с §8.1 — расхождение
/// зафиксировано в docs/OPEN_QUESTIONS_LOG.md (допущение S2.20).
/// </para>
/// <para>
/// CHECK-ограничение в SQLite не изменяется, поэтому таблица реестра
/// печати пересобирается миграцией. Проверяется, что пересборка не теряет
/// данные, не разрывает связи и сохраняет неизменяемость журнала.
/// </para>
/// </remarks>
public sealed class PrintModeSchemaTests
{
    private const string ВставкаЗаписиПечати =
        """
        INSERT INTO print_jobs (printer_name, paper_size_name, paper_width_mm, paper_height_mm, copies, print_mode, status)
        VALUES ('HP LaserJet', 'DL', 220, 110, 1, @PrintMode, 'SUCCESS');
        """;

    /// <summary>
    /// Режим пробной печати принимается схемой: функция 143 обязана
    /// фиксировать пробную печать в журнале.
    /// </summary>
    [Fact]
    public async Task Схема_ДопускаетРежимПробнойПечати()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        await база.Connection.ExecuteAsync(ВставкаЗаписиПечати, new { PrintMode = PrintModes.Test });

        var режимы = await база.Connection.QueryAsync<string>("SELECT print_mode FROM print_jobs;");

        Assert.Equal([PrintModes.Test], режимы.ToList());
    }

    /// <summary>
    /// Неизвестный режим печати схемой не принимается: перечисление
    /// ограничено.
    /// </summary>
    [Fact]
    public async Task Схема_ОтклоняетНеизвестныйРежимПечати()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            ВставкаЗаписиПечати,
            new { PrintMode = "неизвестный" }));
    }

    /// <summary>
    /// Доменное перечисление режимов печати содержит пробную печать.
    /// </summary>
    [Fact]
    public void Домен_ПеречислениеРежимовПечатиСодержитПробнуюПечать()
    {
        Assert.Contains(PrintModes.Test, PrintModes.All);
        Assert.Equal(4, PrintModes.All.Count);
    }

    /// <summary>
    /// Миграция, пересобирающая таблицу реестра печати, сохраняет её данные
    /// и связи: записи журнала и снимки истории печати не теряются.
    /// </summary>
    [Fact]
    public async Task Миграция_ПересборкаТаблицыСохраняетДанныеЖурналаПечати()
    {
        await using var база = await SqliteTestDatabase.CreateAsync(сМиграциями: false);

        // Миграция 004 пересобирает таблицу реестра печати: база приводится к
        // состоянию до неё.
        await ПрименитьМиграцииДоAsync(база, "004");

        // До пересборки режим пробной печати схемой не принимается.
        await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            ВставкаЗаписиПечати,
            new { PrintMode = PrintModes.Test }));

        await база.Connection.ExecuteAsync(ВставкаЗаписиПечати, new { PrintMode = PrintModes.Selected });

        var записьПечати = await база.Connection.QuerySingleAsync<long>("SELECT id FROM print_jobs;");

        await база.Connection.ExecuteAsync(
            """
            INSERT INTO print_history (recipient_address_snapshot, sender_address_snapshot, copy_count, template_description, print_job_id)
            VALUES ('Снимок получателя', 'Снимок отправителя', 1, 'Конверт DL', @PrintJobId);
            """,
            new { PrintJobId = записьПечати });

        // Применяется миграция 004 — пересборка таблицы реестра печати.
        var применённые = await new MigrationRunner().ApplyAsync(база.Connection);

        Assert.Contains(применённые, миграция => миграция.Version == "004");

        // Данные на месте.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM print_jobs;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM print_history;"));

        var режим = await база.Connection.QuerySingleAsync<string>("SELECT print_mode FROM print_jobs;");
        Assert.Equal(PrintModes.Selected, режим);

        var снимок = await база.Connection.QuerySingleAsync<string>(
            "SELECT recipient_address_snapshot FROM print_history;");
        Assert.Equal("Снимок получателя", снимок);

        // Связь журнала печати и истории печати цела.
        var нарушений = (await база.Connection.QueryAsync("PRAGMA foreign_key_check;")).Count();
        Assert.Equal(0, нарушений);

        // После пересборки режим пробной печати принимается.
        await база.Connection.ExecuteAsync(ВставкаЗаписиПечати, new { PrintMode = PrintModes.Test });

        Assert.Equal(2, await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM print_jobs;"));
    }

    /// <summary>
    /// Неизменяемость реестра печати (ТЗ §3.1.6) сохраняется после
    /// пересборки таблицы: записи журнала нельзя изменить или удалить.
    /// </summary>
    [Fact]
    public async Task Миграция_ПересборкаТаблицыСохраняетНеизменяемостьЖурнала()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        await база.Connection.ExecuteAsync(ВставкаЗаписиПечати, new { PrintMode = PrintModes.Test });

        var ошибкаИзменения = await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "UPDATE print_jobs SET copies = 2;"));

        Assert.Contains("неизменяема", ошибкаИзменения.Message, StringComparison.OrdinalIgnoreCase);

        var ошибкаУдаления = await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "DELETE FROM print_jobs;"));

        Assert.Contains("неизменяема", ошибкаУдаления.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM print_jobs;"));
    }

    /// <summary>
    /// Проверка внешних ключей после миграции возвращается в исходное
    /// состояние: пересборка таблицы не оставляет базу без контроля ссылочной
    /// целостности.
    /// </summary>
    [Fact]
    public async Task Миграция_ВозвращаетПроверкуВнешнихКлючей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync(сМиграциями: false);

        // Миграция 004 требует отключения внешних ключей: проверяется, что они
        // возвращаются в исходное состояние.
        await ПрименитьМиграцииДоAsync(база, "004");

        await new MigrationRunner().ApplyAsync(база.Connection);

        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("PRAGMA foreign_keys;"));

        // Ссылочная целостность действительно контролируется.
        await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "INSERT INTO print_history (recipient_address_snapshot, sender_address_snapshot, copy_count, template_description, print_job_id) "
            + "VALUES ('Снимок', 'Отправитель', 1, 'Конверт', 999999);"));
    }

    /// <summary>
    /// Применяет миграции схемы с номерами меньше указанного: база приводится
    /// к состоянию предыдущей версии схемы.
    /// </summary>
    /// <param name="база">Тестовая база данных.</param>
    /// <param name="доВерсии">
    /// Номер версии, до которой применяются миграции (не включая её саму).
    /// </param>
    /// <returns>Задача завершения.</returns>
    private static async Task ПрименитьМиграцииДоAsync(SqliteTestDatabase база, string доВерсии)
    {
        var миграции = MigrationCatalog.Load(typeof(MigrationRunner).Assembly).ToList();

        Assert.True(миграции.Count >= 2, "Ожидается не менее двух миграций схемы.");

        foreach (var миграция in миграции.Where(
            м => string.CompareOrdinal(м.Version, доВерсии) < 0))
        {
            await база.Connection.ExecuteAsync(миграция.Sql);

            await база.Connection.ExecuteAsync(
                "INSERT INTO schema_version (version, description, applied_at) VALUES (@Version, @Description, @AppliedAt);",
                new
                {
                    миграция.Version,
                    миграция.Description,
                    AppliedAt = DateTime.UtcNow,
                });
        }
    }
}
