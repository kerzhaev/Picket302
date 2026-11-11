namespace Picket302.App.UI.Components;

/// <summary>
/// Пометка строки-заголовка группы в таблице (ТЗ §8.1, функция 10:
/// «Группировка строк в списке, настройка и снятие группировки»).
/// </summary>
/// <param name="Ключ">Значение колонки группировки.</param>
public sealed record ЗаголовокГруппы(string Ключ);

/// <summary>
/// Группировка строк таблицы адресной книги сворачиваемыми заголовками.
/// </summary>
/// <remarks>
/// Контроллер работает с несвязанной таблицей (строки добавляются
/// <c>Rows.Add</c>, тег строки — идентификатор записи). Заголовки групп
/// вставляются перед первым вхождением группы в текущем порядке строк:
/// после сортировки или поиска вызывающий код повторно вызывает
/// <see cref="Перестроить"/>. Свернутые группы переживают перестроение
/// (состояние хранится по ключу группы), снятие группировки возвращает
/// исходный порядок без заголовков.
/// </remarks>
public sealed class RowGroupingController : IDisposable
{
    private DataGridView? _таблица;
    private string? _колонка;
    private readonly HashSet<string> _свёрнутыеКлючи = new();
    private bool _перестройка;

    /// <summary>Имя колонки группировки; <see langword="null"/> — без группировки.</summary>
    public string? КолонкаГруппировки => _колонка;

    /// <summary>Число строк-заголовков групп в текущем состоянии.</summary>
    public int КоличествоЗаголовков =>
        _таблица?.Rows.Cast<DataGridViewRow>()
            .Count(строка => строка.Tag is ЗаголовокГруппы) ?? 0;

    /// <summary>
    /// Подключает контроллер к таблице: клик по заголовку группы сворачивает
    /// или разворачивает её строки.
    /// </summary>
    /// <param name="таблица">Таблица адресной книги.</param>
    public void Подключить(DataGridView таблица)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        if (_таблица is not null)
        {
            _таблица.CellClick -= ОбработатьКликПоСтроке;
        }

