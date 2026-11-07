using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Гарантированное постусловие чистоты reparse-тестов гарда: teardown
/// фикстуры коллекции <c>ИнфраструктурнаяУборка</c> выполняется после
/// ВСЕХ тестов коллекции независимо от порядка классов — имя класса с
/// префиксом «Z» жизненным циклом не является.
/// </summary>
/// <remarks>
/// Проверка выполняется под межпроцессной блокировкой: она не гоняется с
/// reparse-тестами другого testhost-процесса. Остатки
/// <c>P302GuardTest_*</c> попадают в диагностику ПОЛНЫМ списком до
/// удаления; ошибка teardown делает прогон красным и попадает в отчёт
/// xUnit. Проверяется только точный префикс <c>P302GuardTest_*</c> —
/// чужие каталоги не трогаются.
/// </remarks>
public sealed class ПостусловиеЧистотыGuard : IDisposable
{
    /// <summary>Шаблон имён каталогов reparse-тестов гарда.</summary>
    private const string ШаблонОстатков = "P302GuardTest_*";

    /// <inheritdoc />
    public void Dispose() => ПроверитьЧистоту();

    /// <summary>
    /// Проверяет отсутствие каталогов <c>P302GuardTest_*</c> в %TEMP% под
    /// межпроцессной блокировкой. Остатки фиксируются полным списком в
    /// диагностику до удаления, затем удаляются (после записи в
    /// диагностику), а само постусловие бросает исключение — teardown
    /// красный.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Остались каталоги <c>P302GuardTest_*</c> (со полным списком и,
    /// при наличии, ошибками уборки остатков).
    /// </exception>
    public static void ПроверитьЧистоту()
    {
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var остатки = Directory.GetDirectories(Path.GetTempPath(), ШаблонОстатков);

        if (остатки.Length == 0)
        {
            return;
        }

        // Полный список остатков — в диагностику ДО удаления.
        var список = string.Join("; ", остатки);
        Console.Error.WriteLine("ПостусловиеЧистотыGuard: остались каталоги " + ШаблонОстатков + ": " + список);

        var ошибкиУборки = new List<Exception>();

        foreach (var остаток in остатки)
        {
            try
            {
                Directory.Delete(остаток, recursive: true);

                if (Directory.Exists(остаток))
                {
                    ошибкиУборки.Add(new InvalidOperationException("каталог не удалён: " + остаток));
                }
            }
            catch (Exception ошибка)
            {
                ошибкиУборки.Add(ошибка);
            }
        }

        throw new InvalidOperationException(
            "Постусловие чистоты коллекции: после reparse-тестов гарда остались каталоги "
            + ШаблонОстатков + ": " + список
            + (ошибкиУборки.Count > 0
                ? "; ошибки уборки остатков: " + string.Join(" | ", ошибкиУборки.Select(о => о.Message))
                : string.Empty));
    }
}
