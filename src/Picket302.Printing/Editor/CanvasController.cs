using Picket302.Domain.Serialization;

namespace Picket302.Printing.Editor;

/// <summary>Инструмент палитры редактора (ТЗ §7.5: панель объектов слева).</summary>
public enum BlankTool
{
    /// <summary>Текст (пиктограмма «A»).</summary>
    Text = 0,

    /// <summary>Почтовый индекс (специализированный компонент).</summary>
    PostIndex = 1,

    /// <summary>Рисунок (логотип/эмблема).</summary>
    Picture = 2,

    /// <summary>QR-код.</summary>
    QrCode = 3,

    /// <summary>Прямоугольная зона.</summary>
    Zone = 4,
}

/// <summary>Состояние редактора (ТЗ §7.5).</summary>
public enum EditorState
{
    /// <summary>Макет не загружен (серый холст).</summary>
    NoLayout = 0,

    /// <summary>Загрузка.</summary>
    Loading = 1,

    /// <summary>Готов.</summary>
    Ready = 2,

    /// <summary>Конфликт зон (красная штриховка пересечений и список предупреждений).</summary>
    ZoneConflict = 3,

    /// <summary>Сохранение запрещено (элемент за границами печатной области).</summary>
    SaveForbidden = 4,
}

/// <summary>
/// Контроллер холста встроенного редактора бланков (ТЗ §8.3, функция 38:
/// <c>Printing.Editor.CanvasController</c>; §5.2, §7.5).
/// </summary>
/// <remarks>
/// Внутренние координаты — миллиметры с точностью 0,01 мм. Контроллер
/// реализует семантику флагов Visible/Printable ровно по §7.5:
/// <list type="bullet">
/// <item><c>Visible=false</c> — элемент исключён везде: не рисуется на холсте
/// экрана и не попадает в поток печати;</item>
/// <item><c>Printable=false</c> — элемент рисуется на холсте экрана
/// (рамки, подсказки, разлиновка), но отсекается при выводе на принтер.</item>
/// </list>
/// </remarks>
public sealed class CanvasController
{
    private readonly BlankLayoutValidator _валидатор;

    /// <summary>Загруженный бланк; <see langword="null"/> — состояние «Макет не загружен».</summary>
    public CustomBlank? Blank { get; private set; }

    /// <summary>Имя выделенного поля.</summary>
    public string? SelectedFieldName { get; private set; }

    /// <summary>Текущий инструмент палитры.</summary>
    public BlankTool Tool { get; set; } = BlankTool.Text;

    /// <summary>Единица представления координат.</summary>
    public EditorUnit Unit { get; set; } = EditorUnit.Millimeters;

    /// <summary>Плотность экрана для пиксельного представления, dpi (96/120).</summary>
    public int ScreenDpi { get; set; } = EditorUnits.DefaultScreenDpi;

    /// <summary>Признак привязки к сетке (Snap).</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Последний отчёт проверки разметки.</summary>
    public LayoutValidationReport? LastValidation { get; private set; }

    /// <summary>Текущее состояние редактора.</summary>
    public EditorState State { get; private set; } = EditorState.NoLayout;

    /// <summary>Создаёт контроллер холста.</summary>
    /// <param name="validator">Валидатор разметки; по умолчанию без контроля шрифта.</param>
    public CanvasController(BlankLayoutValidator? validator = null)
    {
        _валидатор = validator ?? new BlankLayoutValidator();
    }

    /// <summary>Открывает макет в редакторе (ТЗ §8.3, функция 38: OpenEditor).</summary>
    /// <param name="blank">Бланк.</param>
    public void Open(CustomBlank blank)
    {
        ArgumentNullException.ThrowIfNull(blank);

        Blank = blank;
        SelectedFieldName = null;
        State = EditorState.Loading;
        ПроверитьРазметку();
    }

    /// <summary>
    /// Выделяет поле по имени.
    /// </summary>
    /// <param name="fieldName">Имя поля; <see langword="null"/> — снять выделение.</param>
    public void Select(string? fieldName)
    {
        SelectedFieldName = fieldName;
    }

