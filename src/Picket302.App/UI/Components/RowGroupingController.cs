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
    private List<long>? _исходныйПорядокИдентификаторов;
    private System.Drawing.Font? _шрифтЗаголовка;
    private System.Drawing.Font? _шрифтИсточникКэша;

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
            _таблица.RowPostPaint -= НарисоватьЗаголовокГруппы;
        }

        _таблица = таблица;
        таблица.CellClick += ОбработатьКликПоСтроке;
        таблица.RowPostPaint += НарисоватьЗаголовокГруппы;
    }

    /// <summary>
    /// Сбрасывает снимок исходного порядка строк: вызывается владельцем при
    /// замене набора строк (перезагрузка после фильтра/поиска), чтобы снятие
    /// группировки восстанавливало порядок ТЕКУЩЕЙ загрузки (ревью S07 F-R2-2).
    /// </summary>
    public void СброситьСнимокПорядка() => _исходныйПорядокИдентификаторов = null;

    /// <summary>
    /// Списочный индекс записи по строке таблицы: соответствие устанавливается
    /// через тег-идентификатор, а не позицию строки — корректно при активной
    /// группировке (заголовки групп и блочный перепорядок не смещают индексы
    /// поиска; ревью S07 F1).
    /// </summary>
    /// <param name="строка">Строка таблицы (может быть строкой-заголовком).</param>
    /// <param name="идентификаторы">Идентификаторы записей в порядке списка поиска.</param>
    /// <returns>Индекс в списке либо −1.</returns>
    public static int СписочныйИндекс(DataGridViewRow? строка, IReadOnlyList<long> идентификаторы)
    {
        if (строка?.Tag is not long идентификатор)
        {
            return -1;
        }

        var позиция = 0;
        foreach (var кандидат in идентификаторы)
        {
            if (кандидат == идентификатор)
            {
                return позиция;
            }

            позиция++;
        }

        return -1;
    }

    /// <summary>
    /// Строка таблицы по идентификатору записи (тег строки) — ревью S07 F1.
    /// </summary>
    public static DataGridViewRow? СтрокаПоИдентификатору(DataGridView таблица, long идентификатор)
    {
        foreach (DataGridViewRow строка in таблица.Rows)
        {
            if (строка.Tag is long тег && тег == идентификатор)
            {
                return строка;
            }
        }

        return null;
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

        // Снятие группировки возвращает и физический порядок строк:
        // блочный перепорядок отменяется по снимку идентификаторов,
        // сделанному до первой группировки (ревью S07 F5).
        if (колонка is null)
        {
            ВосстановитьИсходныйПорядок();
        }
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

            _исходныйПорядокИдентификаторов ??= данныеСтроки
                .Where(строка => строка.Tag is long)
                .Select(строка => (long)строка.Tag!)
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
                строкаЗаголовок.DefaultCellStyle.Font = ШрифтЗаголовка();
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

    /// <summary>
    /// Кэшированный bold-шрифт заголовков: один источник для прорисовки и
    /// стилей строк (аллокация на каждый paint/перестроение протекала бы —
    /// ревью S07 F-R2-1/F-R3-4). Инвалидируется при смене Font таблицы
    /// (например, смена кегля в «Настройке вида таблиц») — ревью S07
    /// F-R3-2. Заменённый экземпляр не утилизируется немедленно: он может
    /// числиться в стилях строк до следующего перестроения.
    /// </summary>
    private System.Drawing.Font ШрифтЗаголовка()
    {
        var текущийШрифт = _таблица!.Font;

        if (_шрифтЗаголовка is null || !ReferenceEquals(_шрифтИсточникКэша, текущийШрифт))
        {
            _шрифтИсточникКэша = текущийШрифт;
            _шрифтЗаголовка = new System.Drawing.Font(текущийШрифт, System.Drawing.FontStyle.Bold);
        }

        return _шрифтЗаголовка;
    }

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

    /// <summary>
    /// Разворачивает группу, если строка скрыта свёрнутой группой —
    /// используется навигацией поиска перед показом записи (ревью S07 F1).
    /// </summary>
    public void РазвернутьПриНеобходимости(DataGridViewRow строка)
    {
        if (_таблица is null || _колонка is null || строка.Visible)
        {
            return;
        }

        var колонка = _таблица.Columns[_колонка];
        if (колонка is null)
        {
            return;
        }

        var ключ = КлючГруппы(строка, колонка.Index);
        if (!_свёрнутыеКлючи.Remove(ключ))
        {
            return;
        }

        foreach (DataGridViewRow данных in _таблица.Rows)
        {
            if (данных.Tag is not ЗаголовокГруппы && КлючГруппы(данных, колонка.Index) == ключ)
            {
                данных.Visible = true;
            }
        }

        ОбновитьНадписьЗаголовка(ключ, свёрнута: false);
    }

    private void ОбновитьНадписьЗаголовка(string ключ, bool свёрнута)
    {
        if (_таблица is null || _колонка is null)
        {
            return;
        }

        var колонка = _таблица.Columns[_колонка];
        if (колонка is null)
        {
            return;
        }

        var число = _таблица.Rows
            .Cast<DataGridViewRow>()
            .Count(р => р.Tag is not ЗаголовокГруппы && КлючГруппы(р, колонка.Index) == ключ);

        var заголовок = _таблица.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(р => р.Tag is ЗаголовокГруппы з && з.Ключ == ключ);

        if (заголовок is not null)
        {
            заголовок.Cells[0].Value = Надпись(ключ, число, свёрнута);
        }
    }

    private void ВосстановитьИсходныйПорядок()
    {
        if (_таблица is null || _исходныйПорядокИдентификаторов is null)
        {
            return;
        }

        var данныеСтроки = _таблица.Rows
            .Cast<DataGridViewRow>()
            .Where(строка => строка.Tag is not ЗаголовокГруппы && !строка.IsNewRow)
            .ToList();

        if (данныеСтроки.Count == 0)
        {
            _исходныйПорядокИдентификаторов = null;
            return;
        }

        var упорядоченныеСтроки = данныеСтроки
            .Where(строка => строка.Tag is long)
            .OrderBy(строка => _исходныйПорядокИдентификаторов.IndexOf((long)строка.Tag!))
            .ToList();

        foreach (var строка in упорядоченныеСтроки)
        {
            _таблица.Rows.Remove(строка);
        }

        foreach (var строка in упорядоченныеСтроки)
        {
            _таблица.Rows.Add(строка);
        }

        _исходныйПорядокИдентификаторов = null;
    }

    /// <summary>
    /// Прорисовка заголовка группы на всю ширину строки: значение в первой
    /// ячейке остаётся машинным (тесты/harness), но визуально обрезается
    /// узкой колонкой VIP — текст рисуется поверх строки (ревью S07 F3).
    /// </summary>
    private void НарисоватьЗаголовокГруппы(object? отправитель, DataGridViewRowPostPaintEventArgs аргументы)
    {
        if (_таблица is null || аргументы.RowIndex < 0
            || аргументы.RowIndex >= _таблица.Rows.Count)
        {
            return;
        }

        if (_таблица.Rows[аргументы.RowIndex].Tag is not ЗаголовокГруппы)
        {
            return;
        }

        var текст = _таблица.Rows[аргументы.RowIndex].Cells[0].Value?.ToString();
        if (string.IsNullOrEmpty(текст))
        {
            return;
        }

        var левыйКрай = _таблица.RowHeadersVisible ? _таблица.RowHeadersWidth : 0;
        var ширина = _таблица.Columns
            .GetColumnsWidth(DataGridViewElementStates.Visible);
        var область = new System.Drawing.Rectangle(
            левыйКрай + 1,
            аргументы.RowBounds.Top,
            Math.Max(1, ширина - 2),
            аргументы.RowBounds.Height - 2);

        using var кистьФона = new System.Drawing.SolidBrush(System.Drawing.SystemColors.ControlLight);
        аргументы.Graphics.FillRectangle(кистьФона, область);

        // Bold-шрифт кэшируется: аллокация GDI-шрифта на каждый paint
        // протекала бы при скролле/выделении (ревью S07 F-R2-1).
        _шрифтЗаголовка ??= new System.Drawing.Font(_таблица.Font, System.Drawing.FontStyle.Bold);
        TextRenderer.DrawText(
            аргументы.Graphics,
            текст,
            ШрифтЗаголовка(),
            new System.Drawing.Point(область.Left + 4, область.Top + 1),
            System.Drawing.SystemColors.WindowText);
    }

    /// <summary>Отключает контроллер от таблицы.</summary>
    public void Dispose()
    {
        if (_таблица is not null)
        {
            _таблица.CellClick -= ОбработатьКликПоСтроке;
            _таблица.RowPostPaint -= НарисоватьЗаголовокГруппы;
            _таблица = null;
        }

        _шрифтЗаголовка?.Dispose();
        _шрифтЗаголовка = null;
    }
}
