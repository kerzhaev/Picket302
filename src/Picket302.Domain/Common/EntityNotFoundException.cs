namespace Picket302.Domain.Common;

/// <summary>
/// Ошибка «запись не найдена» (ТЗ §8.1, функция 1: «Если запись не найдена —
/// генерация <c>EntityNotFoundException</c>»).
/// </summary>
public sealed class EntityNotFoundException : Exception
{
    /// <summary>Создаёт исключение с текстом нарушения.</summary>
    /// <param name="message">Текст нарушения.</param>
    public EntityNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт исключение с текстом нарушения и внутренней причиной.</summary>
    /// <param name="message">Текст нарушения.</param>
    /// <param name="innerException">Внутренняя причина.</param>
    public EntityNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Создаёт исключение для сущности указанного типа с заданным идентификатором.
    /// </summary>
    /// <typeparam name="T">Тип сущности.</typeparam>
    /// <param name="id">Идентификатор записи.</param>
    /// <returns>Исключение с готовым текстом нарушения.</returns>
    public static EntityNotFoundException For<T>(long id)
        => new($"{typeof(T).Name} с идентификатором {id} не найден.");
}
