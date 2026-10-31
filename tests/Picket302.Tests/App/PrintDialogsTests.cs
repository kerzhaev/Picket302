using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Domain.Entities;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка диалогов печати: учёт опции «Показывать список шаблонов перед
/// печатью», кнопки «Настройки принтера», флага подавления предупреждения о
/// разлиновке и диалогов проверки размера бумаги (ТЗ §8.6, функции 78, 82,
/// 83, 89).
/// </summary>
public sealed class PrintDialogsTests
{
    private static PrintTemplate Шаблон(long id, string имя) => new()
    {
        Id = id,
        Name = имя,
        Kind = PrintTemplateKind.Envelope,
        Format = "C6",
        WidthMm = 324m,
        HeightMm = 229m,
    };

    private static readonly PrinterDescriptor Принтер = new(
        "Microsoft Print to PDF",
        "PORTPROMPT:",
        PrinterState.Ready,
        IsDefault: true);

    private static ComboBox ПереченьШаблонов(FormEnvelopePrint форма) =>
        форма.Controls
            .OfType<ComboBox>()
            .Single(перечень => перечень.Items.OfType<string>().Any(текст => текст.Contains("Конверт", StringComparison.Ordinal)));

    private static CheckBox Флажок(FormEnvelopePrint форма, string текст) =>
        форма.Controls.OfType<CheckBox>().Single(флажок => флажок.Text == текст);