    /// <summary>Возвращает выделенное поле.</summary>
    /// <returns>Поле либо <see langword="null"/>.</returns>
    public BlankFieldSpec? GetSelected() =>
        Blank?.AllFields().FirstOrDefault(поле => поле.Name == SelectedFieldName);

    /// <summary>
    /// Определяет поле по точке холста (щелчок мышью).
    /// </summary>
    /// <param name="page">Номер страницы.</param>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    /// <param name="includeScreenOnly">
    /// Включать элементы, выводимые только на экран (<c>Printable=false</c>):
    /// они кликабельны на холсте, поскольку Visible-элементы показываются.
    /// </param>
    /// <returns>Имя поля либо <see langword="null"/>.</returns>
    public string? HitTest(int page, decimal xMm, decimal yMm, bool includeScreenOnly = true)
    {
        if (Blank is null)
        {
            return null;
        }

        // Visible=false исключает элемент полностью — он не попадает ни в холст,
        // ни в печать (ТЗ §7.5). Printable=false остаётся на холсте.
        var кандидаты = Blank.Pages
            .Where(страница => страница.Number == page)
            .SelectMany(страница => страница.Fields)
            .Where(поле => поле.Visible && (includeScreenOnly || поле.Printable))
            .OrderBy(поле => поле.Kind == BlankFieldKinds.Zone ? 1 : 0);

        foreach (var поле in кандидаты)
        {
            var г = поле.Bounds;

            if (xMm >= г.XMm && xMm <= г.RightMm && yMm >= г.YMm && yMm <= г.BottomMm)
            {
                return поле.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Поля для печати: элементы с Visible=true и Printable=true
    /// (ТЗ §7.5: Printable=false отсекается в потоке команд принтера).
    /// </summary>
    /// <param name="page">Номер страницы.</param>
    /// <returns>Список печатаемых полей.</returns>
    public IReadOnlyList<BlankFieldSpec> GetPrintableFields(int page)
    {
        if (Blank is null)
        {
            return Array.Empty<BlankFieldSpec>();
        }

        return Blank.Pages
            .Where(страница => страница.Number == page)
            .SelectMany(страница => страница.Fields)
            .Where(поле => поле.Visible && поле.Printable)
            .ToList();
    }

    /// <summary>
    /// Поля для отрисовки на холсте экрана: элементы с Visible=true
    /// (ТЗ §7.5: Printable=false рисуется на экране, но не печатается).
    /// </summary>
    /// <param name="page">Номер страницы.</param>
    /// <returns>Список полей холста.</returns>
    public IReadOnlyList<BlankFieldSpec> GetCanvasFields(int page)
    {
        if (Blank is null)
        {
            return Array.Empty<BlankFieldSpec>();
        }

        return Blank.Pages
            .Where(страница => страница.Number == page)
            .SelectMany(страница => страница.Fields)
            .Where(поле => поле.Visible)
            .ToList();
    }

    /// <summary>
    /// Перемещает поле с привязкой к сетке и перепроверкой разметки
    /// (ТЗ §8.3, функция 38: «Перемещение поля „Кому“ мышью сохраняет новые
    /// координаты x_mm, y_mm в XML»).
    /// </summary>
    /// <param name="fieldName">Имя поля.</param>
    /// <param name="xMm">Новая координата X, мм.</param>
    /// <param name="yMm">Новая координата Y, мм.</param>
    /// <returns><see langword="false"/>, если поле не найдено.</returns>
    public bool MoveField(string fieldName, decimal xMm, decimal yMm)
    {
        var поле = Blank?.AllFields().FirstOrDefault(п => п.Name == fieldName);

        if (поле is null)
        {
            return false;
        }

        поле.Bounds.XMm = SnapToGrid ? EditorUnits.Snap(Math.Round(xMm, 2)) : Math.Round(xMm, 2);
        поле.Bounds.YMm = SnapToGrid ? EditorUnits.Snap(Math.Round(yMm, 2)) : Math.Round(yMm, 2);

        ПроверитьРазметку();

        return true;
    }

    /// <summary>
    /// Изменяет габариты поля (маркеры изменения размера).
    /// </summary>
    /// <param name="fieldName">Имя поля.</param>
    /// <param name="widthMm">Ширина, мм.</param>
    /// <param name="heightMm">Высота, мм.</param>
    /// <returns><see langword="false"/>, если поле не найдено.</returns>
    public bool ResizeField(string fieldName, decimal widthMm, decimal heightMm)
    {
        var поле = Blank?.AllFields().FirstOrDefault(п => п.Name == fieldName);

        if (поле is null)
        {
            return false;
        }

        поле.Bounds.WidthMm = Math.Max(0.01m, Math.Round(widthMm, 2));
        поле.Bounds.HeightMm = Math.Max(0.01m, Math.Round(heightMm, 2));

        ПроверитьРазметку();

        return true;
    }

    /// <summary>
    /// Добавляет поле текущим инструментом палитры.
    /// </summary>
    /// <param name="page">Номер страницы.</param>
    /// <param name="xMm">Положение по горизонтали, мм.</param>
    /// <param name="yMm">Положение по вертикали, мм.</param>
    /// <param name="fieldName">Имя нового поля.</param>
    /// <returns>Созданное поле либо <see langword="null"/>, если макет не загружен.</returns>
    public BlankFieldSpec? AddField(int page, decimal xMm, decimal yMm, string fieldName)
    {
        if (Blank is null)
        {
            return null;
        }

        if (Blank.AllFields().Any(поле => поле.Name == fieldName))
        {
            throw new InvalidOperationException($"Поле с именем «{fieldName}» уже существует на макете.");
        }

        var страница = Blank.Pages.FirstOrDefault(п => п.Number == page);

        if (страница is null)
        {
            страница = new BlankPage { Number = page };
            Blank.Pages.Add(страница);
        }

        var поле = new BlankFieldSpec
        {
            Name = fieldName,
            Kind = Tool switch
            {
                BlankTool.PostIndex => BlankFieldKinds.PostIndex,
                BlankTool.Picture => BlankFieldKinds.Picture,
                BlankTool.QrCode => BlankFieldKinds.Qr,
                BlankTool.Zone => BlankFieldKinds.Zone,
                _ => BlankFieldKinds.Text,
            },
            Bounds = new BlankBounds
            {
                XMm = SnapToGrid ? EditorUnits.Snap(xMm) : xMm,
                YMm = SnapToGrid ? EditorUnits.Snap(yMm) : yMm,
                WidthMm = 35m,
                HeightMm = 6m,
            },
        };

        if (поле.Kind == BlankFieldKinds.PostIndex)
        {
            поле.PostIndex = new BlankPostIndexSpec();
            поле.Bounds.HeightMm = 8m;
        }

        страница.Fields.Add(поле);
        Select(fieldName);
        ПроверитьРазметку();

        return поле;
    }

    /// <summary>Удаляет поле с макета.</summary>
    /// <param name="fieldName">Имя поля.</param>
    /// <returns><see langword="true"/>, если поле удалено.</returns>
    public bool RemoveField(string fieldName)
    {
        if (Blank is null)
        {
            return false;
        }

        foreach (var страница in Blank.Pages)
        {
            var поле = страница.Fields.FirstOrDefault(п => п.Name == fieldName);

            if (поле is not null)
            {
                страница.Fields.Remove(поле);

                if (SelectedFieldName == fieldName)
                {
                    Select(null);
                }

                ПроверитьРазметку();

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Разрешено ли сохранение: отсутствуют нарушения, блокирующие сохранение
    /// (ТЗ §7.5: состояние «Сохранение запрещено» — элемент за границами
    /// печатной области).
    /// </summary>
    public bool CanSave() => LastValidation is null || !LastValidation.SaveBlocked;

    /// <summary>Перепроверяет разметку и обновляет состояние редактора.</summary>
    public void RefreshValidation() => ПроверитьРазметку();

    private void ПроверитьРазметку()
    {
        if (Blank is null)
        {
            State = EditorState.NoLayout;
            LastValidation = null;

            return;
        }

        LastValidation = _валидатор.Validate(Blank);

        State = LastValidation.SaveBlocked
            ? EditorState.SaveForbidden
            : LastValidation.Overlaps.Count > 0
                ? EditorState.ZoneConflict
                : EditorState.Ready;
    }
}
