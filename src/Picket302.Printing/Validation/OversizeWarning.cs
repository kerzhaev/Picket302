using System.Drawing;
using System.Globalization;

namespace Picket302.Printing.Validation;

/// <summary>
/// Показ блокирующего предупреждения о несоответствии размеров (ТЗ §8.6,
/// функция 85: <c>Printing.Validation.OversizeWarning</c>).
/// </summary>
/// <remarks>
/// Централизованная точка вывода предупреждения «Конверт слишком большой для
/// выбранного принтера» с числовым указанием обеих величин (мм) и перечнем
/// доступных форматов выбранного принтера.
/// </remarks>
public sealed class OversizeWarning
{
    /// <summary>Заголовок блокирующего предупреждения.</summary>
    public const string Title = "Конверт слишком большой для выбранного принтера";

    private readonly IOversizeWarningPresenter? _presenter;
    private readonly IReadOnlyList<string> _доступныеФорматы;

    /// <summary>
    /// Создаёт средство вывода предупреждения.
    /// </summary>
    /// <param name="presenter">Показатель модального окна (слой представления).</param>
    /// <param name="availablePaperSizes">Форматы носителя, доступные на принтере.</param>
    public OversizeWarning(
        IOversizeWarningPresenter? presenter = null,
        IReadOnlyList<string>? availablePaperSizes = null)
    {
        _presenter = presenter;
        _доступныеФорматы = availablePaperSizes ?? Array.Empty<string>();
    }

    /// <summary>
    /// Формирует текст блокирующего предупреждения.
    /// </summary>
    /// <param name="blank">Размер бланка, мм.</param>
    /// <param name="paper">Размер печатной области носителя, мм.</param>
    /// <returns>Текст предупреждения.</returns>
    public string BuildMessage(SizeF blank, SizeF paper) => BuildMessage(blank, paper, Title);

    /// <summary>
    /// Формирует текст блокирующего предупреждения с заданным заголовком.
    /// </summary>
    /// <param name="blank">Размер бланка, мм.</param>
    /// <param name="paper">Размер печатной области носителя, мм.</param>
    /// <param name="title">Заголовок предупреждения.</param>
    /// <returns>Текст предупреждения.</returns>
    /// <remarks>
    /// Заголовок по умолчанию — формулировка ТЗ §8.6, функции 85; проверка
    /// метрик ТЗ §6.2 использует собственную формулировку ошибки.
    /// </remarks>
    public string BuildMessage(SizeF blank, SizeF paper, string title)
    {
        var инвариант = CultureInfo.InvariantCulture;

        var текст = string.Create(
            инвариант,
            $"{title}. Размер бланка: {blank.Width:0.##}×{blank.Height:0.##} мм; " +
            $"размер печатной области носителя: {paper.Width:0.##}×{paper.Height:0.##} мм.");

        if (_доступныеФорматы.Count == 0)
        {
            return текст;
        }

        return текст + " Доступные форматы принтера: " + string.Join(", ", _доступныеФорматы) + ".";
    }

    /// <summary>
    /// Выводит блокирующее предупреждение о слишком большом конверте.
    /// </summary>
    /// <param name="blank">Размер бланка, мм.</param>
    /// <param name="paper">Размер печатной области носителя, мм.</param>
    public void ShowOversizeDialog(SizeF blank, SizeF paper)
    {
        _presenter?.Show(BuildMessage(blank, paper));
    }
}

/// <summary>
/// Показатель модального окна предупреждения о размере конверта.
/// </summary>
public interface IOversizeWarningPresenter
{
    /// <summary>
    /// Показывает модальное окно с текстом предупреждения.
    /// </summary>
    /// <param name="message">Текст предупреждения.</param>
    void Show(string message);
}
