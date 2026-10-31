using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Rules;

/// <summary>
/// Проверка ИНН (УНП) контрагента (ТЗ §8.1, функция 1: «валидирует ИНН
/// (10 или 12 цифр по контрольным суммам)»; §3.1.1: «ИНН (10 знаков для ЮЛ,
/// 12 для ФЛ) / УНП»).
/// </summary>
/// <remarks>
/// <para>
/// Контрольные суммы вычисляются по опубликованному алгоритму ФНС:
/// для десятизначного ИНН контрольной является десятая цифра, для
/// двенадцатизначного — одиннадцатая и двенадцатая.
/// </para>
/// <para>
/// Алгоритм контрольной суммы УНП (9 знаков) в ТЗ не приведён, поэтому для УНП
/// проверяется только формат — девять цифр. См. <c>docs/OPEN_QUESTIONS_LOG.md</c>.
/// </para>
/// </remarks>
public static class InnValidator
{
    /// <summary>Количество знаков ИНН юридического лица.</summary>
    public const int LegalEntityLength = 10;

    /// <summary>Количество знаков ИНН физического лица.</summary>
    public const int NaturalPersonLength = 12;

    /// <summary>Количество знаков УНП.</summary>
    public const int UnpLength = 9;

    /// <summary>Множители контрольной суммы десятизначного ИНН.</summary>
    private static readonly int[] Множители10 = [2, 4, 10, 3, 5, 9, 4, 6, 8];

    /// <summary>Множители контрольной суммы одиннадцатой цифры двенадцатизначного ИНН.</summary>
    private static readonly int[] Множители11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8];

    /// <summary>Множители контрольной суммы двенадцатой цифры двенадцатизначного ИНН.</summary>
    private static readonly int[] Множители12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8];

    /// <summary>
    /// Проверяет ИНН (УНП) контрагента.
    /// </summary>
    /// <param name="inn">Значение ИНН либо УНП.</param>
    /// <param name="type">Тип контрагента.</param>
    /// <returns>
    /// Успешный результат с нормализованным значением либо текст нарушения.
    /// </returns>
    public static Result<string> Validate(string? inn, CounterpartyType type)
    {
        if (string.IsNullOrWhiteSpace(inn))
        {
            return Result<string>.Failure("ИНН (УНП) не задан.");
        }

        var значение = inn.Trim();

        if (!значение.All(char.IsAsciiDigit))
        {
            return Result<string>.Failure("ИНН (УНП) должен состоять только из цифр.");
        }

        switch (значение.Length)
        {
            case LegalEntityLength:
                if (type == CounterpartyType.NaturalPerson)
                {
                    return Result<string>.Failure(
                        $"ИНН физического лица должен содержать {NaturalPersonLength} цифр.");
                }

                return IsValidLegalEntityInn(значение)
                    ? Result<string>.Success(значение)
                    : Result<string>.Failure("Некорректная контрольная сумма ИНН.");

            case NaturalPersonLength:
                if (type == CounterpartyType.LegalEntity)
                {
                    return Result<string>.Failure(
                        $"ИНН юридического лица должен содержать {LegalEntityLength} цифр.");
                }

                return IsValidNaturalPersonInn(значение)
                    ? Result<string>.Success(значение)
                    : Result<string>.Failure("Некорректная контрольная сумма ИНН.");

            case UnpLength:
                // Алгоритм контрольной суммы УНП в ТЗ не приведён — проверяется формат.
                return Result<string>.Success(значение);

            default:
                return Result<string>.Failure(
                    $"ИНН должен содержать {LegalEntityLength} цифр (юридическое лицо), "
                    + $"{NaturalPersonLength} цифр (физическое лицо) либо {UnpLength} цифр (УНП).");
        }
    }

    /// <summary>
    /// Проверяет контрольную сумму десятизначного ИНН юридического лица.
    /// </summary>
    /// <param name="inn">Десятизначный ИНН.</param>
    /// <returns><see langword="true"/>, если контрольная сумма верна.</returns>
    public static bool IsValidLegalEntityInn(string inn)
        => inn.Length == LegalEntityLength
            && inn.All(char.IsAsciiDigit)
            && КонтрольнаяЦифра(inn, Множители10, LegalEntityLength - 1) == inn[LegalEntityLength - 1] - '0';

    /// <summary>
    /// Проверяет контрольные суммы двенадцатизначного ИНН физического лица.
    /// </summary>
    /// <param name="inn">Двенадцатизначный ИНН.</param>
    /// <returns><see langword="true"/>, если обе контрольные суммы верны.</returns>
    public static bool IsValidNaturalPersonInn(string inn)
        => inn.Length == NaturalPersonLength
            && inn.All(char.IsAsciiDigit)
            && КонтрольнаяЦифра(inn, Множители11, NaturalPersonLength - 2) == inn[NaturalPersonLength - 2] - '0'
            && КонтрольнаяЦифра(inn, Множители12, NaturalPersonLength - 1) == inn[NaturalPersonLength - 1] - '0';

    /// <summary>
    /// Вычисляет контрольную цифру по сумме произведений цифр на множители.
    /// </summary>
    /// <param name="inn">Проверяемое число.</param>
    /// <param name="множители">Множители контрольной суммы.</param>
    /// <param name="длина">Число учитываемых цифр.</param>
    /// <returns>Контрольная цифра (0–9).</returns>
    private static int КонтрольнаяЦифра(string inn, int[] множители, int длина)
    {
        var сумма = 0;

        for (var i = 0; i < длина; i++)
        {
            сумма += (inn[i] - '0') * множители[i];
        }

        return (сумма % 11) % 10;
    }
}
