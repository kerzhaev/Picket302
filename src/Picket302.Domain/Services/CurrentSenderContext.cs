namespace Picket302.Domain.Services;

/// <summary>
/// Глобальный текущий отправитель (ТЗ §8.1, функция 16: «Глобальный текущий
/// отправитель»).
/// </summary>
/// <remarks>
/// <para>
/// Контекст хранит идентификатор активного отправителя и подставляет его
/// реквизиты во все вновь создаваемые документы. Хранение идентификатора —
/// обязанность хранилища: <see cref="ICurrentSenderStore"/> (реестр
/// <c>HKCU\Software\PEPro\CurrentSender</c> и таблица <c>parameters</c>).
/// </para>
/// <para>
/// Модель единственная: альтернативная отдельная таблица отправителей не
/// ведётся — отправителем является запись адресной книги с установленным
/// флагом <c>is_sender</c> (ТЗ §8.1, функция 139).
/// </para>
/// </remarks>
public sealed class CurrentSenderContext
{
    private readonly ICurrentSenderStore _хранилище;

    /// <summary>Создаёт контекст текущего отправителя.</summary>
    /// <param name="хранилище">Хранилище идентификатора активного отправителя.</param>
    public CurrentSenderContext(ICurrentSenderStore хранилище)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        _хранилище = хранилище;
    }

    /// <summary>
    /// Идентификатор активного отправителя; <see langword="null"/> — отправитель
    /// не выбран.
    /// </summary>
    public long? CurrentSenderId { get; private set; }

    /// <summary>
    /// Назначает активного отправителя и сохраняет выбор в хранилище
    /// (ТЗ §8.1, функция 16).
    /// </summary>
    /// <param name="senderId">Идентификатор отправителя.</param>
    public void SetGlobalSender(long senderId)
    {
        if (senderId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senderId), senderId, "Идентификатор отправителя не задан.");
        }

        CurrentSenderId = senderId;
        _хранилище.Write(senderId);
    }

    /// <summary>
    /// Сбрасывает выбор отправителя и удаляет его из хранилища.
    /// </summary>
    public void ClearGlobalSender()
    {
        CurrentSenderId = null;
        _хранилище.Write(null);
    }

    /// <summary>
    /// Читает сохранённого отправителя из хранилища — значение, записанное
    /// предыдущим запуском программы.
    /// </summary>
    /// <returns>Идентификатор сохранённого отправителя либо <see langword="null"/>.</returns>
    public long? Load()
    {
        CurrentSenderId = _хранилище.Read();
        return CurrentSenderId;
    }

    /// <summary>
    /// Проверяет, что сохранённый отправитель существует; если отправитель
    /// удалён, выбор сбрасывается на первую запись справочника
    /// (ТЗ §8.1, функция 16: «Удаление текущего отправителя сбрасывает контекст
    /// на первую запись из справочника»).
    /// </summary>
    /// <param name="существующиеОтправители">
    /// Идентификаторы существующих отправителей справочника.
    /// </param>
    /// <returns>Идентификатор действующего отправителя либо <see langword="null"/>.</returns>
    public long? Resolve(IReadOnlyList<long> существующиеОтправители)
    {
        ArgumentNullException.ThrowIfNull(существующиеОтправители);

        var сохранённый = Load();

        if (сохранённый is not null && существующиеОтправители.Contains(сохранённый.Value))
        {
            return сохранённый;
        }

        var первый = существующиеОтправители.Count > 0 ? существующиеОтправители[0] : (long?)null;

        if (первый is null)
        {
            ClearGlobalSender();
            return null;
        }

        SetGlobalSender(первый.Value);
        return первый;
    }
}

/// <summary>
/// Хранилище идентификатора активного отправителя (ТЗ §8.1, функция 16).
/// </summary>
public interface ICurrentSenderStore
{
    /// <summary>Читает сохранённый идентификатор отправителя.</summary>
    /// <returns>Идентификатор отправителя либо <see langword="null"/>.</returns>
    long? Read();

    /// <summary>Сохраняет идентификатор отправителя.</summary>
    /// <param name="senderId">
    /// Идентификатор отправителя; <see langword="null"/> — выбор сброшен.
    /// </param>
    void Write(long? senderId);
}
