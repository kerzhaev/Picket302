using Picket302.Domain.Entities;
using Picket302.Printing.Audit;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка журналирования параметров печати (ТЗ §6.5; §8.6, функция 88:
/// <c>Printing.Audit.PrintAuditWriter</c>).
/// </summary>
public sealed class PrintAuditWriterTests
{
    private static PrintJobRecord Запись() =>
        new()
        {
            PrinterName = "HP LaserJet Pro M402dn",
            PaperWidthMm = 229,
            PaperHeightMm = 162,
            PaperSizeName = "C5",
            PaperSource = "Tray 1",
            Duplex = "Simplex",
            TemplateName = "Envelope_C5_Custom.lbk",
            Orientation = TemplateOrientation.Landscape,
            FontName = "Tahoma",
            FontSizePt = 9.0m,
            IndexFontName = "ZIPcode",
            IndexLineWidthMm = 0.45m,
            OffsetXMm = 0m,
            OffsetYMm = 1.5m,
            ObjectCount = 12,
            PageCount = 1,
            CopyCount = 150,
            Status = PrintJobStatus.Success,
            Duration = TimeSpan.FromSeconds(4.2),
        };

    [Fact]
    public void FormatRecord_УспешнаяСессия_СоответствуетФорматуСпецификации()
    {
        var ожидается = string.Join(
            "\n",
            "------------- Вывод на принтер: HP LaserJet Pro M402dn",
            "------------- Параметры принтера: paper=229x162mm, source=Tray 1, duplex=Simplex",
            "Шаблон: Envelope_C5_Custom.lbk, форма 229x162 мм, ориентация Landscape",
            "Имя шрифта: Tahoma, Размер шрифта: 9.0pt, Шрифт индекса: ZIPcode (0.45mm)",
            "Смещение: X=0.00мм Y=1.50мм",
            "Объектов: 12, страниц: 1, копий: 150",
            "Статус: УСПЕШНО, Затрачено времени: 4.2с",
            "-------------");

        Assert.Equal(ожидается, PrintAuditWriter.FormatRecord(Запись()));
    }

    [Theory]
    [InlineData(PrintJobStatus.Success, "УСПЕШНО")]
    [InlineData(PrintJobStatus.Aborted, "ПРЕРВАНО")]
    [InlineData(PrintJobStatus.Failed, "ОШИБКА")]
    [InlineData(PrintJobStatus.BlockedSize, "ЗАБЛОКИРОВАНО")]
    public void FormatRecord_ИтогСессии_ОтображаетсяНаРусском(string статус, string ожидается)
    {
        var текст = PrintAuditWriter.FormatRecord(Запись() with { Status = статус });

        Assert.Contains($"Статус: {ожидается}", текст);
    }

    [Fact]
    public void WriteSessionRecord_ПишетФайлАудита()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302-аудит-" + Guid.NewGuid().ToString("N"));

        try
        {
            var журналист = new PrintAuditWriter(каталог);

            журналист.WriteSessionRecord(Запись());

            var файл = Path.Combine(каталог, PrintAuditWriter.FileName);
            Assert.True(File.Exists(файл));

            var содержимое = File.ReadAllText(файл);
            Assert.Contains("Вывод на принтер: HP LaserJet Pro M402dn", содержимое);
            Assert.Contains("Статус: УСПЕШНО", содержимое);
        }
        finally
        {
            if (Directory.Exists(каталог))
            {
                Directory.Delete(каталог, true);
            }
        }
    }

    [Fact]
    public void WriteSessionRecord_СбойЗаписи_ВыбрасываетИсключениеАудита()
    {
        var преграда = Path.Combine(Path.GetTempPath(), "picket302-" + Guid.NewGuid().ToString("N"));

        File.WriteAllText(преграда, "не каталог");

        try
        {
            var журналист = new PrintAuditWriter(преграда);

            Assert.Throws<PrintAuditException>(() => журналист.WriteSessionRecord(Запись()));
        }
        finally
        {
            File.Delete(преграда);
        }
    }
}
