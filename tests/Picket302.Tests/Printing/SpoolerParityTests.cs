using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using System.Drawing.Printing;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессии F-A4 и F-A5 спулера печати.
///
/// F-A4 — при фильтре чётности пропущенные страницы прогонялись через событие
/// PrintPage и уходили в спулер пустыми листами; выводится только отобранная
/// очередь страниц.
/// F-A5 — отмена задания фиксировалась как успех: спулер прерывался через
/// AbortDoc на отдельном дескрипторе (не действовал), отмена теперь приводит
/// к OperationCanceledException из PrintAsync.
/// </summary>
public sealed class SpoolerParityTests
{
    /// <summary>
    /// Контроллер предпросмотра: вывод страниц без системного задания печати.
    /// </summary>
    private static void НастроитьПредпросмотр(PrintDocument документ)
        => документ.PrintController = new PreviewPrintController();

    [Fact]
    public async Task PrintAsync_НечётныеСтраницы_ВыводятсяТолькоОтобранные()
    {
        var выведенные = new List<int>();
        var спулер = new SystemDrawingPrintSpooler
        {
            НастройкаДокументаДляПроверок = НастроитьПредпросмотр,
        };

        var контекст = new PrintJobContext
        {
            TotalPages = 4,
            Parity = PageParity.OddOnly,
        };

        await спулер.PrintAsync(
            контекст,
            _ => выведенные.Add(контекст.CurrentPage),
            CancellationToken.None);

        Assert.Equal([1, 3], выведенные);
    }

    [Fact]
    public async Task PrintAsync_ЧётныеСтраницы_ВыводятсяТолькоОтобранные()
    {
        var выведенные = new List<int>();
        var спулер = new SystemDrawingPrintSpooler
        {
            НастройкаДокументаДляПроверок = НастроитьПредпросмотр,
        };

        var контекст = new PrintJobContext
        {
            TotalPages = 4,
            Parity = PageParity.EvenOnly,
        };

        await спулер.PrintAsync(
            контекст,
            _ => выведенные.Add(контекст.CurrentPage),
            CancellationToken.None);

        Assert.Equal([2, 4], выведенные);
    }

    [Fact]
    public async Task PrintAsync_ОтменаДоВывода_ВозвращаетОперациюОтменённой()
    {
        var спулер = new SystemDrawingPrintSpooler
        {
            НастройкаДокументаДляПроверок = НастроитьПредпросмотр,
        };

        var контекст = new PrintJobContext
        {
            TotalPages = 2,
            Parity = PageParity.All,
        };

        using var источник = new CancellationTokenSource();

        await источник.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            спулер.PrintAsync(контекст, _ => { }, источник.Token));
    }

    [Fact]
    public async Task PrintAsync_ПустаяОчередьЧётности_ВыводНеВыполняется()
    {
        var вызовов = 0;
        var спулер = new SystemDrawingPrintSpooler
        {
            НастройкаДокументаДляПроверок = документ => документ.PrintPage += (_, _) => вызовов++,
        };

        var контекст = new PrintJobContext
        {
            TotalPages = 0,
            Parity = PageParity.EvenOnly,
        };

        await спулер.PrintAsync(контекст, _ => { }, CancellationToken.None);

        Assert.Equal(0, вызовов);
    }
}
