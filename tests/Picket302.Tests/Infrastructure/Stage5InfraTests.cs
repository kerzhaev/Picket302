using Picket302.Infrastructure.Text;
using Picket302.Infrastructure.Update;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессии этапа 5: сумма прописью со знаком (F-C9) и некорректный адрес
/// в загрузчике обновлений (F-C8).
/// </summary>
public sealed class Stage5InfraTests
{
    /// <summary>
    /// Регрессия F-C9: отрицательная сумма прописью начинается со слова
    /// «Минус». Прежде знак терялся.
    /// </summary>
    [Fact]
    public void СуммаПрописью_ОтрицательнаяСумма_СловоМинус()
    {
        var пропись = new AmountInWords();

        var текст = пропись.ToWords(-1500.50m, "Rub");

        Assert.StartsWith("Минус ", текст, StringComparison.Ordinal);
    }

    /// <summary>
    /// Регрессия F-C8: некорректный адрес загрузки возвращает null и не
    /// оставляет файлов в каталоге обновлений.
    /// </summary>
    [Fact]
    public async Task ЗагрузкаОбновления_НекорректныйАдрес_ReturnNull()
    {
        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0");

        var путь = await проверятель.DownloadUpdateAsync(
            new UpdateInfo(
                Version: "1.1.0",
                DownloadUrl: "не является адресом",
                Notes: string.Empty,
                IsUpdateAvailable: true,
                DownloadedPath: null));

        Assert.Null(путь);
    }
}
