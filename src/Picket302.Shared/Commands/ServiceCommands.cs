using Picket302.Shared.Dtos;

namespace Picket302.Shared.Commands;

/// <summary>
/// Фиксация печатного события в общем журнале.
/// </summary>
/// <remarks>
/// Событие отправляет клиент, который выполнил печать на своём рабочем месте:
/// служба не печатает и не знает, что напечатано на самом деле. Ответные
/// коды различают «подготовлено», «передано локальному спулеру», «ошибка» и
/// «неизвестный исход после обрыва».
/// </remarks>
/// <param name="MailIds">Отправления.</param>
/// <param name="TemplateDescription">Описание использованного шаблона.</param>
/// <param name="Outcome">Исход печати.</param>
/// <param name="PrinterName">Имя принтера рабочего места.</param>
/// <param name="PaperSizeName">Формат бумаги.</param>
/// <param name="Copies">Число экземпляров.</param>
/// <param name="PrintMode">Режим печати.</param>
/// <param name="ErrorText">Текст ошибки при исходе «ошибка».</param>
public sealed record RecordPrintEventRequest(
    IReadOnlyList<long> MailIds,
    string TemplateDescription,
    PrintEventOutcome Outcome,
    string? PrinterName = null,
    string? PaperSizeName = null,
    int Copies = 1,
    string PrintMode = "selected",
    string? ErrorText = null);

/// <summary>
/// Запись в журнал email-рассылки.
/// </summary>
/// <param name="Recipient">Получатель.</param>
/// <param name="Status">Статус отправки.</param>
/// <param name="SmtpCode">Код ответа SMTP-сервера.</param>
/// <param name="ServerResponse">Ответ SMTP-сервера.</param>
/// <param name="Error">Текст ошибки.</param>
/// <param name="SentAtUtc">Момент отправки (UTC).</param>
public sealed record EmailLogEntryDto(
    string Recipient,
    string Status,
    int? SmtpCode,
    string? ServerResponse,
    string? Error,
    DateTime SentAtUtc);

/// <summary>
/// Пакетная запись в журнал email-рассылки.
/// </summary>
/// <param name="Entries">Записи журнала.</param>
public sealed record WriteEmailLogRequest(IReadOnlyList<EmailLogEntryDto> Entries);

/// <summary>
/// Ответ записи в журнал email-рассылки.
/// </summary>
/// <param name="Written">Число записанных строк.</param>
public sealed record WriteEmailLogResponse(int Written);

/// <summary>
/// Запрос данных отправлений для печати.
/// </summary>
/// <param name="MailIds">Идентификаторы отправлений.</param>
public sealed record GetPrintDataRequest(IReadOnlyList<long> MailIds);

/// <summary>
/// Ответ с данными отправлений для печати.
/// </summary>
/// <param name="Items">Данные отправлений.</param>
public sealed record GetPrintDataResponse(IReadOnlyList<PrintableItemDto> Items);
