using System.Windows.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Components;

/// <summary>
/// Настройка вида таблиц: цвета, «полосатая таблица», заголовки столбцов,
/// показ и скрытие столбцов, сброс к умолчаниям
/// (ТЗ §8.11, функция 131: «Настройка вида таблиц»).
/// </summary>
/// <remarks>
/// <para>
/// Настройки читаются из файла настроек интерфейса и применяются к таблице
/// двумя шагами: <see cref="ЗагрузитьAsync"/> читает данные, а
/// <see cref="Применить"/> изменяет элементы таблицы.
/// </para>
/// <para>
/// Шаги разделены намеренно: элементы таблицы изменяются только в потоке,
/// который ими владеет (поток интерфейса). Если совмещать чтение файла и
/// изменение таблицы в одном асинхронном методе, обработка продолжится уже
/// в другом потоке, а обращение к элементу таблицы из чужого потока приводит
/// к зависанию программы.
/// </para>
/// <para>
/// Состояние столбцов (показ, подпись, ширина) хранится по идентификатору
/// таблицы, оформление (цвета, «зебра», показ заголовков столбцов) — единое
/// для всех таблиц программы.
/// </para>
/// </remarks>
public sealed class GridViewSettingsController
{
    /// <summary>
    /// Загруженные настройки вида таблицы: оформление и сохранённое
    /// состояние столбцов.
    /// </summary>
    /// <param name="GridId">Идентификатор таблицы.</param>
    /// <param name="Appearance">Оформление таблиц.</param>
    /// <param name="Configuration">Сохранённое состояние столбцов либо null.</param>
    public sealed record GridSettings(
        string GridId,
        GridAppearance Appearance,
        GridConfigDto? Configuration);

    private readonly UiSettingsStorage _хранилище;
    private readonly Dictionary<string, IReadOnlyList<GridColumnConfig>> _состоянияПоУмолчанию =
        new(StringComparer.Ordinal);

