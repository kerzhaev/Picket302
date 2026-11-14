using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Service.Configuration;
using Picket302.Service.Data;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки первичного импорта базы на сервер: согласованный снимок вместо
/// копирования файла, проверки целостности, сохранность исходника и
/// переключение только после полной проверки.
/// </summary>
public sealed class ServerBaseImportTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест с временным каталогом.</summary>
    public ServerBaseImportTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Каталог остаётся: временные файлы проверки не критичны.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт исходную базу с данными и возвращает путь к ней.
    /// </summary>
    /// <param name="наименование">Наименование первого контрагента.</param>
    /// <returns>Путь к базе.</returns>
    private async Task<string> СоздатьИсходнуюБазуAsync(string наименование)
    {
        var путь = Path.Combine(_каталог, $"source-{Guid.NewGuid():N}.db");

        var сессияФабрика = new ServerDatabase(путь);
        await сессияФабрика.ПодготовитьAsync();

        await using (var сессияДанных = сессияФабрика.СоздатьСессию())
        {
            await сессияДанных.Connection.ExecuteAsync(
                """
                INSERT INTO groups (name, is_default) VALUES ('Поставщики', 0);
                INSERT INTO counterparties (type, name, is_active, is_sender,
                    legal_zip, legal_str1, address_main_type)
                VALUES (0, @Name, 1, 0, '101000', @Name, 0);
                """,
                new { Name = наименование });
        }

        return путь;
    }

    [Fact]
    public async Task Импорт_Переносит_Данные_И_Сохраняет_Исходник()
    {
        var базаСлужбы = new ServerDatabase(Path.Combine(_каталог, "picket302.db"));
        await базаСлужбы.ПодготовитьAsync();

        var путьИсточника = await СоздатьИсходнуюБазуAsync("ООО «Импортёр»");

        var импорт = new ServerBaseImport(базаСлужбы, _каталог);
        var итог = await импорт.ВыполнитьAsync(путьИсточника);

        Assert.True(итог.Успешно, итог.Сообщение);
        Assert.Equal(1, итог.Контрагентов);
        Assert.NotEmpty(итог.ВерсияСхемы);

        // Данные доступны в базе службы после переключения.
        await using var сессия = базаСлужбы.СоздатьСессию();

        var наименование = await сессия.Connection.ExecuteScalarAsync<string>(
            "SELECT name FROM counterparties LIMIT 1;");

        Assert.Equal("ООО «Импортёр»", наименование);

        // Исходная база не удалена и не перезаписана.
        Assert.True(File.Exists(путьИсточника));

        // Прежняя (пустая) база службы сохранена рядом.
        Assert.True(File.Exists(Path.Combine(_каталог, ServerBaseImport.ИмяПрежнейБазы)));
    }

    [Fact]
    public async Task Повреждённый_Исходник_Отклоняется_Без_Переключения()
    {
        var базаСлужбы = new ServerDatabase(Path.Combine(_каталог, "picket302.db"));
        await базаСлужбы.ПодготовитьAsync();

        var путьИсточника = Path.Combine(_каталог, "broken.db");
        await File.WriteAllBytesAsync(путьИсточника, "это не база данных"u8.ToArray());

        var импорт = new ServerBaseImport(базаСлужбы, _каталог);
        var итог = await импорт.ВыполнитьAsync(путьИсточника);

        Assert.False(итог.Успешно);

        // База службы не заменена: в ней по-прежнему пустая схема.
        await using var сессия = базаСлужбы.СоздатьСессию();

        var контрагентов = await сессия.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM counterparties;");

        Assert.Equal(0, контрагентов);
    }

    [Fact]
    public async Task Отсутствующий_Исходник_Отклоняется()
    {
        var базаСлужбы = new ServerDatabase(Path.Combine(_каталог, "picket302.db"));
        await базаСлужбы.ПодготовитьAsync();

        var импорт = new ServerBaseImport(базаСлужбы, _каталог);
        var итог = await импорт.ВыполнитьAsync(Path.Combine(_каталог, "нет-такого.db"));

        Assert.False(итог.Успешно);
        Assert.Contains("не найден", итог.Сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Импорт_Базы_В_Саму_Себя_Отклоняется()
    {
        var путьСлужбы = Path.Combine(_каталог, "picket302.db");
        var базаСлужбы = new ServerDatabase(путьСлужбы);
        await базаСлужбы.ПодготовитьAsync();

        var импорт = new ServerBaseImport(базаСлужбы, _каталог);
        var итог = await импорт.ВыполнитьAsync(путьСлужбы);

        Assert.False(итог.Успешно);
    }

    [Fact]
    public async Task Незаполненный_Путь_Отклоняется()
    {
        var базаСлужбы = new ServerDatabase(Path.Combine(_каталог, "picket302.db"));
        await базаСлужбы.ПодготовитьAsync();

        var импорт = new ServerBaseImport(базаСлужбы, _каталог);
        var итог = await импорт.ВыполнитьAsync("   ");

        Assert.False(итог.Успешно);
        Assert.Contains("не указан", итог.Сообщение, StringComparison.Ordinal);
    }
}
