namespace Picket302.Domain.Common;

/// <summary>
/// Результат операции доменного слоя: либо значение, либо описание ошибки.
/// </summary>
/// <remarks>
/// В ТЗ (<c>SPEC_INPUT/IMPLEMENTATION_SPEC.md</c>) тип <c>Result&lt;T&gt;</c> используется
/// как тип возврата доменных операций (§8.1, функция 4: <c>public static Result&lt;Address&gt; Create(...)</c>),
/// но его состав не описан. Принят минимальный состав: признак успеха, значение,
/// текст ошибки и список предупреждений. Коды ошибок не вводятся — в ТЗ их нет.
/// </remarks>
/// <typeparam name="T">Тип значения при успешном исходе.</typeparam>
public sealed class Result<T> where T : notnull
{
    private static readonly string[] ПустыеПредупреждения = Array.Empty<string>();

    private readonly T _value;

    private Result(T value, string? error, IReadOnlyList<string> warnings)
    {
        IsSuccess = error is null;
        _value = value;
        Error = error;
        Warnings = warnings;
    }

    /// <summary>Признак успешного исхода операции.</summary>
    public bool IsSuccess { get; }

    /// <summary>Признак неуспешного исхода операции.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Текст ошибки; <see langword="null"/> при успешном исходе.</summary>
    public string? Error { get; }

    /// <summary>
    /// Предупреждения, возникшие при успешном исходе (например, обрезка слишком длинной
    /// адресной строки). При неуспешном исходе пуст.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Значение успешного исхода. Обращение при неуспешном исходе выбрасывает
    /// <see cref="InvalidOperationException"/> — проверять <see cref="IsSuccess"/> обязательно.
    /// </summary>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"Значение результата недоступно: операция завершилась ошибкой. {Error}");

    /// <summary>Извлекает значение без исключения при неуспешном исходе.</summary>
    /// <param name="value">
    /// Значение успешного исхода; при неуспешном исходе — значение по умолчанию
    /// для типа <typeparamref name="T"/>.
    /// </param>
    /// <returns><see langword="true"/>, если операция завершилась успешно.</returns>
    public bool TryGetValue(out T value)
    {
        value = IsSuccess ? _value : default!;
        return IsSuccess;
    }

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="value">Значение успешного исхода.</param>
    /// <param name="warnings">Предупреждения, не препятствующие успешному исходу.</param>
    /// <returns>Успешный результат.</returns>
    public static Result<T> Success(T value, params string[] warnings)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(warnings);

        IReadOnlyList<string> copy = warnings.Length == 0
            ? ПустыеПредупреждения
            : Array.AsReadOnly((string[])warnings.Clone());

        return new Result<T>(value, null, copy);
    }

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="error">Текст ошибки на русском языке.</param>
    /// <returns>Неуспешный результат.</returns>
    public static Result<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result<T>(default!, error, ПустыеПредупреждения);
    }
}
