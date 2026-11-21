using Picket302.Domain.Serialization;
using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Импорт макетов исходной программы (файлы .fr3) в нашу модель бланка.
/// </summary>
/// <remarks>
/// Импорт переносит готовую раскладку настоящих бланков: размеры страницы,
/// поля с привязкой к данным, гарнитуры и поворот текста (на конвертах адрес
/// печатается вертикальной колонкой, снизу вверх).
/// </remarks>
public sealed class Fr3BlankImporterTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "Picket302Fr3_" + Guid.NewGuid().ToString("N"));

    public Fr3BlankImporterTests() => Directory.CreateDirectory(_каталог);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_каталог, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог проверки не критичен.
        }
    }

    /// <summary>
    /// Макет в формате FastReport: страница, полоса и поля с привязками.
    /// </summary>
    private string ЗаписатьМакет(
        string paperWidth = "111",
        string paperHeight = "159.9",
        string bandLeft = "0",
        string bandTop = "16",
        string memo = """
            <TfrxMemoView Name="Memo1" Left="220" Top="-3" Width="20" Height="262" Font.Name="Arial" Rotation="90" Text="[edt_str_to1]" />
            """)
    {
        var путь = Path.Combine(_каталог, "Конверт.fr3");

        var содержимое = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TfrxReport ReportOptions.Name="Конверт_C6">
              <TfrxReportPage Name="Page1" PaperWidth="{paperWidth}" PaperHeight="{paperHeight}">
                <TfrxPageHeader Name="PageHeader1" Left="{bandLeft}" Top="{bandTop}" Width="419" Height="558">
                  {memo}
                  <TfrxZipCodeView Name="ZipCode2" Left="172" Top="332" Width="158" Height="40" />
                </TfrxPageHeader>
              </TfrxReportPage>
            </TfrxReport>
            """;

        File.WriteAllText(путь, содержимое, new System.Text.UTF8Encoding(false));

        return путь;
    }

    [Fact]
    public void Импорт_Переносит_Размеры_Страницы()
    {
        var бланк = Fr3BlankImporter.Import(ЗаписатьМакет());

        Assert.Equal(111m, бланк.Geometry.WidthMm);
        Assert.Equal(159.9m, бланк.Geometry.HeightMm);
        Assert.Equal(BlankOrientation.Portrait, бланк.Geometry.Orientation);
    }

    [Fact]
    public void Импорт_Переносит_Поле_С_Тегом_И_Координатами()
    {
        var бланк = Fr3BlankImporter.Import(ЗаписатьМакет());

        var поле = Assert.Single(бланк.AllFields(), п => п.Name == "[edt_str_to1]");

        // координаты FastReport пересчитываются в миллиметры (1/96 дюйма),
        // смещение полосы прибавляется
        Assert.Equal(220m * 25.4m / 96m, поле.Bounds.XMm, 2);
        Assert.Equal((16m - 3m) * 25.4m / 96m, поле.Bounds.YMm, 2);
        Assert.Equal(BlankFieldKinds.Text, поле.Kind);
    }

    [Fact]
    public void Импорт_Переносит_Поворот_Текста()
    {
        var бланк = Fr3BlankImporter.Import(ЗаписатьМакет());

        var поле = Assert.Single(бланк.AllFields(), п => п.Name == "[edt_str_to1]");

        Assert.Equal(90, поле.Rotation);
    }

    [Fact]
    public void Импорт_Переносит_Кодовый_Штамп()
    {
        var бланк = Fr3BlankImporter.Import(ЗаписатьМакет());

        var штамп = Assert.Single(бланк.AllFields(), п => п.Kind == BlankFieldKinds.PostIndex);

        Assert.Equal("ZIPcode", штамп.FontName);
        Assert.True(штамп.Printable);
    }

    [Fact]
    public void Импорт_Файла_Без_Полей_Даёт_Понятную_Ошибку()
    {
        var путь = Path.Combine(_каталог, "Пустой.fr3");

        File.WriteAllText(
            путь,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><TfrxReport><TfrxReportPage Name=\"Page1\" PaperWidth=\"111\" PaperHeight=\"160\" /></TfrxReport>",
            new System.Text.UTF8Encoding(false));

        var ошибка = Assert.Throws<InvalidDataException>(() => Fr3BlankImporter.Import(путь));

        Assert.Contains("поля", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Импорт_Отсутствующего_Файла_Даёт_Понятную_Ошибку()
    {
        var путь = Path.Combine(_каталог, "НетТакого.fr3");

        Assert.Throws<FileNotFoundException>(() => Fr3BlankImporter.Import(путь));
    }
}
