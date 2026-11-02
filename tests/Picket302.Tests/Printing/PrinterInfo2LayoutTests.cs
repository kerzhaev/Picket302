using System.Reflection;
using System.Runtime.InteropServices;
using Picket302.Printing.Devices;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессии F-A1 и F-A6: разметка <c>PRINTER_INFO_2</c> и значения
/// констант winspool.h.
///
/// F-A1 — прежде в структуре отсутствовали <c>Parameters</c> и
/// <c>SecurityDescriptor</c>: при перечислении принтеров все поля после
/// <c>Datatype</c> (в том числе <c>Status</c> и <c>Attributes</c>) читались
/// со смещением в 16 байт.
/// F-A6 — значения констант состояния и признака принтера по умолчанию
/// расходились с winspool.h.
/// </summary>
public sealed class PrinterInfo2LayoutTests
{
    /// <summary>Тип закрытой структуры PrinterInfo2 из PrinterManager.</summary>
    private static readonly Type ТипСтруктуры =
        typeof(PrinterManager).GetNestedType("PrinterInfo2", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Структура PrinterInfo2 не найдена.");

    /// <summary>Закрытый метод сопоставления статуса и состояния.</summary>
    private static readonly MethodInfo ОпределитьСостояние =
        typeof(PrinterManager).GetMethod(
            "ОпределитьСостояние",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(uint)])
        ?? throw new InvalidOperationException("Метод ОпределитьСостояние не найден.");

    [Fact]
    public void PrinterInfo2_РазмерСоответствуетТринадцатиУказателямИВосьмиDWORD()
    {
        // 13 × 8 байт (указатели на x64) + 8 × 4 байта (DWORD) = 136.
        Assert.Equal(136, Marshal.SizeOf(ТипСтруктуры));
    }

    [Theory]
    [InlineData(0x00000000, PrinterState.Ready)]
    [InlineData(0x00000080, PrinterState.Offline)]   // PRINTER_STATUS_OFFLINE
    [InlineData(0x00001000, PrinterState.Offline)]   // PRINTER_STATUS_NOT_AVAILABLE
    [InlineData(0x00000002, PrinterState.Error)]     // PRINTER_STATUS_ERROR
    [InlineData(0x00000001, PrinterState.NotReady)]  // PRINTER_STATUS_PAUSED
    [InlineData(0x00000010, PrinterState.NotReady)]  // PRINTER_STATUS_PAPEROUT
    [InlineData(0x00040000, PrinterState.NotReady)]  // PRINTER_STATUS_NO_TONER
    [InlineData(0x00000100, PrinterState.NotReady)]  // неизвестный бит — не «вне сети»
    public void ОпределитьСостояние_СопоставляетНовыеБитыWinspool(uint статус, PrinterState ожидаемое)
    {
        var состояние = (PrinterState)ОпределитьСостояние.Invoke(null, [статус])!;

        Assert.Equal(ожидаемое, состояние);
    }
}
