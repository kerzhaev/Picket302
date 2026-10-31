using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Printing.Warnings;

/// <summary>
/// Обучающее предупреждение о физической подаче конвертов «меньшей стороной»
/// (ТЗ §8.5, функция 147: <c>Printing.Warnings.FeedOrientationHint</c>).
/// </summary>
/// <remarks>
/// <para>Текст: «Для корректной печати конверты следует загружать в принтер
/// узкой стороной». Тот же совет обязателен в справочном разделе, включая
/// приём печати конверта на листе A4 с фиксацией скотчем для принтеров, плохо
/// захватывающих конверты.</para>
/// <para>Флаг подавления хранится в профиле пользователя: после включения
/// галки предупреждение не показывается до сброса настроек.</para>
/// </remarks>
public sealed class FeedOrientationHint
{
    /// <summary>Ключ флага подавления в профиле пользователя.</summary>
    public const string SuppressionKey = "SuppressFeedOrientationWarning";

    /// <summary>Текст предупреждения.</summary>
    public const string WarningText =
        "Для корректной печати конверты следует загружать в принтер узкой стороной.";

    /// <summary>Текст галки подавления предупреждения.</summary>
    public const string SuppressCaption = "Больше не показывать это сообщение";

    /// <summary>Текст справочного раздела о подаче конвертов.</summary>
    public const string HelpText =
        "Для корректной печати конверты следует загружать в принтер узкой стороной. "
        + "Для принтеров, плохо захватывающих конверты, принимайте печать конверта "
        + "на листе A4 с фиксацией скотчем.";

    private readonly IParameterRepository _параметры;

    /// <summary>
    /// Создаёт средство предупреждения о подаче конвертов.
    /// </summary>
    /// <param name="parameters">Хранилище параметров профиля пользователя.</param>
    public FeedOrientationHint(IParameterRepository parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _параметры = parameters;
    }

    /// <summary>
    /// Определяет, следует ли показывать предупреждение.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если предупреждение нужно показать.</returns>
    public async Task<bool> ShouldShowAsync(CancellationToken cancellationToken = default)
    {
        var значение = await _параметры
            .GetValueAsync(SuppressionKey, cancellationToken)
            .ConfigureAwait(false);

        return !string.Equals(значение, "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Показывает предупреждение о подаче конвертов, если оно не подавлено
    /// пользователем (ТЗ §8.5, функция 147).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если предупреждение показано.</returns>
    public async Task<bool> ShowFeedOrientationWarningIfNeededAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await ShouldShowAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Сохраняет флаг подавления предупреждения.
    /// </summary>
    /// <param name="suppressed">Признак подавления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SetSuppressedAsync(bool suppressed, CancellationToken cancellationToken = default)
    {
        var параметр = await _параметры
            .GetByNameAsync(SuppressionKey, cancellationToken)
            .ConfigureAwait(false);

        if (параметр is null)
        {
            await _параметры
                .AddAsync(
                    new Parameter { Name = SuppressionKey, Value = suppressed ? "1" : "0" },
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        параметр.Value = suppressed ? "1" : "0";
        await _параметры.UpdateAsync(параметр, cancellationToken).ConfigureAwait(false);
    }
}
