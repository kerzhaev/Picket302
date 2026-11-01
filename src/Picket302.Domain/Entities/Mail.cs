using Picket302.Domain.ValueObjects;

namespace Picket302.Domain.Entities;

/// <summary>
/// Почтовое отправление или письмо рассылки (ТЗ §3.1.3, таблица <c>mails</c>).
/// </summary>
/// <remarks>
/// Жизненный цикл отправления — последовательная смена статусов с проставлением
/// временных меток (ТЗ §8.1, функция 24 «Жизненный цикл отправления (4 даты)»):
/// <c>date_created</c> → <c>date_printed</c> → <c>date_dispatched</c> →
/// <c>date_confirmation</c> / <c>date_canceled</c>. Переход в «Подтверждено»
/// без промежуточного «Отправлено» недопустим.
/// Адрес получателя хранится структурированно: индекс и шесть строк
/// (ТЗ §3.1.19: адрес никогда не хранится одной строкой).
/// </remarks>
public sealed class Mail
{
    /// <summary>Минимальное число экземпляров отправления (ТЗ §3.1.3).</summary>
    public const int MinimumCopyCount = 1;

    /// <summary>Идентификатор отправления (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Контрагент-получатель.</summary>
    public long CounterpartyId { get; set; }

    /// <summary>Контрагент-отправитель.</summary>
    public long SenderId { get; set; }

    /// <summary>Рассылка, в которую входит отправление.</summary>
    public long? DistributionId { get; set; }

    /// <summary>Формат конверта: C6, DL, C5, C4 и др.</summary>
    public string? EnvelopeType { get; set; }

    /// <summary>Вид уведомления: ф.119, 112эп.</summary>
    public string? NoticeType
    {
        get => _noticeType;
        set
        {
            _noticeType = value;
            NotificationType = NotificationTypeMapper.Разобрать(value);
        }
    }

    private string? _noticeType;

    /// <summary>
    /// Вид уведомления о вручении (ТЗ §8.1, функция 32: «Выбор вида уведомления
    /// о вручении»).
    /// </summary>
    public NotificationType NotificationType { get; private set; } = NotificationType.None;

    /// <summary>Количество экземпляров; не меньше единицы.</summary>
    public int CopyCount { get; set; } = MinimumCopyCount;

    /// <summary>Признак приоритетного адресата.</summary>
    public bool IsVip { get; set; }

    /// <summary>Технологическое примечание.</summary>
    public string? Note { get; set; }

    /// <summary>Сумма объявленной ценности.</summary>
    public decimal DeclaredValue { get; set; }

    /// <summary>Вес отправления, кг.</summary>
    public decimal Weight { get; set; }

    /// <summary>Плата за пересылку.</summary>
    public decimal Fee { get; set; }

    /// <summary>Статус отправления.</summary>
    public MailStatus Status { get; set; } = MailStatus.Draft;

    /// <summary>Требуется печать уведомления о вручении.</summary>
    public bool NeedNotification { get; set; }

    /// <summary>Требуется печать обратного адреса.</summary>
    public bool NeedReturnAddress { get; set; } = true;

    /// <summary>Категория отправления.</summary>
    public MailCategory MailCategory { get; set; } = MailCategory.Simple;

    /// <summary>Момент создания отправления (UTC).</summary>
    public DateTime DateCreated { get; set; }

    /// <summary>Момент печати (UTC).</summary>
    public DateTime? DatePrinted { get; set; }

    /// <summary>Момент отправки (UTC).</summary>
    public DateTime? DateDispatched { get; set; }

    /// <summary>Момент подтверждения вручения (UTC).</summary>
    public DateTime? DateConfirmation { get; set; }

    /// <summary>Момент отмены (UTC).</summary>
    public DateTime? DateCanceled { get; set; }

    /// <summary>Входящий регистрационный номер.</summary>
    public string? IncomingNumber { get; set; }

    /// <summary>Исходящий регистрационный номер.</summary>
    public string? OutgoingNumber { get; set; }

    /// <summary>Почтовый реестр, в который включено отправление.</summary>
    public long? PostRegisterId { get; set; }

    /// <summary>ШПИ либо почтовый идентификатор (14 знаков).</summary>
    public string? RussianPostId { get; set; }

    /// <summary>Номер партии Почты России.</summary>
    public string? RussianPostBatch { get; set; }

