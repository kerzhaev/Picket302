using System.Security.Cryptography;
using System.Text.Json;
using Picket302.App;
using Picket302.Infrastructure;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Защитные проверки изоляции профиля: App-тесты, создающие состав
/// приложения и главное окно, не должны изменять реальные файлы
/// пользователя в <c>%AppData%\Picket302</c> — ни реквизиты подключения,
/// ни базу данных, ни журналы.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ProfileIsolationTests
{
    /// <summary>Реальный каталог профиля приложения.</summary>
    private static readonly string КаталогПрофиля = Picket302ProfilePaths.КаталогДанныхПоУмолчанию();

    [Fact]
    public async Task СоставПриложенияВИзолированномПрофиле_НеИзменяетРеальныйПрофиль()
    {
        // Реальный профиль — общий ресурс: межпроцессная блокировка
        // защищает отпечатки от параллельного testhost/acceptance-прогона
        // (владение мьютексом в выделенном потоке — безопасно для async).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var до = СнятьОтпечатки();

        using (var профиль = new ИзолированныйПрофиль())
        {
            using var окно = await профиль.СоздатьОболочкуAsync();

            await окно.ИнициализироватьAsync();

            Assert.True(окно.Инициализировано);
        }

        var после = СнятьОтпечатки();

        // Ни один реальный файл не создан, не изменён и не удалён: имена и
        // отпечатки совпадают. Отсутствующий файл даёт пустой отпечаток, так
        // что «файл не создан» проверяется тем же сравнением.
        Assert.Equal(до.Keys.OrderBy(имя => имя, StringComparer.Ordinal), после.Keys.OrderBy(имя => имя, StringComparer.Ordinal));

        foreach (var (путь, отпечатокДо) in до)
        {
            Assert.True(
                после.TryGetValue(путь, out var отпечатокПосле) && отпечатокПосле == отпечатокДо,
                $"Файл реального профиля изменён тестом: {путь}");
        }
    }

    [Fact]
    public async Task РеквизитыПодключения_СоздаютсяТолькоВоВременномПрофиле()
    {
        // Реальный профиль — общий ресурс (см. первый тест класса).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var реальныйПуть = Path.Combine(КаталогПрофиля, Picket302ProfilePaths.ИмяФайлаПодключения);
        var отпечатокДо = ОтпечатокФайла(реальныйПуть);

        string временныйПуть;

        using (var профиль = new ИзолированныйПрофиль())
        {
            временныйПуть = профиль.Пути.ConnectionSettingsPath;

            using var окно = await профиль.СоздатьОболочкуAsync();

            await окно.ИнициализироватьAsync();

            Assert.True(
                File.Exists(временныйПуть),
                "Реквизиты подключения должны создаваться во временном профиле, а не в реальном.");

            using var документ = JsonDocument.Parse(File.ReadAllText(временныйПуть));

            Assert.True(
                документ.RootElement.GetProperty("FirstConnectionDone").GetBoolean(),
                "Реквизиты во временном профиле должны отмечать первое подключение.");

            Assert.Equal(
                профиль.Пути.DatabasePath,
                документ.RootElement.GetProperty("DatabasePath").GetString());
        }

        Assert.Equal(отпечатокДо, ОтпечатокФайла(реальныйПуть));
    }

    /// <summary>
    /// Снимает отпечатки всех файлов реального профиля: относительный путь —
    /// SHA-256 и время изменения. Отсутствующий файл не даёт записи, поэтому
    /// «создан» и «изменён» различимы сравнением словарей.
    /// </summary>
    /// <returns>Словарь отпечатков.</returns>
    private static Dictionary<string, string> СнятьОтпечатки()
    {
        var отпечатки = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(КаталогПрофиля))
        {
            return отпечатки;
        }

        foreach (var файл in Directory.EnumerateFiles(КаталогПрофиля, "*", SearchOption.AllDirectories))
        {
            var относительный = Path.GetRelativePath(КаталогПрофиля, файл);

            отпечатки[относительный] = ОтпечатокФайла(файл) ?? string.Empty;
        }

        return отпечатки;
    }

    /// <summary>
    /// Возвращает отпечаток файла: SHA-256 содержимого и время изменения.
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <returns>Отпечаток либо <see langword="null"/> — файла нет.</returns>
    private static string? ОтпечатокФайла(string путь)
    {
        if (!File.Exists(путь))
        {
            return null;
        }

        using var поток = File.OpenRead(путь);

        // Время изменения в отпечатке ловит перезапись тем же содержимым.
        return Convert.ToHexString(SHA256.HashData(поток))
            + "|" + File.GetLastWriteTimeUtc(путь).Ticks;
    }

    /// <summary>
    /// Пустой профиль без демо-данных остаётся пустым: <c>создатьДемоБазу:
    /// false</c> — рабочий путь runtime-проверки пустой адресной книги.
    /// </summary>
    [Fact]
    public async Task ПустойПрофильБезДемоБазы_АдреснаяКнигаОстаётсяПустой()
    {
        using var профиль = new ИзолированныйПрофиль();

        // Владение у профиля: состав освобождается в Dispose профиля даже
        // при упавшем Assert.
        var состав = профиль.ВзятьВладение(
            await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

        var страница = await состав.Контрагенты
            .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 100), null, CancellationToken.None);

        Assert.Equal(0, страница.TotalCount);
        Assert.Empty(await состав.Контрагенты.GetSendersAsync(CancellationToken.None));
    }

    /// <summary>
    /// Демо-база появляется только при явном запросе: <c>создатьДемоБазу:
    /// true</c> наполняет пустую адресную книгу, <c>false</c> — нет.
    /// </summary>
    [Fact]
    public async Task ДемоБазаСоздаётсяТолькоПриЯвномЗапросе()
    {
        using var пустой = new ИзолированныйПрофиль();
        var составПустой = пустой.ВзятьВладение(
            await AppServices.СоздатьAsync(пустой.Пути, создатьДемоБазу: false));

        Assert.Equal(
            0,
            (await составПустой.Контрагенты
                .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 100), null, CancellationToken.None))
            .TotalCount);

        using var демо = new ИзолированныйПрофиль();
        var составДемо = демо.ВзятьВладение(
            await AppServices.СоздатьAsync(демо.Пути, создатьДемоБазу: true));

        Assert.True(
            (await составДемо.Контрагенты
                .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 100), null, CancellationToken.None))
            .TotalCount > 0);
    }

    /// <summary>
    /// Гард изоляции отказывает до создания состава, если путь профиля
    /// совпадает с производственным каталогом пользователя.
    /// </summary>
    [Fact]
    public void ИзолированныйПрофиль_ОтказываетНаПроизводственномПути()
    {
        // Корень = родитель производственного каталога: структура профиля
        // добавляет к корню имя «Picket302», поэтому родитель даёт точное
        // совпадение с производственным путём.
        var родитель = Path.GetDirectoryName(КаталогПрофиля)
            ?? throw new InvalidOperationException("У профиля нет родительского каталога.");

        var ошибка = Assert.Throws<InvalidOperationException>(() => new ИзолированныйПрофиль(родитель));

        Assert.Contains("производственн", ошибка.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sentinel-файл в реальном профиле пользователя не изменяется
    /// тестовым прогоном: SHA-256 и LastWriteTimeUtc до и после совпадают,
    /// новые файлы реального профиля не появляются.
    /// </summary>
    [Fact]
    public async Task SentinelФайлРеальногоПрофиля_НеИзменяетсяПрогоном()
    {
        // Sentinel в реальном профиле — общий ресурс: без блокировки
        // параллельный testhost/acceptance-прогон со своим sentinel
        // давал ложный «новый файл реального профиля».
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        Directory.CreateDirectory(КаталогПрофиля);

        var sentinel = Path.Combine(КаталогПрофиля, "picket302-review-sentinel.tmp");

        File.WriteAllText(sentinel, $"sentinel-{Guid.NewGuid():N}");

        try
        {
            var отпечатокДо = ОтпечатокФайла(sentinel);
            var профильДо = СнятьОтпечатки();

            using (var профиль = new ИзолированныйПрофиль())
            {
                using var окно = await профиль.СоздатьОболочкуAsync(создатьДемоБазу: true);

                await окно.ИнициализироватьAsync();

                Assert.True(окно.Инициализировано);
            }

            Assert.Equal(отпечатокДо, ОтпечатокФайла(sentinel));

            var профильПосле = СнятьОтпечатки();

            Assert.Equal(
                профильДо.Keys.OrderBy(имя => имя, StringComparer.Ordinal),
                профильПосле.Keys.OrderBy(имя => имя, StringComparer.Ordinal));
        }
        finally
        {
            File.Delete(sentinel);
        }
    }

    /// <summary>
    /// Журнал сбоев тестового контура пишется во временный профиль: файл
    /// журнала появляется в <c>пути.LogsDirectory</c>, реальный профиль
    /// не получает новых файлов журнала.
    /// </summary>
    [Fact]
    public void ЖурналСбоевТестовогоКонтура_ПишетВоВременныйПрофиль()
    {
        // Реальный профиль — общий ресурс (см. первый тест класса).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var журналРеальногоДо = СнятьОтпечатки()
            .Where(пара => пара.Key.StartsWith("logs", StringComparison.Ordinal))
            .ToDictionary(пара => пара.Key, пара => пара.Value);

        // Проверка выполняется внутри блока профиля: утилизация удаляет
        // весь временный корень вместе с журналом.
        using (var профиль = new ИзолированныйПрофиль())
        {
            using (var хаб = new Picket302.Infrastructure.Logging.LogHub(профиль.Пути.LogsDirectory))
            {
                хаб.LogError(new InvalidOperationException("Проверка изоляции журнала."));
            }

            Assert.True(
                Directory.Exists(профиль.Пути.LogsDirectory)
                && Directory.EnumerateFiles(профиль.Пути.LogsDirectory).Any(),
                "Файл журнала должен создаваться во временном профиле.");
        }

        var журналРеальногоПосле = СнятьОтпечатки()
            .Where(пара => пара.Key.StartsWith("logs", StringComparison.Ordinal))
            .ToDictionary(пара => пара.Key, пара => пара.Value);

        Assert.Equal(
            журналРеальногоДо.Keys.OrderBy(имя => имя, StringComparer.Ordinal),
            журналРеальногоПосле.Keys.OrderBy(имя => имя, StringComparer.Ordinal));
    }
}
