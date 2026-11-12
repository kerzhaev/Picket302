using System.Drawing;
using System.Text;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Picket302.Domain.Postal;
using Picket302.Domain.Serialization;
using Picket302.Infrastructure.Templates;
using Picket302.Printing.Editor;
using Picket302.Printing.Templates;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Встроенный редактор бланков (ТЗ §7.5: FormBlankEditor, вызов F8;
/// §5.2: правила компоновки; §8.3, функция 38).
/// </summary>
/// <remarks>
/// <para>Дерево панелей: холст в центре (сетка, привязка Snap, выделение,
/// перетаскивание, красная штриховка конфликтов), палитра объектов слева
/// («Текст», «Почтовый индекс», «Рисунок», «QR-код», «Прямоугольная зона»),
/// инспектор свойств справа (Font, HAlign/VAlign, WordWrap, Rotation, Frame,
/// Visible, Printable, Bounds, Min/MaxFontHeight, MaxChars) и вкладка
/// «События» со скриптом onBeforePrint.</para>
/// <para>Переключатель единиц измерения мм / pt / px (96/120 DPI) — в меню
/// «Вид». Семантика флагов — ровно по §7.5: Visible=false исключает элемент
/// везде; Printable=false оставляет элемент на экране и отсекает при печати.
/// Элемент за границами печатной области запрещает сохранение.</para>
/// </remarks>
public sealed class FormBlankEditor : Form
{
    private readonly CanvasController _контроллер;
    private readonly BlankXmlSerializer _сериализатор = new();

    private readonly Panel _холст = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(112, 112, 112) };
    private readonly ListBox _палитра = new() { Dock = DockStyle.Left, Width = 160 };
    private readonly PropertyGrid _инспектор = new() { Dock = DockStyle.Right, Width = 260 };
    private readonly TextBox _скрипт = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9f) };
    private readonly ListBox _предупреждения = new() { Dock = DockStyle.Bottom, Height = 96, IntegralHeight = false };
    private readonly StatusStrip _статус = new();
    private readonly ToolStripStatusLabel _статусТекст = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    /// <summary>Масштаб холста: пикселей на миллиметр.</summary>
    public const float PixelsPerMm = 3.0f;

    /// <summary>Номер отображаемой страницы.</summary>
    private int _страница = 1;

    /// <summary>Текущий бланк.</summary>
    public CustomBlank? Blank => _контроллер.Blank;

    /// <summary>Контроллер холста (состояние, проверка разметки).</summary>
    public CanvasController Controller => _контроллер;

    /// <summary>Создаёт редактор с макетом (ТЗ §7.5: новый шаблон создаётся
    /// копированием существующего, а не с нуля).</summary>
    /// <param name="blank">Бланк; <see langword="null"/> — состояние «Макет не загружен».</param>
    /// <param name="zipcodeFontAvailable">Контроль шрифта ZIPcode; <see langword="null"/> — без контроля.</param>
    /// <summary>Библиотека клипарта (S26, функция 158); null — инструмент
    /// «картинка» создаёт picture-поле без клипарта.</summary>
    private readonly ClipartLibrary? _клипарт;

    public FormBlankEditor(
        CustomBlank? blank = null,
        Func<bool>? zipcodeFontAvailable = null,
        ClipartLibrary? клипарт = null)
    {
        _клипарт = клипарт;
        Text = "Редактор бланков";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 760);
        WindowState = FormWindowState.Maximized;

        _контроллер = new CanvasController(new BlankLayoutValidator(zipcodeFontAvailable));

        _холст.AccessibleName = "Холст бланка";
        _холст.AccessibleDescription = "Рабочая область для размещения и редактирования элементов бланка.";
        _палитра.AccessibleName = "Палитра элементов бланка";
        _палитра.AccessibleDescription = "Выбор типа элемента, который можно добавить на холст бланка.";
        _инспектор.AccessibleName = "Свойства выбранного элемента";
        _инспектор.AccessibleDescription = "Редактирование свойств выбранного элемента бланка.";
        _скрипт.AccessibleName = "Скрипт перед печатью";
        _скрипт.AccessibleDescription = "Текст скрипта onBeforePrint для выбранного элемента бланка.";
        _предупреждения.AccessibleName = "Предупреждения разметки";
        _предупреждения.AccessibleDescription = "Список проблем разметки, которые могут запретить сохранение бланка.";

        ПостроитьПанели();
        ПостроитьМеню();

        _палитра.Items.AddRange(new object[]
        {
            "Текст",
            "Почтовый индекс",
            "Рисунок",
            "QR-код",
            "Прямоугольная зона",
        });
        _палитра.SelectedIndex = 0;
        _палитра.SelectedIndexChanged += (_, _) => _контроллер.Tool = ПалитраИнструмент();

        _холст.Paint += РисоватьХолст;
        _холст.MouseClick += ЩелчокХолста;
        _холст.MouseDoubleClick += ДвойнойЩелчокХолста;

        _статус.Items.Add(_статусТекст);
        _предупреждения.SelectedIndexChanged += (_, _) => ВыделитьПоПредупреждению();

        if (blank is not null)
        {
            Open(blank);
        }
        else
        {
            _статусТекст.Text = "Макет не загружен";
        }
    }

    /// <summary>Открывает макет в редакторе (ТЗ §8.3, функция 38: OpenEditor).</summary>
    /// <param name="blank">Бланк.</param>
    public void Open(CustomBlank blank)
    {
        ArgumentNullException.ThrowIfNull(blank);

        _контроллер.Open(blank);
        _страница = blank.Pages.Count > 0 ? blank.Pages[0].Number : 1;
        _инспектор.SelectedObject = null;
        ОбновитьЭкран();
    }

    /// <summary>Сериализует макет; блокируется при выходе элемента за
    /// границы печатной области (ТЗ §7.5, состояние «Сохранение запрещено»).
    /// Не показывает диалогов — пригодно для автоматизации.</summary>
    /// <returns>Текст blank.xml либо <see langword="null"/>, если сохранение запрещено.</returns>
    public string? TrySave()
    {
        if (_контроллер.Blank is null)
        {
            return null;
        }

        _контроллер.RefreshValidation();

        if (!_контроллер.CanSave())
        {
            return null;
        }

        return _сериализатор.SerializeBlank(_контроллер.Blank);
    }

    /// <summary>Сохранение из меню: при блокировке выводит предупреждение
    /// «Сохранение запрещено» (ТЗ §7.5; §8.3, функция 38).</summary>
    /// <returns>Текст blank.xml либо <see langword="null"/>, если сохранение запрещено.</returns>
    public string? Save()
    {
        var xml = TrySave();

        if (xml is null && _контроллер.Blank is not null)
        {
            MessageBox.Show(
                this,
                "Сохранение запрещено: элемент за границами печатной области.",
                "Редактор бланков",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return xml;
    }

    /// <summary>Записывает XML бланка в файл, выбранный оператором (F-E20):
    /// пункт меню «Сохранить» прежде ничего не сохранял.</summary>
    /// <remarks>
    /// Путь выбирается диалогом <c>SaveFileDialog</c> (фильтр blank.xml);
    /// файл записывается в UTF-8 без BOM. Каталог пользовательских бланков
    /// в профиле не заведён, поэтому выбор пути остаётся за оператором.
    /// </remarks>
    public void СохранитьВФайл()
    {
        var xml = Save();

        if (xml is null)
        {
            return;
        }

        using var диалог = new SaveFileDialog
        {
            Filter = "Бланк Picket302 (blank.xml)|blank.xml|XML|*.xml",
            FileName = "blank.xml",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(диалог.FileName, xml, new UTF8Encoding(false));

        MessageBox.Show(
            this,
            $"Бланк сохранён: {диалог.FileName}",
            "Редактор бланков",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>Переключает единицу представления координат (мм / pt / px).</summary>
    /// <param name="unit">Единица.</param>
    /// <param name="dpi">Плотность экрана для пикселей (96/120).</param>
    public void SetUnit(EditorUnit unit, int dpi = EditorUnits.DefaultScreenDpi)
    {
        _контроллер.Unit = unit;
        _контроллер.ScreenDpi = dpi;
        _холст.Invalidate();
        ОбновитьСтатус();
    }

    /// <summary>Выделяет поле по имени и обновляет инспектор.</summary>
    /// <param name="fieldName">Имя поля; <see langword="null"/> — снять выделение.</param>
    public void Select(string? fieldName)
    {
        _контроллер.Select(fieldName);
        _инспектор.SelectedObject = _контроллер.GetSelected();
        _скрипт.Text = _контроллер.GetSelected()?.OnBeforePrint ?? string.Empty;
        _холст.Invalidate();
    }

    /// <summary>Сохраняет текст скрипта onBeforePrint в выделенное поле
    /// (вкладка «События»; имя компонента обязано совпадать с именем на макете).</summary>
    /// <param name="script">Текст скрипта.</param>
    public void ApplyScript(string script)
    {
        var поле = _контроллер.GetSelected();

        if (поле is not null)
        {
            поле.OnBeforePrint = string.IsNullOrWhiteSpace(script) ? null : script;
        }
    }

    private void ПостроитьПанели()
    {
        var события = new TabControl { Dock = DockStyle.Bottom, Height = 200 };
        события.AccessibleName = "Дополнительные панели редактора бланка";
        var вкладкаСобытий = new TabPage("События");
        вкладкаСобытий.AccessibleName = "События бланка";
        вкладкаСобытий.Controls.Add(_скрипт);
        var вкладкаПредупреждений = new TabPage("Предупреждения");
        вкладкаПредупреждений.AccessibleName = "Предупреждения бланка";
        вкладкаПредупреждений.Controls.Add(_предупреждения);
        события.TabPages.Add(вкладкаСобытий);
        события.TabPages.Add(вкладкаПредупреждений);

        // Вкладка «События»: скрипт onBeforePrint сохраняется в выделенное
        // поле при потере фокуса (ТЗ §7.5).
        _скрипт.LostFocus += (_, _) => ApplyScript(_скрипт.Text);

        Controls.Add(_холст);
        Controls.Add(_инспектор);
        Controls.Add(_палитра);
        Controls.Add(события);
        Controls.Add(_статус);
    }

    private void ПостроитьМеню()
    {
        var меню = new MenuStrip();
        меню.AccessibleName = "Меню редактора бланков";
        var вид = new ToolStripMenuItem("Вид");
        var настройки = new ToolStripMenuItem("Настройки редактора");

        var пункты = new (string Название, EditorUnit Единица)[]
        {
            ("Миллиметры (мм)", EditorUnit.Millimeters),
            ("Пункты (pt)", EditorUnit.Points),
            ("Пиксели 96 DPI (px)", EditorUnit.Pixels),
            ("Пиксели 120 DPI (px)", EditorUnit.Pixels),
        };

        foreach (var (название, единица) in пункты)
        {
            var пункт = new ToolStripMenuItem(название);
            пункт.AccessibleName = $"Единицы измерения: {название}";

            пункт.Click += (_, _) => SetUnit(единица, название.Contains("120") ? EditorUnits.LargeScreenDpi : EditorUnits.DefaultScreenDpi);
            настройки.DropDownItems.Add(пункт);
        }

        вид.DropDownItems.Add(настройки);

        var файл = new ToolStripMenuItem("Файл");
        var сохранить = new ToolStripMenuItem("Сохранить");
        сохранить.AccessibleName = "Сохранить бланк в файл";

        // Меню «Сохранить» записывает XML в выбранный файл (F-E20): прежде
        // пункт вызывал Save() и отбрасывал результат — сохранение не
        // выполнялось.
        сохранить.Click += (_, _) => СохранитьВФайл();
        файл.DropDownItems.Add(сохранить);

        var объект = new ToolStripMenuItem("Объект");
        var удалить = new ToolStripMenuItem("Удалить");
        удалить.AccessibleName = "Удалить выбранный элемент бланка";

        удалить.Click += (_, _) =>
        {
            if (_контроллер.SelectedFieldName is { } имя)
            {
                _контроллер.RemoveField(имя);
                Select(null);
                ОбновитьЭкран();
            }
        };

        объект.DropDownItems.Add(удалить);

        // Разметка бланка: пресет «стандартная разметка» и режим «без
        // стандартной разметки» (ТЗ §8.11, функция 145).
        var макет = new ToolStripMenuItem("Макет");
        var стандартная = new ToolStripMenuItem("Стандартная разметка");
        var безРазметки = new ToolStripMenuItem("Без стандартной разметки (поля вручную)");
        стандартная.AccessibleName = "Применить стандартную разметку бланка";
        безРазметки.AccessibleName = "Отключить стандартную разметку бланка";

        стандартная.Click += (_, _) => ПрименитьРазметку(StandardLayoutMode.Standard);
        безРазметки.Click += (_, _) => ПрименитьРазметку(StandardLayoutMode.None);

        макет.DropDownItems.Add(стандартная);
        макет.DropDownItems.Add(безРазметки);

        меню.Items.Add(файл);
        меню.Items.Add(вид);
        меню.Items.Add(объект);
        меню.Items.Add(макет);
        MainMenuStrip = меню;
        Controls.Add(меню);
    }

    /// <summary>
    /// Применяет к макету режим разметки: пресет «стандартная разметка» либо
    /// режим «без стандартной разметки» (ТЗ §8.11, функция 145).
    /// </summary>
    /// <param name="режим">Режим разметки.</param>
    /// <returns>Итог применения пресета; при отсутствии макета — <see langword="null"/>.</returns>
    public StandardLayoutResult? ПрименитьРазметку(StandardLayoutMode режим)
    {
        if (_контроллер.Blank is not { } бланк)
        {
            return null;
        }

        var формат = ОпределитьФормат(бланк);
        var страна = string.IsNullOrWhiteSpace(бланк.Country) ? EnvelopeFormatCatalog.DefaultCountryCode : бланк.Country;

        var итог = new StandardLayoutApplier().Применить(бланк, формат, страна, режим);

        ОбновитьЭкран();

        if (итог.Предупреждение is { } предупреждение)
        {
            MessageBox.Show(this, предупреждение, "Разметка бланка", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return итог;
    }

    /// <summary>
    /// Определяет обозначение формата конверта по имени макета: пресет
    /// разметки выбирается по формату (ТЗ §8.11, функция 145).
    /// </summary>
    /// <param name="бланк">Макет бланка.</param>
    /// <returns>Обозначение формата либо «C6».</returns>
    private static string ОпределитьФормат(CustomBlank бланк)
    {
        var имя = бланк.Name ?? string.Empty;

        foreach (var формат in new[] { "DL/О", "DL", "C6/О", "C6", "C5", "C4" })
        {
            if (имя.Contains(формат, StringComparison.OrdinalIgnoreCase))
            {
                return формат;
            }
        }

        return "C6";
    }

    private BlankTool ПалитраИнструмент() => _палитра.SelectedIndex switch
    {
        1 => BlankTool.PostIndex,
        2 => BlankTool.Picture,
        3 => BlankTool.QrCode,
        4 => BlankTool.Zone,
        _ => BlankTool.Text,
    };

    private void ЩелчокХолста(object? sender, MouseEventArgs e)
    {
        var ммX = (decimal)(e.X / PixelsPerMm);
        var ммY = (decimal)(e.Y / PixelsPerMm);

        var имя = _контроллер.HitTest(_страница, ммX, ммY);

        if (имя is null && e.Button == MouseButtons.Left)
        {
            // Размещение нового объекта текущим инструментом палитры.
            var счётчик = 1;

            while (_контроллер.Blank?.AllFields().Any(поле => поле.Name == "Поле" + счётчик) == true)
            {
                счётчик++;
            }

            var поле = _контроллер.AddField(_страница, ммX, ммY, "Поле" + счётчик);

            if (поле is not null)
            {
                имя = поле.Name;

                // S26 (функция 158): инструмент «картинка» открывает выбор
                // клипарта; выбранная графика привязывается к полю и
                // воспроизводится в предпросмотре и печати.
                if (ПалитраИнструмент() == BlankTool.Picture && _клипарт is not null)
                {
                    using var пикер = new FormClipartPicker(_клипарт);
                    if (пикер.ShowDialog(this) == DialogResult.OK && пикер.ВыбранныйId is not null)
                    {
                        поле.ClipartId = пикер.ВыбранныйId;
                    }
                }
            }
        }

        Select(имя);
        ОбновитьЭкран();
    }

    private void ДвойнойЩелчокХолста(object? sender, MouseEventArgs e)
    {
        // Перетаскивание мышью: двойной щелчок сдвигает выделенное поле к точке.
        if (_контроллер.SelectedFieldName is { } имя)
        {
            var поле = _контроллер.GetSelected();

            if (поле is not null)
            {
                _контроллер.MoveField(
                    имя,
                    (decimal)(e.X / PixelsPerMm),
                    (decimal)(e.Y / PixelsPerMm));
                ОбновитьЭкран();
            }
        }
    }

    private void РисоватьХолст(object? sender, PaintEventArgs e)
    {
        if (_контроллер.Blank is not { } бланк)
        {
            return;
        }

        var графика = e.Graphics;
        графика.SmoothingMode = SmoothingMode.None;

        // Лист бланка с сеткой и линейками.
        var ширинаЛиста = (float)бланк.Geometry.WidthMm * PixelsPerMm;
        var высотаЛиста = (float)бланк.Geometry.HeightMm * PixelsPerMm;
        var лист = new RectangleF(16f, 16f, ширинаЛиста, высотаЛиста);

        using (var белая = new SolidBrush(Color.White))
        {
            графика.FillRectangle(белая, лист);
        }

        using (var пероСетки = new Pen(Color.FromArgb(230, 230, 230), 1f))
        {
            for (var мм = 0m; мм <= бланк.Geometry.WidthMm; мм += 10m)
            {
                var x = 16f + (float)мм * PixelsPerMm;
                графика.DrawLine(пероСетки, x, лист.Top, x, лист.Bottom);
            }

            for (var мм = 0m; мм <= бланк.Geometry.HeightMm; мм += 10m)
            {
                var y = 16f + (float)мм * PixelsPerMm;
                графика.DrawLine(пероСетки, лист.Left, y, лист.Right, y);
            }
        }

        var конфликтные = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (первый, второй) in _контроллер.LastValidation?.Overlaps ?? new List<(string, string)>())
        {
            конфликтные.Add(первый);
            конфликтные.Add(второй);
        }

        foreach (var поле in _контроллер.GetCanvasFields(_страница))
        {
            var прямоугольник = Габарит(поле);
            var печатается = поле.Printable;

            // Red hatch for conflicting zones (§5.2), gray hatch for
            // screen-only elements (Printable=false).
            var штриховка = конфликтные.Contains(поле.Name)
                ? HatchStyle.WideUpwardDiagonal
                : !печатается ? HatchStyle.LightUpwardDiagonal : (HatchStyle?)null;

            var цвет = конфликтные.Contains(поле.Name)
                ? Color.Red
                : !печатается ? Color.Gray : Color.DimGray;

            using (var перо = new Pen(цвет, конфликтные.Contains(поле.Name) ? 1.6f : 1f))
            {
                графика.DrawRectangle(перо, прямоугольник.X, прямоугольник.Y, прямоугольник.Width, прямоугольник.Height);
            }

            if (штриховка is not null)
            {
                using var кисть = new HatchBrush(штриховка.Value, Color.FromArgb(160, цвет), Color.Transparent);
                графика.FillRectangle(кисть, прямоугольник);
            }

            if (поле.Name == _контроллер.SelectedFieldName)
            {
                using var пероВыделения = new Pen(Color.DodgerBlue, 1.8f);
                графика.DrawRectangle(пероВыделения, прямоугольник.X, прямоугольник.Y, прямоугольник.Width, прямоугольник.Height);
            }

            using var шрифт = new Font("Segoe UI", 7f);
            графика.DrawString(поле.Name, шрифт, Brushes.Black, прямоугольник.X + 1f, прямоугольник.Y + 1f);
        }
    }

    private RectangleF Габарит(BlankFieldSpec поле) => new(
        16f + (float)поле.Bounds.XMm * PixelsPerMm,
        16f + (float)поле.Bounds.YMm * PixelsPerMm,
        (float)поле.Bounds.WidthMm * PixelsPerMm,
        (float)поле.Bounds.HeightMm * PixelsPerMm);

    private void ВыделитьПоПредупреждению()
    {
        var текст = _предупреждения.SelectedItem?.ToString();

        if (текст is null)
        {
            return;
        }

        foreach (var поле in _контроллер.Blank?.AllFields() ?? Enumerable.Empty<BlankFieldSpec>())
        {
            if (текст.Contains('«' + поле.Name + '»'))
            {
                Select(поле.Name);

                return;
            }
        }
    }

    private void ОбновитьЭкран()
    {
        ОбновитьСтатус();
        ОбновитьПредупреждения();
        _холст.Invalidate();
    }

    private void ОбновитьСтатус()
    {
        var состояние = _контроллер.State switch
        {
            EditorState.NoLayout => "Макет не загружен",
            EditorState.Loading => "Загрузка",
            EditorState.Ready => "Готов",
            EditorState.ZoneConflict => "Конфликт зон",
            EditorState.SaveForbidden => "Сохранение запрещено",
            _ => string.Empty,
        };

        _статусТекст.Text = $"{состояние}  |  Единицы: {_контроллер.Unit}  |  Выделено: {_контроллер.SelectedFieldName ?? "—"}";
    }

    private void ОбновитьПредупреждения()
    {
        _предупреждения.BeginUpdate();
        _предупреждения.Items.Clear();

        foreach (var сообщение in _контроллер.LastValidation?.Messages ?? (IReadOnlyList<string>)Array.Empty<string>())
        {
            _предупреждения.Items.Add(сообщение);
        }

        _предупреждения.EndUpdate();
    }
}
