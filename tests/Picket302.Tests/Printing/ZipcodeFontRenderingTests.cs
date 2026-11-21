using System.Drawing;
using Picket302.Infrastructure.Fonts;
using Picket302.Tests.Packaging;
using Picket302.Printing.Elements;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия молчаливой подмены шрифта кодового штампа (регрессия 21.11.2026).
/// </summary>
/// <remarks>
/// <para>
/// Шрифт ZIPcode не виден GDI+ в перечислении установленных шрифтов, пока он
/// не зарегистрирован в системе: вызов <c>AddFontResourceW</c> возвращает
/// успех, но гарнитура в перечислении не появляется. Поэтому создание
/// <c>Font("ZIPcode")</c> молча даёт «Microsoft Sans Serif», и на конверте
/// вместо машиночитаемого индекса печатается бессмыслица.
/// </para>
/// <para>
/// Проверяется, что гард берёт шрифт из файла комплекта поставки и отдаёт
/// семейство для отрисовки, и что индекс рисуется именно этим семейством.
/// </para>
/// </remarks>
public sealed class ZipcodeFontRenderingTests
{
    /// <summary>Каталог шрифтов комплекта поставки относительно корня репозитория.</summary>
    private const string FontsRelativePath = @"src\Picket302.App\Fonts";

    /// <summary>
    /// Гард находит шрифт в файле комплекта поставки и отдаёт семейство.
    /// </summary>
    [Fact]
    public void Гард_Отдаёт_Семейство_Из_Файла_Поставки()
    {
        var каталог = Path.Combine(RepositoryRoot.Resolve(), FontsRelativePath);
        Assert.True(Directory.Exists(каталог), $"Каталог шрифтов не найден: {каталог}");

        var гард = new ZipcodeFontGuard(каталог);

        Assert.True(гард.IsZipcodeFontAvailable);
        Assert.NotNull(гард.PostIndexFontFamily);
        Assert.Equal(
            ZipcodeFontGuard.FontName,
            гард.PostIndexFontFamily!.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Шрифта нет ни в системе, ни в комплекте — печать индекса заблокирована,
    /// а не подменена другим шрифтом.
    /// </summary>
    [Fact]
    public void Без_Шрифта_Индекс_Заблокирован()
    {
        var пустой = Path.Combine(Path.GetTempPath(), "Picket302ПустойШрифтов_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(пустой);

        try
        {
            var гард = new ZipcodeFontGuard(пустой);

            // Шрифт может быть установлен в системе на машине разработчика —
            // тогда проверка неприменима и пропускается честно.
            var семейство = гард.PostIndexFontFamily;

            if (семейство is not null)
            {
                Assert.Equal(
                    ZipcodeFontGuard.FontName,
                    семейство.Name,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var проверка = new PostIndexRenderer(гард).Validate(new PostIndexSpec());

                Assert.True(проверка.IsBlocked);
                Assert.Contains(
                    проверка.Warnings,
                    текст => текст.Contains("ZIPcode"));
            }
        }
        finally
        {
            Directory.Delete(пустой, recursive: true);
        }
    }

    /// <summary>
    /// Индекс рисуется семейством, полученным от гарда, а не поиском гарнитуры
    /// по имени: контекст фиксирует имя семейства.
    /// </summary>
    [Fact]
    public void Индекс_Рисуется_Семейством_От_Гарда()
    {
        var контекст = new ФиксирующийКонтекст();
        var гард = new ZipcodeFontGuard(Path.Combine(RepositoryRoot.Resolve(), FontsRelativePath));

        new PostIndexRenderer(гард).RenderMachineReadableIndex(контекст, "101000", new PostIndexSpec());

        Assert.Equal(6, контекст.Вызовы.Count);
        Assert.All(контекст.Вызовы, вызов =>
            Assert.Equal(ZipcodeFontGuard.FontName, вызов.Шрифт, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Контекст, фиксирующий имя семейства при отрисовке.</summary>
    private sealed class ФиксирующийКонтекст : IGraphicsContext
    {
        public List<(string Текст, string Шрифт)> Вызовы { get; } = new();

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(string text, string fontName, float widthMm, float heightMm, float xMm, float yMm)
        {
            Вызовы.Add((text, fontName));
        }

        public void DrawStringMm(string text, FontFamily family, float widthMm, float heightMm, float xMm, float yMm)
        {
            Вызовы.Add((text, family.Name));
        }

        public void DrawStringMm(string text, string fontName, float sizePt, bool bold, float xMm, float yMm)
        {
        }
    }
}
