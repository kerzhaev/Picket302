using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Persistence;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка атомарности сквозного счётчика регистрационных номеров
/// (ТЗ §8.1, функция 25: «Сквозная регистрационная нумерация»).
/// </summary>
/// <remarks>
/// Нумерация не должна допускать коллизий: параллельные запросы обязаны
/// получать разные последовательные номера.
/// </remarks>
public sealed class RegistrationNumberStoreTests
{
    [Fact]
    public async Task NextAsync_ПоследовательныеВызовы_ДаютВозрастающиеНомера()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var хранилище = new ParameterRegistrationNumberStore(база.Session);

        var первый = await хранилище.NextAsync("счётчик.тест");
        var второй = await хранилище.NextAsync("счётчик.тест");
        var третий = await хранилище.NextAsync("счётчик.тест");

        Assert.Equal(1, первый);
        Assert.Equal(2, второй);
        Assert.Equal(3, третий);
    }

    [Fact]
    public async Task NextAsync_РазныеКлючи_СчётчикиНезависимы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var хранилище = new ParameterRegistrationNumberStore(база.Session);

        await хранилище.NextAsync("счётчик.первый");
        await хранилище.NextAsync("счётчик.первый");

        var другой = await хранилище.NextAsync("счётчик.второй");

        Assert.Equal(1, другой);
    }

    [Fact]
    public async Task ПараллельныеЗапросы_НомераНеКоллизируют()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_нумерация_{Guid.NewGuid():N}.db");

        try
        {
            await using (var подготовка = await SqliteTestDatabase.CreateFileAsync(путь))
            {
                // Схема применена, база готова к параллельной работе.
            }

            const int Потоков = 4;
            const int ЗапросовНаПоток = 15;

            var номера = new System.Collections.Concurrent.ConcurrentBag<long>();

            var задачи = new List<Task>();

            for (var поток = 0; поток < Потоков; поток++)
            {
                задачи.Add(Task.Run(async () =>
                {
                    var строка = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                    {
                        DataSource = путь,
                        Pooling = false,
                    }.ToString();

                    await using var соединение = new SqliteUnitOfWork(строка);
                    var хранилище = new ParameterRegistrationNumberStore(соединение);

                    for (var запрос = 0; запрос < ЗапросовНаПоток; запрос++)
                    {
                        номера.Add(await хранилище.NextAsync("счётчик.параллельный"));
                    }
                }));
            }

            await Task.WhenAll(задачи);

            var всего = Потоков * ЗапросовНаПоток;

            Assert.Equal(всего, номера.Count);
            Assert.Equal(всего, номера.Distinct().Count());

            // Номера образуют непрерывный ряд 1..N без пропусков и повторов.
            var упорядоченные = номера.OrderBy(номер => номер).ToArray();

            for (var индекс = 0; индекс < всего; индекс++)
            {
                Assert.Equal(индекс + 1, упорядоченные[индекс]);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public async Task ГенераторНомера_ПараллельныеВызовы_НомераУникальны()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var генератор = new RegistrationNumberGenerator(new ParameterRegistrationNumberStore(база.Session));

        var номера = new System.Collections.Concurrent.ConcurrentBag<string>();

        var задачи = Enumerable.Range(0, 30)
            .Select(_ => Task.Run(async () =>
                номера.Add(await генератор.GenerateNextOutgoingNumberAsync(
                    moment: new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc)))))
            .ToArray();

        await Task.WhenAll(задачи);

        Assert.Equal(30, номера.Count);
        Assert.Equal(30, номера.Distinct().Count());
        Assert.All(номера, номер => Assert.StartsWith("ИСХ-2026/", номер, StringComparison.Ordinal));
    }
}

/// <summary>
/// Проверка виртуального постраничного просмотра (ТЗ §8.1, функция 11:
/// «Виртуальный постраничный просмотр (Pagination)»).
/// </summary>
public sealed class VirtualDataBufferTests
{
    [Fact]
    public async Task GetPage_СмещениеИРазмер_ВозвращаетОкноДанных()
    {
        var буфер = new VirtualDataBuffer<int>((смещение, размер, _) =>
        {
            var элементы = Enumerable.Range(смещение, размер).ToArray();

            return Task.FromResult(new Page<int>(элементы, смещение, размер, 1000));
        });

        var страница = await буфер.GetPage(2, 100);

        Assert.Equal(200, страница.Offset);
        Assert.Equal(100, страница.Items.Count);
        Assert.Equal(200, страница.Items[0]);
        Assert.Equal(299, страница.Items[^1]);
        Assert.Equal(1000, страница.TotalCount);
    }

    [Fact]
    public async Task GetPage_ОтрицательныйИндекс_ОтсчитываетОтНачала()
    {
        var буфер = new VirtualDataBuffer<int>((смещение, размер, _) =>
            Task.FromResult(new Page<int>(Enumerable.Range(смещение, размер).ToArray(), смещение, размер, 10)));

        var страница = await буфер.GetPage(-5, 50);

        Assert.Equal(0, страница.Offset);
        Assert.Equal(0, страница.Items[0]);
    }

    [Fact]
    public async Task GetPage_НулевойРазмер_ИспользуетсяРазмерПоУмолчанию()
    {
        var буфер = new VirtualDataBuffer<int>((смещение, размер, _) =>
            Task.FromResult(new Page<int>(Enumerable.Range(смещение, размер).ToArray(), смещение, размер, 10)));

        var страница = await буфер.GetPage(0, 0);

        Assert.Equal(VirtualDataBuffer<int>.DefaultPageSize, страница.Limit);
    }

    [Fact]
    public async Task GetTotalCountAsync_ВозвращаетОбщееЧислоЗаписей()
    {
        var буфер = new VirtualDataBuffer<int>((_, размер, _) =>
            Task.FromResult(new Page<int>(new int[размер], 0, размер, 150_000)));

        Assert.Equal(150_000, await буфер.GetTotalCountAsync());
    }

    [Fact]
    public void FormatPaginationText_ПанельПагинации_ПоказываетДиапазонИВсего()
    {
        Assert.Equal("Записи 1-100 из 150000", VirtualDataBuffer<int>.FormatPaginationText(0, 100, 150_000));
        Assert.Equal("Записи 101-200 из 150000", VirtualDataBuffer<int>.FormatPaginationText(1, 100, 150_000));
        Assert.Equal("Записи 0-0 из 0", VirtualDataBuffer<int>.FormatPaginationText(0, 100, 0));
    }

    [Fact]
    public void FormatPaginationText_ПоследняяСтраница_ОграничиваетсяОбщимЧислом()
    {
        Assert.Equal("Записи 101-150 из 150", VirtualDataBuffer<int>.FormatPaginationText(1, 100, 150));
    }
}
