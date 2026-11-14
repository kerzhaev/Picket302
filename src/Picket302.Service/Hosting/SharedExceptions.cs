using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Основа ошибок серверной команды: ошибка, которую можно показать оператору.
/// </summary>
public abstract class SharedCommandException : Exception
{
    /// <summary>
    /// Создаёт ошибку команды.
    /// </summary>
    /// <param name="код">Код ошибки протокола.</param>
    /// <param name="сообщение">Сообщение оператору на русском языке.</param>
    /// <param name="подробности">Дополнительные сведения.</param>
    protected SharedCommandException(string код, string сообщение, string? подробности = null)
        : base(сообщение)
    {
        Код = код;
        Сообщение = сообщение;
        Подробности = подробности;
    }

    /// <summary>Код ошибки протокола.</summary>
    public string Код { get; }

    /// <summary>Сообщение оператору.</summary>
    public string Сообщение { get; }

    /// <summary>Дополнительные сведения.</summary>
    public string? Подробности { get; }
}

/// <summary>
/// Запись изменена другим рабочим местом: версия устарела.
/// </summary>
public sealed class SharedConflictException : SharedCommandException
{
    /// <summary>
    /// Создаёт ошибку конфликта версий.
    /// </summary>
    /// <param name="сообщение">Сообщение оператору.</param>
    /// <param name="подробности">Дополнительные сведения.</param>
    public SharedConflictException(string сообщение, string? подробности = null)
        : base(SharedErrorCodes.Conflict, сообщение, подробности)
    {
    }
}

/// <summary>
/// Данные не прошли проверку доменных правил.
/// </summary>
public sealed class SharedValidationException : SharedCommandException
{
    /// <summary>
    /// Создаёт ошибку проверки данных.
    /// </summary>
    /// <param name="сообщение">Сообщение оператору.</param>
    public SharedValidationException(string сообщение)
        : base(SharedErrorCodes.ValidationFailed, сообщение)
    {
    }
}

/// <summary>
/// Запрошенная запись не найдена.
/// </summary>
public sealed class SharedNotFoundException : SharedCommandException
{
    /// <summary>
    /// Создаёт ошибку «не найдено».
    /// </summary>
    /// <param name="сообщение">Сообщение оператору.</param>
    public SharedNotFoundException(string сообщение)
        : base(SharedErrorCodes.NotFound, сообщение)
    {
    }
}

/// <summary>
/// На серверном компьютере не хватает места для записи.
/// </summary>
public sealed class SharedInsufficientSpaceException : SharedCommandException
{
    /// <summary>
    /// Создаёт ошибку нехватки места.
    /// </summary>
    /// <param name="сообщение">Сообщение оператору.</param>
    public SharedInsufficientSpaceException(string сообщение)
        : base(SharedErrorCodes.InsufficientSpace, сообщение)
    {
    }
}
