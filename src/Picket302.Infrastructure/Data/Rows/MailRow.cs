using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;

namespace Picket302.Infrastructure.Data.Rows;

/// <summary>
/// Плоская строка таблицы <c>mails</c> (ТЗ §3.1.3).
/// </summary>
internal sealed class MailRow
{
    public long Id { get; set; }

    public long CounterpartyId { get; set; }

    public long SenderId { get; set; }

    public long? DistributionId { get; set; }

    public string? EnvelopeType { get; set; }

    public string? NoticeType { get; set; }

    public int CopyCount { get; set; }

    public bool IsVip { get; set; }

    public string? Note { get; set; }

    public decimal DeclaredValue { get; set; }

    public decimal Weight { get; set; }

    public decimal Fee { get; set; }

    public int Status { get; set; }

    public bool NeedNotification { get; set; }

    public bool NeedReturnAddress { get; set; }

    public int MailCategory { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime? DatePrinted { get; set; }

    public DateTime? DateDispatched { get; set; }

    public DateTime? DateConfirmation { get; set; }

    public DateTime? DateCanceled { get; set; }

    public string? IncomingNumber { get; set; }

    public string? OutgoingNumber { get; set; }

    public long? PostRegisterId { get; set; }

    public string? RussianPostId { get; set; }

    public string? RussianPostBatch { get; set; }

    public long? InventoryId { get; set; }

    /// <summary>Папка письма (ТЗ §8.1, функция 22).</summary>
    public long? FolderId { get; set; }

    public string ToZip { get; set; } = string.Empty;

    public string? ToStr1 { get; set; }

    public string? ToStr2 { get; set; }

    public string? ToStr3 { get; set; }

    public string? ToStr4 { get; set; }

    public string? ToStr5 { get; set; }

    public string? ToStr6 { get; set; }

    /// <summary>Собирает доменную сущность из строки таблицы.</summary>
    /// <returns>Отправление со структурированным адресом получателя.</returns>
    public Mail ToEntity()
    {
        var отправление = new Mail
        {
            Id = Id,
            CounterpartyId = CounterpartyId,
            SenderId = SenderId,
            DistributionId = DistributionId,
            EnvelopeType = EnvelopeType,
            NoticeType = NoticeType,
            CopyCount = CopyCount,
            IsVip = IsVip,
            Note = Note,
            DeclaredValue = DeclaredValue,
            Weight = Weight,
            Fee = Fee,
            Status = (MailStatus)Status,
            NeedNotification = NeedNotification,
            NeedReturnAddress = NeedReturnAddress,
            MailCategory = (MailCategory)MailCategory,
            DateCreated = DateCreated,
            DatePrinted = DatePrinted,
            DateDispatched = DateDispatched,
            DateConfirmation = DateConfirmation,
            DateCanceled = DateCanceled,
            IncomingNumber = IncomingNumber,
            OutgoingNumber = OutgoingNumber,
            PostRegisterId = PostRegisterId,
            RussianPostId = RussianPostId,
            RussianPostBatch = RussianPostBatch,
            InventoryId = InventoryId,
            FolderId = FolderId,
        };

        var адрес = Address.Create(
            ToZip,
            [
                ToStr1 ?? string.Empty,
                ToStr2 ?? string.Empty,
                ToStr3 ?? string.Empty,
                ToStr4 ?? string.Empty,
                ToStr5 ?? string.Empty,
                ToStr6 ?? string.Empty,
            ]);

        if (адрес.IsSuccess)
        {
            отправление.RecipientAddress = адрес.Value;
        }

        return отправление;
    }
}
