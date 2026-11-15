using Picket302.Domain.Entities;
using Picket302.Printing;

namespace Picket302.Application.Services;

/// <summary>
/// Регистратор истории печати конвертов поверх доменного сервиса писем
/// (ТЗ §3.1.7, §8.4, функция 64).
/// </summary>
/// <remarks>
/// <para>
/// Снимок адресов получателя и отправителя на момент печати формирует
/// <see cref="MailService.RegisterPrintAsync"/>: последующие изменения карточек
/// контрагентов не изменяют исторические записи (ТЗ §3.1.19). Сбой записи
/// пробрасывается вызывающему процессу: состоявшаяся печать от сбоя журнала
/// не перестаёт быть состоявшейся, а несостоявшаяся — не превращается в
/// состоявшуюся.
/// </para>
/// <para>
/// Полный набор исходов («подготовлено», «ошибка», «неизвестный исход»)
/// ведёт общий журнал совместного режима: там исход нужен, чтобы отличить
/// «не напечатано» от «неизвестно». Одиночный журнал остаётся журналом
/// напечатанных конвертов и хранит только состоявшиеся выводы.
/// </para>
/// </remarks>
public sealed class PrintHistoryRecorder : IPrintHistoryRecorder
{
    private readonly IPrintHistoryWriter _письма;
    private readonly bool _фиксироватьНеуспешныеИсходы;

    /// <summary>
    /// Создаёт регистратор истории печати.
    /// </summary>
    /// <param name="mails">
    /// Служба записи печатных событий: в одиночном режиме — локальная служба
    /// писем, в совместном — запись через серверную службу.
    /// </param>
    /// <param name="фиксироватьНеуспешныеИсходы">
    /// Фиксировать ли в журнале исходы, при которых конверт не напечатан
    /// («подготовлено», «ошибка», «неизвестный исход»). В одиночном режиме —
    /// нет: журнал напечатанных конвертов хранит только состоявшиеся выводы.
    /// </param>
    public PrintHistoryRecorder(IPrintHistoryWriter mails, bool фиксироватьНеуспешныеИсходы = false)
    {
        ArgumentNullException.ThrowIfNull(mails);

        _письма = mails;
        _фиксироватьНеуспешныеИсходы = фиксироватьНеуспешныеИсходы;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        IReadOnlyList<long> mailIds,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default,
        PrintOutcome outcome = PrintOutcome.Spooled)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        if (string.IsNullOrWhiteSpace(templateDescription))
        {
            throw new ArgumentException(
                "Описание шаблона печати не задано.",
                nameof(templateDescription));
        }

        if (outcome != PrintOutcome.Spooled && !_фиксироватьНеуспешныеИсходы)
        {
            return;
        }

        foreach (var mailId in mailIds)
        {
            var результат = await _письма
                .RegisterPrintAsync(
                    mailId,
                    printJobId,
                    templateDescription,
                    cancellationToken,
                    outcome)
                .ConfigureAwait(false);

            if (результат.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Не удалось записать историю печати отправления {mailId}: {результат.Error}");
            }
        }
    }
}
