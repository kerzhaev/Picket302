using System.Windows.Forms;
using Picket302.Application.Queries;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Components;

/// <summary>
/// Сортировка адресной книги по клику на заголовок столбца
/// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
/// </summary>
/// <remarks>
/// <para>
/// Контроллер сопоставляет заголовок столбца с колонкой адресной книги по
/// каталогу <see cref="AddressBookColumnCatalog"/> и формирует ключ сортировки
/// из белого списка репозитория. Повторный клик по той же колонке меняет
/// направление сортировки.
/// </para>
/// <para>
/// Ключ сортировки и направление отдаются вызывающим кодом через
/// <see cref="ПостроитьКритерии"/> — запрос к базе выполняется вызывающим
/// кодом, контроллер отвечает только за разбор действия оператора.
/// </para>
/// </remarks>
public sealed class AddressBookSortController
{
    private DataGridView? _таблица;

    /// <summary>
    /// Ключ колонки сортировки; <see langword="null"/> — сортировка не задана.
    /// </summary>
    public string? КолонкаСортировки { get; private set; }

    /// <summary>Признак сортировки по убыванию.</summary>
    public bool ПоУбыванию { get; private set; }

    /// <summary>Событие изменения сортировки.</summary>
    public event EventHandler? СортировкаИзменена;

    /// <summary>
    /// Подключает контроллер к таблице адресной книги: сортировка выполняется
    /// контроллером, штатная сортировка таблицы отключается.
    /// </summary>
    /// <param name="таблица">Таблица адресной книги.</param>
    public void Подключить(DataGridView таблица)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        if (_таблица is not null)
        {
            _таблица.ColumnHeaderMouseClick -= ОбработатьКликЗаголовка;
        }

        _таблица = таблица;

        foreach (DataGridViewColumn столбец in таблица.Columns)
        {
            столбец.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        таблица.ColumnHeaderMouseClick += ОбработатьКликЗаголовка;
    }

    /// <summary>
    /// Обрабатывает клик по заголовку столбца: устанавливает колонку
    /// сортировки и, при повторном клике по той же колонке, меняет
    /// направление.
    /// </summary>
    /// <param name="индексКолонки">Индекс столбца в таблице.</param>
    /// <returns>
    /// <see langword="true"/>, если столбец входит в состав адресной книги и
    /// сортировка изменена; иначе — <see langword="false"/>.
    /// </returns>
    public bool ОбработатьКликПоЗаголовку(int индексКолонки)
    {
        if (_таблица is null)
        {
            return false;
        }

        if (индексКолонки < 0 || индексКолонки >= _таблица.Columns.Count)
        {
            return false;
        }

        var столбец = _таблица.Columns[индексКолонки];
        var колонка = AddressBookColumnCatalog.Найти(столбец.Name);

        if (колонка is null)
        {
            return false;
        }

        if (string.Equals(КолонкаСортировки, колонка.SortKey, StringComparison.OrdinalIgnoreCase))
        {
            ПоУбыванию = !ПоУбыванию;
        }
        else
        {
            КолонкаСортировки = колонка.SortKey;
            ПоУбыванию = false;
        }

        ОбновитьЗначокСортировки(столбец);

        СортировкаИзменена?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Формирует критерии сортировки для выборки контрагентов.
    /// </summary>
    /// <returns>Критерии слоя запроса с заданной колонкой и направлением сортировки.</returns>
    public FilterCriteria ПостроитьКритерии() => new()
    {
        SortColumn = КолонкаСортировки,
        SortDescending = ПоУбыванию,
    };

    /// <summary>Сбрасывает сортировку.</summary>
    public void Сбросить()
    {
        КолонкаСортировки = null;
        ПоУбыванию = false;

        if (_таблица is null)
        {
            return;
        }

        foreach (DataGridViewColumn столбец in _таблица.Columns)
        {
            столбец.HeaderCell.SortGlyphDirection = SortOrder.None;
        }
    }

    /// <summary>
    /// Обработчик клика по заголовку столбца таблицы.
    /// </summary>
    /// <param name="отправитель">Таблица.</param>
    /// <param name="аргументы">Аргументы события.</param>
    private void ОбработатьКликЗаголовка(object? отправитель, DataGridViewCellMouseEventArgs аргументы)
    {
        ОбработатьКликПоЗаголовку(аргументы.ColumnIndex);
    }

    /// <summary>
    /// Показывает значок направления сортировки в заголовке выбранного столбца.
    /// </summary>
    /// <param name="выбранный">Отсортированный столбец.</param>
    private void ОбновитьЗначокСортировки(DataGridViewColumn выбранный)
    {
        if (_таблица is null)
        {
            return;
        }

        foreach (DataGridViewColumn столбец in _таблица.Columns)
        {
            столбец.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        выбранный.HeaderCell.SortGlyphDirection = ПоУбыванию
            ? SortOrder.Descending
            : SortOrder.Ascending;
    }
}