    /// <summary>Создаёт контроллер настроек вида таблиц.</summary>
    /// <param name="хранилище">Хранилище настроек интерфейса.</param>
    public GridViewSettingsController(UiSettingsStorage хранилище)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        _хранилище = хранилище;
    }

    /// <summary>
    /// Читает настройки вида таблицы из файла настроек: оформление и
    /// сохранённое состояние столбцов.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Загруженные настройки вида таблицы.</returns>
    public async Task<GridSettings> ЗагрузитьAsync(
        string gridId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        var оформление = await _хранилище.LoadAppearanceAsync(cancellationToken).ConfigureAwait(false);
        var конфигурация = await _хранилище
            .LoadGridConfigurationAsync(gridId, cancellationToken)
            .ConfigureAwait(false);

        return new GridSettings(gridId, оформление, конфигурация);
    }

    /// <summary>
    /// Применяет загруженные настройки к таблице: цвета, «полосатая таблица»,
    /// показ заголовков столбцов, подписи и видимость столбцов.
    /// </summary>
    /// <remarks>
    /// Метод выполняется в потоке вызова — в нём и должны изменяться
    /// элементы таблицы.
    /// </remarks>
    /// <param name="таблица">Таблица, к которой применяются настройки.</param>
    /// <param name="настройки">Загруженные настройки вида таблицы.</param>
    /// <returns>Применённое оформление таблиц.</returns>
    public GridAppearance Применить(DataGridView таблица, GridSettings настройки)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentNullException.ThrowIfNull(настройки);

        // Штатное состояние столбцов запоминается один раз — до применения
        // сохранённых настроек; к нему выполняется сброс.
        ЗапомнитьСостояниеПоУмолчанию(настройки.GridId, таблица);

        GridAppearanceManager.Применить(таблица, настройки.Appearance);
        таблица.ColumnHeadersVisible = настройки.Appearance.HeadersVisible;

        if (настройки.Configuration is not null)
        {
            ПрименитьСостояниеСтолбцов(таблица, настройки.Configuration.Columns);
        }

        return настройки.Appearance;
    }

    /// <summary>
    /// Снимает текущее состояние таблицы (подписи, ширина, видимость
    /// столбцов) и сохраняет его.
    /// </summary>
    /// <param name="таблица">Таблица, состояние которой сохраняется.</param>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task СохранитьAsync(
        DataGridView таблица,
        string gridId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        var конфигурация = new GridConfigDto(gridId, СнятьСостояние(таблица));

        await _хранилище
            .SaveGridConfigurationAsync(gridId, конфигурация, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Скрывает столбец таблицы и сохраняет новое состояние столбцов
    /// (ТЗ §8.11, функция 131: «скрытие колонок»).
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="индекс">Индекс скрываемого столбца.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения состояния столбцов.</returns>
    public async Task СкрытьСтолбецAsync(
        DataGridView таблица,
        string gridId,
        int индекс,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        if (индекс < 0 || индекс >= таблица.Columns.Count)
        {
            return;
        }

        таблица.Columns[индекс].Visible = false;

        await СохранитьAsync(таблица, gridId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает все скрытые столбцы таблицы и сохраняет новое состояние
    /// столбцов (ТЗ §8.11, функция 131: «скрытие колонок»).
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения состояния столбцов.</returns>
    public async Task ПоказатьВсеСтолбцыAsync(
        DataGridView таблица,
        string gridId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        foreach (DataGridViewColumn столбец in таблица.Columns)
        {
            столбец.Visible = true;
        }

        await СохранитьAsync(таблица, gridId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает штатные настройки вида таблицы и сохраняет их: цвета,
    /// «полосатая таблица», показ заголовков столбцов, подписи и видимость
    /// столбцов (ТЗ §8.11, функция 131: «сброс к умолчаниям»).
    /// </summary>
    /// <param name="таблица">Таблица, для которой выполняется сброс.</param>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Штатное оформление таблиц.</returns>
    public async Task<GridAppearance> СброситьКУмолчаниямAsync(
        DataGridView таблица,
        string gridId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        // Оформление едино для всех таблиц программы — сброс применяется
        // ко всем зарегистрированным таблицам.
        var результат = GridAppearanceManager.ResetToDefaults(
            GridAppearanceManager.GridAppearanceScope.Grid);

        GridAppearanceManager.Применить(таблица, результат.Appearance);
        таблица.ColumnHeadersVisible = результат.Appearance.HeadersVisible;

        ВосстановитьСостояниеПоУмолчанию(gridId, таблица);

        // Состояние столбцов снимается до ожидания: чтение свойств таблицы
        // выполняется в потоке вызова (поток интерфейса).
        var конфигурация = new GridConfigDto(gridId, СнятьСостояние(таблица));

        await _хранилище.SaveAppearanceAsync(результат.Appearance, cancellationToken).ConfigureAwait(false);

        await _хранилище
            .SaveGridConfigurationAsync(gridId, конфигурация, cancellationToken)
            .ConfigureAwait(false);

        return результат.Appearance;
    }

    /// <summary>
    /// Применяет сохранённое состояние столбцов: видимость, подпись и ширину.
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="столбцы">Сохранённое состояние столбцов.</param>
    private static void ПрименитьСостояниеСтолбцов(
        DataGridView таблица,
        IReadOnlyList<GridColumnConfig> столбцы)
    {
        foreach (var сохранённый in столбцы)
        {
            var столбец = таблица.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(колонка => string.Equals(
                    колонка.Name,
                    сохранённый.Name,
                    StringComparison.Ordinal));

            if (столбец is null)
            {
                continue;
            }

            столбец.Visible = сохранённый.Visible;

            if (!string.IsNullOrWhiteSpace(сохранённый.Caption))
            {
                столбец.HeaderText = сохранённый.Caption;
            }

            if (сохранённый.Width > 0)
            {
                столбец.Width = сохранённый.Width;
            }
        }
    }

    /// <summary>
    /// Снимает текущее состояние столбцов таблицы.
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <returns>Описание столбцов в порядке отображения.</returns>
    private static IReadOnlyList<GridColumnConfig> СнятьСостояние(DataGridView таблица)
    {
        var состояние = new List<GridColumnConfig>(таблица.Columns.Count);
        var порядок = 0;

        foreach (DataGridViewColumn столбец in таблица.Columns)
        {
            состояние.Add(new GridColumnConfig(
                столбец.Name,
                столбец.HeaderText,
                столбец.Width,
                столбец.Visible,
                порядок));

            порядок++;
        }

        return состояние;
    }

    /// <summary>
    /// Запоминает штатное состояние столбцов таблицы — состояние до применения
    /// сохранённых настроек. Запоминается один раз для каждой таблицы.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="таблица">Таблица.</param>
    private void ЗапомнитьСостояниеПоУмолчанию(string gridId, DataGridView таблица)
    {
        if (_состоянияПоУмолчанию.ContainsKey(gridId))
        {
            return;
        }

        _состоянияПоУмолчанию[gridId] = СнятьСостояние(таблица);
    }

    /// <summary>
    /// Восстанавливает штатное состояние столбцов таблицы.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="таблица">Таблица.</param>
    private void ВосстановитьСостояниеПоУмолчанию(string gridId, DataGridView таблица)
    {
        if (!_состоянияПоУмолчанию.TryGetValue(gridId, out var состояние))
        {
            return;
        }

        ПрименитьСостояниеСтолбцов(таблица, состояние);
    }
}
