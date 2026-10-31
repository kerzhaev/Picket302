using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Режим сортировки писем в реестре (ТЗ §8.4, функция 59: «по исходящему
/// номеру, по алфавиту получателей, по индексу места назначения»).
/// </summary>
public enum RegistrySortMode
{
    /// <summary>По исходящему номеру.</summary>
    ByOutgoingNumber = 0,

    /// <summary>По алфавиту получателей.</summary>
    ByRecipientName = 1,

    /// <summary>По индексу места назначения.</summary>
    ByDestinationZip = 2,
}

/// <summary>
/// Настраиваемая сортировка писем в реестре (ТЗ §8.4, функция 59:
/// <c>Application.Services.RegistrySorter.SortMails</c>).
/// </summary>
public sealed class RegistrySorter
{
    /// <summary>
    /// Сортирует список строк реестра (ТЗ §8.4, функция 59).
    /// </summary>
    /// <param name="mails">Коллекция отправлений.</param>
    /// <param name="mode">Режим сортировки.</param>
    /// <param name="recipientNameResolver">
    /// Разрешитель наименования получателя по идентификатору контрагента;
    /// обязателен для режима «по алфавиту получателей».
    /// </param>
    /// <returns>Отсортированный список.</returns>
    /// <remarks>
    /// Сортировка по алфавиту ставит получателя «Абрамов» выше «Борисов»
    /// независимо от даты создания письма; исходящий номер сравнивается
    /// естественно (числовые фрагменты — по значению).
    /// </remarks>
    public IEnumerable<Mail> SortMails(
        IEnumerable<Mail> mails,
        RegistrySortMode mode,
        Func<long, string>? recipientNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(mails);

        return mode switch
        {
            RegistrySortMode.ByOutgoingNumber => mails
                .OrderBy(письмо => письмо.OutgoingNumber, NaturalComparer.Instance),
            RegistrySortMode.ByRecipientName => SortByRecipientName(mails, recipientNameResolver),
            RegistrySortMode.ByDestinationZip => mails
                .OrderBy(письмо => письмо.RecipientAddress?.Zip, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Неизвестный режим сортировки реестра."),
        };
    }

    private static IEnumerable<Mail> SortByRecipientName(
        IEnumerable<Mail> письма,
        Func<long, string>? разрешитель)
    {
        if (разрешитель is null)
        {
            throw new ArgumentException(
                "Для сортировки по алфавиту получателей требуется разрешитель наименований контрагентов.",
                nameof(разрешитель));
        }

        return письма
            .OrderBy(письмо => разрешитель(письмо.CounterpartyId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(письмо => письмо.Id);
    }

    /// <summary>
    /// Естественное сравнение строк: числовые фрагменты сравниваются по
    /// значению, поэтому «№9» стоит раньше «№12».
    /// </summary>
    private sealed class NaturalComparer : IComparer<string?>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var i = 0;
            var j = 0;

            while (i < left.Length && j < right.Length)
            {
                if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
                {
                    var началоI = i;

                    while (i < left.Length && char.IsDigit(left[i]))
                    {
                        i++;
                    }

                    var началоJ = j;

                    while (j < right.Length && char.IsDigit(right[j]))
                    {
                        j++;
                    }

                    // Числовые фрагменты сравниваются по значению.
                    var числоI = long.Parse(left[началоI..i], System.Globalization.CultureInfo.InvariantCulture);
                    var числоJ = long.Parse(right[началоJ..j], System.Globalization.CultureInfo.InvariantCulture);

                    if (числоI != числоJ)
                    {
                        return числоI.CompareTo(числоJ);
                    }

                    continue;
                }

                var разница = left[i].CompareTo(right[j]);

                if (разница != 0)
                {
                    return разница;
                }

                i++;
                j++;
            }

            return (left.Length - i).CompareTo(right.Length - j);
        }
    }
}
