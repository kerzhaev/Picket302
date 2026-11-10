using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Picket302.App;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.Domain.Entities;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Preview;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Контрактные проверки адаптивного редизайна 2026-11-05: корневая
/// компоновка вкладки «Адресная книга» на SplitContainer/TableLayoutPanel,
/// адаптивные диалоги печати и предпросмотра, empty-state пустого дерева
/// «Отправленные» и понятное подтверждение сохранения вида таблицы.
/// Проверяются свойства компоновки иерархии, а не пиксельные координаты;
/// COM, реестр, принтер и физическая печать не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ResponsiveUiContractTests
{
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

    private static TabPage Вкладка(MainShell окно, string название) => окно.Вкладки.TabPages
        .Cast<TabPage>()
        .Single(страница => страница.Text == название);

    // ------------------------------------------------------------------
    // MainShell: корневая адаптивная компоновка «Адресной книги».
    // ------------------------------------------------------------------

    /// <summary>
    /// Вкладка «Адресная книга» использует корневой горизонтальный
    /// SplitContainer: верх — редакторы, низ — таблицы (этап 1, п. 2).
    /// </summary>
    [Fact]
    public void MainShell_АдреснаяКнига_КорневойГоризонтальныйРазделитель()
    {
        using var окно = new MainShell();

        var страница = Вкладка(окно, "Адресная книга");

        var корень = страница.Controls.Cast<Control>()
            .OfType<SplitContainer>()
            .Single(разделитель => разделитель.Orientation == Orientation.Horizontal);

        Assert.Equal(DockStyle.Fill, корень.Dock);
    }

    /// <summary>
    /// Верхняя зона — TableLayoutPanel тремя колонками без фиксированной
    /// высоты 220 px; нижняя — вертикальный разделитель с двумя таблицами
    /// (этап 1, п. 3–4).
    /// </summary>
    [Fact]
    public void MainShell_АдреснаяКнига_ВерхТаблицейНизРазделителемТаблиц()
    {
        using var окно = new MainShell();

        var страница = Вкладка(окно, "Адресная книга");
        var корень = страница.Controls.Cast<Control>()
            .OfType<SplitContainer>()
            .Single(разделитель => разделитель.Orientation == Orientation.Horizontal);

        var верх = корень.Panel1.Controls.Cast<Control>().OfType<TableLayoutPanel>().Single();
        Assert.Equal(3, верх.ColumnCount);
        Assert.Equal(DockStyle.Fill, верх.Dock);
        Assert.NotEqual(220, верх.Height); // фиксированная высота упразднена

        var низ = корень.Panel2.Controls.Cast<Control>().OfType<SplitContainer>().Single();
        Assert.Equal(Orientation.Vertical, низ.Orientation);

        Assert.Contains(окно.ТаблицаОтправителей, Обойти(низ.Panel1).Concat([окно.ТаблицаОтправителей]));
        Assert.Contains(окно.ТаблицаПолучателей, Обойти(низ.Panel2).Concat([окно.ТаблицаПолучателей]));
    }

    /// <summary>
    /// Кнопки действий не начинаются с F5/F6/F7, а горячие клавиши остаются
    /// в подсказках и описаниях (этап 2, п. 4–5).
    /// </summary>
    [Fact]
    public void MainShell_КнопкиДействий_БезХоткеевВТекстеНоСПодсказками()
    {
        using var окно = new MainShell();

        var кнопки = Обойти(окно).OfType<Button>().ToList();

        Assert.DoesNotContain(кнопки, кнопка => кнопка.Text.StartsWith("F5", StringComparison.Ordinal));
        Assert.DoesNotContain(кнопки, кнопка => кнопка.Text.StartsWith("F6", StringComparison.Ordinal));
        Assert.DoesNotContain(кнопки, кнопка => кнопка.Text.StartsWith("F7", StringComparison.Ordinal));
        Assert.Contains(кнопки, кнопка => кнопка.Text == "Печать текущего конверта");
        Assert.Contains(кнопки, кнопка => кнопка.Text == "Предпросмотр и печать");
        Assert.Contains(кнопки, кнопка => кнопка.Text == "Фильтры…");

        var печать = кнопки.Single(кнопка => кнопка.Text == "Печать текущего конверта");
        var превью = кнопки.Single(кнопка => кнопка.Text == "Предпросмотр и печать");

        Assert.Contains("F5", печать.AccessibleDescription, StringComparison.Ordinal);
        Assert.Contains("F6", превью.AccessibleDescription, StringComparison.Ordinal);

        // Меню сохраняет горячие клавиши в подписях пунктов.
        var пунктыПечати = окно.MainMenuStrip!.Items.Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Печать")
            .DropDownItems.Cast<ToolStripItem>()
            .Select(пункт => пункт.Text)
            .ToList();

        Assert.Contains("Текущий конверт (F5)", пунктыПечати);
        Assert.Contains("Выделенные с предпросмотром (F6)", пунктыПечати);
        Assert.Contains("Выделенные без предпросмотра (F7)", пунктыПечати);
        Assert.Contains("Редактор бланков (F8)", пунктыПечати);

        var менюПечати = окно.MainMenuStrip.Items.Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Печать");

        foreach (var подпись in new[]
                 {
                     "Текущий конверт (F5)",
                     "Выделенные с предпросмотром (F6)",
                     "Выделенные без предпросмотра (F7)",
                     "Редактор бланков (F8)",
                     "Настройки принтера…",
                 })
        {
            var пункт = менюПечати.DropDownItems
                .OfType<ToolStripMenuItem>()
                .Single(элемент => элемент.Text == подпись);

            Assert.NotNull(пункт.Image);
        }

        var пунктыФильтра = окно.MainMenuStrip.Items.Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Вид")
            .DropDownItems.Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Фильтры")
            .DropDownItems.Cast<ToolStripItem>()
            .Select(пункт => пункт.Text)
            .ToList();

        Assert.Contains("Избранные", пунктыФильтра);
        Assert.Contains("Все категории", пунктыФильтра);
    }

    /// <summary>
    /// При минимальном размере окна ключевые кнопки остаются в клиентской
    /// области (этап 1, п. 6; этап 9, п. 2).
    /// </summary>
    [Fact]
    public void MainShell_МинимальныйРазмер_КнопкиВКлиентскойОбласти()
    {
        using var окно = new MainShell();

        // Дескриптор дерева контролов заставляет SplitContainer распределить
        // панели реального размера до проверки границ.
        окно.CreateControl();
        окно.ClientSize = окно.MinimumSize;
        окно.PerformLayout();

        var кнопки = Обойти(окно).OfType<Button>()
            .Where(кнопка => !string.IsNullOrWhiteSpace(кнопка.Text))
            .ToList();

        Assert.NotEmpty(кнопки);
        Assert.All(кнопки, кнопка =>
        {
            Assert.True(
                окно.ClientRectangle.Contains(кнопка.Bounds),
                $"Кнопка «{кнопка.Text}» выходит за клиентскую область: {кнопка.Bounds}.");
        });
    }

    /// <summary>
    /// Диалог Ctrl+F сохраняет реальные поисковые действия и одновременно
    /// повторяет структуру legacy-поверхности: образец, область, режим
    /// совпадения, просмотр и параметры регистра/формата.
    /// </summary>
    [Fact]
    public void FormSearch_СтруктураПараметров_СоответствуетLegacyИФункциональна()
    {
        using var форма = new FormSearch("гост", совпадений: 3);
        форма.CreateControl();

        var метки = Обойти(форма).OfType<Label>().Select(метка => метка.Text).ToList();
        Assert.Contains("Образец:", метки);
        Assert.Contains("Поиск в:", метки);
        Assert.Contains("Совпадение:", метки);
        Assert.Contains("Просмотр:", метки);

        var списки = Обойти(форма).OfType<ComboBox>().ToList();
        Assert.Equal(3, списки.Count);
        Assert.Contains(списки, список => список.Items.Cast<object>().Contains("Наименование"));
        Assert.Contains(списки, список => список.Items.Cast<object>().Contains("С любой части поля"));
        Assert.Contains(списки, список => список.Items.Cast<object>().Contains("Все"));

        var флаги = Обойти(форма).OfType<CheckBox>().Select(флаг => флаг.Text).ToList();
        Assert.Contains("С учетом регистра", флаги);
        Assert.Contains("С учетом формата", флаги);
        Assert.Equal("гост", форма.Запрос);
        Assert.Equal(РежимСовпадения.Содержит, форма.Режим);
        Assert.False(форма.УчитыватьРегистр);
        Assert.False(форма.УчитыватьФормат);
        Assert.True(форма.ClientSize.Width >= 500);
        Assert.True(форма.ClientSize.Height >= 140);
    }

    // ------------------------------------------------------------------
    // «Отправленные»: empty-state без бессмысленного скролла.
    // ------------------------------------------------------------------

    /// <summary>
    /// При пустом дереве папок подсказка видна, а пустое дерево скрыто —
    /// горизонтальная полоса прокрутки не показывается (этап 5).
    /// </summary>
    [Fact]
    public void MainShell_ПустоеДеревоПапок_СкрытоСПодсказкой()
    {
        using var окно = new MainShell();

        var страница = Вкладка(окно, "Отправленные");
        var дерево = Обойти(страница).OfType<TreeView>().Single();
        var подсказка = Обойти(страница)
            .OfType<Label>()
            .Single(метка => метка.Text.Contains("Папок пока нет", StringComparison.Ordinal));

        Assert.Empty(дерево.Nodes);
        Assert.True(окно.ПоказанаПодсказкаПустыхПапок);

        // Появление папок возвращает дерево: состояние пересчитывается тем же
        // обновителем, что вызывается после загрузки папок из базы.
        дерево.Nodes.Add("Тестовая папка (1)");
        typeof(MainShell)
            .GetMethod("ОбновитьПустотуДерева", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(окно, null);

        Assert.False(окно.ПоказанаПодсказкаПустыхПапок);
    }

    /// <summary>
    /// Полоса кнопок «Кому» растягивается по содержимому: обе строки
    /// («Добавить…Открыть список в Excel» и «Сохранить вид таблицы»,
    /// «Вид таблиц…») полностью видимы в координатах формы при
    /// минимальном размере окна. Прежняя фиксированная высота 50 px
    /// обрезала второй ряд кнопок (runtime-приёмка 2026-11-06).
    /// </summary>
    [Fact]
    public void MainShell_ПолосаКнопокКому_ОбеСтрокиПолностьюВидимы()
    {
        using var окно = new MainShell();
        окно.CreateControl();
        окно.ClientSize = окно.MinimumSize;
        окно.PerformLayout();

        var поток = Обойти(Вкладка(окно, "Адресная книга"))
            .OfType<FlowLayoutPanel>()
            .Single(панель => панель.Controls.OfType<Button>().Any(кнопка => кнопка.Text == "Предпросмотр и печать"));

        Assert.True(поток.AutoSize, "Полоса кнопок «Кому» должна расти по содержимому.");

        foreach (var кнопка in поток.Controls.OfType<Button>())
        {
            var причина = LayoutAssertions.ПричинаВыходаЗаГраницы(кнопка, окно);

            Assert.True(причина is null, причина);
        }
    }

    /// <summary>
    /// Текст подтверждения сохранения вида таблицы называет таблицу и
    /// сохранённые параметры (этап 6, E2).
    /// </summary>
    [Fact]
    public void MainShell_ПодтверждениеВида_НазываетТаблицуИПараметры()
    {
        var текст = MainShell.ТекстСохраненияВида();

        Assert.Contains("Адресная книга получателей", текст, StringComparison.Ordinal);
        Assert.Contains("видимость", текст, StringComparison.Ordinal);
        Assert.Contains("подписи", текст, StringComparison.Ordinal);
        Assert.Contains("ширины столбцов", текст, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // FormEnvelopePrint: адаптивность и живучесть.
    // ------------------------------------------------------------------

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
    /// Переключение показа перечня шаблонов не выбрасывает исключение и не
    /// выносит кнопки за клиентскую область (этап 3, п. 4).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_СменаРежимаШаблонов_КомпоновкаЦела()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6"), Шаблон(2, "Конверт DL") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        var флажокСписка = Обойти(форма).OfType<CheckBox>()
            .Single(флажок => флажок.Text == "Показывать список шаблонов перед печатью");

        foreach (var показывать in new[] { false, true, false })
        {
            флажокСписка.Checked = показывать;
            форма.PerformLayout();

            Assert.Equal(показывать, форма.ПереченьШаблоновПоказывается);

            var кнопки = Обойти(форма).OfType<Button>()
                .Where(кнопка => кнопка.Text is "Печать" or "Отмена")
                .ToList();

            Assert.All(кнопки, кнопка => Assert.True(
                форма.ClientRectangle.Contains(кнопка.Bounds),
                $"Кнопка «{кнопка.Text}» вне клиентской области при показе шаблонов = {показывать}."));
        }
    }

    /// <summary>
    /// Длинное предупреждение разлиновки не перекрывает кнопки: предупреждение
    /// переносится и остаётся в клиентской области (этап 3, п. 4).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_ПредупреждениеВидно_НеПерекрываетКнопки()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: true);

        var разлиновка = Обойти(форма).OfType<CheckBox>()
            .Single(флажок => флажок.Text == "Размеченный типографский конверт");

        разлиновка.Checked = true;
        форма.CreateControl();
        форма.PerformLayout();

        Assert.True(форма.ПредупреждениеРазлиновкиПоказывается);

        var печать = Обойти(форма).OfType<Button>().Single(кнопка => кнопка.Text == "Печать");
        var предупреждение = Обойти(форма).OfType<Label>()
            .Single(метка => метка.Text == Picket302.Printing.Engine.PreprintedMode.Warning);
        var предупреждениеВФорме = ГраницыВФорме(предупреждение, форма);
        var печатьВФорме = ГраницыВФорме(печать, форма);


        Assert.True(
            форма.ClientRectangle.Contains(предупреждениеВФорме),
            $"Предупреждение выходит за клиентскую область {форма.ClientRectangle}: {предупреждениеВФорме}.");
        Assert.True(
            форма.ClientRectangle.Contains(печатьВФорме),
            $"Кнопка печати выходит за клиентскую область {форма.ClientRectangle}: {печатьВФорме}.");
        // Границы сравниваются в координатах формы: контролы лежат в разных
        // контейнерах, и Parent-локальные прямоугольники несопоставимы.
        Assert.False(предупреждениеВФорме.IntersectsWith(печатьВФорме));
    }

    /// <summary>
    /// Сформированный запрос сохраняет прежнюю семантитуру полей (этап 3, п. 6).
    /// </summary>
    [Fact]
    public void FormEnvelopePrint_Запрос_СохраняетСемантикуПолей()
    {
        using var форма = new FormEnvelopePrint(
            new[] { Шаблон(1, "Конверт C6") },
            new[] { Принтер },
            показатьСписокШаблонов: false,
            копий: 3);

        var запрос = форма.СформироватьЗапрос([7, 8, 9]);

        Assert.Equal([7L, 8L, 9L], запрос.MailIds);
        Assert.Equal(1, запрос.TemplateId);
        Assert.Equal(Принтер.Name, запрос.PrinterName);
        Assert.Equal(3, запрос.Copies);
        Assert.Equal(DuplexMode.Duplex, запрос.Duplex);
        Assert.Equal(PageParity.All, запрос.Parity);
        Assert.True(запрос.ShowPreview);
    }

    /// <summary>
    /// Вычисляет границы контрола в координатах формы обходом родителей:
    /// экранные преобразования ненадёжны для непоказанного окна.
    /// </summary>
    private static Rectangle ГраницыВФорме(Control контроль, Form форма)
    {
        var x = контроль.Left;
        var y = контроль.Top;

        for (var родитель = контроль.Parent; родитель is not null && родитель != форма; родитель = родитель.Parent)
        {
            x += родитель.Left;
            y += родитель.Top;
        }

        return new Rectangle(x, y, контроль.Width, контроль.Height);
    }

    // ------------------------------------------------------------------
    // FormPrintPreview: масштаб, прокрутка, кнопки.
    // ------------------------------------------------------------------

    private static PreviewDocument Документ(
        float ширинаМм = 324f,
        float высотаМм = 229f,
        string? баннер = null)
    {
        using var ссылка = Graphics.FromHwnd(IntPtr.Zero);
        var dc = ссылка.GetHdc();

        Metafile страница;
        try
        {
            страница = new Metafile(new MemoryStream(), dc, EmfType.EmfPlusDual);
        }
        finally
        {
            ссылка.ReleaseHdc(dc);
        }

        return new PreviewDocument
        {
            Pages = [страница],
            PageSizeMm = new SizeF(ширинаМм, высотаМм),
            PrintableAreaMm = new RectangleF(10, 10, ширинаМм - 20, высотаМм - 20),
            BannerMessage = баннер,
        };
    }

    /// <summary>
    /// Форма создаётся на синтетическом документе, масштаб содержит все
    /// значения, кнопки существуют и назначены (этап 9, п. 4).
    /// </summary>
    [Fact]
    public void FormPrintPreview_МасштабИКнопки_СоответствуютКонтракту()
    {
        using var документ = Документ();
        using var форма = new FormPrintPreview(документ);

        var зум = Обойти(форма).OfType<ComboBox>().Single();
        Assert.Equal(FormPrintPreview.ЗначенияМасштаба, зум.Items.Cast<string>().ToArray());
        Assert.Equal("100%", зум.SelectedItem);

        var печать = Обойти(форма).OfType<Button>().Single(кнопка => кнопка.Text == "Печать");
        var закрыть = Обойти(форма).OfType<Button>().Single(кнопка => кнопка.Text == "Закрыть");

        Assert.Same(печать, форма.AcceptButton);
        Assert.Same(закрыть, форма.CancelButton);
        Assert.False(форма.ПечатьРазрешена);
    }

    /// <summary>
    /// Смена масштаба меняет размер холста: изображение перерисовывается,
    /// а не остаётся прежним (F-E22; этап 4, п. 5).
    /// </summary>
    [Fact]
    public void FormPrintPreview_СменаМасштабаМеняетРазмерХолста()
    {
        using var документ = Документ();
        using var форма = new FormPrintPreview(документ);

        var зум = Обойти(форма).OfType<ComboBox>().Single();
        var холст = Обойти(форма).OfType<Panel>()
            .Single(панель => панель.Parent is ScrollableControl { AutoScroll: true });

        зум.SelectedIndex = 4; // 200 %
        форма.PerformLayout();

        var ширинаПри200 = холст.Width;

        зум.SelectedIndex = 0; // 50 %
        форма.PerformLayout();

        Assert.True(ширинаПри200 > холст.Width, "Холст при 200 % должен быть крупнее холста при 50 %.");
    }

    /// <summary>
    /// Большая страница при 200 % не обрезается: прокрутка включена и её
    /// границы превышают видимую область (этап 4, п. 4).
    /// </summary>
    [Fact]
    public void FormPrintPreview_КрупныйДокумент_Прокручивается()
    {
        using var документ = Документ(ширинаМм: 450f, высотаМм: 320f, баннер: "Расхождение метрик: печатная область меньше бланка.");
        using var форма = new FormPrintPreview(документ);

        var прокрутка = Обойти(форма).OfType<Panel>().Single(панель => панель.AutoScroll);
        var зум = Обойти(форма).OfType<ComboBox>().Single();

        зум.SelectedIndex = 4; // 200 %
        форма.PerformLayout();

        Assert.True(прокрутка.AutoScrollMinSize.Width > прокрутка.ClientSize.Width
            || прокрутка.AutoScrollMinSize.Height > прокрутка.ClientSize.Height,
            "Крупный документ обязан быть достижимым прокруткой.");
    }

    /// <summary>
    /// Пустой список страниц не ломает окно: показывается понятное состояние
    /// (этап 4, п. 6).
    /// </summary>
    [Fact]
    public void FormPrintPreview_БезСтраниц_НеПадает()
    {
        using var документ = new PreviewDocument { PageSizeMm = new SizeF(324, 229) };
        using var форма = new FormPrintPreview(документ);

        var зум = Обойти(форма).OfType<ComboBox>().Single();

        foreach (var индекс in Enumerable.Range(0, зум.Items.Count))
        {
            зум.SelectedIndex = индекс;
            форма.PerformLayout();
        }

        Assert.NotNull(форма);
    }

    /// <summary>
    /// Кнопки предпросмотра доступны клавиатуре и имеют подписи для
    /// вспомогательных технологий (этап 8).
    /// </summary>
    [Fact]
    public void FormPrintPreview_КнопкиДоступныИПодписаны()
    {
        using var документ = Документ();
        using var форма = new FormPrintPreview(документ);

        var кнопки = Обойти(форма).OfType<Button>().ToList();

        Assert.All(кнопки, кнопка => Assert.False(string.IsNullOrWhiteSpace(кнопка.AccessibleName)));
        Assert.All(кнопки, кнопка => Assert.True(форма.ClientRectangle.Contains(кнопка.Bounds)));
    }
}
