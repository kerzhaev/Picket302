namespace Picket302.Domain.Common;

/// <summary>
/// Результат проверки настроек или реквизитов (ТЗ §8.7, функция 93:
/// <c>public ValidationResult ValidateSmtpSettings(SmtpSettings settings)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Тип единый для всех проверок настроек программы: проверка учётной записи SMTP,
/// реквизитов подключения к серверу БД, параметров лицензирования. Коды ошибок
/// не вводятся — в ТЗ их нет; перечень полей с текстом ошибки позволяет
/// показать сообщение рядом с конкретным полем.
/// </para>
/// </remarks>
public sealed class ValidationResult
{
    private static readonly ValidationError[] ПустыеОшибки = Array.Empty<ValidationError>();

    private readonly IReadOnlyList<ValidationError> _ошибки;

    private ValidationResult(IReadOnlyList<ValidationError> ошибки)
    {
        _ошибки = ошибки;
    }

    /// <summary>Признак отсутствия ошибок.</summary>
    public bool IsValid => _ошибки.Count == 0;

    /// <summary>Признак наличия ошибок.</summary>
    public bool HasErrors => !IsValid;

    /// <summary>Перечень ошибок проверки.</summary>
    public IReadOnlyList<ValidationError> Errors => _ошибки;

    /// <summary>
    /// Текст ошибки для поля; пустая строка, если поле корректно.
    /// </summary>
    /// <param name="field">Имя поля.</param>
    /// <returns>Текст ошибки либо пустая строка.</returns>
    public string ErrorFor(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        foreach (var ошибка in _ошибки)
        {
            if (string.Equals(ошибка.Field, field, StringComparison.Ordinal))
            {
                return ошибка.Message;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Сводный текст ошибок: сообщения перечисляются через точку с запятой.
    /// </summary>
    /// <returns>Сводный текст; пустая строка при отсутствии ошибок.</returns>
    public string Summary()
        => string.Join("; ", _ошибки.Select(ошибка => ошибка.Message));

    /// <summary>Создаёт результат без ошибок.</summary>
    /// <returns>Успешный результат проверки.</returns>
    public static ValidationResult Success() => new(ПустыеОшибки);

    /// <summary>
    /// Создаёт результат с одной ошибкой.
    /// </summary>
    /// <param name="field">Имя поля, в котором обнаружена ошибка.</param>
    /// <param name="message">Текст ошибки на русском языке.</param>
    /// <returns>Неуспешный результат проверки.</returns>
    public static ValidationResult Failure(string field, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ValidationResult(new[] { new ValidationError(field ?? string.Empty, message) });
    }

    /// <summary>
    /// Создаёт результат с перечнем ошибок.
    /// </summary>
    /// <param name="errors">Перечень ошибок проверки.</param>
    /// <returns>Результат проверки.</returns>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var список = errors.ToArray();

        return список.Length == 0 ? Success() : new ValidationResult(список);
    }
}

/// <summary>
/// Ошибка проверки настроек с указанием поля (ТЗ §8.7, функция 93).
/// </summary>
/// <param name="Field">Имя поля, в котором обнаружена ошибка.</param>
/// <param name="Message">Текст ошибки на русском языке.</param>
public sealed record ValidationError(string Field, string Message);
