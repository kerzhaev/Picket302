using Microsoft.Data.Sqlite;
using Picket302.App.Shared;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки локальных sidecar-хранилищ рабочего места: изоляция калибровки
/// между компьютерами и перенос из прежней локальной базы без её изменения.
/// </summary>
public sealed class SidecarIsolationTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест с временным каталогом.</summary>
    public SidecarIsolationTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Sidecar_" + Guid.NewGuid().ToString("N"));
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
    /// Создаёт локальную базу с профилем калибровки и возвращает путь.
    /// </summary>
    /// <param name="смещение">Смещение калибровки.</param>
    /// <returns>Путь к базе.</returns>
    private async Task<string> СоздатьЛокальнуюБазуСКалибровкойAsync(decimal смещение)
    {
        var путь = Path.Combine(_каталог, $"local-{Guid.NewGuid():N}.db");

        await using var соединение = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = путь }.ToString());

        await соединение.OpenAsync().ConfigureAwait(false);

        await using (var команда = соединение.CreateCommand())
        {
            команда.CommandText =
                "CREATE TABLE printer_profiles (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, printer_name TEXT NOT NULL, " +
                "envelope_format TEXT NOT NULL, offset_x_mm NUMERIC NOT NULL DEFAULT 0, " +
                "offset_y_mm NUMERIC NOT NULL DEFAULT 0, feed_alignment INTEGER NOT NULL DEFAULT 0, " +
                "fix_a4 INTEGER NOT NULL DEFAULT 0, duplex_capable INTEGER NOT NULL DEFAULT 0, " +
                "is_default INTEGER NOT NULL DEFAULT 0, devmode_template BLOB NULL);" +
                "INSERT INTO printer_profiles (printer_name, envelope_format, offset_x_mm, offset_y_mm) " +
                "VALUES ('Принтер ПК', 'C6', @X, @Y);";

            команда.Parameters.AddWithValue("@X", смещение);
            команда.Parameters.AddWithValue("@Y", смещение);

            await команда.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await соединение.CloseAsync().ConfigureAwait(false);

        return путь;
    }

    [Fact]
    public async Task Калибровка_Компьютера_A_Не_Появляется_У_Компьютера_B()
    {
        // Рабочее место A: свой каталог профиля и своя калибровка.
        var каталогA = Path.Combine(_каталог, "pc-a");
        Directory.CreateDirectory(каталогA);

        var хранилищеA = new LocalPrinterProfileStore(каталогA);

        await хранилищеA.AddAsync(new PrinterProfile
        {
            PrinterName = "Принтер A",
            EnvelopeFormat = "C6",
            OffsetXMm = 1.5m,
            OffsetYMm = -0.5m,
        });

        // Рабочее место B: свой каталог профиля, калибровки нет.
        var каталогB = Path.Combine(_каталог, "pc-b");
        Directory.CreateDirectory(каталогB);

        var хранилищеB = new LocalPrinterProfileStore(каталогB);

        var профилиB = await хранилищеB.GetByPrinterAsync("Принтер A");

        Assert.Empty(профилиB);

        var поФорматуB = await хранилищеB.GetByPrinterAndFormatAsync("Принтер A", "C6");

        Assert.Null(поФорматуB);
    }

    [Fact]
    public async Task Перенос_Из_Локальной_Базы_Не_Меняет_Исходник()
    {
        var путьБазы = await СоздатьЛокальнуюБазуСКалибровкойAsync(2.5m);

        // Снимок файла снимается после освобождения пула соединений: иначе
        // копия не может открыться на чтение.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var снимокДо = await File.ReadAllBytesAsync(путьБазы);

        var каталогSidecar = Path.Combine(_каталог, "pc-shared");
        Directory.CreateDirectory(каталогSidecar);

        var хранилище = new LocalPrinterProfileStore(каталогSidecar);
        var прежняя = Picket302.Infrastructure.Repositories.ПрофилиПринтеровЧтение.Прочитать(путьБазы);

        Assert.Single(прежняя);

        var перенесено = хранилище.ПеренестиИз(прежняя);

        Assert.Equal(1, перенесено);

        var поФормату = await хранилище.GetByPrinterAndFormatAsync("Принтер ПК", "C6");

        Assert.NotNull(поФормату);
        Assert.Equal(2.5m, поФормату!.OffsetXMm);

        // Исходная база не изменена: содержимое файла совпадает байт в байт.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var снимокПосле = await File.ReadAllBytesAsync(путьБазы);

        Assert.Equal(снимокДо, снимокПосле);

        // Повторный перенос не создаёт дублей.
        var повторно = хранилище.ПеренестиИз(прежняя);

        Assert.Equal(0, повторно);
    }

    [Fact]
    public async Task Локальные_Параметры_Рабочих_Мест_Изолированы()
    {
        var каталогA = Path.Combine(_каталог, "params-a");
        var каталогB = Path.Combine(_каталог, "params-b");
        Directory.CreateDirectory(каталогA);
        Directory.CreateDirectory(каталогB);

        var хранилищеA = new LocalParameterStore(каталогA);
        var хранилищеB = new LocalParameterStore(каталогB);

        await хранилищеA.AddAsync(new Parameter
        {
            Name = "ПоследнийПринтер",
            Value = "Принтер A",
        });

        var значениеB = await хранилищеB.GetValueAsync("ПоследнийПринтер");

        Assert.Null(значениеB);
    }
}
