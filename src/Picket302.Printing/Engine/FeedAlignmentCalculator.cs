using System.Drawing;
using Picket302.Domain.Entities;

namespace Picket302.Printing.Engine;

/// <summary>
/// Программное выравнивание подачи конверта (ТЗ §8.4, функция 46:
/// <c>Printing.Engine.FeedAlignmentCalculator.CalculateOrigin</c>).
/// </summary>
/// <remarks>
/// Смещение начала координат зависит от конструкции лотка принтера: подача по
/// центру, по левому краю либо по правому краю.
/// </remarks>
public sealed class FeedAlignmentCalculator
{
    /// <summary>
    /// Рассчитывает смещение начала координат при подаче конверта в лоток.
    /// </summary>
    /// <param name="alignment">Способ подачи.</param>
    /// <param name="sheetSize">Размер листа, мм.</param>
    /// <param name="envelopeSize">Размер конверта, мм.</param>
    /// <returns>Смещение начала координат, мм.</returns>
    public PointF CalculateOrigin(FeedAlignment alignment, SizeF sheetSize, SizeF envelopeSize)
    {
        var отступX = (float)((sheetSize.Width - envelopeSize.Width) / 2.0);
        var отступY = (float)((sheetSize.Height - envelopeSize.Height) / 2.0);

        return alignment switch
        {
            FeedAlignment.Left => new PointF(0f, отступY),
            FeedAlignment.Right => new PointF(отступX * 2f, отступY),
            _ => new PointF(отступX, отступY),
        };
    }

    /// <summary>
    /// Рассчитывает смещение начала координат при подаче конверта в лоток с
    /// учётом ориентации подачи.
    /// </summary>
    /// <remarks>
    /// Если конверт подаётся в принтер узкой стороной, лист повёрнут на 90°,
    /// поэтому его габариты меняются местами: расчёт ведётся по переставленным
    /// ширине и высоте листа. При подаче широкой стороной результат совпадает
    /// с результатом <see cref="CalculateOrigin(FeedAlignment, SizeF, SizeF)"/>.
    /// </remarks>
    /// <param name="alignment">Способ подачи.</param>
    /// <param name="sheetSize">Размер листа, мм.</param>
    /// <param name="envelopeSize">Размер конверта, мм.</param>
    /// <param name="поУзкойСтороне">
    /// <see langword="true"/> — конверт подаётся узкой стороной (лист повёрнут),
    /// <see langword="false"/> — широкой стороной.
    /// </param>
    /// <returns>Смещение начала координат, мм.</returns>
    public PointF CalculateOrigin(
        FeedAlignment alignment,
        SizeF sheetSize,
        SizeF envelopeSize,
        bool поУзкойСтороне)
    {
        var лист = поУзкойСтороне
            ? new SizeF(sheetSize.Height, sheetSize.Width)
            : sheetSize;

        return CalculateOrigin(alignment, лист, envelopeSize);
    }
}
