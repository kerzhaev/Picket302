using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Статус ключа, снятие ключа и активация на нескольких управляющих машинах
/// (ТЗ §8.10, функция 123: <c>public LicenseStatus GetStatus();
/// public void RemoveKey()</c>).
/// </summary>
/// <remarks>
/// Снятие ключа возвращает пробный режим с сохранением данных: удаляются
/// только сведения о лицензии, пользовательские данные не затрагиваются.
/// </remarks>
public sealed class LicenseManager
{
    private readonly LicenseStore _хранилище;
    private readonly LicenseStateMachine _автомат;

    /// <summary>
    /// Создаёт менеджер лицензии.
    /// </summary>
    /// <param name="store">Хранилище данных лицензии.</param>
    /// <param name="machine">Автомат состояний лицензии.</param>
    /// <exception cref="ArgumentNullException">Хранилище или автомат не заданы.</exception>
    public LicenseManager(LicenseStore store, LicenseStateMachine machine)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(machine);

        _хранилище = store;
        _автомат = machine;
    }

    /// <summary>
    /// Возвращает статус лицензии: редакция, срок истечения, владелец.
    /// </summary>
    /// <returns>Текущий статус лицензии.</returns>
    public LicenseStatus GetStatus()
    {
        _автомат.EvaluateState();

        return _автомат.Status;
    }

    /// <summary>
    /// Снимает ключ: лицензия удаляется, программа возвращается в пробный
    /// режим, пользовательские данные сохраняются.
    /// </summary>
    public void RemoveKey()
    {
        _хранилище.RemoveKey();

        _автомат.EvaluateState();
    }

    /// <summary>
    /// Добавляет привязку лицензии к управляющей машине: один ключ может
    /// обслуживать несколько машин в пределах лимита привязок
    /// (ТЗ §8.10, функция 123).
    /// </summary>
    /// <param name="machineId">Идентификатор оборудования.</param>
    /// <returns>
    /// <see langword="true"/> — привязка зарегистрирована либо уже была
    /// зарегистрирована; <see langword="false"/> — исчерпан предел привязок
    /// либо лицензия отсутствует.
    /// </returns>
    public bool AddBinding(string machineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        if (!_хранилище.TryLoad(out var лицензия))
        {
            return false;
        }

        return _хранилище.AddBinding(machineId, DateTime.UtcNow, лицензия.MaxBindings);
    }
}
