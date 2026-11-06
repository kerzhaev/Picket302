using System.Drawing;
using System.Windows.Forms;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки реальной компоновки: перевод границ контрола в координаты
/// формы через цепочку родителей. Прямое сравнение
/// <c>контрол.Bounds</c> (родительских координат) с
/// <c>формой.ClientRectangle</c> некорректно, когда контрол вложен в
/// Panel/TableLayoutPanel/FlowLayoutPanel.
/// </summary>
public static class LayoutAssertions
{
    /// <summary>
    /// Возвращает границы контрола в координатах формы, суммируя смещения
    /// по цепочке родителей.
    /// </summary>
    /// <param name="контрол">Проверяемый контрол.</param>
    /// <param name="форма">Форма-вершина.</param>
    /// <returns>Прямоугольник в координатах клиентской области формы.</returns>
    public static Rectangle ГраницыВФорме(Control контрол, Form форма)
    {
        ArgumentNullException.ThrowIfNull(контрол);
        ArgumentNullException.ThrowIfNull(форма);

        var x = контрол.Left;
        var y = контрол.Top;

        for (var родитель = контрол.Parent; родитель is not null && !ReferenceEquals(родитель, форма); родитель = родитель.Parent)
        {
            x += родитель.Left;
            y += родитель.Top;
        }

        return new Rectangle(x, y, контрол.Width, контрол.Height);
    }

    /// <summary>
    /// Проверяет, что контрол полностью внутри клиентской области формы.
    /// </summary>
    /// <param name="контрол">Контрол.</param>
    /// <param name="форма">Форма.</param>
    /// <returns>Сообщение об ошибке либо <see langword="null"/>.</returns>
    public static string? ПричинаВыходаЗаГраницы(Control контрол, Form форма)
    {
        var границы = ГраницыВФорме(контрол, форма);

        return форма.ClientRectangle.Contains(границы)
            ? null
            : $"Контрол «{контрол.Text}» вне клиентской области {форма.ClientRectangle}: {границы}.";
    }
}
