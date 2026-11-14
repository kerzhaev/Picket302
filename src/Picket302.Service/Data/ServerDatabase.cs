using System.Globalization;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;

namespace Picket302.Service.Data;

/// <summary>
/// Фабрика сессий базы данных серверной службы.
/// </summary>
/// <remarks>
/// <para>
/// Служба открывает файл базы данных только сама. Сессия создаётся
/// <b>на каждый запрос</b> и освобождается сразу после обработки: долгоживущая
/// сессия на все запросы означала бы, что один клиент держит транзакцию и
/// блокирует остальных.
/// </para>
/// <para>
/// Параметры соединения подобраны для пяти одновременных клиентов:
/// <list type="bullet">
/// <item><description>режим WAL — читатели не блокируют писателя; применяется
/// только потому, что файл базы лежит на локальном диске серверного ПК;</description></item>
/// <item><description>тайм-аут ожидания занятой базы — вместо мгновенной ошибки
/// «база занята»;</description></item>
/// <item><description>включённые внешние ключи — целостность общих данных
/// обеспечивается самой схемой.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ServerDatabase
{
    /// <summary>Тайм-аут ожидания занятой базы, миллисекунды.</summary>
    public const int BusyTimeoutMilliseconds = 10000;

    private readonly string _путьБазы;

    /// <summary>
    /// Создаёт фабрику над указанным файлом базы данных.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    public ServerDatabase(string путьБазы)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);

        _путьБазы = Path.GetFullPath(путьБазы);
    }

    /// <summary>Путь к файлу базы данных.</summary>
    public string ПутьБазы => _путьБазы;

    /// <summary>
    /// Создаёт сессию для одного запроса. Сессия обязана быть освобождена
    /// вызывающим кодом.
    /// </summary>
    /// <returns>Готовая сессия базы данных.</returns>
    public SqliteUnitOfWork СоздатьСессию()
    {
        var сессия = new SqliteUnitOfWork(СтрокаПодключения());

        НастроитьСоединение(сессия.Connection);

        return сессия;
    }

    /// <summary>
    /// Создаёт сессию для произвольного файла базы (staging импорта).
    /// </summary>
    /// <param name="путь">Путь к файлу базы.</param>
    /// <returns>Готовая сессия.</returns>
    public SqliteUnitOfWork СоздатьСессиюДляФайла(string путь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путь);

        var сессия = new SqliteUnitOfWork(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(путь),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default,
            }.ToString());

        НастроитьСоединение(сессия.Connection);

        return сессия;
    }

    /// <summary>
    /// Включает для базы режим WAL и применяет миграции схемы.
    /// </summary>
    /// <remarks>
    /// Режим WAL устанавливается один раз при старте службы: это свойство
    /// файла базы, а не соединения. Он допустим только потому, что файл базы
    /// лежит на локальном диске серверного компьютера.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения подготовки.</returns>
    public async Task ПодготовитьAsync(CancellationToken cancellationToken = default)
    {
        var каталог = Path.GetDirectoryName(_путьБазы);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        await using (var сессия = СоздатьСессию())
        {
            await using (var команда = сессия.Connection.CreateCommand())
            {
                команда.CommandText = "PRAGMA journal_mode = WAL;";

                await команда.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await new MigrationRunner()
                .ApplyAsync(сессия.Connection, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Проверяет целостность базы и отсутствие нарушений внешних ключей.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текст отчёта; пусто — нарушений нет.</returns>
    public async Task<string> ПроверитьЦелостностьAsync(CancellationToken cancellationToken = default)
    {
        await using (var сессия = СоздатьСессию())
        {
            await using (var команда = сессия.Connection.CreateCommand())
            {
                команда.CommandText = "PRAGMA integrity_check;";

                var целостность = Convert.ToString(
                    await команда.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) ?? string.Empty;

                if (!string.Equals(целостность, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Проверка целостности: {целостность}";
                }
            }

            await using (var командаКлючей = сессия.Connection.CreateCommand())
            {
                командаКлючей.CommandText = "PRAGMA foreign_key_check;";

                await using var нарушения = await командаКлючей
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

                var строки = new List<string>();

                while (await нарушения.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    строки.Add(string.Join(
                        " | ",
                        нарушения.GetValue(0),
                        нарушения.GetValue(1),
                        нарушения.GetValue(2)));
                }

                return строки.Count == 0 ? string.Empty : string.Join(Environment.NewLine, строки);
            }
        }
    }

    /// <summary>
    /// Формирует строку подключения к файлу базы данных.
    /// </summary>
    /// <returns>Строка подключения.</returns>
    private string СтрокаПодключения()
    {
        var построитель = new SqliteConnectionStringBuilder
        {
            DataSource = _путьБазы,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        };

        return построитель.ToString();
    }

    /// <summary>
    /// Задаёт параметры соединения: тайм-аут ожидания занятой базы и включение
    /// внешних ключей.
    /// </summary>
    /// <param name="соединение">Соединение сессии.</param>
    private static void НастроитьСоединение(SqliteConnection соединение)
    {
        using var команда = соединение.CreateCommand();

        команда.CommandText =
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}; PRAGMA foreign_keys = ON;";

        команда.ExecuteNonQuery();
    }
}
