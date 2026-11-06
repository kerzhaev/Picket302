using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Printing.Preview;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Окно предпросмотра печати (ТЗ §8.6, функция 80: «Окно предпросмотра: холст,
/// линейка, зум, панель „Прервать“, кнопка „Печать“»).
/// </summary>
/// <remarks>
/// <para>
/// Предпросмотр показывает результат того же конвейера рендеринга, что и
/// физическая печать: масштабирование изображения не влияет на координаты
/// печати.
/// </para>
/// <para>
/// Компоновка построена на панелях <see cref="DockStyle.Fill"/> без
/// координат; крупный при 150–200 % документ прокручивается, а не обрезается
/// (редизайн 2026-11-05, этап 4).
/// </para>
/// </remarks>
public sealed class FormPrintPreview : Form
{
    private const float ОтступХолстаМм = 8f;

    private readonly PreviewDocument _документ;
    private readonly ToolTip _подсказки = new();

    /// <summary>Прокручиваемая подложка: холст вычисляемого размера внутри.</summary>
    private readonly Panel _прокрутка = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = EnvelopeUiTheme.WorkspaceBackground,
    };

    /// <summary>Холст предпросмотра: размер вычисляется по документу и масштабу.</summary>
    private readonly Panel _холст = new()
    {
        BackColor = SystemColors.Window,
    };

    private readonly ComboBox _зум = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };

    /// <summary>
    /// Создаёт окно предпросмотра.
    /// </summary>
    /// <param name="документ">Документ предпросмотра.</param>
    public FormPrintPreview(PreviewDocument документ)
    {
        ArgumentNullException.ThrowIfNull(документ);

        _документ = документ;

        Text = "Предпросмотр печати";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(420, 320);
        ClientSize = new Size(760, 580);

        _холст.Paint += Отрисовать;

        _зум.Items.AddRange(new object[] { "50%", "75%", "100%", "150%", "200%" });
        _зум.SelectedIndex = 2;
        _зум.AccessibleName = "Масштаб предпросмотра";
        _зум.AccessibleDescription = "Масштаб изображения предпросмотра; координаты печати не меняются";
        _подсказки.SetToolTip(_зум, "Масштаб предпросмотра (на печать не влияет)");

        // Смена масштаба перерисовывает холст и пересчитывает прокрутку
        // (F-E22; этап 4, п. 5).
        _зум.SelectedIndexChanged += (_, _) => ПересчитатьХолст();

        var кнопкаПечать = new Button
        {
            Text = "Печать",
            AutoSize = true,
            TabIndex = 1,
        };
        кнопкаПечать.AccessibleName = "Печать";
        кнопкаПечать.AccessibleDescription = "Подтвердить печать документа";
        _подсказки.SetToolTip(кнопкаПечать, "Подтвердить печать документа");
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(кнопкаПечать);
        кнопкаПечать.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var кнопкаЗакрыть = new Button
        {
            Text = "Закрыть",
            AutoSize = true,
            TabIndex = 2,
        };
        кнопкаЗакрыть.AccessibleName = "Закрыть предпросмотр";
        _подсказки.SetToolTip(кнопкаЗакрыть, "Закрыть окно без печати");
        кнопкаЗакрыть.Click += (_, _) => Close();

        var панельУправления = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 8, 6),
        };

        var надписьМасштаба = new Label
        {
            Text = "Масштаб:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        панельУправления.Controls.AddRange([надписьМасштаба, _зум, кнопкаПечать, кнопкаЗакрыть]);

        _прокрутка.Controls.Add(_холст);

        var корень = new Panel { Dock = DockStyle.Fill };
        корень.Controls.Add(_прокрутка);
        корень.Controls.Add(панельУправления);

        Controls.Add(корень);

        AcceptButton = кнопкаПечать;
        CancelButton = кнопкаЗакрыть;

        // Прокрутка пересчитывается и при изменении размера окна (этап 4, п. 5).
        Resize += (_, _) => ПересчитатьХолст();

        EnvelopeUiTheme.ОформитьОкно(this);
        ПересчитатьХолст();
    }

    /// <summary>Признак согласия оператора на печать.</summary>
    public bool ПечатьРазрешена => DialogResult == DialogResult.OK;

    /// <summary>Список значений масштаба для проверок компоновки.</summary>
    public static IReadOnlyList<string> ЗначенияМасштаба => ["50%", "75%", "100%", "150%", "200%"];

    private double ТекущийМасштаб => _зум.SelectedIndex switch
    {
        0 => 0.5,
        1 => 0.75,
        3 => 1.5,
        4 => 2.0,
        _ => 1.0,
    };

    /// <summary>
    /// Пересчитывает размер холста по документу и масштабу и задаёт границы
    /// прокрутки: крупный документ остаётся достижимым полосами прокрутки,
    /// а не обрезается (этап 4, п. 4).
    /// </summary>
    private void ПересчитатьХолст()
    {
        var масштаб = ТекущийМасштаб;
        var пиксельНаМм = 96f / 25.4f;

        var ширинаПикс = (int)Math.Ceiling((_документ.PageSizeMm.Width + (2 * ОтступХолстаМм)) * пиксельНаМм * масштаб);
        var высотаПикс = (int)Math.Ceiling((_документ.PageSizeMm.Height + (2 * ОтступХолстаМм)) * пиксельНаМм * масштаб);

        // Сообщение баннера занимает место под страницей и переносится:
        // длинный текст не выходит за холст.
        if (!string.IsNullOrEmpty(_документ.BannerMessage))
        {
            using var шрифт = EnvelopeUiTheme.ШрифтТекста(9f);

            var текстБаннера = new RectangleF(
                8,
                высотаПикс,
                Math.Max(200, ширинаПикс - 16),
                0);

            var размеры = TextRenderer.MeasureText(
                _документ.BannerMessage,
                шрифт,
                new Size((int)текстБаннера.Width, int.MaxValue),
                TextFormatFlags.WordBreak);

            высотаПикс += 8 + размеры.Height;
        }

        _холст.Size = new Size(Math.Max(ширинаПикс, 120), Math.Max(высотаПикс, 120));
        _холст.Location = Point.Empty;
        _прокрутка.AutoScrollMinSize = _холст.Size;
        _холст.Invalidate();
    }

    private void Отрисовать(object? отправитель, PaintEventArgs событие)
    {
        событие.Graphics.Clear(_холст.BackColor);

        if (_документ.Pages.Count == 0)
        {
            using var шрифтПустоты = EnvelopeUiTheme.ШрифтТекста(9f, FontStyle.Italic);
            TextRenderer.DrawText(
                событие.Graphics,
                "Нет страниц для предпросмотра.",
                шрифтПустоты,
                _холст.ClientRectangle,
                EnvelopeUiTheme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            return;
        }

        var масштаб = ТекущийМасштаб;
        var страница = _документ.Pages[0];

        var пиксельНаМм = 96f / 25.4f;
        var отступПикс = ОтступХолстаМм * пиксельНаМм * (float)масштаб;

        var прямоугольник = new RectangleF(
            отступПикс,
            отступПикс,
            _документ.PageSizeMm.Width * пиксельНаМм * (float)масштаб,
            _документ.PageSizeMm.Height * пиксельНаМм * (float)масштаб);

        using (var рамка = new Pen(Color.Black))
        {
            событие.Graphics.DrawRectangle(рамка, прямоугольник.X, прямоугольник.Y, прямоугольник.Width, прямоугольник.Height);
        }

        // Воспроизведение метафайла напрямую: прежний записывающий контекст
        // Graphics.FromImage(страница) вместе с DrawImage вызывал
        // OutOfMemoryException у GDI+ при первой отрисовке (найдено runtime-
        // приёмкой 2026-11-06; записывающий контекст здесь не нужен).
        событие.Graphics.DrawImage(
            страница,
            прямоугольник,
            new RectangleF(0, 0, _документ.PageSizeMm.Width, _документ.PageSizeMm.Height),
            GraphicsUnit.Millimeter);

        if (_документ.PrintableAreaMm.Width > 0 && _документ.PrintableAreaMm.Height > 0)
        {
            using var границы = new Pen(Color.Red, 1f) { DashStyle = DashStyle.Dash };

            var левая = прямоугольник.Left + (_документ.PrintableAreaMm.X * пиксельНаМм * (float)масштаб);
            var верх = прямоугольник.Top + (_документ.PrintableAreaMm.Y * пиксельНаМм * (float)масштаб);
            var ширина = _документ.PrintableAreaMm.Width * пиксельНаМм * (float)масштаб;
            var высота = _документ.PrintableAreaMm.Height * пиксельНаМм * (float)масштаб;

            событие.Graphics.DrawRectangle(границы, левая, верх, ширина, высота);
        }

        foreach (var поле in _документ.Fields)
        {
            using var контур = new Pen(Color.FromArgb(120, 0, 0, 255), 1f);

            var левая = прямоугольник.Left + (поле.Bounds.X * пиксельНаМм * (float)масштаб);
            var верх = прямоугольник.Top + (поле.Bounds.Y * пиксельНаМм * (float)масштаб);
            var ширина = поле.Bounds.Width * пиксельНаМм * (float)масштаб;
            var высота = поле.Bounds.Height * пиксельНаМм * (float)масштаб;

            событие.Graphics.DrawRectangle(контур, левая, верх, ширина, высота);
        }

        if (!string.IsNullOrEmpty(_документ.BannerMessage))
        {
            using var шрифт = EnvelopeUiTheme.ШрифтТекста(9f);
            var областьБаннера = new RectangleF(
                8,
                прямоугольник.Bottom + 8,
                Math.Max(200, _холст.Width - 16),
                _холст.Height - прямоугольник.Bottom - 8);

            TextRenderer.DrawText(
                событие.Graphics,
                _документ.BannerMessage,
                шрифт,
                Rectangle.Round(областьБаннера),
                Color.DarkRed,
                TextFormatFlags.WordBreak | TextFormatFlags.Top);
        }
    }
}
