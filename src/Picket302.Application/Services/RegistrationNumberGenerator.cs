using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Сквозная регистрационная нумерация
/// (ТЗ §8.1, функция 25: «Сквозная регистрационная нумерация»).
/// </summary>
/// <remarks>
/// Генератор атомарно инкрементирует счётчик отправлений и формирует строку
/// номера по маске оператора (например, <c>ИСХ-{YYYY}/{SEQ:5}</c>). Два
/// параллельных запроса получают разные последовательные номера без коллизий.
/// </remarks>
public sealed class RegistrationNumberGenerator
{
    /// <summary>Маска номера по умолчанию (ТЗ §8.1, функция 25).</summary>
    public const string DefaultTemplateMask = "ИСХ-{YYYY}/{SEQ:5}";

    /// <summary>Префикс ключа счётчика в таблице параметров.</summary>
    public const string CounterKeyPrefix = "registration.outgoing.";

    private readonly IRegistrationNumberStore _хранилище;

    /// <summary>Создаёт генератор регистрационных номеров.</summary>
    /// <param name="хранилище">Хранилище атомарного счётчика.</param>
    public RegistrationNumberGenerator(IRegistrationNumberStore хранилище)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        _хранилище = хранилище;
    }

    /// <summary>
    /// Формирует следующий исходящий номер по маске.
    /// </summary>
    /// <param name="templateMask">
    /// Маска номера; при пустом значении используется
    /// <see cref="DefaultTemplateMask"/>.
    /// </param>
    /// <param name="moment">
    /// Момент времени, определяющий год в номере; по умолчанию — текущий UTC.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Следующий регистрационный номер.</returns>
    public async Task<string> GenerateNextOutgoingNumberAsync(
        string? templateMask = null,
        DateTime? moment = null,
        CancellationToken cancellationToken = default)
    {
        var маска = string.IsNullOrWhiteSpace(templateMask) ? DefaultTemplateMask : templateMask;
        var счётчик = await _хранилище
            .NextAsync(CounterKeyPrefix + маска, cancellationToken)
            .ConfigureAwait(false);

        var дата = (moment ?? DateTime.UtcNow).ToUniversalTime();

        return RegistrationNumberMask.Format(маска, дата.Year, счётчик);
    }
}
