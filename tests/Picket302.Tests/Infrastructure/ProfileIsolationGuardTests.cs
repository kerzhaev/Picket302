using System.Runtime.InteropServices;
using Picket302.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Production-safe гард изоляции профиля: равенство, вложенность в обе
/// стороны, регистр, разделители, sibling-префикс и reparse-ссылки.
/// Тесты используют синтетические пути — реальный производственный
/// профиль не читается и не изменяется.
/// </summary>
/// <remarks>
/// Контракт reparse-ветвей (обязательная Windows acceptance-среда):
/// невозможность создать junction/symbolic link — <see cref="Fail"/>
/// (красный тест), а не тихий <c>return</c>; создание проверяется
/// низкоуровневым атрибутом reparse point через
/// <c>GetFileAttributesW</c>; cleanup ссылок и временной базы
/// проверяется, ошибка cleanup делает тест красным.
/// </remarks>
[Collection("ИнфраструктурнаяУборка")]
public sealed class ProfileIsolationGuardTests
{
    /// <summary>Синтетический производственный каталог.</summary>
    private const string Производственный = @"C:\prod\Root\Picket302";

    /// <summary>
    /// Фикстура постусловия коллекции: внедряется явно, чтобы xUnit
    /// гарантированно создал её и выполнил teardown после всех тестов.
    /// </summary>
    private readonly ПостусловиеЧистотыGuard _постусловие;

    /// <summary>Создаёт тест с фикстурой постусловия коллекции.</summary>
    /// <param name="постусловие">Фикстура постусловия коллекции.</param>
    public ProfileIsolationGuardTests(ПостусловиеЧистотыGuard постусловие)
    {
        _постусловие = постусловие;
    }

    /// <summary>Атрибут reparse point (FILE_ATTRIBUTE_REPARSE_POINT).</summary>
    private const uint АтрибутReparsePoint = 0x00000400;

    /// <summary>Возвращаемое значение GetFileAttributesW для отсутствующего пути.</summary>
    private const uint НесуществующийАтрибут = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFileAttributesW(string имя);

    /// <summary>
    /// Путь существует (низкоуровневая проверка, пригодная для
    /// dangling-ссылок, которые ни File.Exists, ни Directory.Exists
    /// не видят).
    /// </summary>
    private static bool СуществуетПуть(string путь) =>
        GetFileAttributesW(путь) != НесуществующийАтрибут;

    /// <summary>
    /// Путь является reparse point (junction или symbolic link);
    /// работает и для dangling-ссылок.
    /// </summary>
    private static bool ЯвляетсяReparsePoint(string путь)
    {
        var атрибуты = GetFileAttributesW(путь);

        return атрибуты != НесуществующийАтрибут && (атрибуты & АтрибутReparsePoint) != 0;
    }

