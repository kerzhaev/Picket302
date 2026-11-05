using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Picket302.Infrastructure.Templates;

/// <summary>
/// Элемент локального каталога клипартов (ТЗ §8.10, функция 158).
/// </summary>
/// <param name="Id">Идентификатор элемента.</param>
/// <param name="Name">Русское наименование для каталога и подсказок.</param>
public sealed record ClipartItem(string Id, string Name);

/// <summary>
/// Локальный каталог клипартов (ТЗ §8.10, функция 158): встроенные
/// графические элементы почтовой тематики вычерчиваются кодом и раздаются
/// как PNG. Каталог не обращается к сети, не читает внешних файлов и не
/// тянет тяжёлых бинарных ресурсов — рисунки детерминированы и воспроизводимы.
/// </summary>
/// <remarks>
/// Элемент назначается логотипом контрагента (колонка <c>logo</c>) и
/// печатается на конверте существующим конвейером <c>EnvelopeRenderer</c>;
/// изменение формата бланков для клипарта не требуется.
/// </remarks>
public sealed class ClipartLibrary
{
    /// <summary>Сторона квадратного растра элемента в пикселях.</summary>
    public const int РазмерЭлемента = 128;

    /// <summary>Цвет линий рисунка: нейтральный тёмно-серый интерфейсный тон.</summary>
    private static readonly Color Чернила = Color.FromArgb(55, 65, 81);

    /// <summary>Цвет заливки акцентных деталей рисунка.</summary>
    private static readonly Color Заливка = Color.FromArgb(0, 102, 153);

    private static readonly ClipartItem[] Элементы =
    [
        new("конверт", "Конверт"),
        new("марка", "Почтовая марка"),
        new("голубь", "Голубь мира"),
        new("штамп", "Штамп «Отправлено»"),
        new("стрелка", "Стрелка"),
        new("звезда", "Звезда"),
        new("рамка", "Рамка"),
        new("галочка", "Галочка"),
        new("рожок", "Почтовый рожок"),
        new("шкала", "Штрихкод"),
    ];

    private readonly Dictionary<string, byte[]> _кэш = [];

    /// <summary>
    /// Создаёт каталог клипартов.
    /// </summary>
    public ClipartLibrary()
    {
        foreach (var элемент in Элементы)
        {
            _кэш[элемент.Id] = Нарисовать(элемент.Id);
        }
    }

    /// <summary>Возвращает перечень элементов каталога.</summary>
    public IReadOnlyList<ClipartItem> Items => Элементы;

    /// <summary>
    /// Возвращает PNG-растр элемента по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор элемента.</param>
    /// <param name="png">PNG-байты рисунка.</param>
    /// <returns>
    /// <see langword="true"/>, если элемент найден; отсутствующий или
    /// повреждённый идентификатор не приводит к падению.
    /// </returns>
    public bool TryGet(string id, out byte[] png)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_кэш.TryGetValue(id, out var байты) && байты.Length > 0)
        {
            png = байты;
            return true;
        }

        png = [];
        return false;
    }

    /// <summary>Вычерчивает элемент каталога как PNG.</summary>
    /// <param name="id">Идентификатор элемента.</param>
    /// <returns>PNG-байты.</returns>
    private static byte[] Нарисовать(string id)
    {
        using var растр = new Bitmap(РазмерЭлемента, РазмерЭлемента);
        using var графика = Graphics.FromImage(растр);
        графика.SmoothingMode = SmoothingMode.AntiAlias;
        графика.Clear(Color.White);

        using var перо = new Pen(Чернила, 5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var кисть = new SolidBrush(Заливка);

        switch (id)
        {
            case "конверт":
                графика.DrawRectangle(перо, 16, 36, 96, 60);
                графика.DrawLine(перо, 16, 36, 64, 76);
                графика.DrawLine(перо, 64, 76, 112, 36);
                break;

            case "марка":
                графика.DrawRectangle(перо, 24, 24, 80, 80);
                графика.DrawRectangle(перо, 38, 38, 52, 52);
                графика.FillEllipse(кисть, 56, 56, 16, 16);
                break;

            case "голубь":
                графика.DrawBezier(
                    перо,
                    new Point(20, 74),
                    new Point(38, 40),
                    new Point(84, 34),
                    new Point(104, 58));
                графика.DrawBezier(
                    перо,
                    new Point(104, 58),
                    new Point(88, 66),
                    new Point(56, 70),
                    new Point(36, 90));
                графика.FillEllipse(кисть, 100, 52, 10, 10);
                графика.DrawLine(перо, 20, 74, 40, 88);
                break;

            case "штамп":
                графика.DrawEllipse(перо, 22, 22, 84, 84);
                графика.DrawEllipse(перо, 32, 32, 64, 64);
                using (var шрифт = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point))
                {
                    var размер = графика.MeasureString("ОТПР.", шрифт);
                    графика.DrawString(
                        "ОТПР.",
                        шрифт,
                        кисть,
                        (РазмерЭлемента - размер.Width) / 2f,
                        (РазмерЭлемента - размер.Height) / 2f);
                }

                break;

            case "стрелка":
                графика.DrawLine(перо, 24, 64, 100, 64);
                графика.DrawLine(перо, 76, 40, 100, 64);
                графика.DrawLine(перо, 76, 88, 100, 64);
                break;

            case "звезда":
                {
                    var точки = new PointF[10];
                    for (var i = 0; i < 10; i++)
                    {
                        var радиус = i % 2 == 0 ? 48f : 20f;
                        var угол = -Math.PI / 2 + i * Math.PI / 5;
                        точки[i] = new PointF(
                            64f + (float)(радиус * Math.Cos(угол)),
                            64f + (float)(радиус * Math.Sin(угол)));
                    }

                    графика.FillPolygon(кисть, точки);
                    break;
                }

            case "рамка":
                графика.DrawRectangle(перо, 20, 20, 88, 88);
                графика.DrawRectangle(перо, 30, 30, 68, 68);
                break;

            case "галочка":
                графика.DrawLine(перо, 24, 66, 52, 94);
                графика.DrawLine(перо, 52, 94, 104, 34);
                break;

            case "рожок":
                графика.DrawArc(перо, 18, 40, 84, 48, 200, 140);
                графика.DrawLine(перо, 30, 52, 30, 84);
                графика.FillEllipse(кисть, 24, 46, 12, 12);
                графика.DrawLine(перо, 98, 46, 112, 34);
                графика.DrawLine(перо, 100, 56, 116, 50);
                break;

            case "шкала":
                {
                    var x = 20f;
                    var рисунки = new[] { 6f, 3f, 6f, 2f, 8f, 3f, 5f, 2f, 6f, 3f, 7f, 2f, 5f };
                    foreach (var толщина in рисунки)
                    {
                        графика.FillRectangle(кисть, x, 44f, толщина, 40f);
                        x += толщина + 4f;
                    }

                    break;
                }

            default:
                графика.DrawRectangle(перо, 24, 24, 80, 80);
                break;
        }

        using var поток = new MemoryStream();
        растр.Save(поток, ImageFormat.Png);
        return поток.ToArray();
    }
}
