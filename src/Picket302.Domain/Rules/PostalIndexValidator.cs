using Picket302.Domain.Common;

namespace Picket302.Domain.Rules;

/// <summary>
/// Проверка почтового индекса (ТЗ §3.1.1: индекс <c>VARCHAR(6)</c>;
/// §8.1, функция 4).
/// </summary>
public static class PostalIndexValidator
{
    /// <summary>Количество знаков почтового индекса.</summary>
    public const int Length = 6;

    /// <summary>
    /// Проверяет, что значение является почтовым индексом: ровно шесть цифр.
    /// </summary>
    /// <param name="index">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение корректно.</returns>
    public static bool IsValid(string? index)
        => index is not null
            && index.Length == Length
            && index.All(char.IsAsciiDigit);

    /// <summary>
    /// Проверяет почтовый индекс и возвращает результат с нормализованным значением.
    /// </summary>
    /// <param name="index">Проверяемое значение.</param>
    /// <returns>Успешный результат либо текст нарушения.</returns>
    public static Result<string> Validate(string? index)
    {
        if (string.IsNullOrWhiteSpace(index))
        {
            return Result<string>.Failure("Почтовый индекс не задан.");
        }

        var значение = index.Trim();

        if (значение.Length != Length)
        {
            return Result<string>.Failure(
                $"Почтовый индекс должен состоять ровно из {Length} цифровых символов.");
        }

        if (!значение.All(char.IsAsciiDigit))
        {
            return Result<string>.Failure("Почтовый индекс должен состоять только из цифр.");
        }

        return Result<string>.Success(значение);
    }
}
