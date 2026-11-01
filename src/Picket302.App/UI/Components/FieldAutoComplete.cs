using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Подбор значения поля из имеющихся записей (ТЗ §8.11, функция 133:
/// <c>public IEnumerable&lt;string&gt; SuggestValues(string fieldName, string prefix)</c>;
/// §7.7: «пустое поле + несколько нажатий BackSpace»).
/// </summary>
/// <remarks>
/// <para>
/// Значения берутся выборкой DISTINCT по столбцу поля; подбор возвращает
/// уникальные значения, отсортированные по алфавиту. Пустое поле и нажатие
/// BackSpace приводят к циклическому подставлению значений поля.
/// </para>
/// </remarks>
public sealed class FieldAutoComplete
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _загрузчик;

    /// <summary>
    /// Создаёт механизм подбора значений поля.
    /// </summary>
    /// <param name="загрузчик">Загрузчик имеющихся значений поля.</param>
    public FieldAutoComplete(Func<string, CancellationToken, Task<IReadOnlyList<string>>> загрузчик)
    {
        ArgumentNullException.ThrowIfNull(загрузчик);

        _загрузчик = загрузчик;
    }

    /// <summary>
    /// Возвращает значения поля, подходящие под набранный префикс.
    /// </summary>
    /// <param name="fieldName">Имя поля.</param>
    /// <param name="prefix">Набранный префикс.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Уникальные значения поля по алфавиту.</returns>
    public async Task<IReadOnlyList<string>> SuggestValuesAsync(
        string fieldName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var значения = await _загрузчик(fieldName, cancellationToken).ConfigureAwait(true);

        IEnumerable<string> уникальные = значения
            .Where(значение => !string.IsNullOrWhiteSpace(значение))
            .Select(значение => значение.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(значение => значение, StringComparer.CurrentCultureIgnoreCase);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            var образец = prefix.Trim();

            уникальные = уникальные.Where(значение =>
                значение.StartsWith(образец, StringComparison.CurrentCultureIgnoreCase));
        }

        return уникальные.ToArray();
    }

    /// <summary>
    /// Возвращает очередное значение для подстановки: значения перебираются
    /// циклически (ТЗ §7.7: «несколько нажатий BackSpace подставляют
    /// существующие значения этого поля»).
    /// </summary>
    /// <param name="значения">Имеющиеся значения поля.</param>
    /// <param name="индекс">Текущий индекс перебора.</param>
    /// <returns>Значение для подстановки либо <see langword="null"/>.</returns>
    public static string? Следующее(IReadOnlyList<string> значения, int индекс)
    {
        ArgumentNullException.ThrowIfNull(значения);

        if (значения.Count == 0)
        {
            return null;
        }

        var нормализованный = ((индекс % значения.Count) + значения.Count) % значения.Count;

        return значения[нормализованный];
    }

    /// <summary>
    /// Определяет, что подбор значения должен быть предложен: курсор в пустом
    /// поле и нажата клавиша BackSpace.
    /// </summary>
    /// <param name="полеПустое">Признак пустого поля ввода.</param>
    /// <param name="нажатаяКлавиша">Нажатая клавиша.</param>
    /// <returns><see langword="true"/>, если подбор значений должен быть предложен.</returns>
    public static bool ТребуетсяПодбор(bool полеПустое, Keys нажатаяКлавиша)
        => полеПустое && нажатаяКлавиша == Keys.Back;
}
