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
/// Прерывание задания выполняется вызовом <c>AbortDoc</c> (ТЗ §6.1).
/// </remarks>
public sealed class SystemDrawingPrintSpooler : IPrintSpooler
{
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

        using var прерыватель = new AbortScope(context.Printer.PrinterName, cancellationToken);

        // Отбор страниц задания по чётности (ТЗ §8.6, функция 87): при
        // двухпроходной печати первый проход выводит нечётные страницы,
        // второй — чётные.
        var отобранныеСтраницы = new PageParityFilter()
            .FilterPageNumbers(context.TotalPages, context.Parity)
            .ToHashSet();

        var номерСтраницы = 0;

        документ.PrintPage += (_, событие) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                событие.Cancel = true;
                return;
            }

            номерСтраницы++;

            var последняя = номерСтраницы >= context.TotalPages;

            if (!отобранныеСтраницы.Contains(номерСтраницы))
            {
                событие.HasMorePages = !последняя;
                return;
            }

            if (событие.Graphics is not { } графикаGdi)
            {
                событие.Cancel = true;
                return;
            }

            context.CurrentPage = номерСтраницы;

            var графика = new GdiGraphicsContext(графикаGdi);
            render(графика);
            событие.HasMorePages = !последняя;
        };

        документ.Print();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Область прерывания задания: по требованию отмены вызывается
    /// <c>AbortDoc</c> для принтера (ТЗ §6.1).
    /// </summary>
    private sealed class AbortScope : IDisposable
    {
        private readonly IntPtr _дескриптор;
        private readonly CancellationTokenRegistration _регистрация;

        public AbortScope(string printerName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return;
            }

            if (!Winspool.OpenPrinterW(printerName, out var дескриптор, IntPtr.Zero))
            {
                return;
            }

            _дескриптор = дескриптор;
            _регистрация = cancellationToken.Register(() => Winspool.AbortDoc(_дескриптор));
        }

        public void Dispose()
        {
            _регистрация.Dispose();

            if (_дескриптор != IntPtr.Zero)
            {
                Winspool.ClosePrinter(_дескриптор);
            }
        }
    }
}
