using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Подбор значения поля из имеющихся записей
/// (ТЗ §7.7: «Дополнительно: подбор значения поля из имеющихся записей — курсор
/// в пустое поле, несколько нажатий BackSpace подставляют существующие значения
/// этого поля»; §8.1, функция 133).
/// </summary>
/// <remarks>
/// Механизм срабатывает, когда курсор стоит в пустом поле поля ввода и
/// пользователь нажимает BackSpace: полю предлагаются значения, уже встречающиеся
/// в записях этого поля.
/// </remarks>
public sealed class FieldValueAutoComplete
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _загрузчик;

    /// <summary>
    /// Создаёт подбор значений поля.
    /// </summary>
    /// <param name="загрузчик">Загрузчик имеющихся значений поля.</param>
    public FieldValueAutoComplete(Func<string, CancellationToken, Task<IReadOnlyList<string>>> загрузчик)
    {
        ArgumentNullException.ThrowIfNull(загрузчик);
        _загрузчик = загрузчик;
    }

    /// <summary>
    /// Возвращает имеющиеся значения поля.
    /// </summary>
    /// <param name="поле">Имя поля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список значений поля.</returns>
    public Task<IReadOnlyList<string>> ПолучитьЗначенияAsync(
        string поле,
        CancellationToken cancellationToken = default)
        => _загрузчик(поле, cancellationToken);

    /// <summary>
    /// Определяет, что подбор значений должен быть предложен: курсор в пустом
    /// поле и нажата клавиша BackSpace.
    /// </summary>
    /// <param name="полеПустое">Признак пустого поля ввода.</param>
    /// <param name="нажатаяКлавиша">Нажатая клавиша.</param>
    /// <returns><see langword="true"/>, если подбор значений должен быть предложен.</returns>
    public static bool ТребуетсяПодбор(bool полеПустое, Keys нажатаяКлавиша)
        => полеПустое && нажатаяКлавиша == Keys.Back;

    /// <summary>
    /// Возвращает значение, подставляемое в поле по совпавшему префиксу.
    /// </summary>
    /// <param name="значения">Имеющиеся значения поля.</param>
    /// <param name="префикс">Набранный префикс.</param>
    /// <returns>Первое совпавшее значение либо <see langword="null"/>.</returns>
    public static string? Подобрать(IReadOnlyList<string> значения, string? префикс)
    {
        ArgumentNullException.ThrowIfNull(значения);

        if (string.IsNullOrWhiteSpace(префикс))
        {
            return значения.Count > 0 ? значения[0] : null;
        }

        var образец = префикс.Trim();

        return значения.FirstOrDefault(значение =>
            значение.StartsWith(образец, StringComparison.CurrentCultureIgnoreCase));
    }
}
