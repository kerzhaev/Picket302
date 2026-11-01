using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Хранение пользовательских фильтров адресной книги
/// (ТЗ §8.1, функция 9: «сохранённые пользовательские фильтры»).
/// </summary>
/// <remarks>
/// Фильтры сохраняются в файле настроек интерфейса вместе с прочими
/// настройками отображения — отдельный файл не создаётся. Имя фильтра уникально
/// в пределах набора: сохранение под существующим именем обновляет фильтр.
/// </remarks>
public sealed class SavedFilterService
{
    private readonly IUiSettingsStore _хранилище;

    /// <summary>Создаёт службу сохранённых фильтров.</summary>
    /// <param name="хранилище">Хранилище настроек интерфейса.</param>
    public SavedFilterService(IUiSettingsStore хранилище)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        _хранилище = хранилище;
    }

    /// <summary>
    /// Возвращает перечень сохранённых фильтров.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список сохранённых фильтров по возрастанию имени.</returns>
    public async Task<IReadOnlyList<SavedFilter>> ПолучитьСписокAsync(
        CancellationToken cancellationToken = default)
    {
        var фильтры = await _хранилище.LoadSavedFiltersAsync(cancellationToken).ConfigureAwait(false);

        return фильтры
            .OrderBy(фильтр => фильтр.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Сохраняет фильтр под указанным именем. Фильтр с таким же именем
    /// обновляется — имена уникальны в пределах набора.
    /// </summary>
    /// <param name="имя">Имя фильтра.</param>
    /// <param name="критерии">Сохраняемые условия отбора и сортировки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённый фильтр.</returns>
    public async Task<SavedFilter> СохранитьAsync(
        string имя,
        FilterCriteria критерии,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имя);
        ArgumentNullException.ThrowIfNull(критерии);

        var фильтр = new SavedFilter
        {
            Name = имя.Trim(),
            Criteria = критерии,
        };

        await _хранилище.SaveSavedFilterAsync(фильтр, cancellationToken).ConfigureAwait(false);

        return фильтр;
    }

    /// <summary>
    /// Возвращает условия отбора сохранённого фильтра.
    /// </summary>
    /// <param name="имя">Имя фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Условия фильтра либо <see langword="null"/>, если фильтр не сохранён.</returns>
    public async Task<FilterCriteria?> ПолучитьКритерииAsync(
        string имя,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имя);

        var фильтры = await _хранилище.LoadSavedFiltersAsync(cancellationToken).ConfigureAwait(false);

        var фильтр = фильтры.FirstOrDefault(сохранённый => string.Equals(
            сохранённый.Name,
            имя.Trim(),
            StringComparison.OrdinalIgnoreCase));

        return фильтр?.Criteria;
    }

    /// <summary>
    /// Удаляет сохранённый фильтр.
    /// </summary>
    /// <param name="имя">Имя удаляемого фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если фильтр был удалён.</returns>
    public Task<bool> УдалитьAsync(string имя, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имя);

        return _хранилище.DeleteSavedFilterAsync(имя.Trim(), cancellationToken);
    }

    /// <summary>
    /// Переименовывает сохранённый фильтр. Новое имя должно быть свободно —
    /// имена уникальны в пределах набора.
    /// </summary>
    /// <param name="прежнееИмя">Текущее имя фильтра.</param>
    /// <param name="новоеИмя">Новое имя фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <see langword="true"/>, если фильтр переименован; <see langword="false"/> —
    /// фильтр с прежним именем не найден либо новое имя уже занято.
    /// </returns>
    public async Task<bool> ПереименоватьAsync(
        string прежнееИмя,
        string новоеИмя,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(прежнееИмя);
        ArgumentException.ThrowIfNullOrWhiteSpace(новоеИмя);

        var прежнее = прежнееИмя.Trim();
        var новое = новоеИмя.Trim();

        var фильтры = await _хранилище.LoadSavedFiltersAsync(cancellationToken).ConfigureAwait(false);

        var фильтр = фильтры.FirstOrDefault(сохранённый => string.Equals(
            сохранённый.Name,
            прежнее,
            StringComparison.OrdinalIgnoreCase));

        if (фильтр is null)
        {
            return false;
        }

        var имяЗанято = фильтры.Any(сохранённый => string.Equals(
            сохранённый.Name,
            новое,
            StringComparison.OrdinalIgnoreCase));

        if (имяЗанято)
        {
            return false;
        }

        await _хранилище.SaveSavedFilterAsync(
            new SavedFilter { Name = новое, Criteria = фильтр.Criteria },
            cancellationToken).ConfigureAwait(false);

        await _хранилище.DeleteSavedFilterAsync(прежнее, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
