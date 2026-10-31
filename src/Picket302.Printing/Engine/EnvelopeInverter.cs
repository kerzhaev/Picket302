using Picket302.Domain.Entities;

namespace Picket302.Printing.Engine;

/// <summary>
/// Перестановка зон адреса отправителя и получателя (ТЗ §8.4, функция 43:
/// <c>Printing.Engine.EnvelopeInverter.InvertAddresses</c>).
/// </summary>
/// <remarks>
/// Зоны меняются местами для печати на клапане конверта и для
/// специализированных обратных отправлений: блок «Кому» переносится в
/// верхний левый угол, блок «От кого» — в правый нижний.
/// </remarks>
public sealed class EnvelopeInverter
{
    /// <summary>Роль зоны адресата.</summary>
    public const string RecipientZone = "BottomRecipient";

    /// <summary>Роль зоны отправителя.</summary>
    public const string SenderZone = "TopSender";

    /// <summary>
    /// Меняет местами зоны расположения адреса отправителя и получателя.
    /// </summary>
    /// <param name="fields">Поля бланка; координаты изменяются на месте.</param>
    public void InvertAddresses(IReadOnlyList<BlankField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var зонаПолучателя = fields
            .Where(поле => string.Equals(поле.ZoneRole, RecipientZone, StringComparison.Ordinal))
            .ToList();

        var зонаОтправителя = fields
            .Where(поле => string.Equals(поле.ZoneRole, SenderZone, StringComparison.Ordinal))
            .ToList();

        if (зонаПолучателя.Count == 0 || зонаОтправителя.Count == 0)
        {
            return;
        }

        var началоПолучателя = НачалоЗоны(зонаПолучателя);
        var началоОтправителя = НачалоЗоны(зонаОтправителя);

        foreach (var поле in зонаПолучателя)
        {
            поле.XMm += началоОтправителя.X - началоПолучателя.X;
            поле.YMm += началоОтправителя.Y - началоПолучателя.Y;
        }

        foreach (var поле in зонаОтправителя)
        {
            поле.XMm += началоПолучателя.X - началоОтправителя.X;
            поле.YMm += началоПолучателя.Y - началоОтправителя.Y;
        }
    }

    private static (decimal X, decimal Y) НачалоЗоны(IReadOnlyList<BlankField> поля)
    {
        var началоX = поля.Min(поле => поле.XMm);
        var началоY = поля.Min(поле => поле.YMm);

        return (началоX, началоY);
    }
}
