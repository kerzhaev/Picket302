namespace Picket302.App.UI;

/// <summary>
/// Текст индикации текущей базы для строки состояния.
/// </summary>
/// <remarks>
/// <para>
/// Раньше текст строился только из пути к локальному файлу. В общем режиме
/// путь пуст, и после «База:» не оставалось ничего: оператор не мог отличить
/// общую базу от локальной. Здесь режим задаётся явно.
/// </para>
/// <para>
/// В общий текст попадают только узел и порт службы. Секрет устройства,
/// отпечаток сертификата и административный токен в интерфейс не выводятся.
/// </para>
/// </remarks>
public static class DatabaseStateText
{
    /// <summary>
    /// Собирает тексты индикации базы: обычный текст, подсказку и имя для
    /// средств доступности.
    /// </summary>
    /// <param name="адресОбщейБазы">
    /// Адрес службы в виде «узел:порт»; <see langword="null"/> или пусто —
    /// одиночный режим.
    /// </param>
    /// <param name="путьБазы">Путь к локальному файлу базы.</param>
    /// <returns>Согласованный набор текстов для строки состояния.</returns>
    public static DatabaseStateTexts Построить(string? адресОбщейБазы, string? путьБазы)
    {
        if (!string.IsNullOrWhiteSpace(адресОбщейБазы))
        {
            var адрес = адресОбщейБазы.Trim();

            return new DatabaseStateTexts(
                $"Общая база: {адрес}",
                $"Данные находятся в базе службы {адрес}. Локальный файл базы не используется.",
                $"Общая база: {адрес}");
        }

        var файл = string.IsNullOrWhiteSpace(путьБазы)
            ? string.Empty
            : Path.GetFileName(путьБазы);

        if (string.IsNullOrWhiteSpace(файл))
        {
            return new DatabaseStateTexts(
                "Локальная база: не выбрана",
                путьБазы ?? string.Empty,
                "Локальная база не выбрана");
        }

        return new DatabaseStateTexts(
            $"Локальная база: {файл}",
            путьБазы!,
            $"Локальная база: {путьБазы}");
    }
}

/// <summary>
/// Согласованные тексты индикации базы.
/// </summary>
/// <param name="Text">Текст в строке состояния.</param>
/// <param name="Tooltip">Всплывающая подсказка.</param>
/// <param name="AccessibleName">Имя для средств доступности.</param>
public sealed record DatabaseStateTexts(string Text, string Tooltip, string AccessibleName);