    [Fact]
    public void FormEnvelopePrint_ОпцияВключена_ПереченьШаблоновПоказывается()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        Assert.True(форма.ПоказатьСписокШаблонов);
        Assert.True(форма.ПереченьШаблоновПоказывается);
        Assert.Equal("Конверт C6", форма.ВыбранныйШаблон?.Name);
    }

    [Fact]
    public void FormEnvelopePrint_ОпцияВыключена_ПереченьШаблоновНеПоказывается()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: false);

        Assert.False(форма.ПоказатьСписокШаблонов);
        Assert.False(форма.ПереченьШаблоновПоказывается);
    }

    [Fact]
    public void FormEnvelopePrint_ОпцияВыключена_ПрименяетсяПоследнийШаблон()
    {
        var последний = Шаблон(2, "Конверт DL");

        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6"), последний },
            new[] { Принтер },
            показатьСписокШаблонов: false,
            последнийШаблон: последний);

        Assert.NotNull(форма.ВыбранныйШаблон);
        Assert.Equal(последний.Id, форма.ВыбранныйШаблон!.Id);

        // Перечень остаётся заполненным: скрывается только его показ.
        Assert.Equal(2, ПереченьШаблонов(форма).Items.Count);
    }

    [Fact]
    public void FormEnvelopePrint_ОпцияВыключена_БезПоследнегоШаблона_БерётсяПервый()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6"), Шаблон(2, "Конверт DL") },
            new[] { Принтер },
            показатьСписокШаблонов: false);

        Assert.NotNull(форма.ВыбранныйШаблон);
        Assert.Equal(1, форма.ВыбранныйШаблон!.Id);
    }

    [Fact]
    public void FormEnvelopePrint_СодержитКнопкуНастроекПринтера()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        Assert.Contains(
            форма.Controls.OfType<Button>(),
            кнопка => кнопка.Text == "Настройки принтера");
    }

    [Fact]
    public void FormEnvelopePrint_ФлажокПодавленияВиденТолькоВРежимеРазлиновки()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        var флажокРазлиновки = Флажок(форма, "Размеченный типографский конверт");
        var флажокПодавления = Флажок(форма, PreprintedMode.SuppressCaption);

        Assert.False(форма.ПредупреждениеРазлиновкиПоказывается);
        Assert.False(флажокПодавления.Checked);

        флажокРазлиновки.Checked = true;

        Assert.True(форма.ПредупреждениеРазлиновкиПоказывается);
        Assert.False(форма.ПодавитьПредупреждениеРазлиновки);

        флажокПодавления.Checked = true;

        Assert.True(форма.ПодавитьПредупреждениеРазлиновки);
    }

    [Fact]
    public void FormEnvelopePrint_ПредупреждениеРазлиновки_СкрытоПриПодавленномСообщении()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true,
            показыватьПредупреждениеРазлиновки: false);

        var флажокРазлиновки = Флажок(форма, "Размеченный типографский конверт");

        флажокРазлиновки.Checked = true;

        Assert.False(форма.ПредупреждениеРазлиновкиПоказывается);
    }

    [Fact]
    public void FormPaperSizeBlocked_СодержитТриКнопкиПоСпецификации()
    {
        using var форма = new FormPaperSizeBlocked(new SizeF(324f, 229f), new SizeF(210f, 297f), "Microsoft Print to PDF");

        var надписи = форма.Controls.OfType<Button>().Select(кнопка => кнопка.Text).ToArray();

        Assert.Contains("Отмена", надписи);
        Assert.Contains("Зафиксировать A4", надписи);
        Assert.Contains("Выбрать другой принтер", надписи);
        Assert.Equal(PaperSizeBlockedDecision.Отмена, форма.Решение);
    }

    [Fact]
    public void FormPaperSizeBlocked_ПоказываетОбаРазмераВМиллиметрах()
    {
        using var форма = new FormPaperSizeBlocked(new SizeF(324f, 229f), new SizeF(210f, 297f), "Microsoft Print to PDF");

        var текст = string.Join(
            " ",
            форма.Controls.OfType<Label>().Select(надпись => надпись.Text));

        Assert.Contains("324", текст);
        Assert.Contains("229", текст);
        Assert.Contains("210", текст);
        Assert.Contains("297", текст);
        Assert.Contains("мм", текст);
    }

    [Fact]
    public void OversizeWarningPresenter_ПустойСписокФорматов_СообщаетОбЭтомЯвно()
    {
        var показатель = new ТестовыйПоказатель();

        var предупреждение = new Picket302.Printing.Validation.OversizeWarning(показатель, Array.Empty<string>());

        предупреждение.ShowOversizeDialog(new SizeF(324f, 229f), new SizeF(210f, 297f));

        Assert.NotNull(показатель.Сообщение);
        Assert.Contains("Конверт слишком большой для выбранного принтера", показатель.Сообщение!);
        Assert.Contains("324", показатель.Сообщение!);
        Assert.Contains("229", показатель.Сообщение!);
        Assert.Contains("210", показатель.Сообщение!);
        Assert.Contains("297", показатель.Сообщение!);

        // Перечень форматов пуст — окно сообщает об этом прямо.
        var текст = new OversizeWarningPresenter(Array.Empty<string>()).СобратьТекст(показатель.Сообщение!);

        Assert.Contains("Список доступных форматов принтера пуст", текст);
    }

    [Fact]
    public void OversizeWarningPresenter_ЕстьФорматы_ПеречисляетИх()
    {
        var показатель = new ТестовыйПоказатель();

        var предупреждение = new Picket302.Printing.Validation.OversizeWarning(показатель, new[] { "A4", "DL" });

        предупреждение.ShowOversizeDialog(new SizeF(324f, 229f), new SizeF(210f, 297f));

        Assert.NotNull(показатель.Сообщение);
        Assert.Contains("Доступные форматы принтера: A4, DL", показатель.Сообщение!);

        var текст = new OversizeWarningPresenter(new[] { "A4", "DL" }).СобратьТекст(показатель.Сообщение!);

        Assert.DoesNotContain("Список доступных форматов принтера пуст", текст);
    }

    [Fact]
    public void FormOversizeWarning_ПоказываетТекстПредупреждения()
    {
        using var форма = new FormOversizeWarning("Конверт слишком большой для выбранного принтера. Размер бланка: 324×229 мм.");

        var текст = string.Join(" ", форма.Controls.OfType<Label>().Select(надпись => надпись.Text));

        Assert.Contains("Конверт слишком большой для выбранного принтера", текст);
        Assert.Contains("324", текст);
    }

    /// <summary>Показатель предупреждения, запоминающий текст сообщения.</summary>
    private sealed class ТестовыйПоказатель : Picket302.Printing.Validation.IOversizeWarningPresenter
    {
        public string? Сообщение { get; private set; }

        public void Show(string message)
        {
            Сообщение = message;
        }
    }
}