    /// <summary>Опись вложений, к которой относится отправление.</summary>
    public long? InventoryId { get; set; }

    /// <summary>Адрес получателя: почтовый индекс и шесть адресных строк.</summary>
    public Address? RecipientAddress { get; set; }

    /// <summary>
    /// Папка письма в пользовательской классификации исходящей корреспонденции
    /// (ТЗ §8.1, функция 22: «Таблица folders, колонка mails.folder_id»).
    /// </summary>
    public long? FolderId { get; set; }

    /// <summary>
    /// Назначает категорию почтового отправления (ТЗ §8.1, функция 23:
    /// «Классификация категории почтового отправления»).
    /// </summary>
    /// <param name="category">
    /// Категория отправления: Простое, Заказное, С объявленной ценностью,
    /// 1-й класс. Категория определяет набор доступных бланков и тарификацию.
    /// </param>
    public void SetCategory(MailCategory category)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Неизвестная категория отправления.");
        }

        MailCategory = category;
    }

    /// <summary>
    /// Выбирает вид уведомления о вручении (ТЗ §8.1, функция 32: «Выбор вида
    /// уведомления о вручении»).
    /// </summary>
    /// <param name="type">
    /// Вид уведомления: ф.119 (РФ), национальная форма либо совмещённый перевод
    /// ф.112эп. Значение <see cref="NotificationType.None"/> означает, что
    /// уведомление не требуется.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрано неизвестное значение вида уведомления.
    /// </exception>
    public void SelectNotificationForm(NotificationType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный вид уведомления.");
        }

        NotificationType = type;

        // Флаг «нужно уведомление» ставится автоматически: уведомление требуется
        // для всех выбранных форм, кроме явного «не требуется».
        NeedNotification = type != NotificationType.None;

        NoticeType = type.Ключ();
    }

    /// <summary>
    /// Перемещает отправление в указанную папку (ТЗ §8.1, функция 22).
    /// </summary>
    /// <param name="folderId">Идентификатор папки.</param>
    public void MoveToFolder(long folderId) => FolderId = folderId;

    /// <summary>Проверяет, допустим ли переход в указанный статус.</summary>
    /// <param name="newStatus">Целевой статус.</param>
    /// <returns><see langword="true"/>, если переход допустим.</returns>
    public bool CanTransitionTo(MailStatus newStatus) => newStatus switch
    {
        MailStatus.Draft => Status == MailStatus.Draft,
        MailStatus.Printed => Status == MailStatus.Draft,
        MailStatus.Dispatched => Status == MailStatus.Printed,
        MailStatus.Confirmed => Status == MailStatus.Dispatched,
        MailStatus.Canceled => Status is MailStatus.Draft or MailStatus.Printed or MailStatus.Dispatched,
        _ => false,
    };

    /// <summary>
    /// Переводит отправление в новый статус и проставляет соответствующую
    /// временную метку (ТЗ §8.1, функция 24).
    /// </summary>
    /// <param name="newStatus">Целевой статус.</param>
    /// <exception cref="InvalidOperationException">
    /// Переход в указанный статус недопустим из текущего статуса.
    /// </exception>
    public void TransitionStatus(MailStatus newStatus) => TransitionStatus(newStatus, DateTime.UtcNow);

    /// <summary>
    /// Переводит отправление в новый статус с заданным моментом времени.
    /// </summary>
    /// <param name="newStatus">Целевой статус.</param>
    /// <param name="moment">Момент перехода (UTC).</param>
    /// <exception cref="InvalidOperationException">
    /// Переход в указанный статус недопустим из текущего статуса.
    /// </exception>
    public void TransitionStatus(MailStatus newStatus, DateTime moment)
    {
        if (!CanTransitionTo(newStatus))
        {
            throw new InvalidOperationException(
                $"Недопустимый переход статуса отправления: «{Status}» → «{newStatus}».");
        }

        var метка = DateTime.SpecifyKind(moment, DateTimeKind.Utc);

        switch (newStatus)
        {
            case MailStatus.Printed:
                DatePrinted = метка;
                break;
            case MailStatus.Dispatched:
                DateDispatched = метка;
                break;
            case MailStatus.Confirmed:
                DateConfirmation = метка;
                break;
            case MailStatus.Canceled:
                DateCanceled = метка;
                break;
            case MailStatus.Draft:
            default:
                break;
        }

        Status = newStatus;
    }
}
