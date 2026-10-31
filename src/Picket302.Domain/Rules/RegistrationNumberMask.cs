using System.Globalization;
using System.Text;

namespace Picket302.Domain.Rules;

/// <summary>
/// Форматирование регистрационного номера по маске (ТЗ §8.1, функция 25:
/// «Сквозная регистрационная нумерация», маска вида <c>ИСХ-{YYYY}/{SEQ:5}</c>).
/// </summary>
/// <remarks>
/// <para>
/// Поддерживаются два вида подстановок:
/// </para>
/// <list type="bullet">
/// <item><description><c>{YYYY}</c> — четырёхзначный год.</description></item>
/// <item><description>
/// <c>{SEQ:n}</c> — порядковый номер, дополненный нулями до <c>n</c> знаков.
/// </description></item>
/// </list>
/// <para>
/// Неизвестные подстановки сохраняются в виде обычного текста: маска приходит
/// из настроек оператора, и нераспознанный фрагмент не должен приводить к
/// потере номера.
/// </para>
/// </remarks>
public static class RegistrationNumberMask
{
    /// <summary>Подстановка года.</summary>
    public const string YearPlaceholder = "{YYYY}";

    /// <summary>Начало подстановки порядкового номера.</summary>
    public const string SequencePlaceholder = "{SEQ";

    /// <summary>
    /// Форматирует номер по маске.
    /// </summary>
    /// <param name="mask">Маска номера, например <c>ИСХ-{YYYY}/{SEQ:5}</c>.</param>
    /// <param name="year">Год, подставляемый в <c>{YYYY}</c>.</param>
    /// <param name="sequence">Порядковый номер, подставляемый в <c>{SEQ:n}</c>.</param>
    /// <returns>Строка номера по маске.</returns>
    public static string Format(string mask, int year, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mask);

        var результат = new StringBuilder(mask.Length + 16);
        var позиция = 0;

        while (позиция < mask.Length)
        {
            var начало = mask.IndexOf('{', позиция);

            if (начало < 0)
            {
                результат.Append(mask, позиция, mask.Length - позиция);
                break;
            }

            результат.Append(mask, позиция, начало - позиция);

            var конец = mask.IndexOf('}', начало);

            if (конец < 0)
            {
                // Незакрытая подстановка — обычный текст.
                результат.Append(mask, начало, mask.Length - начало);
                break;
            }

            var содержимое = mask.Substring(начало + 1, конец - начало - 1);

            результат.Append(Подставить(содержимое, year, sequence));
            позиция = конец + 1;
        }

        return результат.ToString();
    }

    /// <summary>
    /// Подставляет значение одиночной подстановки.
    /// </summary>
    /// <param name="содержимое">Содержимое подстановки без фигурных скобок.</param>
    /// <param name="year">Год.</param>
    /// <param name="sequence">Порядковый номер.</param>
    /// <returns>Подставленное значение либо исходный текст подстановки.</returns>
    private static string Подставить(string содержимое, int year, long sequence)
    {
        if (string.Equals(содержимое, "YYYY", StringComparison.Ordinal))
        {
            return year.ToString("D4", CultureInfo.InvariantCulture);
        }

        if (содержимое.StartsWith("SEQ", StringComparison.Ordinal))
        {
            var остаток = содержимое[3..];

            if (остаток.StartsWith(':') && int.TryParse(остаток[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var ширина))
            {
                return sequence.ToString($"D{Math.Max(1, ширина)}", CultureInfo.InvariantCulture);
            }

            return sequence.ToString(CultureInfo.InvariantCulture);
        }

        return "{" + содержимое + "}";
    }
}
