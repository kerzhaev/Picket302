using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Picket302.Domain.Entities;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Metrics;
using Picket302.Printing.Rendering;

namespace Picket302.Printing.Devices;

/// <summary>
/// Спулер печати на системном диспетчере (ТЗ §6.1, §10.1).
/// </summary>
/// <remarks>
/// Печать выполняется средствами <c>System.Drawing.Printing</c> с передачей
/// задания в <c>winspool.drv</c>; собственный драйвер принтера не создаётся.
/// Прерывание задания выполняется .NET: обработчик PrintPage выставляет
/// <c>событие.Cancel</c>, и PrintDocument прерывает активное задание
/// (AbortDoc для дескриптора активного задания); отдельный AbortDoc на
/// собственном дескрипторе принтера задание не прерывал (F-A5).
/// </remarks>
public sealed class SystemDrawingPrintSpooler : IPrintSpooler
{
    /// <summary>
    /// Настройка печатного документа перед выводом: точка автоматических
    /// проверок (PreviewPrintController вместо системного контроллера);
    /// в работе не задаётся.
    /// </summary>
    public Action<PrintDocument>? НастройкаДокументаДляПроверок { get; set; }

    /// <summary>
    /// Печатает задание.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="render">Отрисовка страницы.</param>
    /// <param name="cancellationToken">Токен отмены задания.</param>
    public Task PrintAsync(
        PrintJobContext context,
        Action<IGraphicsContext> render,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(render);

        using var документ = new PrintDocument();

        if (!string.IsNullOrWhiteSpace(context.Printer.PrinterName))
        {
            документ.PrinterSettings.PrinterName = context.Printer.PrinterName;
        }

        var параметрыСтраницы = документ.PrinterSettings.DefaultPageSettings;

        if (!string.IsNullOrWhiteSpace(context.PaperSizeName))
        {
            // PaperSize принимает размеры в сотых долях дюйма, а контекст
            // печати хранит миллиметры (ТЗ §6.1). Без перевода бланк C6
            // (114 x 162 мм) уходил бы в спулер как 114 x 162 сотых дюйма
            // (28,9 x 41,1 мм).
            параметрыСтраницы.PaperSize = new PaperSize(
                context.PaperSizeName,
                PrintUnits.MmToHundredthsOfInch(context.PaperWidthMm),
                PrintUnits.MmToHundredthsOfInch(context.PaperHeightMm));
        }

        параметрыСтраницы.Landscape = context.Orientation == TemplateOrientation.Landscape;

        документ.PrinterSettings.Duplex = context.Duplex == DuplexMode.Duplex
            ? Duplex.Vertical
            : Duplex.Simplex;

        // Число копий задания (ТЗ §6.5, §7.2): значение из UI фиксируется в
        // журнале печати (copy_count, §3.1.3), поэтому физический вывод обязан
        // ему соответствовать. Драйверу передаётся счётчик копий; диапазон
        // спулера Windows — 1…999.
        if (context.Copies > 1)
        {
            документ.PrinterSettings.Copies = (short)Math.Clamp(context.Copies, 1, 999);
        }

        документ.DefaultPageSettings = параметрыСтраницы;

        // Точка автоматических проверок: замена контроллера вывода.
        НастройкаДокументаДляПроверок?.Invoke(документ);

        // Отбор страниц задания по чётности (ТЗ §8.6, функция 87): при
        // двухпроходной печати первый проход выводит нечётные страницы,
        // второй — чётные. Выводятся только отобранные страницы: событие
        // PrintPage для пропущенной страницы выводило бы пустой лист (F-A4).
        var очередь = new PageParityFilter()
            .FilterPageNumbers(context.TotalPages, context.Parity)
            .OrderBy(x => x)
            .ToList();

        if (очередь.Count == 0)
        {
            // Выводить нечего: вызов Print() создал бы пустое задание.
            return Task.CompletedTask;
        }

        var показанных = 0;

        документ.PrintPage += (_, событие) =>
        {
            // Прерывание выполняется .NET: Cancel завершает задание.
            if (cancellationToken.IsCancellationRequested)
            {
                событие.Cancel = true;
                return;
            }

            if (показанных >= очередь.Count)
            {
                событие.HasMorePages = false;
                return;
            }

            if (событие.Graphics is not { } графикаGdi)
            {
                событие.Cancel = true;
                return;
            }

            context.CurrentPage = очередь[показанных];

            var графика = new GdiGraphicsContext(графикаGdi);
            render(графика);

            показанных++;
            событие.HasMorePages = показанных < очередь.Count;
        };

        документ.Print();

        // Задание, прерванное через PrintPage.Cancel, не считается успехом:
        // отмена отражается исключением для контроллера сессии (F-A5).
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
