using Picket302.Printing.Validation;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Показатель модального окна предупреждения о слишком большом конверте
/// (ТЗ §8.6, функция 85): слой представления приложения для
/// <c>Printing.Validation.OversizeWarning</c>.
/// </summary>
/// <remarks>
/// Перечень доступных форматов выбранного принтера передаётся слою печати, и
/// тот включается в текст предупреждения. Если перечень пуст, окно сообщает
/// об этом прямо, а не показывает пустой список.
/// </remarks>
public sealed class OversizeWarningPresenter : IOversizeWarningPresenter
{
    private readonly IReadOnlyList<string> _доступныеФорматы;

    /// <summary>
    /// Создаёт показатель предупреждения.
    /// </summary>
    /// <param name="доступныеФорматы">Форматы носителя, доступные на принтере.</param>
    public OversizeWarningPresenter(IReadOnlyList<string>? доступныеФорматы = null)
    {
        _доступныеФорматы = доступныеФорматы ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public void Show(string message)
    {
        using var окно = new FormOversizeWarning(СобратьТекст(message));
        окно.ShowDialog();
    }

    /// <summary>
    /// Дополняет текст предупреждения сведениями о перечне доступных форматов
    /// принтера: если перечень пуст, окно сообщает об этом прямо, а не
    /// показывает пустой список.
    /// </summary>
    /// <param name="message">Текст предупреждения.</param>
    /// <returns>Итоговый текст предупреждения.</returns>
    public string СобратьТекст(string message) =>
        _доступныеФорматы.Count == 0
            ? message + " Список доступных форматов принтера пуст."
            : message;
}
