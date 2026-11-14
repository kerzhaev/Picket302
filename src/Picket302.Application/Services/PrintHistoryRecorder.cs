using Picket302.Printing;

namespace Picket302.Application.Services;

/// <summary>
/// Регистратор истории печати конвертов поверх доменного сервиса писем
/// (ТЗ §3.1.7, §8.4, функция 64).
/// </summary>
/// <remarks>
/// Снимок адресов получателя и отправителя на момент печати формирует
/// <see cref="MailService.RegisterPrintAsync"/>: последующие изменения карточек
/// контрагентов не изменяют исторические записи (ТЗ §3.1.19). Сбой записи
/// пробрасывается вызывающему процессу — печатная транзакция считается
/// незавершённой (ТЗ §2.2).
/// </remarks>
public sealed class PrintHistoryRecorder : IPrintHistoryRecorder
{
    private readonly IPrintHistoryWriter _письма;

    /// <summary>
    /// Создаёт регистратор истории печати.
    /// </summary>
    /// <param name="mails">
    /// Служба записи печатных событий: в одиночном режиме — локальная служба
    /// писем, в совместном — запись через серверную службу.
    /// </param>
    public PrintHistoryRecorder(IPrintHistoryWriter mails)
    {
        ArgumentNullException.ThrowIfNull(mails);

        _письма = mails;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        IReadOnlyList<long> mailIds,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        if (string.IsNullOrWhiteSpace(templateDescription))
        {
            throw new ArgumentException(
                "Описание шаблона печати не задано.",
                nameof(templateDescription));
        }

        foreach (var mailId in mailIds)
        {
            var результат = await _письма
                .RegisterPrintAsync(
                    mailId,
                    printJobId,
                    templateDescription,
                    cancellationToken)
                .ConfigureAwait(false);

            if (результат.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Не удалось записать историю печати отправления {mailId}: {результат.Error}");
            }
        }
    }
}
