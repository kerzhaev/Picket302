using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Встроенный калькулятор программы (ТЗ §8.12, функция 163: «встроенные
/// калькулятор и всплывающий календарь»).
/// </summary>
public sealed class FormCalculator : Form
{
    private readonly TextBox _дисплей = new()
    {
        Dock = DockStyle.Top,
        Height = 28,
        ReadOnly = true,
        TextAlign = HorizontalAlignment.Right,
        Font = new Font("Segoe UI", 12f),
        AccessibleName = "Результат вычисления",
        AccessibleDescription = "Текущее число или результат операции калькулятора.",
    };

    private readonly Label _операция = new()
    {
        Dock = DockStyle.Top,
        Height = 18,
        TextAlign = ContentAlignment.MiddleRight,
        AccessibleName = "Текущая операция",
        AccessibleDescription = "Операция и накопленное значение перед вычислением.",
    };

    private double _накопитель;
    private double _текущее;
    private string _знакОперации = string.Empty;
    private bool _новыйВвод = true;

    /// <summary>
    /// Буфер ввода числа (F-E15): прежде «,» прибавляла 0,1 к числу, и
    /// дробный ввод был невозможен.
    /// </summary>
    private string _буферВвода = string.Empty;

    /// <summary>
    /// Создаёт калькулятор.
    /// </summary>
    public FormCalculator()
    {
        Text = "Калькулятор";
        AccessibleName = "Калькулятор";
        AccessibleDescription = "Встроенный калькулятор для вычисления арифметических выражений.";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(280, 300);

        ПостроитьИнтерфейс();
        ОбновитьДисплей();
    }

    /// <summary>
    /// Текст, выведенный на дисплей калькулятора.
    /// </summary>
    public string DisplayText => _дисплей.Text;

    /// <summary>
    /// Вычисляет результат выражения (используется проверками).
    /// </summary>
    /// <param name="левое">Левое число.</param>
    /// <param name="знак">Знак операции: +, -, *, /.</param>
    /// <param name="правое">Правое число.</param>
    /// <returns>Результат вычисления.</returns>
    public static double Вычислить(double левое, string знак, double правое) => знак switch
    {
        "+" => левое + правое,
        "-" => левое - правое,
        "*" => левое * правое,
        "/" => правое == 0 ? double.NaN : левое / правое,
        _ => правое,
    };

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            AccessibleName = "Клавиатура калькулятора",
            AccessibleDescription = "Цифровые клавиши и операции встроенного калькулятора.",
        };

        for (var колонка = 0; колонка < 4; колонка++)
        {
            панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        for (var строка = 0; строка < 5; строка++)
        {
            панель.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        }

        var надписи = new[]
        {
            "7", "8", "9", "/",
            "4", "5", "6", "*",
            "1", "2", "3", "-",
            "0", ",", "C", "+",
        };

        var индекс = 0;

        for (var строка = 0; строка < 4; строка++)
        {
            for (var колонка = 0; колонка < 4; колонка++)
            {
                var надпись = надписи[индекс++];
                var кнопка = new Button
                {
                    Text = надпись,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(1),
                    AccessibleName = $"Клавиша {надпись}",
                    AccessibleDescription = $"Нажать клавишу «{надпись}» калькулятора.",
                };

                кнопка.Click += (_, _) => Нажать(надпись);

                панель.Controls.Add(кнопка, колонка, строка);
            }
        }

        var равно = new Button
        {
            Text = "=",
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            AccessibleName = "Вычислить результат",
            AccessibleDescription = "Выполнить текущую арифметическую операцию.",
        };
        равно.Click += (_, _) => Нажать("=");

        var плюсМинус = new Button
        {
            Text = "±",
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            AccessibleName = "Изменить знак числа",
            AccessibleDescription = "Изменить знак текущего числа на противоположный.",
        };
        плюсМинус.Click += (_, _) => Нажать("±");

        панель.Controls.Add(равно, 0, 4);
        панель.Controls.Add(плюсМинус, 1, 4);
        панель.Controls.Add(new Label(), 2, 4);
        панель.Controls.Add(new Label(), 3, 4);

        Controls.Add(панель);
        Controls.Add(_операция);
        Controls.Add(_дисплей);
    }

    /// <summary>
    /// Обрабатывает нажатие клавиши калькулятора.
    /// </summary>
    /// <param name="клавиша">Нажатая клавиша.</param>
    public void Нажать(string клавиша)
    {
        ArgumentNullException.ThrowIfNull(клавиша);

        switch (клавиша)
        {
            case "C":
                _накопитель = 0;
                _текущее = 0;
                _знакОперации = string.Empty;
                _новыйВвод = true;
                _буферВвода = string.Empty;
                break;

            case "=":
                if (_знакОперации.Length > 0)
                {
                    var результат = Вычислить(_накопитель, _знакОперации, _текущее);

                    if (double.IsNaN(результат))
                    {
                        _операция.Text = "Деление на ноль не допускается";
                        _текущее = 0;
                    }
                    else
                    {
                        _текущее = результат;
                        _накопитель = результат;
                    }

                    _знакОперации = string.Empty;
                    _новыйВвод = true;
                    _буферВвода = string.Empty;
                }

                break;

            case "±":
                _текущее = -_текущее;
                break;

            case "+" or "-" or "*" or "/":
                _накопитель = _текущее;
                _знакОперации = клавиша;
                _новыйВвод = true;
                _буферВвода = string.Empty;
                break;

            case ",":
                // Запятая добавляется в буфер один раз (F-E15).
                if (_новыйВвод)
                {
                    _буферВвода = "0";
                    _новыйВвод = false;
                }

                if (!_буферВвода.Contains(','))
                {
                    _буферВвода += ",";
                }

                _текущее = РазобратьБуфер();
                break;

            default:
                if (char.IsDigit(клавиша[0]))
                {
                    if (_новыйВвод)
                    {
                        _буферВвода = string.Empty;
                        _новыйВвод = false;
                    }

                    _буферВвода += клавиша;
                    _текущее = РазобратьБуфер();
                }

                break;
        }

        ОбновитьДисплей();
    }

    /// <summary>
    /// Разбирает буфер ввода в число; пустой или испорченный буфер даёт ноль.
    /// </summary>
    /// <returns>Число из буфера ввода.</returns>
    private double РазобратьБуфер()
    {
        if (_буферВвода.Length == 0)
        {
            return 0;
        }

        return double.TryParse(
            _буферВвода,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out var число) ? число : 0;
    }

    /// <summary>
    /// Обновляет содержимое дисплея.
    /// </summary>
    private void ОбновитьДисплей()
    {
        _дисплей.Text = _текущее.ToString("0.####", CultureInfo.CurrentCulture);
        _операция.Text = _знакОперации.Length > 0 ? $"{_накопитель:0.####} {_знакОперации}" : string.Empty;
    }
}