        _таблица = таблица;
        таблица.CellClick += ОбработатьКликПоСтроке;
    }

    /// <summary>
    /// Задаёт колонку группировки (<see langword="null"/> — снять группировку)
    /// и перестраивает таблицу.
    /// </summary>
    /// <param name="колонка">Имя колонки либо <see langword="null"/>.</param>
    public void Применить(string? колонка)
    {
        _колонка = колонка;
        _свёрнутыеКлючи.Clear();
        Перестроить();
    }

    /// <summary>
    /// Перестраивает заголовки групп по текущему порядку и видимости строк,
    /// сохраняя выделение по тегам записей и свёрнутость групп.
    /// </summary>
    public void Перестроить()
    {
        if (_таблица is null || _перестройка)
        {
            return;
        }

        _перестройка = true;
        try
        {
            var выделенныеТеги = _таблица.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(строка => строка.Tag is not ЗаголовокГруппы and not null)
                .Select(строка => строка.Tag!)
                .ToHashSet();

            foreach (var строка in _таблица.Rows.Cast<DataGridViewRow>().ToList())
            {
                if (строка.Tag is ЗаголовокГруппы)
                {
                    _таблица.Rows.Remove(строка);
                }
                else
                {
                    строка.Visible = true;
                }
            }

            if (_колонка is null)
            {
                ВосстановитьВыделение(выделенныеТеги);
                return;
            }

            var колонкаГруппировки = _таблица.Columns[_колонка];
            if (колонкаГруппировки is null)
            {
                ВосстановитьВыделение(выделенныеТеги);
                return;
            }

            var данныеСтроки = _таблица.Rows
                .Cast<DataGridViewRow>()
                .Where(строка => строка.Tag is not ЗаголовокГруппы && !строка.IsNewRow)
                .OrderBy(строка => строка.Index)
                .ToList();

            // Строки одной группы выстраиваются в непрерывный блок (порядок
            // групп — по первому вхождению, порядок внутри группы сохраняется),
            // как в легаси-списке с группировкой.
            var блоки = new Dictionary<string, List<DataGridViewRow>>();
            var порядокГрупп = new List<string>();

            foreach (var строка in данныеСтроки)
            {
                var ключ = КлючГруппы(строка, колонкаГруппировки.Index);

                if (!блоки.TryGetValue(ключ, out var блок))
                {
                    блок = [];
                    блоки[ключ] = блок;
                    порядокГрупп.Add(ключ);
                }

                блок.Add(строка);
            }

            foreach (var строка in данныеСтроки)
            {
                _таблица.Rows.Remove(строка);
            }

            foreach (var ключ in порядокГрупп)
            {
                var строкаЗаголовокИндекс = _таблица.Rows.Add();
                var строкаЗаголовок = _таблица.Rows[строкаЗаголовокИндекс];
                строкаЗаголовок.Tag = new ЗаголовокГруппы(ключ);
                строкаЗаголовок.ReadOnly = true;
                строкаЗаголовок.DefaultCellStyle.BackColor =
                    System.Drawing.SystemColors.ControlLight;
                строкаЗаголовок.DefaultCellStyle.Font =
                    new System.Drawing.Font(_таблица!.Font ?? new System.Drawing.Font("Segoe UI", 8f),
                        System.Drawing.FontStyle.Bold);
                строкаЗаголовок.Cells[0].Value =
                    Надпись(ключ, блоки[ключ].Count, свёрнута: _свёрнутыеКлючи.Contains(ключ));

                foreach (var строка in блоки[ключ])
                {
                    _таблица.Rows.Add(строка);
                }
            }

            СвернутьСкрытые(данныеСтроки);
            ВосстановитьВыделение(выделенныеТеги);
        }
        finally
        {
            _перестройка = false;
        }
    }

    /// <summary>Видимый текст заголовка группы (для проверок).</summary>
    public IReadOnlyList<string> НадписиЗаголовков() =>
        _таблица?.Rows
            .Cast<DataGridViewRow>()
            .Where(строка => строка.Tag is ЗаголовокГруппы)
            .Select(строка => строка.Cells[0].Value?.ToString() ?? string.Empty)
            .ToList() ?? [];

    private static string КлючГруппы(DataGridViewRow строка, int номерКолонки)
    {
        var значение = строка.Cells[номерКолонки].Value?.ToString();
        return string.IsNullOrWhiteSpace(значение) ? "(без группы)" : значение.Trim();
    }

    private static string Надпись(string ключ, int число, bool свёрнута) =>
        $"{(свёрнута ? "+" : "−")} {ключ} ({число})";

    private void СвернутьСкрытые(List<DataGridViewRow> данныеСтроки)
    {
        if (_таблица is null || _колонка is null)
        {
            return;
        }

        var индексКолонки = _таблица.Columns[_колонка]!.Index;

        foreach (var строка in данныеСтроки)
        {
            var ключ = КлючГруппы(строка, индексКолонки);

            if (_свёрнутыеКлючи.Contains(ключ))
            {
                строка.Visible = false;
            }
        }
    }

    private void ВосстановитьВыделение(HashSet<object> теги)
    {
        if (_таблица is null || теги.Count == 0)
        {
            return;
        }

        foreach (DataGridViewRow строка in _таблица.Rows)
        {
            if (!строка.IsNewRow && строка.Tag is not ЗаголовокГруппы && теги.Contains(строка.Tag!))
            {
                строка.Selected = true;
            }
        }
    }

    private void ОбработатьКликПоСтроке(object? отправитель, DataGridViewCellEventArgs аргументы)
    {
        if (_таблица is null || _колонка is null || аргументы.RowIndex < 0)
        {
            return;
        }

        ПереключитьСвораченность(_таблица.Rows[аргументы.RowIndex]);
    }

    /// <summary>
    /// Сворачивает или разворачивает группу по клику на её заголовке;
    /// для прочих строк ничего не делает. Публично для UI-тестов.
    /// </summary>
    /// <param name="строка">Строка таблицы (ожидается заголовок группы).</param>
    public void ПереключитьСвораченность(DataGridViewRow строка)
    {
        if (_таблица is null || _колонка is null)
        {
            return;
        }

        if (строка.Tag is not ЗаголовокГруппы заголовок)
        {
            return;
        }

        var свёрнута = !_свёрнутыеКлючи.Remove(заголовок.Ключ);

        if (свёрнута)
        {
            _свёрнутыеКлючи.Add(заголовок.Ключ);
        }

        var индексКолонки = _таблица.Columns[_колонка]!.Index;
        var число = 0;

        foreach (DataGridViewRow данных in _таблица.Rows)
        {
            if (данных.Tag is not ЗаголовокГруппы
                && КлючГруппы(данных, индексКолонки) == заголовок.Ключ)
            {
                число++;
                данных.Visible = !свёрнута;
            }
        }

        строка.Cells[0].Value = Надпись(заголовок.Ключ, число, свёрнута);
    }

    /// <summary>Отключает контроллер от таблицы.</summary>
    public void Dispose()
    {
        if (_таблица is not null)
        {
            _таблица.CellClick -= ОбработатьКликПоСтроке;
            _таблица = null;
        }
    }
}
