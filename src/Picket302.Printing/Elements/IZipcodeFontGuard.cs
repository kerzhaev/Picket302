namespace Picket302.Printing.Elements;

/// <summary>
/// Контроль наличия шрифта ZIPcode (ТЗ §6.3; §8.6, функция 90:
/// <c>Infrastructure.Fonts.ZipcodeFontGuard</c>).
/// </summary>
/// <remarks>
/// Шрифт ZIPcode поставляется с программой. Скрытый фолбэк на Arial/Times
/// запрещён: при отсутствии шрифта печать конверта блокируется.
/// </remarks>
public interface IZipcodeFontGuard
{
    /// <summary>
    /// Признак доступности шрифта ZIPcode в системе.
    /// </summary>
    bool IsZipcodeFontAvailable { get; }

    /// <summary>
    /// Проверяет наличие шрифта ZIPcode, при необходимости регистрируя его
    /// в текущем сеансе.
    /// </summary>
    /// <returns><see langword="true"/>, если шрифт доступен.</returns>
    bool EnsureZipcodeFontInstalled();
}
