using Picket302.Domain.Common;

namespace Picket302.Domain.Rules;

/// <summary>
/// Проверка банковских реквизитов (ТЗ §8.1, функция 1: «валидирует ИНН
/// (10 или 12 цифр по контрольным суммам), КПП (9 символов), БИК (9 цифр)»).
/// </summary>
public static class BankRequisitesValidator
{
    /// <summary>Количество знаков КПП.</summary>
    public const int KppLength = 9;

    /// <summary>Количество знаков БИК.</summary>
    public const int BikLength = 9;

    /// <summary>
    /// Проверяет КПП: девять цифр.
    /// </summary>
    /// <param name="kpp">Проверяемое значение.</param>
    /// <returns>Успешный результат с нормализованным значением либо текст нарушения.</returns>
    public static Result<string> ValidateKpp(string? kpp)
    {
        if (string.IsNullOrWhiteSpace(kpp))
        {
            return Result<string>.Failure("КПП не задан.");
        }

        var значение = kpp.Trim();

        if (значение.Length != KppLength)
        {
            return Result<string>.Failure($"КПП должен содержать {KppLength} цифр.");
        }

        if (!значение.All(char.IsAsciiDigit))
        {
            return Result<string>.Failure("КПП должен состоять только из цифр.");
        }

        return Result<string>.Success(значение);
    }

    /// <summary>
    /// Проверяет БИК: девять цифр.
    /// </summary>
    /// <param name="bik">Проверяемое значение.</param>
    /// <returns>Успешный результат с нормализованным значением либо текст нарушения.</returns>
    public static Result<string> ValidateBik(string? bik)
    {
        if (string.IsNullOrWhiteSpace(bik))
        {
            return Result<string>.Failure("БИК не задан.");
        }

        var значение = bik.Trim();

        if (значение.Length != BikLength)
        {
            return Result<string>.Failure($"БИК должен содержать {BikLength} цифр.");
        }

        if (!значение.All(char.IsAsciiDigit))
        {
            return Result<string>.Failure("БИК должен состоять только из цифр.");
        }

        return Result<string>.Success(значение);
    }
}
