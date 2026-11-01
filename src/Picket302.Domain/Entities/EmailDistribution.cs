using Picket302.Domain.Email;

namespace Picket302.Domain.Entities;

/// <summary>
/// Электронная рассылка — отдельный канал отправки, не связанный с бумажными
/// отправлениями (ТЗ §8.7, функция 92: «Электронная рассылка (отдельно от
/// бумажной)»).
/// </summary>
/// <remarks>
/// <para>
/// Рассылка не создаёт записей таблицы <c>mails</c>: она лишь формирует письма
/// и пишет журнал отправки. Получатели задаются выборкой — по категории,
/// по фильтру или по выделению в таблице.
/// </para>
/// <para>
/// Тема и тело письма содержат маркеры полей в синтаксисе <c>#Имя#</c> — том
/// же, что применяется в шаблонах документов (ТЗ §8.5, функция 66).
/// </para>
/// </remarks>
public sealed class EmailDistribution
{
    /// <summary>Идентификатор рассылки (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование рассылки.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тема письма с маркерами полей.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Тело письма в выбранном формате.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Формат тела письма.</summary>
    public BodyFormat BodyFormat { get; set; } = BodyFormat.PlainText;

    /// <summary>Адрес отправителя (конверт отправки).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Обратный адрес для ответа (Reply-To).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Получатели рассылки.</summary>
    public List<EmailRecipient> Recipients { get; } = [];

    /// <summary>Пути к вложениям письма.</summary>
    public List<string> Attachments { get; } = [];

    /// <summary>Момент создания рассылки (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Устанавливает формат и содержимое тела письма (ТЗ §8.7, функция 96:
    /// <c>public void SetBodyFormat(BodyFormat format, string body)</c>).
    /// </summary>
    /// <param name="format">Формат тела письма.</param>
    /// <param name="body">Содержимое тела письма.</param>
    /// <remarks>
    /// Для HTML выполняется базовая санитизация разметки: удаляются элементы
    /// <c>script</c>, обработчики событий и прочие исполняемые вставки.
    /// </remarks>
    public void SetBodyFormat(BodyFormat format, string body)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Неизвестный формат тела письма.");
        }

        BodyFormat = format;
        Body = format == BodyFormat.Html ? HtmlBodySanitizer.Sanitize(body) : body ?? string.Empty;
    }
}

/// <summary>
/// Получатель электронной рассылки (ТЗ §8.7, функция 92: данные — счётчик
/// <c>counterparties.email</c>).
/// </summary>
/// <param name="CounterpartyId">Идентификатор контрагента-получателя.</param>
/// <param name="Name">Наименование контрагента.</param>
/// <param name="Email">
/// Адрес электронной почты контрагента; пустая строка — адрес отсутствует, и
/// получатель исключается из отправки с перечислением в отчёте.
/// </param>
public sealed record EmailRecipient(long CounterpartyId, string Name, string Email)
{
    /// <summary>Признак получателя без адреса электронной почты.</summary>
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
}
