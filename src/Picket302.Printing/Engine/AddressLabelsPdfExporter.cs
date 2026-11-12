using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using Picket302.Printing.Metrics;
using Picket302.Domain.Entities;


namespace Picket302.Printing.Engine;

/// <summary>
/// Экспорт раскладки адресных этикеток (ТЗ §8.4, функция 146) в PDF:
/// листы A5/A4/A3 заполняются этикетками из <see cref="SheetLayoutResult"/>,
/// каждая этикетка несёт индекс и адресные строки получателя.
/// Печать выполняется в файл через PrintToFile штатного драйвера
/// «Microsoft Print to PDF» — без интерактивных портов и прав админа.
/// </summary>
public sealed class AddressLabelsPdfExporter
{
    /// <summary>Виртуальный принтер по умолчанию для PDF-экспорта.</summary>
    public const string DefaultVirtualPrinter = "Microsoft Print to PDF";

    /// <summary>Одна этикетка: индекс + адресные строки.</summary>
    public sealed record Label(string ZipCode, IReadOnlyList<string> AddressLines);

    /// <summary>
    /// Экспортирует этикетки в PDF-файл. Возвращает число страниц.
    /// </summary>
    /// <param name="labels">Этикетки в порядке заполнения.</param>
    /// <param name="sheet">Размер листа (A5/A4/A3).</param>
    /// <param name="labelWidthMm">Ширина этикетки, мм.</param>
    /// <param name="labelHeightMm">Высота этикетки, мм.</param>
    /// <param name="outputFile">Полный путь к PDF-файлу.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число страниц полученного файла.</returns>
    public int Export(
        IReadOnlyList<Label> labels,
        SheetSize sheet,
        decimal labelWidthMm,
        decimal labelHeightMm,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        if (labels.Count == 0)
        {
            throw new ArgumentException("Список этикеток пуст.", nameof(labels));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var контекст = new PrintJobContext
        {
            Blank = new BlankMetrics
            {
                TemplateName = "Адресные этикетки",
                WidthMm = labelWidthMm,
                HeightMm = labelHeightMm,
                Orientation = TemplateOrientation.Portrait,
            },
            LabelWidthMm = labelWidthMm,
            LabelHeightMm = labelHeightMm,
            Orientation = TemplateOrientation.Portrait,
        };

        var раскладка = new SheetLayoutImposition().LayoutGridOnSheet(
            контекст, rows: 0, cols: 0, new Margins(5m, 5m, 5m, 5m), sheet);

        var (ширинаЛиста, высотаЛиста) = SheetLayoutImposition.РазмерыЛиста(sheet, TemplateOrientation.Portrait);
        var ширинаPt = MmToHundredthsInch(ширинаЛиста);
        var высотаPt = MmToHundredthsInch(высотаЛиста);

        using var документ = new PrintDocument();
        документ.PrinterSettings.PrinterName = DefaultVirtualPrinter;
        документ.PrinterSettings.PrintToFile = true;
        документ.PrinterSettings.PrintFileName = outputFile;
        документ.DocumentName = "PICKET302-ADDRESS-LABELS";
        документ.DefaultPageSettings.PaperSize = new PaperSize(
            sheet.ToString(),
            (int)Math.Round(ширинаPt),
            (int)Math.Round(высотаPt));
        документ.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(0, 0, 0, 0);

        var этикеткиНаЛисте = Math.Max(1, раскладка.НаклеекНаЛисте);
        var всегоСтраниц = (labels.Count + этикеткиНаЛисте - 1) / этикеткиНаЛисте;
        var текущаяСтраница = 0;

        документ.PrintPage += (_, e) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (e.Graphics is null || текущаяСтраница >= всегоСтраниц)
            {
                e.HasMorePages = false;
                return;
            }

            var смещение = текущаяСтраница * этикеткиНаЛисте;

            using var шрифтИндекса = new Font("Consolas", 14f, FontStyle.Bold);
            using var шрифтАдреса = new Font("Arial", 10f);
            using var перо = new Pen(Color.LightGray);

            for (var позиция = 0; позиция < этикеткиНаЛисте; позиция++)
            {
                var индексЭтикетки = смещение + позиция;
                if (индексЭтикетки >= labels.Count)
                {
                    break;
                }

                var координаты = раскладка.Позиции[позиция];
                var x = MmToHundredthsInch(координаты.X);
                var y = MmToHundredthsInch(координаты.Y);
                var ширина = MmToHundredthsInch(координаты.Width);
                var высота = MmToHundredthsInch(координаты.Height);

                e.Graphics.DrawRectangle(перо, x, y, ширина, высота);

                var этикетка = labels[индексЭтикетки];
                e.Graphics.DrawString(
                    этикетка.ZipCode,
                    шрифтИндекса,
                    Brushes.Black,
                    x + 6,
                    y + 6);

                var yСтроки = y + 6 + 22;
                foreach (var строка in этикетка.AddressLines)
                {
                    if (string.IsNullOrWhiteSpace(строка))
                    {
                        continue;
                    }

                    e.Graphics.DrawString(строка, шрифтАдреса, Brushes.Black, x + 6, yСтроки);
                    yСтроки += 16;
                }
            }

            текущаяСтраница++;
            e.HasMorePages = текущаяСтраница < всегоСтраниц;
        };

        // Отмена поддерживается: ThrowIfCancellationRequested в каждой
        // странице; исключение OperationCanceledException сигнализирует
        // вызывающему про отмену до записи файла.
        документ.Print();

        // Ожидание материализации файла (спулер асинхронен).
        for (var попытка = 0; попытка < 40; попытка++)
        {
            Thread.Sleep(250);
            if (File.Exists(outputFile) && new FileInfo(outputFile).Length > 0)
            {
                Thread.Sleep(500);
                return всегоСтраниц;
            }
        }

        throw new InvalidOperationException(
            $"PDF-файл не создан спулером за отведённое время: {outputFile}");
    }

    private static float MmToHundredthsInch(decimal мм) =>
        (float)(мм / 25.4m * 100m);

    private static float MmToHundredthsInch(double мм) =>
        (float)(мм / 25.4 * 100);
}
