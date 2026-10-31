using Picket302.Domain.Serialization;

namespace Picket302.Printing.Editor;

/// <summary>Строгость нарушения разметки.</summary>
public enum LayoutViolationSeverity
{
    /// <summary>Предупреждение: сохранение разрешено.</summary>
    Warning = 0,

    /// <summary>Ошибка: сохранение запрещено (ТЗ §7.5, состояние «Сохранение запрещено»).</summary>
    SaveBlocked = 1,
}

/// <summary>Виды нарушений разметки бланка (ТЗ §7.5, §5.2).</summary>
public enum LayoutViolationKind
{
    /// <summary>Пересечение AABB двух полей.</summary>
    FieldOverlap = 0,

    /// <summary>Выход элемента за границы страницы.</summary>
    OutsidePage = 1,

    /// <summary>Толщина линии индекса вне 0,2–0,5 мм (ГОСТ Р 51506-99).</summary>
    PostIndexLineWidth = 2,

    /// <summary>Отсутствие шрифта ZIPcode в системе.</summary>
    MissingZipcodeFont = 3,
}

/// <summary>Нарушение разметки бланка.</summary>
/// <param name="Kind">Вид нарушения.</param>
/// <param name="Severity">Строгость.</param>
/// <param name="Message">Текст для списка предупреждений редактора.</param>
/// <param name="FirstField">Имя первого поля.</param>
/// <param name="SecondField">Имя второго поля (для пересечений).</param>
public sealed record LayoutViolation(
    LayoutViolationKind Kind,
    LayoutViolationSeverity Severity,
    string Message,
    string? FirstField = null,
    string? SecondField = null);

/// <summary>Результат проверки разметки бланка.</summary>
public sealed class LayoutValidationReport
{
    /// <summary>Нарушения.</summary>
    public List<LayoutViolation> Violations { get; } = new();

    /// <summary>Пересечения полей — конфликтные контуры подсвечиваются красной штриховкой.</summary>
    public List<(string First, string Second)> Overlaps { get; } = new();

    /// <summary>Есть ли нарушения, блокирующие сохранение.</summary>
    public bool SaveBlocked => Violations.Any(нарушение => нарушение.Severity == LayoutViolationSeverity.SaveBlocked);

    /// <summary>Все тексты нарушений.</summary>
    public IReadOnlyList<string> Messages => Violations.Select(нарушение => нарушение.Message).ToList();
}

/// <summary>
/// Проверка пространственной разметки бланка: пересечения AABB, выход за
/// границы печатной области, нормативы кодового штампа (ТЗ §5.2, §7.5).
/// </summary>
public sealed class BlankLayoutValidator
{
    private readonly Func<bool>? _шрифтZipcodeДоступен;

    /// <summary>
    /// Создаёт валидатор разметки.
    /// </summary>
    /// <param name="zipcodeFontAvailable">
    /// Делегат контроля наличия шрифта ZIPcode в системе; <see langword="null"/> —
    /// проверка шрифта не выполняется (например, в тестах без Windows-шрифтов).
    /// </param>
    public BlankLayoutValidator(Func<bool>? zipcodeFontAvailable = null)
    {
        _шрифтZipcodeДоступен = zipcodeFontAvailable;
    }

    /// <summary>
    /// Детекция пересечения AABB (ТЗ §5.2):
    /// <c>Intersect(A,B) = (A.x &lt; B.x+B.w) ∧ (A.x+A.w &gt; B.x) ∧
    /// (A.y &lt; B.y+B.h) ∧ (A.y+A.h &gt; B.y)</c>.
    /// </summary>
    /// <param name="a">Первый габарит, мм.</param>
    /// <param name="b">Второй габарит, мм.</param>
    /// <returns><see langword="true"/>, если прямоугольники пересекаются.</returns>
    public static bool IntersectsAabb(BlankBounds a, BlankBounds b) =>
        a.XMm < b.RightMm && a.RightMm > b.XMm &&
        a.YMm < b.BottomMm && a.BottomMm > b.YMm;

    /// <summary>
    /// Проверяет разметку бланка перед сохранением и перед стартом печати.
    /// </summary>
    /// <param name="blank">Бланк.</param>
    /// <returns>Отчёт с нарушениями.</returns>
    public LayoutValidationReport Validate(CustomBlank blank)
    {
        ArgumentNullException.ThrowIfNull(blank);

        var отчёт = new LayoutValidationReport();

        foreach (var страница in blank.Pages)
        {
            ПроверитьСтраницу(blank, страница, отчёт);
        }

        return отчёт;
    }

    private void ПроверитьСтраницу(CustomBlank бланк, BlankPage страница, LayoutValidationReport отчёт)
    {
        var поля = страница.Fields;

        // Пересечения AABB любых двух полей страницы (ТЗ §5.2).
        for (var i = 0; i < поля.Count; i++)
        {
            for (var j = i + 1; j < поля.Count; j++)
            {
                var a = поля[i];
                var b = поля[j];

                if (IntersectsAabb(a.Bounds, b.Bounds))
                {
                    отчёт.Overlaps.Add((a.Name, b.Name));
                    отчёт.Violations.Add(new LayoutViolation(
                        LayoutViolationKind.FieldOverlap,
                        LayoutViolationSeverity.Warning,
                        $"Пересечение зон: «{a.Name}» и «{b.Name}».",
                        a.Name,
                        b.Name));
                }
            }

            ПроверитьГраницы(бланк, поля[i], отчёт);
            ПроверитьШтамп(поля[i], отчёт);
        }
    }

    private static void ПроверитьГраницы(
        CustomBlank бланк,
        BlankFieldSpec поле,
        LayoutValidationReport отчёт)
    {
        var габариты = поле.Bounds;

        var вышеЗаГраницы = габариты.XMm < 0
            || габариты.YMm < 0
            || габариты.RightMm > бланк.Geometry.WidthMm
            || габариты.BottomMm > бланк.Geometry.HeightMm;

        if (вышеЗаГраницы)
        {
            отчёт.Violations.Add(new LayoutViolation(
                LayoutViolationKind.OutsidePage,
                // ТЗ §7.5: состояние «Сохранение запрещено» — элемент за
                // границами печатной области блокирует сохранение макета.
                LayoutViolationSeverity.SaveBlocked,
                $"Элемент «{поле.Name}» выходит за границы печатной области страницы. Сохранение запрещено.",
                поле.Name));
        }
    }

    private void ПроверитьШтамп(BlankFieldSpec поле, LayoutValidationReport отчёт)
    {
        if (поле.Kind != BlankFieldKinds.PostIndex || поле.PostIndex is not { } штамп)
        {
            return;
        }

        // Толщина линии индекса вне 0,2–0,5 мм (ТЗ §7.5, §6.3).
        if (штамп.LineWidthMm < 0.20m || штамп.LineWidthMm > 0.50m)
        {
            отчёт.Violations.Add(new LayoutViolation(
                LayoutViolationKind.PostIndexLineWidth,
                LayoutViolationSeverity.Warning,
                $"Толщина линии индекса поля «{поле.Name}» вне ГОСТ (0,2–0,5 мм): {штамп.LineWidthMm:0.##} мм.",
                поле.Name));
        }

        if (_шрифтZipcodeДоступен is { } контроль && !контроль())
        {
            отчёт.Violations.Add(new LayoutViolation(
                LayoutViolationKind.MissingZipcodeFont,
                LayoutViolationSeverity.SaveBlocked,
                "Шрифт ZIPcode не найден в системе. Требуется переустановить системные шрифты из комплекта поставки.",
                поле.Name));
        }
    }
}