    /// <summary>Уникальный временный каталог reparse-тестов.</summary>
    private static string НовыйТестовыйКаталог() =>
        Path.Combine(Path.GetTempPath(), "P302GuardTest_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(@"C:\prod\Root\Picket302")]
    [InlineData(@"C:\prod\Root\Picket302\")]
    [InlineData(@"c:\PROD\Root\PICKET302")]
    [InlineData(@"C:/prod/Root/Picket302/")]
    [InlineData(@"C:\prod\Root\Picket302\\")]
    public void Кандидат_СовпадаетСПроизводственным_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("совпадает", причина, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\prod\Root\Picket302\logs")]
    [InlineData(@"C:\prod\Root\Picket302\Secrets\db")]
    [InlineData(@"C:\prod\Root\Picket302\Picket302")]
    [InlineData(@"c:\PROD\Root\picket302\LOGS\app.log")]
    public void Кандидат_ВнутриПроизводственного_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("внутри производственного", причина, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\prod\Root")]
    [InlineData(@"C:\prod")]
    [InlineData(@"C:\prod\Root\")]
    public void Кандидат_СодержитПроизводственный_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("находится внутри каталога профиля", причина, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sibling с общим текстовым префиксом не является потомком и не
    /// блокируется: строковый StartsWith здесь ложноположителен.
    /// </summary>
    [Theory]
    [InlineData(@"C:\prod\Root\Picket302-acceptance")]
    [InlineData(@"C:\prod\Root\Picket302x")]
    [InlineData(@"C:\prod\Root\Picket302_backup\inner")]
    public void Кандидат_SiblingСОбщимПрефиксом_НеБлокируется(string кандидат)
    {
        Assert.Null(ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный));
    }

    [Fact]
    public void Кандидат_ОбычныйВременныйПуть_НеБлокируется()
    {
        Assert.Null(ProfileIsolationGuard.ПричинаОтказа(
            @"C:\Temp\Picket302Acceptance\abc123\Picket302", Производственный));
    }

    /// <summary>
    /// Кандидат через junction/symlink на производственный каталог
    /// отклоняется после канонизации.
    /// </summary>
    [Fact]
    public void Кандидат_ЧерезReparseНаПроизводственный_Отклоняется()
    {
        // Общий ресурс %TEMP%\P302GuardTest_* защищён межпроцессной
        // блокировкой: xUnit-коллекция сериализует только внутри одного
        // testhost-процесса. Отказ получения — красный тест (fail-closed).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var база = НовыйТестовыйКаталог();
        var цель = Path.Combine(база, "prod-target");
        var ссылка = Path.Combine(база, "prod-link");
        var кореньСсылка = Path.Combine(база, "root-link");

        Exception? основная = null;

        try
        {
            Directory.CreateDirectory(цель);

            // Ссылка → цель, совпадающая с production: отказ по равенству.
            СоздатьJunctionОбязательно(ссылка, цель);

            var причина = ProfileIsolationGuard.ПричинаОтказа(ссылка, цель);

            Assert.NotNull(причина);
            Assert.Contains("совпадает", причина, StringComparison.Ordinal);

            // Ссылка → подкаталог цели: отказ по вложенности.
            Directory.CreateDirectory(Path.Combine(цель, "inner"));
            СоздатьJunctionОбязательно(кореньСсылка, Path.Combine(цель, "inner"));

            var причина2 = ProfileIsolationGuard.ПричинаОтказа(
                Path.Combine(кореньСсылка, "Picket302"), цель);

            Assert.NotNull(причина2);
            Assert.Contains("внутри производственного", причина2, StringComparison.Ordinal);
        }
        catch (Exception ошибка)
        {
            основная = ошибка;
        }

        // Ссылки удаляются до рекурсивного удаления базы: рекурсивный
        // обход заходит внутрь цели ссылки. Ошибка cleanup — красный тест.
        var ошибкиУборки = new List<Exception>();
        УдалитьСсылкуОбязательно(кореньСсылка, ошибкиУборки);
        УдалитьСсылкуОбязательно(ссылка, ошибкиУборки);
        УдалитьБазуОбязательно(база, ошибкиУборки);
        СообщитьИтог(основная, ошибкиУборки);
    }

    /// <summary>
    /// Dangling ссылка канонизируется к целевому пути: запись через
    /// ссылку попадает в цель, поэтому канонизированный кандидат и есть
    /// цель. Существование ссылки проверяется низкоуровнево:
    /// <c>File.GetAttributes</c> на dangling-ссылке бросает
    /// <see cref="FileNotFoundException"/>, поэтому используется
    /// <c>GetFileAttributesW</c>.
    /// </summary>
    [Fact]
    public void Кандидат_DanglingСсылка_КанонизируетсяКЦели()
    {
        // Общий ресурс %TEMP%\P302GuardTest_* защищён межпроцессной
        // блокировкой (см. Кандидат_ЧерезReparseНаПроизводственный).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var база = НовыйТестовыйКаталог();
        var цель = Path.Combine(база, "missing-target");
        var ссылка = Path.Combine(база, "dangling-link");

        Exception? основная = null;

        try
        {
            Directory.CreateDirectory(база);
            Directory.CreateSymbolicLink(ссылка, цель);

            // Обязательная среда: создание проверяется, провал — FAIL.
            Assert.True(
                ЯвляетсяReparsePoint(ссылка),
                "dangling-симлинк не создан (reparse-атрибут отсутствует): " + ссылка);

            // Цель отсутствует; существование самой ссылки подтверждается
            // только низкоуровневым атрибутом: File.GetAttributes на
            // dangling-ссылке бросает FileNotFoundException, а обычный
            // Exists проверяет саму ссылку и на dangling-цель молчит.
            Assert.False(СуществуетПуть(цель), "цель dangling-ссылки должна отсутствовать");
            Assert.False(File.Exists(ссылка), "каталог-симлинк не является файлом");

            // Канонизированный кандидат — целевой путь, поэтому сравнение
            // с той же целью даёт «совпадает»: гард не признаёт ссылку
            // безопасной молча.
            var причина = ProfileIsolationGuard.ПричинаОтказа(ссылка, цель);

            Assert.NotNull(причина);
            Assert.Contains("совпадает", причина, StringComparison.Ordinal);
        }
        catch (Exception ошибка)
        {
            основная = ошибка;
        }

        var ошибкиУборки = new List<Exception>();
        УдалитьСсылкуОбязательно(ссылка, ошибкиУборки);
        УдалитьБазуОбязательно(база, ошибкиУборки);
        СообщитьИтог(основная, ошибкиУборки);
    }

    /// <summary>
    /// Зацикленная цепочка ссылок не может быть канонизирована — путь
    /// отклоняется fail-closed вместо признания безопасным.
    /// </summary>
    [Fact]
    public void Кандидат_ЗацикленнаяЦепочкаСсылок_FailClosed()
    {
        // Общий ресурс %TEMP%\P302GuardTest_* защищён межпроцессной
        // блокировкой (см. Кандидат_ЧерезReparseНаПроизводственный).
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var база = НовыйТестовыйКаталог();
        var ссылкаА = Path.Combine(база, "loop-a");
        var ссылкаБ = Path.Combine(база, "loop-b");

        Exception? основная = null;

        try
        {
            Directory.CreateDirectory(база);

            СоздатьJunctionОбязательно(ссылкаА, ссылкаБ);
            СоздатьJunctionОбязательно(ссылкаБ, ссылкаА);

            Assert.Throws<InvalidOperationException>(() =>
                ProfileIsolationGuard.ПричинаОтказа(ссылкаА, Производственный));
        }
        catch (Exception ошибка)
        {
            основная = ошибка;
        }

        var ошибкиУборки = new List<Exception>();
        УдалитьСсылкуОбязательно(ссылкаА, ошибкиУборки);
        УдалитьСсылкуОбязательно(ссылкаБ, ошибкиУборки);
        УдалитьБазуОбязательно(база, ошибкиУборки);
        СообщитьИтог(основная, ошибкиУборки);
    }

    /// <summary>
    /// Создаёт junction (не требует привилегий). Обязательная среда:
    /// любой отказ — красный тест через <see cref="Assert.True"/> вместо
    /// тихого пропуска ветви.
    /// </summary>
    /// <param name="ссылка">Путь ссылки.</param>
    /// <param name="цель">Путь цели.</param>
    private static void СоздатьJunctionОбязательно(string ссылка, string цель)
    {
        using var процесс = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + ссылка + "\" \"" + цель + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        процесс!.WaitForExit(10000);

        Assert.True(
            процесс.ExitCode == 0,
            "mklink /J не создал junction (код " + процесс.ExitCode + "): " + ссылка);

        // Создание подтверждается низкоуровневым атрибутом reparse point:
        // одного кода возврата mklink недостаточно.
        Assert.True(
            ЯвляетсяReparsePoint(ссылка),
            "mklink /J вернул успех, но reparse point не обнаружен: " + ссылка);
    }

    /// <summary>
    /// Удаляет reparse point и проверяет отсутствие. Ошибки собираются в
    /// <paramref name="ошибки"/> — cleanup не проглатывает исключения.
    /// Существование определяется низкоуровнево: dangling-ссылка не видна
    /// ни <c>File.Exists</c>, ни <c>Directory.Exists</c>.
    /// </summary>
    /// <param name="путь">Путь ссылки.</param>
    /// <param name="ошибки">Список ошибок cleanup.</param>
    private static void УдалитьСсылкуОбязательно(string путь, List<Exception> ошибки)
    {
        try
        {
            if (СуществуетПуть(путь))
            {
                // Ссылка на несуществующую цель (dangling) может
                // определяться как файл или как каталог — удаляются
                // обе формы; reparse point снимается самим удалением.
                try
                {
                    Directory.Delete(путь, recursive: false);
                }
                catch (Exception ошибка) when (ошибка is DirectoryNotFoundException or FileNotFoundException)
                {
                    File.Delete(путь);
                }
            }

            if (СуществуетПуть(путь))
            {
                throw new InvalidOperationException("ссылка не удалена: " + путь);
            }
        }
        catch (Exception ошибка)
        {
            ошибки.Add(new InvalidOperationException("cleanup ссылки не удался: " + путь, ошибка));
        }
    }

    /// <summary>
    /// Рекурсивно удаляет временную базу reparse-теста с повторными
    /// попытками и проверкой результата. Ошибка — в
    /// <paramref name="ошибки"/>, не проглатывается.
    /// </summary>
    /// <param name="база">Путь временной базы.</param>
    /// <param name="ошибки">Список ошибок cleanup.</param>
    private static void УдалитьБазуОбязательно(string база, List<Exception> ошибки)
    {
        try
        {
            for (var попытка = 0; попытка < 5; попытка++)
            {
                if (!СуществуетПуть(база))
                {
                    break;
                }

                try
                {
                    Directory.Delete(база, recursive: true);
                }
                catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
                {
                    // Windows нередко отдаёт IOException на первом
                    // рекурсивном удалении — повторяем в рамках бюджета.
                    Thread.Sleep(200);
                }
            }

            if (СуществуетПуть(база))
            {
                throw new InvalidOperationException("временная база не удалена: " + база);
            }
        }
        catch (Exception ошибка)
        {
            ошибки.Add(new InvalidOperationException("cleanup базы не удался: " + база, ошибка));
        }
    }

    /// <summary>
    /// Итог reparse-теста: ошибка тела и ошибки cleanup не маскируют друг
    /// друга — обе доводятся до жёлто-красного результата xUnit.
    /// </summary>
    /// <param name="основная">Ошибка тела теста, если была.</param>
    /// <param name="ошибкиУборки">Ошибки cleanup.</param>
    private static void СообщитьИтог(Exception? основная, List<Exception> ошибкиУборки)
    {
        if (основная is not null && ошибкиУборки.Count > 0)
        {
            throw new AggregateException(
                "Ошибка теста и ошибки cleanup reparse-ветви",
                new[] { основная }.Concat(ошибкиУборки));
        }

        if (ошибкиУборки.Count > 0)
        {
            throw new AggregateException("Ошибка cleanup reparse-ветви", ошибкиУборки);
        }

        if (основная is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(основная).Throw();
        }
    }
}
