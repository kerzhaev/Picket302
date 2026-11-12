using System.Xml.Linq;
using Picket302.Application.Services;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// S28 (ТЗ §8.4, функция 58): дампер промежуточных данных реестров —
/// структурированный XML (Tables/Table/Row/Field), ветка произвольного
/// payload, изоляция ошибки записи (переход на генерацию в памяти) и
/// сохранение опции чекбокса.
/// </summary>
public sealed class ReportDataDumperTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();
    }

    private ReportDataDumper СоздатьДампер() => new(new ParameterRepository(_база.UnitOfWork));

    [Fact]
    public void ДампСтруктурированныхДанных_ТаблицыСтрокиПоля()
    {
        var дампер = СоздатьДампер();

        var датасет = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        {
            ["qrReport_F103"] = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["mail_number"] = "103",
                    ["weight"] = 25m,
                    ["note"] = null,
                },
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["mail_number"] = "104",
                    ["weight"] = 12.5m,
                    ["note"] = "Заказное",
                },
            },
        };

        var путь = дампер.DumpReportDataToTempXml(датасет, "Реестр ф.103");

        Assert.True(дампер.LastDumpSucceeded);
        Assert.NotNull(путь);
        Assert.Equal(путь, дампер.LastDumpPath);
        Assert.True(File.Exists(путь));

        try
        {
            var документ = XDocument.Load(путь!);
            var корень = документ.Root!;

            Assert.Equal("ReportData", корень.Name.LocalName);
            Assert.Equal("Реестр ф.103", (string?)корень.Attribute("report"));

            var таблица = Assert.Single(корень.Descendants("Table"));
            Assert.Equal("qrReport_F103", (string?)таблица.Attribute("name"));

            var строки = таблица.Descendants("Row").ToList();
            Assert.Equal(2, строки.Count);

            var поляПервой = строки[0].Descendants("Field")
                .ToDictionary(поле => (string)поле.Attribute("name")!, поле => поле.Value);
            Assert.Equal("103", поляПервой["mail_number"]);
            Assert.Equal("25", поляПервой["weight"]);
            Assert.Equal(string.Empty, поляПервой["note"]);
        }
        finally
        {
            File.Delete(путь!);
        }
    }

    [Fact]
    public void ДампПроизвольногоОбъекта_ВеткаPayload()
    {
        var дампер = СоздатьДампер();

        var путь = дампер.DumpReportDataToTempXml("произвольные данные", "Отчёт");

        Assert.True(дампер.LastDumpSucceeded);
        Assert.NotNull(путь);

        try
        {
            var документ = XDocument.Load(путь!);

            Assert.Equal("произвольные данные", документ.Root!.Descendants("Payload").Single().Value);
        }
        finally
        {
            File.Delete(путь!);
        }
    }

    [Fact]
    public void ОшибкаЗаписи_ВозвращаетсяNullФлагСброшен()
    {
        var дампер = СоздатьДампер();

        // Payload, выбрасывающий при сериализации: тот же путь изоляции, что
        // и ошибка записи во временную папку (ТЗ §8.4, ф.58 — переход на
        // генерацию в памяти).
        var неисправимый = new НеформатируемыйPayload();

        var путь = дампер.DumpReportDataToTempXml(неисправимый, "Отчёт");

        Assert.Null(путь);
        Assert.False(дампер.LastDumpSucceeded);
        Assert.Null(дампер.LastDumpPath);
    }

    [Fact]
    public void ПовторныйДамп_ОбновляетПутьПоследнегоФайла()
    {
        var дампер = СоздатьДампер();

        var первый = дампер.DumpReportDataToTempXml("первый", "Отчёт");
        var второй = дампер.DumpReportDataToTempXml("второй", "Отчёт");

        try
        {
            Assert.NotEqual(первый, второй);
            Assert.Equal(второй, дампер.LastDumpPath);
            Assert.False(File.Exists(первый) && второй is null);
            Assert.True(File.Exists(второй!));
        }
        finally
        {
            if (первый is not null && File.Exists(первый))
            {
                File.Delete(первый);
            }

            if (второй is not null && File.Exists(второй))
            {
                File.Delete(второй);
            }
        }
    }

    [Fact]
    public async Task ОпцияДампа_ПоУмолчаниюВыключенаИСохраняется()
    {
        var дампер = СоздатьДампер();

        Assert.False(await дампер.GetEnabledAsync());

        await дампер.SetEnabledAsync(true);
        Assert.True(await дампер.GetEnabledAsync());

        // Новая инстанция поверх той же базы видит сохранённую опцию.
        var второй = СоздатьДампер();
        Assert.True(await второй.GetEnabledAsync());

        await второй.SetEnabledAsync(false);
        Assert.False(await дампер.GetEnabledAsync());
    }

    /// <summary>Payload, выбрасывающий исключение при сериализации.</summary>
    private sealed class НеформатируемыйPayload
    {
        public override string ToString() =>
            throw new IOException("имитация недоступности временной папки");
    }
}
