using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;
using Picket302.Service.Data;
using Picket302.Shared.Commands;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки журнала идемпотентности: повтор завершённой операции, конфликт
/// содержимого и перехват устаревшей попытки.
/// </summary>
public sealed class SharedOperationStoreTests
{
    private static ISqliteSession СоздатьСессию(string путь)
    {
        var сессия = new SqliteUnitOfWork(
            new SqliteConnectionStringBuilder { DataSource = путь }.ToString());

        using (var команда = сессия.Connection.CreateCommand())
        {
            команда.CommandText =
                "CREATE TABLE IF NOT EXISTS shared_operations (" +
                "operation_id TEXT NOT NULL PRIMARY KEY, device_id TEXT NOT NULL, " +
                "command_name TEXT NOT NULL, request_hash TEXT NOT NULL, status TEXT NOT NULL, " +
                "result_json TEXT NULL, error_json TEXT NULL, created_at TEXT NOT NULL, " +
                "completed_at TEXT NULL);" +
                "CREATE TABLE IF NOT EXISTS shared_service_meta (" +
                "name TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);";

            команда.ExecuteNonQuery();
        }

        return сессия;
    }

    [Fact]
    public async Task Повтор_Завершённой_Операции_Возвращает_Сохранённый_Результат()
    {
        var путь = Path.Combine(Path.GetTempPath(), "ops_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await using (var сессия = СоздатьСессию(путь))
            {
                var журнал = new SharedOperationStore(сессия);
                var момент = DateTime.UtcNow;

                var начало = await журнал.НачатьAsync("оп-1", "устройство-1", "tests.probe", "хеш", момент);

                Assert.Equal(SharedOperationOutcomeKind.Proceed, начало.Вид);

                await журнал.СохранитьРезультатAsync("оп-1", "{\"id\":5}", момент);

                var повтор = await журнал.НачатьAsync("оп-1", "устройство-1", "tests.probe", "хеш", DateTime.UtcNow);

                Assert.Equal(SharedOperationOutcomeKind.ReplayResult, повтор.Вид);
                Assert.Equal("{\"id\":5}", повтор.Результат);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public async Task Тот_Же_Идентификатор_С_Другим_Содержимым_Отклоняется()
    {
        var путь = Path.Combine(Path.GetTempPath(), "ops_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await using (var сессия = СоздатьСессию(путь))
            {
                var журнал = new SharedOperationStore(сессия);
                var момент = DateTime.UtcNow;

                await журнал.НачатьAsync("оп-2", "устройство-1", "tests.probe", "хеш-1", момент);

                var повтор = await журнал.НачатьAsync("оп-2", "устройство-1", "tests.probe", "хеш-2", DateTime.UtcNow);

                Assert.Equal(SharedOperationOutcomeKind.Conflict, повтор.Вид);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public async Task Устаревшая_Попытка_Перехватывается_И_Не_Может_Зафиксировать_Результат()
    {
        var путь = Path.Combine(Path.GetTempPath(), "ops_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await using (var сессия = СоздатьСессию(путь))
            {
                var журнал = new SharedOperationStore(сессия);

                // Первая попытка началась давно: служба могла завершиться
                // аварийно, не записав результат.
                var моментПервой = DateTime.UtcNow - SharedOperationStore.СрокПерехвата
                    - TimeSpan.FromSeconds(1);

                await журнал.НачатьAsync("оп-3", "устройство-1", "tests.probe", "хеш", моментПервой);

                // Вторая попытка перехватывает операцию.
                var моментВторой = DateTime.UtcNow;

                var перехват = await журнал.НачатьAsync(
                    "оп-3", "устройство-2", "tests.probe", "хеш", моментВторой);

                Assert.Equal(SharedOperationOutcomeKind.Proceed, перехват.Вид);

                // Первая попытка уже не может зафиксировать свой результат:
                // запись принадлежит второй попытке.
                var зафиксировано = await журнал.СохранитьРезультатAsync(
                    "оп-3", "{\"stale\":true}", моментПервой);

                Assert.False(зафиксировано);

                // Вторая попытка фиксирует результат.
                var зафиксированоВторой = await журнал.СохранитьРезультатAsync(
                    "оп-3", "{\"fresh\":true}", моментВторой);

                Assert.True(зафиксированоВторой);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public async Task Свежая_Операция_Не_Перехватывается_Другой_Попыткой()
    {
        var путь = Path.Combine(Path.GetTempPath(), "ops_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await using (var сессия = СоздатьСессию(путь))
            {
                var журнал = new SharedOperationStore(сессия);
                var момент = DateTime.UtcNow;

                await журнал.НачатьAsync("оп-4", "устройство-1", "tests.probe", "хеш", момент);

                var повтор = await журнал.НачатьAsync(
                    "оп-4", "устройство-2", "tests.probe", "хеш", DateTime.UtcNow);

                Assert.Equal(SharedOperationOutcomeKind.Conflict, повтор.Вид);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public async Task Устаревшие_Завершённые_Записи_Удаляются()
    {
        var путь = Path.Combine(Path.GetTempPath(), "ops_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await using (var сессия = СоздатьСессию(путь))
            {
                var журнал = new SharedOperationStore(сессия);
                var старый = DateTime.UtcNow - TimeSpan.FromDays(40);

                await журнал.НачатьAsync("оп-старая", "устройство-1", "tests.probe", "хеш", старый);
                await журнал.СохранитьРезультатAsync("оп-старая", "{}", старый);

                await журнал.НачатьAsync("оп-свежая", "устройство-1", "tests.probe", "хеш", DateTime.UtcNow);

                var удалено = await журнал.УдалитьУстаревшиеAsync(TimeSpan.FromDays(30));

                Assert.Equal(1, удалено);

                // Свежая незавершённая запись сохраняется: повтор операции
                // обязан вернуть прежний результат.
                var повтор = await журнал.НачатьAsync(
                    "оп-свежая", "устройство-1", "tests.probe", "хеш", DateTime.UtcNow);

                Assert.Equal(SharedOperationOutcomeKind.Conflict, повтор.Вид);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }
}
