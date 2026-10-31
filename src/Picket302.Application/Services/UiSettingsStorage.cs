using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Запоминание геометрии и состава колонок таблиц
/// (ТЗ §8.1, функция 12: «Запоминание геометрии и состава колонок»).
/// </summary>
/// <remarks>
/// Конфигурация хранится в профиле пользователя (файл JSON в
/// <c>AppData/Roaming/…</c>). Повреждённый файл автоматически заменяется
/// дефолтным.
/// </remarks>
public sealed class UiSettingsStorage
{
    private readonly IUiSettingsStore _хранилище;

    /// <summary>Создаёт хранилище настроек отображения.</summary>
    /// <param name="хранилище">Хранилище настроек.</param>
    public UiSettingsStorage(IUiSettingsStore хранилище)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        _хранилище = хранилище;
    }

    /// <summary>
    /// Сохраняет конфигурацию таблицы.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="config">Сохраняемая конфигурация.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SaveGridConfigurationAsync(
        string gridId,
        GridConfigDto config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);
        ArgumentNullException.ThrowIfNull(config);

        await _хранилище
            .SaveGridAsync(config with { GridId = gridId }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Читает сохранённую конфигурацию таблицы.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Конфигурация либо <see langword="null"/>, если она не сохранена.</returns>
    public Task<GridConfigDto?> LoadGridConfigurationAsync(
        string gridId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        return _хранилище.LoadGridAsync(gridId, cancellationToken);
    }

    /// <summary>
    /// Читает оформление таблиц; при отсутствии настроек возвращает значения
    /// по умолчанию (фокус 4227327, выделение 6205183, подсказка 3723516).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Оформление таблиц.</returns>
    public Task<GridAppearance> LoadAppearanceAsync(CancellationToken cancellationToken = default)
        => _хранилище.LoadAppearanceAsync(cancellationToken);

    /// <summary>
    /// Сохраняет оформление таблиц.
    /// </summary>
    /// <param name="appearance">Сохраняемое оформление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task SaveAppearanceAsync(GridAppearance appearance, CancellationToken cancellationToken = default)
        => _хранилище.SaveAppearanceAsync(appearance, cancellationToken);
}
