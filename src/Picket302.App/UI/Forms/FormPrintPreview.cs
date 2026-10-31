using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Picket302.Printing.Preview;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Окно предпросмотра печати (ТЗ §8.6, функция 80: «Окно предпросмотра: холст,
/// линейка, зум, панель „Прервать“, кнопка „Печать“»).
/// </summary>
/// <remarks>
/// Предпросмотр показывает результат того же конвейера рендеринга, что и
/// физическая печать: масштабирование изображения не влияет на координаты
/// печати.
/// </remarks>
public sealed class FormPrintPreview : Form
{
    private readonly PreviewDocument _документ;
    private readonly PictureBox _холст = new() { Dock = DockStyle.Fill, BackColor = Color.Gainsboro };
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
        ClientSize = new Size(720, 560);

        _холст.Paint += Отрисовать;

        var панель = new Panel { Dock = DockStyle.Bottom, Height = 40 };

        var надписьМасштаба = new Label
        {
            Text = "Масштаб:",
            AutoSize = true,
            Location = new Point(12, 12),
        };

        _зум.Items.AddRange(new object[] { "50%", "75%", "100%", "150%", "200%" });
        _зум.SelectedIndex = 2;
        _зум.Location = new Point(78, 8);

        var кнопкаПечать = new Button { Text = "Печать", Size = new Size(96, 26) };
        кнопкаПечать.Location = new Point(420, 6);
        кнопкаПечать.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var кнопкаЗакрыть = new Button { Text = "Закрыть", Size = new Size(96, 26) };
        кнопкаЗакрыть.Location = new Point(524, 6);
        кнопкаЗакрыть.Click += (_, _) => Close();

        панель.Controls.AddRange(new Control[] { надписьМасштаба, _зум, кнопкаПечать, кнопкаЗакрыть });

        Controls.Add(_холст);
        Controls.Add(панель);

        AcceptButton = кнопкаПечать;
        CancelButton = кнопкаЗакрыть;
    }

    /// <summary>Признак согласия оператора на печать.</summary>
    public bool ПечатьРазрешена => DialogResult == DialogResult.OK;

    private double ТекущийМасштаб => _зум.SelectedIndex switch
    {
        0 => 0.5,
        1 => 0.75,
        3 => 1.5,
        4 => 2.0,
        _ => 1.0,
    };

    private void Отрисовать(object? отправитель, PaintEventArgs событие)
    {
        событие.Graphics.Clear(_холст.BackColor);

        if (_документ.Pages.Count == 0)
        {
            return;
        }

        var масштаб = ТекущийМасштаб;
        var страница = _документ.Pages[0];

        var ширинаМм = _документ.PageSizeMm.Width * (float)масштаб;
        var высотаМм = _документ.PageSizeMm.Height * (float)масштаб;
        var пиксельНаМм = 96f / 25.4f;

        var прямоугольник = new RectangleF(
            20,
            20,
            ширинаМм * пиксельНаМм,
            высотаМм * пиксельНаМм);

        using (var рамка = new Pen(Color.Black))
        {
            событие.Graphics.DrawRectangle(рамка, прямоугольник.X, прямоугольник.Y, прямоугольник.Width, прямоугольник.Height);
        }

        using (var графика = Graphics.FromImage(страница))
        {
            событие.Graphics.DrawImage(
                страница,
                прямоугольник,
                new RectangleF(0, 0, _документ.PageSizeMm.Width, _документ.PageSizeMm.Height),
                GraphicsUnit.Millimeter);
        }

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
            using var шрифт = new Font("Tahoma", 9f);
            событие.Graphics.DrawString(
                _документ.BannerMessage,
                шрифт,
                Brushes.DarkRed,
                20,
                прямоугольник.Bottom + 8);
        }
    }
}
