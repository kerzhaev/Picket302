using System.Drawing;

namespace Picket302.Printing.Elements;

/// <summary>
/// Графический контекст рендеринга (ТЗ §6.1, §8.6, функции 90 и 47).
/// </summary>
/// <remarks>
/// Все размеры задаются в миллиметрах: конвейер печати работает в физических
/// единицах ГОСТ, а не в пикселях экрана.
/// </remarks>
public interface IGraphicsContext
{
    /// <summary>Цвет отрисовки.</summary>
    Color Color { get; set; }

    /// <summary>Заливает прямоугольник.</summary>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    /// <param name="widthMm">Ширина, мм.</param>
    /// <param name="heightMm">Высота, мм.</param>
    void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm);

    /// <summary>Рисует строку текста в заданных габаритах.</summary>
    /// <param name="text">Текст.</param>
    /// <param name="fontName">Гарнитура шрифта.</param>
    /// <param name="widthMm">Ширина габарита текста, мм.</param>
    /// <param name="heightMm">Высота габарита текста, мм.</param>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    void DrawStringMm(
        string text,
        string fontName,
        float widthMm,
        float heightMm,
        float xMm,
        float yMm);

    /// <summary>
    /// Рисует строку текста с явным кеглем и начертанием (ТЗ §8.3: отметки
    /// «Заказное»/«С уведомлением» — 12pt bold).
    /// </summary>
    /// <param name="text">Текст.</param>
    /// <param name="fontName">Гарнитура шрифта.</param>
    /// <param name="sizePt">Кегль, пунктов.</param>
    /// <param name="bold">Полужирное начертание.</param>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    void DrawStringMm(
        string text,
        string fontName,
        float sizePt,
        bool bold,
        float xMm,
        float yMm);

    /// <summary>Рисует контур прямоугольника.</summary>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    /// <param name="widthMm">Ширина, мм.</param>
    /// <param name="heightMm">Высота, мм.</param>
    void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm);

    /// <summary>Выводит растровое изображение в заданные габариты.</summary>
    /// <param name="image">Изображение.</param>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    /// <param name="widthMm">Ширина, мм.</param>
    /// <param name="heightMm">Высота, мм.</param>
    void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm);
}
