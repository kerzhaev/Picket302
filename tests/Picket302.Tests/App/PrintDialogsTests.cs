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

    /// <summary>
    /// Обходит форму рекурсивно: после перевода диалога на TableLayoutPanel
    /// поля лежат во вложенных контейнерах (редизайн 2026-11-05, этап 3).
    /// </summary>
    private static IEnumerable<Control> Обойти(Control контейнер)
    {
        foreach (Control элемент in контейнер.Controls)
        {
            yield return элемент;

            foreach (var вложенный in Обойти(элемент))
            {
                yield return вложенный;
            }
        }
    }

    private static ComboBox ПереченьШаблонов(FormEnvelopePrint форма) =>
        Обойти(форма)
            .OfType<ComboBox>()
            .Single(перечень => перечень.Items.OfType<string>().Any(текст => текст.Contains("Конверт", StringComparison.Ordinal)));

    private static CheckBox Флажок(FormEnvelopePrint форма, string текст) =>
        Обойти(форма).OfType<CheckBox>().Single(флажок => флажок.Text == текст);

    /// <summary>Проверяет, что поле лежит в таблице компоновки среди предков.</summary>
    private static bool ПредокТаблица(Control поле)
    {
        for (var родитель = поле.Parent; родитель is not null; родитель = родитель.Parent)
        {
            if (родитель is TableLayoutPanel)
            {
                return true;
            }
        }

        return false;
    }

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
            Обойти(форма).OfType<Button>(),
            кнопка => кнопка.Text == "Настройки принтера");
    }

    /// <summary>
    /// Пустой список принтеров не вызывает исключение: поле называет
    /// состояние, кнопка настроек недоступна, запрос формируется с пустым
    /// именем принтера (редизайн 2026-11-05, этап 3, п. 8).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_ПустойСписокПринтеров_ПонятноеСостояниеБезИсключения()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            Array.Empty<PrinterDescriptor>(),
            показатьСписокШаблонов: true);

        var настройки = Обойти(форма).OfType<Button>()
            .Single(кнопка => кнопка.Text == "Настройки принтера");

        Assert.False(настройки.Enabled);
        Assert.Null(форма.ВыбранныйПринтер);
        Assert.Equal(string.Empty, форма.СформироватьЗапрос([1]).PrinterName);
    }

    /// <summary>
    /// Кнопки печати и отмены существуют, назначены AcceptButton и
    /// CancelButton и лежат в layout-контейнере, а не на координатах
    /// (этап 3, п. 2–5).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_КнопкиДиалогаВПотокеПринятыАкцептИОтмена()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        var печать = Обойти(форма).OfType<Button>().Single(кнопка => кнопка.Text == "Печать");
        var отмена = Обойти(форма).OfType<Button>().Single(кнопка => кнопка.Text == "Отмена");

        Assert.IsType<FlowLayoutPanel>(печать.Parent);
        Assert.Same(печать.Parent, отмена.Parent);
        Assert.Same(печать, форма.AcceptButton);
        Assert.Same(отмена, форма.CancelButton);
    }

    /// <summary>
    /// Ключевые поля лежат в layout-контейнерах, а абсолютные координаты
    /// основных элементов не используются (этап 3, п. 1).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_ОсновныеПоля_ВКонтейнерахКомпоновки()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        Assert.True(ПредокТаблица(ПереченьШаблонов(форма)));
        Assert.True(ПредокТаблица(Обойти(форма).OfType<ComboBox>()
            .Single(поле => поле.AccessibleName == "Принтер")));
    }

    /// <summary>
    /// Длинное имя принтера и шаблона не меняет семантику выбора.
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_ДлинныеИмена_ВыборСохраняется()
    {
        var длинный = new PrinterDescriptor(
            new string('А', 80),
            "PORTPROMPT:",
            PrinterState.Ready,
            IsDefault: true);

        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6 — очень длинное наименование шаблона конверта для проверки переноса") },
            new[] { длинный },
            показатьСписокШаблонов: true);

        Assert.Equal(длинный.Name, форма.ВыбранныйПринтер?.Name);
        Assert.Equal("Конверт C6 — очень длинное наименование шаблона конверта для проверки переноса", форма.ВыбранныйШаблон?.Name);
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
