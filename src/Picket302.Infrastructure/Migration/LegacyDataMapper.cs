using System.Globalization;
using System.Security.Cryptography;

using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using ПочтовоеОтправление = Picket302.Domain.Entities.Mail;
using Picket302.Domain.Postal;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;
using Picket302.Domain.ValueObjects;

using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Repositories;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Запись переноса, происходящая из строки исходной таблицы (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// Исходная таблица и ключ строки нужны отчёту: счётчики ведутся по таблицам
/// предшественника, а одна исходная строка может дать несколько целевых записей
/// (контрагент и отправление), но учитывается в счётчике один раз.
/// </remarks>
public interface ILegacySourceRecord
{
    /// <summary>Имя таблицы исходной базы, из которой получена запись.</summary>
    string SourceTable { get; }

    /// <summary>Ключ записи в исходной таблице.</summary>
    string SourceKey { get; }
}

/// <summary>
/// Контрагент исходной базы, подготовленный к переносу (ТЗ §8.9, функции 117 и 118).
/// </summary>
public sealed class LegacyCounterpartyRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ записи для связей внутри плана переноса.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Наименование контрагента.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Краткое наименование.</summary>
    public string? ShortName { get; init; }

    /// <summary>ИНН (УНП).</summary>
    public string? Inn { get; init; }

    /// <summary>КПП.</summary>
    public string? Kpp { get; init; }

    /// <summary>ОГРН.</summary>
    public string? Ogrn { get; init; }

    /// <summary>ОКФС.</summary>
    public string? Okfs { get; init; }

    /// <summary>ОКПО.</summary>
    public string? Okpo { get; init; }

    /// <summary>ОКВЭД.</summary>
    public string? Okved { get; init; }

    /// <summary>БИК банка.</summary>
    public string? Bik { get; init; }

    /// <summary>Корреспондентский счёт.</summary>
    public string? Ks { get; init; }

    /// <summary>Расчётный счёт.</summary>
    public string? Rs { get; init; }

    /// <summary>РКЦ.</summary>
    public string? Rkc { get; init; }

    /// <summary>Наименование банка.</summary>
    public string? BankName { get; init; }

    /// <summary>Серия паспорта.</summary>
    public string? PassportSeries { get; init; }

    /// <summary>Номер паспорта.</summary>
    public string? PassportNumber { get; init; }

    /// <summary>Кем выдан паспорт.</summary>
    public string? PassportIssuedBy { get; init; }

    /// <summary>Дата выдачи паспорта.</summary>
    public DateTime? PassportIssuedDate { get; init; }

    /// <summary>Дата рождения.</summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>Место рождения.</summary>
    public string? BirthPlace { get; init; }

    /// <summary>Должность.</summary>
    public string? Position { get; init; }

    /// <summary>ФИО руководителя.</summary>
    public string? DirectorFio { get; init; }

    /// <summary>Электронная почта.</summary>
    public string? Email { get; init; }

    /// <summary>Телефон.</summary>
    public string? Phone { get; init; }

    /// <summary>Особые примечания.</summary>
    public string? SpecialNotes { get; init; }

    /// <summary>Логотип отправителя (исходная MySQL-схема).</summary>
    public byte[]? Logo { get; init; }

    /// <summary>Тип контрагента.</summary>
    public CounterpartyType Type { get; init; } = CounterpartyType.LegalEntity;

    /// <summary>Основной срез адресации.</summary>
    public AddressMainType MainAddressType { get; init; } = AddressMainType.Postal;

    /// <summary>Юридический адрес.</summary>
    public Address? LegalAddress { get; init; }

    /// <summary>Почтовый адрес.</summary>
    public Address? PostalAddress { get; init; }

    /// <summary>Фактический адрес.</summary>
    public Address? ActualAddress { get; init; }

    /// <summary>Признак важного контрагента.</summary>
    public bool IsVip { get; init; }

    /// <summary>Признак отправителя (текущий отправитель).</summary>
    public bool IsSender { get; init; }

    /// <summary>Наименование группы исходной базы.</summary>
    public string? GroupName { get; init; }

    /// <summary>Пользовательские поля контрагента (неканонические столбцы).</summary>
    public IReadOnlyList<LegacyCounterpartyFieldRecord> CustomFields { get; init; } =
        Array.Empty<LegacyCounterpartyFieldRecord>();

    /// <summary>Признак того, что запись требует проверки.</summary>
    public bool NeedsVerification { get; init; }
}

/// <summary>
/// Пользовательское поле контрагента исходной базы (ТЗ §8.9, функция 118:
/// неканонические столбцы контрагента переносятся в <c>counterparty_fields</c>).
/// </summary>
public sealed class LegacyCounterpartyFieldRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ контрагента в плане переноса.</summary>
    public string CounterpartyKey { get; init; } = string.Empty;

    /// <summary>Имя поля (в макетах доступно по тегу вида <c>[Custom_ИмяПоля]</c>).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Значение поля.</summary>
    public string? Value { get; init; }

    /// <summary>Тип значения поля.</summary>
    public CounterpartyFieldType FieldType { get; init; } = CounterpartyFieldType.Text;
}

/// <summary>
/// Отправление исходной базы, подготовленное к переносу (ТЗ §8.9, функции 117 и 118).
/// </summary>
public sealed class LegacyMailRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ получателя в плане переноса.</summary>
    public string? CounterpartyKey { get; init; }

    /// <summary>Ключ отправителя в плане переноса.</summary>
    public string? SenderKey { get; init; }

    /// <summary>Ключ рассылки в плане переноса.</summary>
    public string? DistributionKey { get; init; }

    /// <summary>Ключ описи вложений в плане переноса.</summary>
    public string? InventoryKey { get; init; }

    /// <summary>Ключ почтового реестра в плане переноса.</summary>
    public string? PostRegisterKey { get; init; }

    /// <summary>Адрес получателя.</summary>
    public Address? RecipientAddress { get; init; }

    /// <summary>Наименование получателя (используется, если контрагент не найден).</summary>
    public string? RecipientName { get; init; }

    /// <summary>Наименование отправителя (используется, если контрагент не найден).</summary>
    public string? SenderName { get; init; }

    /// <summary>Количество экземпляров.</summary>
    public int CopyCount { get; init; } = 1;

    /// <summary>Признак важного отправления.</summary>
    public bool IsVip { get; init; }

    /// <summary>Примечание к отправлению.</summary>
    public string? Note { get; init; }

    /// <summary>Объявленная ценность.</summary>
    public decimal DeclaredValue { get; init; }

    /// <summary>Вес.</summary>
    public decimal Weight { get; init; }

    /// <summary>Пошлина.</summary>
    public decimal Fee { get; init; }

    /// <summary>Статус отправления.</summary>
    public MailStatus Status { get; init; } = MailStatus.Draft;

    /// <summary>Категория отправления.</summary>
    public MailCategory MailCategory { get; init; } = MailCategory.Simple;

    /// <summary>Признак необходимости уведомления.</summary>
    public bool NeedNotification { get; init; }

    /// <summary>Признак необходимости обратного адреса.</summary>
    public bool NeedReturnAddress { get; init; } = true;

    /// <summary>Дата создания отправления.</summary>
    public DateTime DateCreated { get; init; }

    /// <summary>Дата печати.</summary>
    public DateTime? DatePrinted { get; init; }

    /// <summary>Дата отправки.</summary>
    public DateTime? DateDispatched { get; init; }

    /// <summary>Дата подтверждения.</summary>
    public DateTime? DateConfirmation { get; init; }

    /// <summary>Дата отмены.</summary>
    public DateTime? DateCanceled { get; init; }

    /// <summary>Входящий номер.</summary>
    public string? IncomingNumber { get; init; }

    /// <summary>Исходящий номер.</summary>
    public string? OutgoingNumber { get; init; }

    /// <summary>Идентификатор отправления в Почте России.</summary>
    public string? RussianPostId { get; init; }

    /// <summary>Тип конверта.</summary>
    public string? EnvelopeType { get; init; }

    /// <summary>Тип уведомления.</summary>
    public string? NoticeType { get; init; }

    /// <summary>Признак того, что запись требует проверки.</summary>
    public bool NeedsVerification { get; init; }
}

/// <summary>
/// Запись журнала печати исходной базы (ТЗ §8.9, функция 117: <c>addrto_history</c>).
/// </summary>
public sealed class LegacyPrintHistoryRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Дата печати.</summary>
    public DateTime PrintedAt { get; init; }

    /// <summary>Снимок адреса получателя.</summary>
    public string RecipientAddressSnapshot { get; init; } = string.Empty;

    /// <summary>Снимок адреса отправителя.</summary>
    public string SenderAddressSnapshot { get; init; } = string.Empty;

    /// <summary>Количество экземпляров.</summary>
    public int CopyCount { get; init; } = 1;

    /// <summary>Признак важного отправления.</summary>
    public bool IsVip { get; init; }

    /// <summary>Примечание.</summary>
    public string? Note { get; init; }

    /// <summary>Описание применённого макета.</summary>
    public string TemplateDescription { get; init; } = string.Empty;

    /// <summary>Признак того, что запись требует проверки.</summary>
    public bool NeedsVerification { get; init; }
}

/// <summary>
/// Группа исходной базы (ТЗ §8.9, функция 117: <c>groups</c>).
/// </summary>
public sealed class LegacyGroupRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Наименование группы.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Примечание к группе.</summary>
    public string? Note { get; init; }

    /// <summary>Признак группы по умолчанию.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Признак закрытой группы.</summary>
    public bool IsClosed { get; init; }
}

/// <summary>
/// Макет печати исходной базы (ТЗ §8.9, функция 118: <c>Blank</c> → <c>print_templates</c>).
/// </summary>
public sealed class LegacyPrintTemplateRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Наименование макета.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Вид макета (<c>envelope</c>, <c>blank</c>, <c>report</c>, <c>label</c>, <c>notification</c>).</summary>
    public string Kind { get; init; } = PrintTemplateKind.Blank;

    /// <summary>Обозначение формата конверта.</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Ширина макета, мм.</summary>
    public decimal WidthMm { get; init; }

    /// <summary>Высота макета, мм.</summary>
    public decimal HeightMm { get; init; }

    /// <summary>Ориентация макета.</summary>
    public TemplateOrientation Orientation { get; init; } = TemplateOrientation.Portrait;

    /// <summary>Путь к файлу макета.</summary>
    public string FilePath { get; init; } = string.Empty;
}

/// <summary>
/// Параметр исходной базы (ТЗ §8.9, функции 117 и 118: <c>params</c>, <c>Settings</c>).
/// </summary>
public sealed class LegacyParameterRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Имя параметра.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Значение параметра.</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Запись кэша QR-кодов исходной базы (ТЗ §8.9, функция 117: <c>addrblob</c>).
/// </summary>
public sealed class LegacyQrCacheRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Хеш полезной нагрузки (SHA-256).</summary>
    public string PayloadHash { get; init; } = string.Empty;

    /// <summary>Изображение кода.</summary>
    public byte[] ImageBlob { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Опись вложений исходной базы (ТЗ §8.9, функция 117: <c>inventory</c>).
/// </summary>
public sealed class LegacyInventoryRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ описи в плане переноса.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Наименование описи.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Ключ получателя в плане переноса.</summary>
    public string? CounterpartyKey { get; init; }

    /// <summary>Направление отправки.</summary>
    public string? Destination { get; init; }

    /// <summary>Примечание.</summary>
    public string? Note { get; init; }

    /// <summary>Дата создания описи.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Позиция описи вложений исходной базы (ТЗ §8.9, функция 117: <c>inventory_detail</c>).
/// </summary>
public sealed class LegacyInventoryItemRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ позиции в плане переноса.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Ключ описи в плане переноса.</summary>
    public string InventoryKey { get; init; } = string.Empty;

    /// <summary>Наименование позиции.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Количество.</summary>
    public int Quantity { get; init; } = 1;

    /// <summary>Стоимость позиции.</summary>
    public decimal Cost { get; init; }

    /// <summary>Примечание.</summary>
    public string? Note { get; init; }

    /// <summary>Дата создания позиции.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Рассылка исходной базы (ТЗ §8.9, функция 118: <c>Distribution</c>).
/// </summary>
public sealed class LegacyDistributionRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ рассылки в плане переноса.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Наименование рассылки.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Ключ отправителя в плане переноса.</summary>
    public string? SenderKey { get; init; }

    /// <summary>Примечание.</summary>
    public string? Note { get; init; }

    /// <summary>Вид рассылки.</summary>
    public DistributionKind Kind { get; init; } = DistributionKind.OneToMany;

    /// <summary>Состояние рассылки.</summary>
    public DistributionState State { get; init; } = DistributionState.Draft;

    /// <summary>Дата создания рассылки.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Дата отправки рассылки.</summary>
    public DateTime? DispatchedAt { get; init; }
}

/// <summary>
/// Почтовый реестр исходной базы (ТЗ §8.9, функция 118: <c>PostRegister</c>).
/// </summary>
public sealed class LegacyPostRegisterRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ реестра в плане переноса.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Номер реестра.</summary>
    public string RegisterNumber { get; init; } = string.Empty;

    /// <summary>Номер партии.</summary>
    public string? BatchNumber { get; init; }

    /// <summary>Пункт отправления.</summary>
    public string? ShippingPoint { get; init; }

    /// <summary>Логин отслеживания.</summary>
    public string? TrackLogin { get; init; }

    /// <summary>Токен отслеживания.</summary>
    public string? TrackToken { get; init; }

    /// <summary>Дата отправки.</summary>
    public DateTime? SendingDate { get; init; }
}

/// <summary>
/// Документ отправления исходной базы (ТЗ §8.9, функция 118: <c>Document</c>).
/// </summary>
public sealed class LegacyMailDocumentRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ отправления в плане переноса.</summary>
    public string MailKey { get; init; } = string.Empty;

    /// <summary>Номер документа.</summary>
    public string DocumentNumber { get; init; } = string.Empty;
}

/// <summary>
/// Движение документа исходной базы (ТЗ §8.9, функция 118: <c>Movement</c>,
/// <c>DocumentMovement</c>).
/// </summary>
public sealed class LegacyDocumentMovementRecord : ILegacySourceRecord
{
    /// <inheritdoc />
    public string SourceTable { get; init; } = string.Empty;

    /// <inheritdoc />
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>Ключ отправления в плане переноса.</summary>
    public string MailKey { get; init; } = string.Empty;

    /// <summary>Дата движения.</summary>
    public DateTime MovedAt { get; init; }

    /// <summary>Подразделение-получатель.</summary>
    public string TargetDepartment { get; init; } = string.Empty;

    /// <summary>Исполнитель.</summary>
    public string? Executor { get; init; }

    /// <summary>Действие с документом.</summary>
    public DocumentMovementAction? Action { get; init; }
}

/// <summary>
/// План переноса: подготовленные записи, счётчики, предупреждения и соответствия полей
/// (ТЗ §8.9, функции 117 и 118).
/// </summary>
public sealed class LegacyMigrationPlan
{
    /// <summary>
    /// Сборщик отчёта, наполненный картографом: счётчики чтения, предупреждения
    /// и соответствия полей. Писатель переноса продолжает работу с ним же.
    /// </summary>
    internal MigrationReportBuilder? Report { get; init; }

    /// <summary>Контрагенты к переносу.</summary>
    public IReadOnlyList<LegacyCounterpartyRecord> Counterparties { get; init; } =
        Array.Empty<LegacyCounterpartyRecord>();

    /// <summary>Пользовательские поля контрагентов к переносу.</summary>
    public IReadOnlyList<LegacyCounterpartyFieldRecord> CounterpartyFields { get; init; } =
        Array.Empty<LegacyCounterpartyFieldRecord>();

    /// <summary>Отправления к переносу.</summary>
    public IReadOnlyList<LegacyMailRecord> Mails { get; init; } = Array.Empty<LegacyMailRecord>();

    /// <summary>Записи журнала печати к переносу.</summary>
    public IReadOnlyList<LegacyPrintHistoryRecord> PrintHistories { get; init; } =
        Array.Empty<LegacyPrintHistoryRecord>();

    /// <summary>Группы к переносу.</summary>
    public IReadOnlyList<LegacyGroupRecord> Groups { get; init; } = Array.Empty<LegacyGroupRecord>();

    /// <summary>Макеты печати к переносу.</summary>
    public IReadOnlyList<LegacyPrintTemplateRecord> PrintTemplates { get; init; } =
        Array.Empty<LegacyPrintTemplateRecord>();

    /// <summary>Параметры к переносу.</summary>
    public IReadOnlyList<LegacyParameterRecord> Parameters { get; init; } = Array.Empty<LegacyParameterRecord>();

    /// <summary>Записи кэша QR-кодов к переносу.</summary>
    public IReadOnlyList<LegacyQrCacheRecord> QrCaches { get; init; } = Array.Empty<LegacyQrCacheRecord>();

    /// <summary>Описи вложений к переносу.</summary>
    public IReadOnlyList<LegacyInventoryRecord> Inventories { get; init; } = Array.Empty<LegacyInventoryRecord>();

    /// <summary>Позиции описей к переносу.</summary>
    public IReadOnlyList<LegacyInventoryItemRecord> InventoryItems { get; init; } =
        Array.Empty<LegacyInventoryItemRecord>();

    /// <summary>Рассылки к переносу.</summary>
    public IReadOnlyList<LegacyDistributionRecord> Distributions { get; init; } =
        Array.Empty<LegacyDistributionRecord>();

    /// <summary>Почтовые реестры к переносу.</summary>
    public IReadOnlyList<LegacyPostRegisterRecord> PostRegisters { get; init; } =
        Array.Empty<LegacyPostRegisterRecord>();

    /// <summary>Документы отправлений к переносу.</summary>
    public IReadOnlyList<LegacyMailDocumentRecord> MailDocuments { get; init; } =
        Array.Empty<LegacyMailDocumentRecord>();

    /// <summary>Движения документов к переносу.</summary>
    public IReadOnlyList<LegacyDocumentMovementRecord> DocumentMovements { get; init; } =
        Array.Empty<LegacyDocumentMovementRecord>();
}

/// <summary>
/// Картограф переноса: сопоставляет исходные таблицы предшественника с целевой моделью
/// (ТЗ §8.9, функция 117 «Импорт базы из исходной MySQL-схемы», функция 118
/// «Импорт базы из исходной Access-схемы»).
/// </summary>
/// <remarks>
/// <para>
/// Картограф не обращается к базе данных: он работает с нейтральной моделью
/// таблиц и строк, поэтому перенос проверяется без реального сервера MySQL или
/// файла .mdb. Конкретные источники — тонкие адаптеры над драйверами.
/// </para>
/// <para>
/// Недопустимые значения (например, почтовый индекс не из шести цифр) не
/// приводят к отбрасыванию записи: запись помечается как требующая проверки,
/// а в отчёт добавляется предупреждение.
/// </para>
/// </remarks>
public sealed class LegacyDataMapper
{
    /// <summary>Наименование источника MySQL в отчёте миграции.</summary>
    public const string ИсточникMySql = "MySQL";

    /// <summary>Наименование источника Access в отчёте миграции.</summary>
    public const string ИсточникAccess = "Access";

    /// <summary>
    /// Ключ параметра с идентификатором текущего отправителя
    /// (ТЗ §8.1, функция 16; <c>ParameterCurrentSenderStore.ParameterKey</c>).
    /// </summary>
    public const string КлючТекущегоОтправителя = "CurrentSenderId";

    /// <summary>
    /// Читает исходные данные источника и строит план переноса MySQL-схемы.
    /// </summary>
    /// <param name="source">Источник данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>План переноса.</returns>
    public async Task<LegacyMigrationPlan> MapMySqlAsync(
        ILegacyTableSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var данные = await LegacyDataSet.ЗагрузитьAsync(source, cancellationToken).ConfigureAwait(false);

        return MapMySql(данные);
    }

    /// <summary>
    /// Строит план переноса по данным исходной MySQL-схемы (8 таблиц).
    /// </summary>
    /// <param name="data">Прочитанные исходные данные.</param>
    /// <returns>План переноса.</returns>
    public LegacyMigrationPlan MapMySql(LegacyDataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var отчёт = new MigrationReportBuilder(ИсточникMySql);
        ДобавитьСоответствияMySql(отчёт);

        var группы = new List<LegacyGroupRecord>();
        var контрагенты = new List<LegacyCounterpartyRecord>();
        var письма = new List<LegacyMailRecord>();
        var истории = new List<LegacyPrintHistoryRecord>();
        var коды = new List<LegacyQrCacheRecord>();
        var описи = new List<LegacyInventoryRecord>();
        var позиции = new List<LegacyInventoryItemRecord>();
        var параметры = new List<LegacyParameterRecord>();

        // Группы читаются первыми: получатели ссылаются на группу по её идентификатору.
        var именаГрупп = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlGroups, "groups", отчёт))
        {
            var имя = строка.GetString("name");

            if (имя is null)
            {
                отчёт.Пропущено(LegacyTableNames.MySqlGroups, "groups");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.MySqlGroups}»: строка {ОписаниеСтроки(строка)} не имеет наименования — пропущена.");
                continue;
            }

            var идентификатор = строка.GetString("id");

            if (идентификатор is not null)
            {
                именаГрупп[идентификатор] = имя;
            }

            группы.Add(new LegacyGroupRecord
            {
                SourceTable = LegacyTableNames.MySqlGroups,
                SourceKey = идентификатор ?? имя,
                Name = имя,
                Note = строка.GetString("prim"),
                IsDefault = строка.GetBool("fdefault"),
                IsClosed = строка.GetBool("closed"),
            });
        }

        // Отправители: адрес отправителя и логотип.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlSenders, "counterparties", отчёт))
        {
            var адрес = СобратьАдрес(
                строка,
                "ind_from",
                ["str_from1", "str_from2", "str_from3", "str_from4", "str_from5", "str_from6"],
                LegacyTableNames.MySqlSenders,
                отчёт,
                out var требуетПроверки);

            var имя = строка.GetString("str_from1") ?? "Отправитель";

            контрагенты.Add(new LegacyCounterpartyRecord
            {
                SourceTable = LegacyTableNames.MySqlSenders,
                SourceKey = строка.GetString("id") ?? имя,
                Key = Ключ(LegacyTableNames.MySqlSenders, строка.GetString("id")),
                Name = имя,
                Logo = строка.GetBytes("logo"),
                IsSender = true,
                MainAddressType = AddressMainType.Postal,
                PostalAddress = адрес,
                NeedsVerification = требуетПроверки,
                CustomFields = СобратьПоляОтправителя(строка),
            });

            if (требуетПроверки)
            {
                отчёт.ТребуетПроверки(LegacyTableNames.MySqlSenders, "counterparties");
            }
        }

        // Получатели: запись адресной книги и отправление по тем же данным.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlRecipients, "counterparties, mails", отчёт))
        {
            var адрес = СобратьАдрес(
                строка,
                "ind_to",
                ["str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6"],
                LegacyTableNames.MySqlRecipients,
                отчёт,
                out var требуетПроверки);

            var имя = строка.GetString("str_to1") ?? "Получатель";
            var ключ = Ключ(LegacyTableNames.MySqlRecipients, строка.GetString("id"));
            var датаОтправки = строка.GetDateTime("date_send");
            var напечатано = строка.GetBool("print_prim");

            var группа = строка.GetString("group_id");

            контрагенты.Add(new LegacyCounterpartyRecord
            {
                SourceTable = LegacyTableNames.MySqlRecipients,
                SourceKey = строка.GetString("id") ?? имя,
                Key = ключ,
                Name = имя,
                PostalAddress = адрес,
                MainAddressType = AddressMainType.Postal,
                IsVip = строка.GetBool("vip"),
                GroupName = группа is not null && именаГрупп.TryGetValue(группа, out var имяГрупппы)
                    ? имяГрупппы
                    : null,
                NeedsVerification = требуетПроверки,
            });

            письма.Add(new LegacyMailRecord
            {
                SourceTable = LegacyTableNames.MySqlRecipients,
                SourceKey = строка.GetString("id") ?? имя,
                CounterpartyKey = ключ,
                RecipientAddress = адрес,
                RecipientName = имя,
                CopyCount = Math.Max(1, строка.GetInt("copyes") ?? 1),
                IsVip = строка.GetBool("vip"),
                Note = строка.GetString("prim"),
                DateCreated = датаОтправки ?? default,
                Status = напечатано ? MailStatus.Printed : MailStatus.Draft,
                DatePrinted = напечатано ? датаОтправки : null,
                NeedsVerification = требуетПроверки,
            });

            if (требуетПроверки)
            {
                отчёт.ТребуетПроверки(LegacyTableNames.MySqlRecipients, "counterparties, mails");
            }
        }

        // История печати.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlPrintHistory, "print_history", отчёт))
        {
            var адрес = СобратьАдрес(
                строка,
                "ind_to",
                ["str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6"],
                LegacyTableNames.MySqlPrintHistory,
                отчёт,
                out var требуетПроверки);

            истории.Add(new LegacyPrintHistoryRecord
            {
                SourceTable = LegacyTableNames.MySqlPrintHistory,
                SourceKey = строка.GetString("id") ?? string.Empty,
                PrintedAt = строка.GetDateTime("date_print") ?? default,
                RecipientAddressSnapshot = адрес?.ToSnapshot() ?? string.Empty,
                SenderAddressSnapshot = строка.GetString("addrfrom") ?? string.Empty,
                CopyCount = Math.Max(1, строка.GetInt("copyes") ?? 1),
                IsVip = строка.GetBool("vip"),
                Note = строка.GetString("prim"),
                TemplateDescription = строка.GetString("teplate_descr") ?? string.Empty,
                NeedsVerification = требуетПроверки,
            });

            if (требуетПроверки)
            {
                отчёт.ТребуетПроверки(LegacyTableNames.MySqlPrintHistory, "print_history");
            }
        }

        // Кэш QR-кодов.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlQrCache, "qr_cache", отчёт))
        {
            var изображение = строка.GetBytes("img_qrcode");

            if (изображение.Length == 0)
            {
                отчёт.Пропущено(LegacyTableNames.MySqlQrCache, "qr_cache");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.MySqlQrCache}»: строка {ОписаниеСтроки(строка)} не содержит изображения — пропущена.");
                continue;
            }

            коды.Add(new LegacyQrCacheRecord
            {
                SourceTable = LegacyTableNames.MySqlQrCache,
                SourceKey = строка.GetString("id") ?? string.Empty,
                PayloadHash = Convert.ToHexString(SHA256.HashData(изображение)).ToLowerInvariant(),
                ImageBlob = изображение,
            });
        }

        // Описи вложений.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlInventory, "inventories", отчёт))
        {
            var идентификатор = строка.GetString("id");
            var получатель = строка.GetInt("id_main_to");

            описи.Add(new LegacyInventoryRecord
            {
                SourceTable = LegacyTableNames.MySqlInventory,
                SourceKey = идентификатор ?? string.Empty,
                Key = Ключ(LegacyTableNames.MySqlInventory, идентификатор),
                Name = строка.GetString("name") ?? $"Опись {идентификатор ?? "без номера"}",
                CounterpartyKey = получатель is > 0
                    ? Ключ(LegacyTableNames.MySqlRecipients, получатель.Value.ToString(CultureInfo.InvariantCulture))
                    : null,
                Destination = строка.GetString("kuda"),
                Note = строка.GetString("prim"),
                CreatedAt = строка.GetDateTime("date_create") ?? default,
            });
        }

        // Позиции описей вложений.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlInventoryDetail, "inventory_items", отчёт))
        {
            var идентификатор = строка.GetString("id");
            var опись = строка.GetInt("id_main");

            позиции.Add(new LegacyInventoryItemRecord
            {
                SourceTable = LegacyTableNames.MySqlInventoryDetail,
                SourceKey = идентификатор ?? string.Empty,
                Key = Ключ(LegacyTableNames.MySqlInventoryDetail, идентификатор),
                InventoryKey = Ключ(
                    LegacyTableNames.MySqlInventory,
                    опись?.ToString(CultureInfo.InvariantCulture)),
                Name = строка.GetString("name") ?? "Позиция",
                Quantity = Math.Max(1, строка.GetInt("kol") ?? 1),
                Cost = строка.GetDecimal("cost") ?? 0,
                Note = строка.GetString("prim"),
                CreatedAt = строка.GetDateTime("date_create") ?? default,
            });
        }

        // Параметры.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.MySqlParams, "parameters", отчёт))
        {
            var имя = строка.GetString("name");

            if (имя is null)
            {
                отчёт.Пропущено(LegacyTableNames.MySqlParams, "parameters");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.MySqlParams}»: строка {ОписаниеСтроки(строка)} не имеет имени параметра — пропущена.");
                continue;
            }

            параметры.Add(new LegacyParameterRecord
            {
                SourceTable = LegacyTableNames.MySqlParams,
                SourceKey = имя,
                Name = имя,
                Value = строка.GetString("value"),
            });
        }

        return new LegacyMigrationPlan
        {
            Report = отчёт,
            Counterparties = контрагенты,
            Mails = письма,
            PrintHistories = истории,
            Groups = группы,
            Parameters = параметры,
            QrCaches = коды,
            Inventories = описи,
            InventoryItems = позиции,
        };
    }

    /// <summary>
    /// Читает исходные данные источника и строит план переноса Access-схемы.
    /// </summary>
    /// <param name="source">Источник данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>План переноса.</returns>
    public async Task<LegacyMigrationPlan> MapAccessAsync(
        ILegacyTableSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var данные = await LegacyDataSet.ЗагрузитьAsync(source, cancellationToken).ConfigureAwait(false);

        return MapAccess(данные);
    }

    /// <summary>
    /// Строит план переноса по данным исходной Access-схемы (14 таблиц).
    /// </summary>
    /// <param name="data">Прочитанные исходные данные.</param>
    /// <returns>План переноса.</returns>
    public LegacyMigrationPlan MapAccess(LegacyDataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var отчёт = new MigrationReportBuilder(ИсточникAccess);
        ДобавитьСоответствияAccess(отчёт);

        var контрагенты = new List<LegacyCounterpartyRecord>();
        var поля = new List<LegacyCounterpartyFieldRecord>();
        var письма = new List<LegacyMailRecord>();
        var макеты = new List<LegacyPrintTemplateRecord>();
        var параметры = new List<LegacyParameterRecord>();
        var рассылки = new List<LegacyDistributionRecord>();
        var реестры = new List<LegacyPostRegisterRecord>();
        var документы = new List<LegacyMailDocumentRecord>();
        var движения = new List<LegacyDocumentMovementRecord>();

        // Адреса исходной схемы — справочная таблица: её значения переносятся
        // в адресные срезы контрагентов и отправлений.
        var адреса = new Dictionary<string, АдресAccess>(StringComparer.OrdinalIgnoreCase);

        var строкиАдресов = ПолучитьСтроки(
            data,
            LegacyTableNames.AccessAddresses,
            "counterparties, mails",
            отчёт,
            "справочник адресов: значения переносятся в адресные срезы контрагентов и отправлений");

        foreach (var строка in строкиАдресов)
        {
            var идентификатор = строка.GetString("AddressId");

            if (идентификатор is null)
            {
                continue;
            }

            адреса[идентификатор] = new АдресAccess(
                строка.GetString("ZipCode"),
                строка.GetString("AddressLine"),
                строка.GetString("City"),
                строка.GetString("Province"));
        }

        отчёт.Перенесено(LegacyTableNames.AccessAddresses, "counterparties, mails", строкиАдресов.Count);

        // Контрагенты (53 столбца): канонические — в counterparties,
        // остальные — в counterparty_fields с тегом [Custom_*].
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessCounterparties, "counterparties", отчёт))
        {
            var идентификатор = строка.GetString("EconomicMemberId");
            var имя = строка.GetString("LegalName")
                ?? строка.GetString("Organization")
                ?? строка.GetString("FullName")
                ?? строка.GetString("NameTo");

            if (имя is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessCounterparties, "counterparties");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessCounterparties}»: строка {ОписаниеСтроки(строка)} не имеет наименования — пропущена.");
                continue;
            }

            var ключ = Ключ(LegacyTableNames.AccessCounterparties, идентификатор);
            var требуетПроверки = false;

            var юридический = СобратьАдресAccess(строка.GetString("AddressLegal"), адреса, отчёт, out var ошибкаЮр);
            var почтовый = СобратьАдресAccess(строка.GetString("AddressPost"), адреса, отчёт, out var ошибкаПочт);
            var фактический = СобратьАдресAccess(строка.GetString("AddressFact"), адреса, отчёт, out var ошибкаФакт);

            требуетПроверки |= ошибкаЮр | ошибкаПочт | ошибкаФакт;

            контрагенты.Add(new LegacyCounterpartyRecord
            {
                SourceTable = LegacyTableNames.AccessCounterparties,
                SourceKey = идентификатор ?? имя,
                Key = ключ,
                Name = имя,
                ShortName = строка.GetString("ShortName"),
                Inn = строка.GetString("INN"),
                Kpp = строка.GetString("KPP"),
                Ogrn = строка.GetString("OGRN"),
                Okfs = строка.GetString("OKFS"),
                Okpo = строка.GetString("OKPO"),
                Okved = строка.GetString("OKVED"),
                Bik = строка.GetString("BIK"),
                Ks = строка.GetString("KS"),
                Rs = строка.GetString("RS"),
                Rkc = строка.GetString("RKC"),
                BankName = строка.GetString("NameBank"),
                PassportSeries = строка.GetString("PassportSeries"),
                PassportNumber = строка.GetString("PassportNumber"),
                PassportIssuedBy = строка.GetString("PassportGiven"),
                PassportIssuedDate = строка.GetDateTime("PassportDate"),
                BirthDate = строка.GetDateTime("BirthDate"),
                BirthPlace = строка.GetString("BirthPlace"),
                Position = строка.GetString("NPPosition"),
                DirectorFio = СобратьФИО(строка),
                Email = строка.GetString("EconomicMemberEmail"),
                Phone = строка.GetString("Phone"),
                SpecialNotes = строка.GetString("EconomicMemberNote"),
                Type = строка.GetInt("em_type") == 1 ? CounterpartyType.NaturalPerson : CounterpartyType.LegalEntity,
                MainAddressType = ОсновнойСрез(строка.GetInt("MainAddressType")),
                LegalAddress = юридический,
                PostalAddress = почтовый,
                ActualAddress = фактический,
                IsSender = строка.GetBool("Sender"),
                NeedsVerification = требуетПроверки,
                CustomFields = СобратьПоляКонтрагента(строка, ключ, идентификатор ?? имя),
            });

            if (требуетПроверки)
            {
                отчёт.ТребуетПроверки(LegacyTableNames.AccessCounterparties, "counterparties");
            }
        }

        // Пользовательские поля предыдущей системы (таблица CustomFields).
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessCustomFields, "counterparty_fields", отчёт))
        {
            var имяПоля = строка.GetString("Name");
            var владелец = строка.GetString("EconomicMemberId");

            if (имяПоля is null || владелец is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessCustomFields, "counterparty_fields");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessCustomFields}»: строка {ОписаниеСтроки(строка)} не имеет имени поля или контрагента — пропущена.");
                continue;
            }

            поля.Add(new LegacyCounterpartyFieldRecord
            {
                SourceTable = LegacyTableNames.AccessCustomFields,
                SourceKey = $"{владелец}:{имяПоля}",
                CounterpartyKey = Ключ(LegacyTableNames.AccessCounterparties, владелец),
                Name = имяПоля,
                Value = строка.GetString("Meaning"),
            });
        }

        // Письма (39 столбцов).
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessLetters, "mails", отчёт))
        {
            var идентификатор = строка.GetString("LetterId");

            письма.Add(СобратьОтправлениеAccess(
                LegacyTableNames.AccessLetters,
                идентификатор,
                recipientKey: КлючКонтрагентаAccess(строка.GetString("ToEconomicMember")),
                senderKey: КлючКонтрагентаAccess(строка.GetString("FrmEconomicMember")),
                recipientAddress: СобратьАдресAccess(строка.GetString("AddressTo"), адреса, отчёт, out _),
                recipientName: строка.GetString("NameTo"),
                senderName: строка.GetString("NameFrm"),
                dateCreated: строка.GetDateTime("DateCreated"),
                datePrinted: строка.GetDateTime("DatePrinted"),
                dateDispatched: строка.GetDateTime("DateDispatched"),
                dateConfirmation: строка.GetDateTime("DateConfirmation"),
                dateCanceled: строка.GetDateTime("DateCanceled"),
                declaredValue: строка.GetDecimal("AmountOfCOD"),
                weight: строка.GetDecimal("Weight"),
                fee: строка.GetDecimal("FeeForShipping"),
                copyCount: 1,
                isVip: false,
                note: строка.GetString("SpecialNote"),
                needNotification: строка.GetBool("NeedNotification"),
                incomingNumber: null,
                outgoingNumber: строка.GetString("LetterNumber"),
                russianPostId: строка.GetString("PostalNumber"),
                envelopeType: строка.GetString("TypeEnvelope"),
                noticeType: null,
                category: Категория(строка.GetInt("Configuration")),
                distributionKey: КлючРассылкиAccess(строка.GetString("DistributionId")),
                postRegisterKey: КлючРеестраAccess(строка.GetString("PostRegisterId"), строка.GetInt("PostRegisterNumber"))));
        }

        // Входящие документы.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessIncoming, "mails", отчёт))
        {
            var идентификатор = строка.GetString("IncomingId");

            письма.Add(СобратьОтправлениеAccess(
                LegacyTableNames.AccessIncoming,
                идентификатор,
                recipientKey: КлючКонтрагентаAccess(строка.GetString("ToEconomicMember")),
                senderKey: КлючКонтрагентаAccess(строка.GetString("FrmEconomicMember")),
                recipientAddress: null,
                recipientName: строка.GetString("NameTo"),
                senderName: строка.GetString("NameFrom"),
                dateCreated: строка.GetDateTime("IncomingDate"),
                datePrinted: null,
                dateDispatched: null,
                dateConfirmation: null,
                dateCanceled: null,
                declaredValue: null,
                weight: null,
                fee: null,
                copyCount: 1,
                isVip: false,
                note: строка.GetString("IncomingSubject"),
                needNotification: false,
                incomingNumber: строка.GetString("IncomingNumber"),
                outgoingNumber: строка.GetString("OriginalNumber"),
                russianPostId: null,
                envelopeType: null,
                noticeType: null,
                category: MailCategory.Simple,
                distributionKey: null,
                postRegisterKey: null));
        }

        // Бланки (6 столбцов) — макеты печати.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessBlanks, "print_templates", отчёт))
        {
            var идентификатор = строка.GetString("Id");
            var имя = строка.GetString("Name") ?? "Бланк";
            var формат = строка.GetString("EnvSize") ?? string.Empty;
            var (ширина, высота) = ГеометрияФормата(формат);

            макеты.Add(new LegacyPrintTemplateRecord
            {
                SourceTable = LegacyTableNames.AccessBlanks,
                SourceKey = идентификатор ?? имя,
                Name = имя,
                Kind = ВидМакета(строка.GetInt("Type")),
                Format = формат,
                WidthMm = ширина,
                HeightMm = высота,
                Orientation = TemplateOrientation.Portrait,
                FilePath = строка.GetString("Source") ?? string.Empty,
            });
        }

        // Настройки (39 столбцов) — только переносимые ключи.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessSettings, "parameters", отчёт))
        {
            foreach (var (ключ, имяПараметра) in ПереносимыеНастройки)
            {
                var значение = строка.GetString(ключ);

                if (значение is null)
                {
                    continue;
                }

                параметры.Add(new LegacyParameterRecord
                {
                    SourceTable = LegacyTableNames.AccessSettings,
                    SourceKey = имяПараметра,
                    Name = имяПараметра,
                    Value = значение,
                });
            }
        }

        // Рассылки.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessDistributions, "distributions", отчёт))
        {
            var идентификатор = строка.GetString("DistributionId");
            var имя = строка.GetString("DistributionName");

            if (имя is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessDistributions, "distributions");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessDistributions}»: строка {ОписаниеСтроки(строка)} не имеет наименования — пропущена.");
                continue;
            }

            var опубликована = строка.GetDateTime("DatePublication");

            рассылки.Add(new LegacyDistributionRecord
            {
                SourceTable = LegacyTableNames.AccessDistributions,
                SourceKey = идентификатор ?? имя,
                Key = Ключ(LegacyTableNames.AccessDistributions, идентификатор),
                Name = имя,
                SenderKey = КлючКонтрагентаAccess(строка.GetString("dFrmEconomicMember")),
                Note = строка.GetString("SpecialNote"),
                Kind = строка.GetBool("isMultiple") ? DistributionKind.OneToMany : DistributionKind.ManyToOne,
                State = опубликована is null ? DistributionState.Draft : DistributionState.Dispatched,
                CreatedAt = строка.GetDateTime("DateLastEdit") ?? default,
                DispatchedAt = опубликована,
            });
        }

        // Почтовые реестры.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessPostRegisters, "post_registers", отчёт))
        {
            var идентификатор = строка.GetString("PostRegisterId");
            var номер = строка.GetString("NumberRegister");

            if (номер is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessPostRegisters, "post_registers");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessPostRegisters}»: строка {ОписаниеСтроки(строка)} не имеет номера реестра — пропущена.");
                continue;
            }

            реестры.Add(new LegacyPostRegisterRecord
            {
                SourceTable = LegacyTableNames.AccessPostRegisters,
                SourceKey = идентификатор ?? номер,
                Key = Ключ(LegacyTableNames.AccessPostRegisters, идентификатор),
                RegisterNumber = номер,
                BatchNumber = строка.GetString("RussianPostBatchName"),
                ShippingPoint = строка.GetString("RussianPostShippingPoint"),
                TrackLogin = строка.GetString("RussianPostLogin"),
                TrackToken = строка.GetString("RussianPostToken"),
                SendingDate = строка.GetDateTime("RussianPostSendingDate"),
            });
        }

        // Документы отправлений: связываются с письмами и входящими.
        var документыПисем = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessDocuments, "mail_documents", отчёт))
        {
            var номер = строка.GetString("DocumentNumber");
            var входящий = строка.GetString("IncomingId");
            var исходящий = строка.GetString("OutcomingId");

            if (номер is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessDocuments, "mail_documents");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessDocuments}»: строка {ОписаниеСтроки(строка)} не имеет номера документа — пропущена.");
                continue;
            }

            var ключОтправления = входящий is not null
                ? Ключ(LegacyTableNames.AccessIncoming, входящий)
                : исходящий is not null
                    ? Ключ(LegacyTableNames.AccessLetters, исходящий)
                    : null;

            if (ключОтправления is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessDocuments, "mail_documents");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessDocuments}»: документ «{номер}» не связан с письмом или входящим — пропущен.");
                continue;
            }

            документыПисем[Ключ(LegacyTableNames.AccessDocuments, строка.GetString("DocumentId"))] = ключОтправления;

            документы.Add(new LegacyMailDocumentRecord
            {
                SourceTable = LegacyTableNames.AccessDocuments,
                SourceKey = номер,
                MailKey = ключОтправления,
                DocumentNumber = номер,
            });
        }

        // Движения документов.
        var движенияИсходника = new Dictionary<string, LegacyRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessMovements, "document_movements", отчёт))
        {
            var идентификатор = строка.GetString("MovementId");

            if (идентификатор is not null)
            {
                движенияИсходника[идентификатор] = строка;
            }

            var входящий = строка.GetString("IncomingId");

            if (входящий is null)
            {
                отчёт.Пропущено(LegacyTableNames.AccessMovements, "document_movements");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessMovements}»: движение {ОписаниеСтроки(строка)} не связано с входящим документом — пропущено.");
                continue;
            }

            движения.Add(new LegacyDocumentMovementRecord
            {
                SourceTable = LegacyTableNames.AccessMovements,
                SourceKey = идентификатор ?? ОписаниеСтроки(строка),
                MailKey = Ключ(LegacyTableNames.AccessIncoming, входящий),
                MovedAt = строка.GetDateTime("MovementDate") ?? default,
                TargetDepartment = строка.GetString("MovementTitle") ?? string.Empty,
                Executor = строка.GetString("PutInCase"),
                Action = Действие(строка.GetInt("MovementType")),
            });
        }

        // Связи «документ — движение»: переносятся как движения по документу письма.
        foreach (var строка in ПолучитьСтроки(data, LegacyTableNames.AccessDocumentMovements, "document_movements", отчёт))
        {
            var документ = строка.GetString("DocumentId");
            var движение = строка.GetString("MovementId");

            if (документ is null || движение is null
                || !документыПисем.TryGetValue(Ключ(LegacyTableNames.AccessDocuments, документ), out var ключОтправления)
                || !движенияИсходника.TryGetValue(движение, out var исходноеДвижение))
            {
                отчёт.Пропущено(LegacyTableNames.AccessDocumentMovements, "document_movements");
                отчёт.Предупреждение(
                    $"Таблица «{LegacyTableNames.AccessDocumentMovements}»: связь {ОписаниеСтроки(строка)} не разрешается в пару «документ — движение» — пропущена.");
                continue;
            }

            var тип = исходноеДвижение.GetInt("MovementType");

            движения.Add(new LegacyDocumentMovementRecord
            {
                SourceTable = LegacyTableNames.AccessDocumentMovements,
                SourceKey = строка.GetString("DocumentMovementId") ?? $"{документ}:{движение}",
                MailKey = ключОтправления,
                MovedAt = исходноеДвижение.GetDateTime("MovementDate") ?? default,
                TargetDepartment = исходноеДвижение.GetString("MovementTitle") ?? string.Empty,
                Executor = исходноеДвижение.GetString("PutInCase"),
                Action = Действие(тип),
            });
        }

        // Справочные таблицы без собственных целевых таблиц: их строки не переносятся.
        var строкиТипов = ПолучитьСтроки(
            data,
            LegacyTableNames.AccessDocumentTypes,
            "mail_documents",
            отчёт,
            "справочник типов документов: значения учитываются в номере документа, отдельной таблицы нет");

        отчёт.Пропущено(LegacyTableNames.AccessDocumentTypes, "mail_documents", строкиТипов.Count);

        var строкиСхемы = ПолучитьСтроки(
            data,
            LegacyTableNames.AccessDbScheme,
            "parameters",
            отчёт,
            "служебная таблица версии схемы предшественника: не переносится");

        отчёт.Пропущено(LegacyTableNames.AccessDbScheme, "parameters", строкиСхемы.Count);

        return new LegacyMigrationPlan
        {
            Report = отчёт,
            Counterparties = контрагенты,
            CounterpartyFields = поля,
            Mails = письма,
            PrintTemplates = макеты,
            Parameters = параметры,
            Distributions = рассылки,
            PostRegisters = реестры,
            MailDocuments = документы,
            DocumentMovements = движения,
        };
    }

    /// <summary>
    /// Возвращает строки таблицы источника; отсутствующая таблица пропускается
    /// с предупреждением в отчёте (ТЗ §8.9, функции 117 и 118).
    /// </summary>
    /// <param name="data">Прочитанные исходные данные.</param>
    /// <param name="table">Имя таблицы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="report">Сборщик отчёта.</param>
    /// <param name="note">Пояснение к переносу таблицы.</param>
    /// <returns>Строки таблицы либо пустой список.</returns>
    private static IReadOnlyList<LegacyRow> ПолучитьСтроки(
        LegacyDataSet data,
        string table,
        string targetTable,
        MigrationReportBuilder report,
        string? note = null)
    {
        if (!data.HasTable(table))
        {
            report.Счётчик(table, targetTable, "таблица отсутствует в исходной базе — перенос пропущен");
            report.Предупреждение($"Таблица «{table}» отсутствует в исходной базе — пропущена.");
            return Array.Empty<LegacyRow>();
        }

        var строки = data.Rows(table);
        report.Счётчик(table, targetTable, note);
        report.Прочитано(table, targetTable, строки.Count);

        return строки;
    }

    /// <summary>
    /// Собирает адрес исходной записи; недопустимый почтовый индекс не приводит
    /// к отбрасыванию записи — она помечается как требующая проверки.
    /// </summary>
    /// <param name="row">Строка исходной таблицы.</param>
    /// <param name="zipColumn">Столбец с почтовым индексом.</param>
    /// <param name="lineColumns">Столбцы с адресными строками.</param>
    /// <param name="table">Имя исходной таблицы.</param>
    /// <param name="report">Сборщик отчёта.</param>
    /// <param name="needsVerification">Признак того, что запись требует проверки.</param>
    /// <returns>Собранный адрес либо <see langword="null"/>, если данных нет.</returns>
    private static Address? СобратьАдрес(
        LegacyRow row,
        string zipColumn,
        IReadOnlyList<string> lineColumns,
        string table,
        MigrationReportBuilder report,
        out bool needsVerification)
    {
        needsVerification = false;

        var индекс = row.GetString(zipColumn);
        var строки = new string[Address.LineCount];

        for (var номер = 0; номер < Address.LineCount; номер++)
        {
            строки[номер] = номер < lineColumns.Count
                ? row.GetString(lineColumns[номер]) ?? string.Empty
                : string.Empty;
        }

        if (строки.All(string.IsNullOrEmpty) && индекс is null)
        {
            return null;
        }

        var корректныйИндекс = PostalIndexValidator.IsValid(индекс);

        if (!корректныйИндекс)
        {
            needsVerification = true;
            report.Предупреждение(
                $"Таблица «{table}»: почтовый индекс «{индекс ?? string.Empty}» не содержит шести цифр — запись помечена как требующая проверки.");
        }

        var нормализованныйИндекс = корректныйИндекс ? индекс! : НормализоватьИндекс(индекс);
        var результат = Address.Create(нормализованныйИндекс, строки);

        if (результат.IsSuccess)
        {
            return результат.Value;
        }

        report.Предупреждение(
            $"Таблица «{table}»: адрес не собран ({результат.Error}) — запись помечена как требующая проверки.");
        needsVerification = true;

        var запасной = Address.Create("000000", строки);

        return запасной.IsSuccess ? запасной.Value : null;
    }

    /// <summary>
    /// Приводит почтовый индекс к шести цифрам: нецифровые символы отбрасываются,
    /// недостающие цифры дополняются нулями слева, лишние — обрезаются.
    /// </summary>
    /// <param name="index">Исходное значение индекса.</param>
    /// <returns>Индекс из шести цифр.</returns>
    private static string НормализоватьИндекс(string? index)
    {
        var цифры = new string((index ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (цифры.Length == 0)
        {
            return "000000";
        }

        if (цифры.Length < PostalIndexValidator.Length)
        {
            цифры = цифры.PadLeft(PostalIndexValidator.Length, '0');
        }
        else if (цифры.Length > PostalIndexValidator.Length)
        {
            цифры = цифры[..PostalIndexValidator.Length];
        }

        return цифры;
    }

    /// <summary>
    /// Собирает адрес исходной Access-схемы по ссылке на справочную таблицу <c>Address</c>.
    /// </summary>
    /// <param name="addressId">Идентификатор адреса.</param>
    /// <param name="addresses">Справочник адресов.</param>
    /// <param name="report">Сборщик отчёта.</param>
    /// <param name="needsVerification">Признак того, что запись требует проверки.</param>
    /// <returns>Собранный адрес либо <see langword="null"/>.</returns>
    private static Address? СобратьАдресAccess(
        string? addressId,
        IReadOnlyDictionary<string, АдресAccess> addresses,
        MigrationReportBuilder report,
        out bool needsVerification)
    {
        needsVerification = false;

        if (addressId is null)
        {
            return null;
        }

        if (!addresses.TryGetValue(addressId, out var данные))
        {
            needsVerification = true;
            report.Предупреждение(
                $"Адрес «{addressId}» не найден в справочнике адресов исходной базы — запись помечена как требующая проверки.");
            return null;
        }

        var корректныйИндекс = PostalIndexValidator.IsValid(данные.Индекс);

        if (!корректныйИндекс)
        {
            needsVerification = true;
            report.Предупреждение(
                $"Почтовый индекс «{данные.Индекс ?? string.Empty}» в справочнике адресов не содержит шести цифр — запись помечена как требующая проверки.");
        }

        var строки = new string[Address.LineCount];

        строки[0] = данные.Улица ?? string.Empty;
        строки[1] = данные.Город ?? string.Empty;
        строки[2] = данные.Регион ?? string.Empty;

        var индекс = корректныйИндекс ? данные.Индекс! : НормализоватьИндекс(данные.Индекс);
        var результат = Address.Create(индекс, строки);

        if (результат.IsSuccess)
        {
            return результат.Value;
        }

        needsVerification = true;
        report.Предупреждение($"Адрес исходной базы не собран ({результат.Error}) — запись помечена как требующая проверки.");

        var запасной = Address.Create("000000", строки);

        return запасной.IsSuccess ? запасной.Value : null;
    }

    /// <summary>Собирает ФИО руководителя из трёх столбцов исходной схемы.</summary>
    /// <param name="row">Строка исходной таблицы.</param>
    /// <returns>ФИО либо <see langword="null"/>.</returns>
    private static string? СобратьФИО(LegacyRow row)
    {
        var части = new[] { row.GetString("ChiefLastName"), row.GetString("ChiefFirstName"), row.GetString("ChiefMiddleName") }
            .Where(часть => !string.IsNullOrWhiteSpace(часть))
            .ToArray();

        return части.Length == 0 ? null : string.Join(' ', части);
    }

    /// <summary>
    /// Собирает пользовательские поля контрагента из неканонических столбцов
    /// исходной Access-схемы (в макетах они доступны по тегу <c>[Custom_*]</c>).
    /// </summary>
    /// <param name="row">Строка исходной таблицы.</param>
    /// <param name="counterpartyKey">Ключ контрагента в плане переноса.</param>
    /// <param name="sourceKey">Ключ строки исходной таблицы.</param>
    /// <returns>Список пользовательских полей.</returns>
    private static IReadOnlyList<LegacyCounterpartyFieldRecord> СобратьПоляКонтрагента(
        LegacyRow row,
        string counterpartyKey,
        string sourceKey)
    {
        var поля = new List<LegacyCounterpartyFieldRecord>();

        foreach (var столбец in НеканоническиеСтолбцыКонтрагента)
        {
            var значение = row.GetString(столбец);

            if (значение is null)
            {
                continue;
            }

            поля.Add(new LegacyCounterpartyFieldRecord
            {
                SourceTable = LegacyTableNames.AccessCounterparties,
                SourceKey = sourceKey,
                CounterpartyKey = counterpartyKey,
                Name = столбец,
                Value = значение,
                FieldType = ТипПоля(столбец, значение),
            });
        }

        return поля;
    }

    /// <summary>
    /// Собирает пользовательские поля отправителя исходной MySQL-схемы
    /// (описание логотипа и расширение файла).
    /// </summary>
    /// <param name="row">Строка исходной таблицы.</param>
    /// <returns>Список пользовательских полей.</returns>
    private static IReadOnlyList<LegacyCounterpartyFieldRecord> СобратьПоляОтправителя(LegacyRow row)
    {
        var поля = new List<LegacyCounterpartyFieldRecord>();

        foreach (var (столбец, имя) in new[] { ("logo_info", "LogoInfo"), ("file_Ext", "FileExt") })
        {
            var значение = row.GetString(столбец);

            if (значение is null)
            {
                continue;
            }

            поля.Add(new LegacyCounterpartyFieldRecord
            {
                SourceTable = LegacyTableNames.MySqlSenders,
                SourceKey = row.GetString("id") ?? string.Empty,
                CounterpartyKey = Ключ(LegacyTableNames.MySqlSenders, row.GetString("id")),
                Name = имя,
                Value = значение,
            });
        }

        return поля;
    }

    /// <summary>
    /// Собирает отправление исходной Access-схемы (письмо либо входящий документ).
    /// </summary>
    /// <param name="table">Имя исходной таблицы.</param>
    /// <param name="identifier">Идентификатор записи.</param>
    /// <param name="recipientKey">Ключ получателя.</param>
    /// <param name="senderKey">Ключ отправителя.</param>
    /// <param name="recipientAddress">Адрес получателя.</param>
    /// <param name="recipientName">Наименование получателя.</param>
    /// <param name="senderName">Наименование отправителя.</param>
    /// <param name="dateCreated">Дата создания.</param>
    /// <param name="datePrinted">Дата печати.</param>
    /// <param name="dateDispatched">Дата отправки.</param>
    /// <param name="dateConfirmation">Дата подтверждения.</param>
    /// <param name="dateCanceled">Дата отмены.</param>
    /// <param name="declaredValue">Объявленная ценность.</param>
    /// <param name="weight">Вес.</param>
    /// <param name="fee">Пошлина.</param>
    /// <param name="copyCount">Количество экземпляров.</param>
    /// <param name="isVip">Признак важного отправления.</param>
    /// <param name="note">Примечание.</param>
    /// <param name="needNotification">Признак необходимости уведомления.</param>
    /// <param name="incomingNumber">Входящий номер.</param>
    /// <param name="outgoingNumber">Исходящий номер.</param>
    /// <param name="russianPostId">Идентификатор отправления в Почте России.</param>
    /// <param name="envelopeType">Тип конверта.</param>
    /// <param name="noticeType">Тип уведомления.</param>
    /// <param name="category">Категория отправления.</param>
    /// <param name="distributionKey">Ключ рассылки.</param>
    /// <param name="postRegisterKey">Ключ почтового реестра.</param>
    /// <returns>Собранное отправление.</returns>
    private static LegacyMailRecord СобратьОтправлениеAccess(
        string table,
        string? identifier,
        string? recipientKey,
        string? senderKey,
        Address? recipientAddress,
        string? recipientName,
        string? senderName,
        DateTime? dateCreated,
        DateTime? datePrinted,
        DateTime? dateDispatched,
        DateTime? dateConfirmation,
        DateTime? dateCanceled,
        decimal? declaredValue,
        decimal? weight,
        decimal? fee,
        int copyCount,
        bool isVip,
        string? note,
        bool needNotification,
        string? incomingNumber,
        string? outgoingNumber,
        string? russianPostId,
        string? envelopeType,
        string? noticeType,
        MailCategory category,
        string? distributionKey,
        string? postRegisterKey)
    {
        return new LegacyMailRecord
        {
            SourceTable = table,
            SourceKey = identifier ?? string.Empty,
            CounterpartyKey = recipientKey,
            SenderKey = senderKey,
            RecipientAddress = recipientAddress,
            RecipientName = recipientName,
            SenderName = senderName,
            CopyCount = Math.Max(1, copyCount),
            IsVip = isVip,
            Note = note,
            DeclaredValue = declaredValue ?? 0,
            Weight = weight ?? 0,
            Fee = fee ?? 0,
            Status = dateDispatched is null ? MailStatus.Draft : MailStatus.Dispatched,
            MailCategory = category,
            NeedNotification = needNotification,
            DateCreated = dateCreated ?? default,
            DatePrinted = datePrinted,
            DateDispatched = dateDispatched,
            DateConfirmation = dateConfirmation,
            DateCanceled = dateCanceled,
            IncomingNumber = incomingNumber,
            OutgoingNumber = outgoingNumber,
            RussianPostId = russianPostId,
            EnvelopeType = envelopeType,
            NoticeType = noticeType,
            DistributionKey = distributionKey,
            PostRegisterKey = postRegisterKey,
        };
    }

    /// <summary>Определяет основной срез адресации по значению исходного поля.</summary>
    /// <param name="value">Значение поля <c>MainAddressType</c>.</param>
    /// <returns>Основной срез адресации.</returns>
    private static AddressMainType ОсновнойСрез(int? value) => value switch
    {
        0 => AddressMainType.Legal,
        1 => AddressMainType.Postal,
        2 => AddressMainType.Actual,
        _ => AddressMainType.Postal,
    };

    /// <summary>Определяет категорию отправления по значению исходного поля.</summary>
    /// <param name="value">Значение поля <c>Configuration</c>.</param>
    /// <returns>Категория отправления.</returns>
    private static MailCategory Категория(int? value) => value switch
    {
        1 => MailCategory.Registered,
        2 => MailCategory.DeclaredValue,
        3 => MailCategory.FirstClass,
        _ => MailCategory.Simple,
    };

    /// <summary>Определяет вид макета печати по значению исходного поля <c>Type</c>.</summary>
    /// <param name="value">Значение поля.</param>
    /// <returns>Вид макета.</returns>
    private static string ВидМакета(int? value) => value switch
    {
        0 => PrintTemplateKind.Envelope,
        1 => PrintTemplateKind.Blank,
        2 => PrintTemplateKind.Report,
        3 => PrintTemplateKind.Label,
        4 => PrintTemplateKind.Notification,
        _ => PrintTemplateKind.Blank,
    };

    /// <summary>Определяет действие с документом по значению исходного поля.</summary>
    /// <param name="value">Значение поля.</param>
    /// <returns>Действие с документом.</returns>
    private static DocumentMovementAction? Действие(int? value) => value switch
    {
        0 => DocumentMovementAction.Reception,
        1 => DocumentMovementAction.Execution,
        2 => DocumentMovementAction.Archived,
        _ => null,
    };

    /// <summary>
    /// Возвращает геометрию формата конверта из справочника форматов
    /// (ТЗ §8.5, функция 149); неизвестный формат даёт нулевые размеры.
    /// </summary>
    /// <param name="format">Обозначение формата.</param>
    /// <returns>Размеры макета, мм.</returns>
    private static (decimal Ширина, decimal Высота) ГеометрияФормата(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return (0, 0);
        }

        try
        {
            var геометрия = EnvelopeFormatCatalog.Default.Get(
                format,
                EnvelopeFormatCatalog.DefaultCountryCode);

            return (
                геометрия.GetWidthMm(TemplateOrientation.Portrait),
                геометрия.GetHeightMm(TemplateOrientation.Portrait));
        }
        catch (KeyNotFoundException)
        {
            return (0, 0);
        }
    }

    /// <summary>Определяет тип пользовательского поля по его имени и значению.</summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <returns>Тип поля.</returns>
    private static CounterpartyFieldType ТипПоля(string name, string value)
    {
        if (name.Contains("Date", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Time", StringComparison.OrdinalIgnoreCase))
        {
            return CounterpartyFieldType.Date;
        }

        if (name.Contains("Number", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Count", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Last", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.TryParse(
                value.Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out _)
                ? CounterpartyFieldType.Number
                : CounterpartyFieldType.Text;
        }

        return CounterpartyFieldType.Text;
    }

    /// <summary>Формирует ключ записи в плане переноса.</summary>
    /// <param name="table">Имя исходной таблицы.</param>
    /// <param name="identifier">Идентификатор записи.</param>
    /// <returns>Ключ записи.</returns>
    private static string Ключ(string table, string? identifier) => $"{table}:{identifier}";

    /// <summary>Формирует ключ контрагента исходной Access-схемы.</summary>
    /// <param name="identifier">Идентификатор контрагента.</param>
    /// <returns>Ключ контрагента либо <see langword="null"/>.</returns>
    private static string? КлючКонтрагентаAccess(string? identifier)
        => identifier is null ? null : Ключ(LegacyTableNames.AccessCounterparties, identifier);

    /// <summary>Формирует ключ рассылки исходной Access-схемы.</summary>
    /// <param name="identifier">Идентификатор рассылки.</param>
    /// <returns>Ключ рассылки либо <see langword="null"/>.</returns>
    private static string? КлючРассылкиAccess(string? identifier)
        => identifier is null ? null : Ключ(LegacyTableNames.AccessDistributions, identifier);

    /// <summary>
    /// Формирует ключ почтового реестра исходной Access-схемы: по идентификатору
    /// либо, при его отсутствии, по номеру реестра.
    /// </summary>
    /// <param name="identifier">Идентификатор реестра.</param>
    /// <param name="number">Номер реестра.</param>
    /// <returns>Ключ реестра либо <see langword="null"/>.</returns>
    private static string? КлючРеестраAccess(string? identifier, int? number)
    {
        if (identifier is not null)
        {
            return Ключ(LegacyTableNames.AccessPostRegisters, identifier);
        }

        return number is null ? null : $"номер:{number.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Формирует текстовое описание строки исходной таблицы для отчёта.</summary>
    /// <param name="row">Строка исходной таблицы.</param>
    /// <returns>Описание строки.</returns>
    private static string ОписаниеСтроки(LegacyRow row)
    {
        var идентификатор = row.GetString("id")
            ?? row.GetString("EconomicMemberId")
            ?? row.GetString("LetterId")
            ?? row.GetString("IncomingId")
            ?? row.GetString("AddressId")
            ?? row.GetString("DistributionId")
            ?? row.GetString("PostRegisterId")
            ?? row.GetString("DocumentId")
            ?? row.GetString("MovementId")
            ?? row.GetString("BlankId")
            ?? row.GetString("Id");

        return идентификатор is null ? "без идентификатора" : $"с идентификатором {идентификатор}";
    }

    /// <summary>
    /// Неканонические столбцы контрагента исходной Access-схемы: они переносятся
    /// в <c>counterparty_fields</c> и доступны в макетах по тегу <c>[Custom_*]</c>.
    /// </summary>
    private static readonly string[] НеканоническиеСтолбцыКонтрагента =
    [
        "Blocker",
        "Code",
        "DateLastEdit",
        "FirstName",
        "LastName",
        "MiddleName",
        "NameTo",
        "TemplateIncoming",
        "TemplateOriginal",
        "LastNumberIncoming",
        "LastNumberOriginal",
        "LastGenerationTimeIncoming",
        "LastGenerationTimeOriginal",
        "OKOPF",
        "Under",
        "Performer",
        "AddressNotify",
    ];

    /// <summary>
    /// Переносимые ключи настроек исходной Access-схемы: текущий отправитель
    /// (хранится флагом <c>Sender</c> у контрагента), флаги печати и общие
    /// параметры. Учётные данные и параметры внешних сервисов не переносятся.
    /// </summary>
    private static readonly (string Ключ, string Имя)[] ПереносимыеНастройки =
    [
        ("LastIncomingNumber", Parameter.LastIncomingNumberKey),
        ("LastOutcomingNumber", Parameter.LastOutgoingNumberKey),
        ("CountLetterReg", "CountLetterReg"),
        ("UseShortname", "UseShortname"),
        ("UseWordWrap", "UseWordWrap"),
        ("RevertAddressLineOrder", "RevertAddressLineOrder"),
        ("SortLetterRegAlphabet", "SortLetterRegAlphabet"),
        ("SaveXML", "SaveXML"),
        ("FormRegister", "FormRegister"),
        ("DefaultEnvelope", "DefaultEnvelope"),
        ("DefaultNotify", "DefaultNotify"),
        ("DefaultSystemEnvelope", "DefaultSystemEnvelope"),
        ("DefaultSystemNotify", "DefaultSystemNotify"),
        ("DefaultInventory", "DefaultInventory"),
        ("DefaultPostRegister", "DefaultPostRegister"),
        ("AddressWrapType", "AddressWrapType"),
        ("LastUsingExternalSearch", "LastUsingExternalSearch"),
        ("ContractNumber", "ContractNumber"),
        ("SortOrder", "SortOrder"),
        ("UserName", "UserName"),
        ("NeedName", "NeedName"),
    ];

    /// <summary>Добавляет соответствия полей исходной MySQL-схемы в отчёт.</summary>
    /// <param name="report">Сборщик отчёта.</param>
    private static void ДобавитьСоответствияMySql(MigrationReportBuilder report)
    {
        report.Соответствия(LegacyTableNames.MySqlSenders, "counterparties",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("ind_from", "post_zip", "почтовый индекс отправителя"),
            ("str_from1", "post_str1", null),
            ("str_from2", "post_str2", null),
            ("str_from3", "post_str3", null),
            ("str_from4", "post_str4", null),
            ("str_from5", "post_str5", null),
            ("str_from6", "post_str6", null),
            ("logo", "logo", "логотип отправителя"),
            ("logo_info", "counterparty_fields.LogoInfo", "пользовательское поле"),
            ("file_Ext", "counterparty_fields.FileExt", "пользовательское поле"),
        ]);

        report.Соответствия(LegacyTableNames.MySqlRecipients, "counterparties",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("ind_to", "post_zip", "почтовый индекс получателя"),
            ("group_id", "group_id", "идентификатор группы переносится по наименованию"),
            ("str_to1", "post_str1", null),
            ("str_to2", "post_str2", null),
            ("str_to3", "post_str3", null),
            ("str_to4", "post_str4", null),
            ("str_to5", "post_str5", null),
            ("str_to6", "post_str6", null),
            ("copyes", "mails.copy_count", null),
            ("vip", "is_vip", null),
            ("prim", "mails.note", null),
            ("date_send", "mails.date_created", null),
            ("print_prim", "mails.status", "признак печати: 1 — «Напечатано», 0 — «Черновик»"),
        ]);

        report.Соответствия(LegacyTableNames.MySqlPrintHistory, "print_history",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("date_print", "printed_at", null),
            ("ind_to", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to1", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to2", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to3", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to4", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to5", "recipient_address_snapshot", "снимок адреса получателя"),
            ("str_to6", "recipient_address_snapshot", "снимок адреса получателя"),
            ("vip", "is_vip", null),
            ("copyes", "copy_count", null),
            ("prim", "note", null),
            ("teplate_descr", "template_description", "описание применённого макета"),
            ("print_prim", null, "признак печати основного бланка не переносится"),
            ("id_env", null, "идентификатор конверта не переносится"),
            ("addrfrom", "sender_address_snapshot", "снимок адреса отправителя"),
        ]);

        report.Соответствия(LegacyTableNames.MySqlQrCache, "qr_cache",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("img_qrcode", "image_blob", "хеш полезной нагрузки вычисляется по изображению (SHA-256)"),
        ]);

        report.Соответствия(LegacyTableNames.MySqlGroups, "groups",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("name", "name", null),
            ("prim", "note", null),
            ("fdefault", "is_default", null),
            ("closed", "is_closed", null),
        ]);

        report.Соответствия(LegacyTableNames.MySqlInventory, "inventories",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("id_main_to", "counterparty_id", "ссылка на получателя"),
            ("name", "name", null),
            ("komu", null, "дублирует получателя, заданного ссылкой id_main_to"),
            ("kuda", "destination", null),
            ("prim", "note", null),
            ("date_create", "created_at", null),
        ]);

        report.Соответствия(LegacyTableNames.MySqlInventoryDetail, "inventory_items",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("id_main", "inventory_id", "ссылка на опись"),
            ("name", "name", null),
            ("kol", "quantity", null),
            ("cost", "cost", null),
            ("prim", "note", null),
            ("date_create", "created_at", null),
        ]);

        report.Соответствия(LegacyTableNames.MySqlParams, "parameters",
        [
            ("id", null, "идентификатор исходной таблицы"),
            ("name", "name", null),
            ("value", "value", null),
        ]);
    }

    /// <summary>Добавляет соответствия полей исходной Access-схемы в отчёт.</summary>
    /// <param name="report">Сборщик отчёта.</param>
    private static void ДобавитьСоответствияAccess(MigrationReportBuilder report)
    {
        report.Соответствия(LegacyTableNames.AccessAddresses, "counterparties",
        [
            ("AddressId", null, "справочная таблица: значения переносятся в адресные срезы"),
            ("AddressLine", "post_str1", "улица, дом"),
            ("City", "post_str2", "населённый пункт"),
            ("Province", "post_str3", "регион"),
            ("ZipCode", "post_zip", "почтовый индекс"),
        ]);

        report.Соответствия(LegacyTableNames.AccessBlanks, "print_templates",
        [
            ("Id", null, "идентификатор исходной таблицы"),
            ("Name", "name", null),
            ("Description", null, "описание дублирует наименование"),
            ("EnvSize", "format", "обозначение формата конверта"),
            ("Source", "file_path", null),
            ("Type", "kind", "0 — конверт, 1 — бланк, 2 — отчёт, 3 — этикетка, 4 — уведомление"),
        ]);

        report.Соответствия(LegacyTableNames.AccessCustomFields, "counterparty_fields",
        [
            ("EconomicMemberId", "counterparty_id", "ссылка на контрагента"),
            ("Name", "field_name", "в макетах доступно по тегу [Custom_ИмяПоля]"),
            ("Meaning", "field_value", null),
        ]);

        report.Соответствия(LegacyTableNames.AccessDbScheme, "parameters",
        [
            ("LastChangeDate", null, "служебная таблица версии схемы — не переносится"),
            ("LastChangedRevision", null, "служебная таблица версии схемы — не переносится"),
            ("Version", null, "служебная таблица версии схемы — не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessDistributions, "distributions",
        [
            ("DistributionId", null, "идентификатор исходной таблицы"),
            ("DistributionName", "name", null),
            ("dFrmEconomicMember", "sender_id", "ссылка на отправителя"),
            ("SpecialNote", "note", null),
            ("isMultiple", "kind", "признак рассылки нескольким получателям"),
            ("DateLastEdit", "created_at", null),
            ("DatePublication", "dispatched_at", "публикация рассылки"),
            ("AmountOfCOD", null, "сумма относится к отправлениям рассылки"),
            ("Content", null, "содержимое рассылки не переносится"),
            ("DateLastEdit_", null, null),
            ("FeeForShipping", null, "пошлина переносится в отправления"),
            ("InventoryXML", null, "опись в XML не переносится"),
            ("isEmail", null, "электронная рассылка не переносится"),
            ("isHtml", null, "электронная рассылка не переносится"),
            ("NameFrm", null, "отправитель задаётся ссылкой dFrmEconomicMember"),
            ("Price", null, "цена относится к отправлениям рассылки"),
            ("RNote", null, "дублирует примечание рассылки"),
            ("SenderEmail", null, "электронная рассылка не переносится"),
            ("Subject", null, "тема рассылки не переносится"),
            ("Weight", null, "вес относится к отправлениям рассылки"),
            ("Configuration", null, "конфигурация рассылки не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessDocuments, "mail_documents",
        [
            ("DocumentId", null, "идентификатор исходной таблицы"),
            ("DocumentNumber", "doc_number", null),
            ("DocumentType", null, "тип документа учитывается в номере"),
            ("DocumentDate", null, "дата документа не переносится"),
            ("DocumentPages", null, "объём документа не переносится"),
            ("DocumentCopies", null, "количество экземпляров не переносится"),
            ("IncomingId", "mail_id", "ссылка на входящий документ"),
            ("OutcomingId", "mail_id", "ссылка на письмо"),
        ]);

        report.Соответствия(LegacyTableNames.AccessDocumentMovements, "document_movements",
        [
            ("DocumentMovementId", null, "идентификатор исходной таблицы"),
            ("DocumentId", "mail_id", "документ определяет отправление"),
            ("MovementId", "moved_at, target_department, executor, action", "движение определяет действие"),
        ]);

        report.Соответствия(LegacyTableNames.AccessDocumentTypes, "mail_documents",
        [
            ("DocumentTypeId", null, "справочник типов документов — не переносится"),
            ("Name", null, "справочник типов документов — не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessCounterparties, "counterparties",
        [
            ("EconomicMemberId", null, "идентификатор исходной таблицы"),
            ("LegalName", "name", null),
            ("Organization", "name", "резервное наименование"),
            ("FullName", "name", "резервное наименование"),
            ("NameTo", "counterparty_fields.NameTo", "неканоническое поле"),
            ("ShortName", "short_name", null),
            ("INN", "inn", null),
            ("KPP", "kpp", null),
            ("OGRN", "ogrn", null),
            ("OKFS", "okfs", null),
            ("OKPO", "okpo", null),
            ("OKVED", "okved", null),
            ("OKOPF", "counterparty_fields.OKOPF", "неканоническое поле"),
            ("BIK", "bik", null),
            ("KS", "ks", null),
            ("RS", "rs", null),
            ("RKC", "rkc", null),
            ("NameBank", "bank_name", null),
            ("PassportSeries", "passport_series", null),
            ("PassportNumber", "passport_number", null),
            ("PassportGiven", "passport_issued_by", null),
            ("PassportDate", "passport_issued_date", null),
            ("BirthDate", "birth_date", null),
            ("BirthPlace", "birth_place", null),
            ("NPPosition", "position", null),
            ("ChiefLastName", "director_fio", "фамилия руководителя"),
            ("ChiefFirstName", "director_fio", "имя руководителя"),
            ("ChiefMiddleName", "director_fio", "отчество руководителя"),
            ("EconomicMemberEmail", "email", null),
            ("Phone", "phone", null),
            ("EconomicMemberNote", "special_notes", null),
            ("em_type", "type", "1 — физическое лицо, иначе — юридическое"),
            ("MainAddressType", "address_main_type", "0 — юридический, 1 — почтовый, 2 — фактический"),
            ("Sender", "is_sender", "текущий отправитель"),
            ("AddressLegal", "legal_zip, legal_str1..6", "адрес из справочника Address"),
            ("AddressPost", "post_zip, post_str1..6", "адрес из справочника Address"),
            ("AddressFact", "fact_zip, fact_str1..6", "адрес из справочника Address"),
            ("AddressText", "post_raw", "исходная строка адреса"),
            ("AddressNotify", "counterparty_fields.AddressNotify", "неканоническое поле"),
            ("Blocker", "counterparty_fields.Blocker", "неканоническое поле"),
            ("Code", "counterparty_fields.Code", "неканоническое поле"),
            ("DateLastEdit", "counterparty_fields.DateLastEdit", "неканоническое поле"),
            ("FirstName", "counterparty_fields.FirstName", "неканоническое поле"),
            ("LastName", "counterparty_fields.LastName", "неканоническое поле"),
            ("MiddleName", "counterparty_fields.MiddleName", "неканоническое поле"),
            ("TemplateIncoming", "counterparty_fields.TemplateIncoming", "неканоническое поле"),
            ("TemplateOriginal", "counterparty_fields.TemplateOriginal", "неканоническое поле"),
            ("LastNumberIncoming", "counterparty_fields.LastNumberIncoming", "неканоническое поле"),
            ("LastNumberOriginal", "counterparty_fields.LastNumberOriginal", "неканоническое поле"),
            ("LastGenerationTimeIncoming", "counterparty_fields.LastGenerationTimeIncoming", "неканоническое поле"),
            ("LastGenerationTimeOriginal", "counterparty_fields.LastGenerationTimeOriginal", "неканоническое поле"),
            ("Under", "counterparty_fields.Under", "неканоническое поле"),
            ("Performer", "counterparty_fields.Performer", "неканоническое поле"),
        ]);

        report.Соответствия(LegacyTableNames.AccessIncoming, "mails",
        [
            ("IncomingId", null, "идентификатор исходной таблицы"),
            ("IncomingNumber", "incoming_number", null),
            ("IncomingSubject", "note", null),
            ("IncomingDate", "date_created", null),
            ("OriginalNumber", "outgoing_number", null),
            ("NameTo", "counterparty_id", "получатель по наименованию"),
            ("ToEconomicMember", "counterparty_id", "ссылка на контрагента"),
            ("NameFrom", "sender_id", "отправитель по наименованию"),
            ("FrmEconomicMember", "sender_id", "ссылка на контрагента"),
            ("Channel", null, "канал поступления не переносится"),
            ("DateLastEdit", null, "дата правки не переносится"),
            ("IncomingDocument", null, "текст документа не переносится"),
            ("IncomingNotes", null, "примечания не переносятся"),
            ("NeedScan", null, "признак сканирования не переносится"),
            ("OriginalDate", null, "дата оригинала не переносится"),
            ("Resolution", null, "резолюция не переносится"),
            ("DateIncludeInRegister", null, "дата включения в реестр не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessLetters, "mails",
        [
            ("LetterId", null, "идентификатор исходной таблицы"),
            ("LetterNumber", "outgoing_number", null),
            ("DateCreated", "date_created", null),
            ("DatePrinted", "date_printed", null),
            ("DateDispatched", "date_dispatched", null),
            ("DateConfirmation", "date_confirmation", null),
            ("DateCanceled", "date_canceled", null),
            ("AmountOfCOD", "declared_value", "объявленная ценность"),
            ("Weight", "weight", null),
            ("FeeForShipping", "fee", null),
            ("Price", null, "цена дублирует объявленную ценность"),
            ("SpecialNote", "note", null),
            ("RNote", null, "дублирует примечание"),
            ("NeedNotification", "need_notification", null),
            ("PostalNumber", "russian_post_id", null),
            ("RussianPostId", null, "дублирует PostalNumber"),
            ("PostRegisterId", "post_register_id", "ссылка на почтовый реестр"),
            ("PostRegisterNumber", "post_register_id", "ссылка на реестр по номеру"),
            ("TypeEnvelope", "envelope_type", null),
            ("AddressNotify", null, "адрес уведомления не переносится"),
            ("Configuration", "mail_category", "категория отправления"),
            ("DistributionId", "distribution_id", "ссылка на рассылку"),
            ("AddressTo", "to_zip, to_str1..6", "адрес получателя"),
            ("AddressFrom", null, "адрес отправителя переносится как снимок в журнал печати"),
            ("NameTo", "counterparty_id", "получатель по наименованию"),
            ("ToEconomicMember", "counterparty_id", "ссылка на контрагента"),
            ("NameFrm", "sender_id", "отправитель по наименованию"),
            ("FrmEconomicMember", "sender_id", "ссылка на контрагента"),
            ("Content", null, "содержимое письма не переносится"),
            ("Subject", null, "тема письма не переносится"),
            ("InventoryXML", null, "опись в XML не переносится"),
            ("isEmail", null, "электронное письмо не переносится"),
            ("isHtml", null, "электронное письмо не переносится"),
            ("LetterEmail", null, "электронное письмо не переносится"),
            ("ReplyToEmail", null, "электронное письмо не переносится"),
            ("SenderEmail", null, "электронное письмо не переносится"),
            ("DateLastEdit", null, "дата правки не переносится"),
            ("DatePublication", null, "дата публикации не переносится"),
            ("DateAddedInPostRegister", null, "дата включения в реестр не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessMovements, "document_movements",
        [
            ("MovementId", null, "идентификатор исходной таблицы"),
            ("IncomingId", "mail_id", "ссылка на входящий документ"),
            ("MovementDate", "moved_at", null),
            ("MovementTitle", "target_department", null),
            ("MovementType", "action", "0 — приём, 1 — передача на исполнение, 2 — в архив"),
            ("PerformerId", "executor", "исполнитель"),
            ("PutInCase", "executor", "исполнитель"),
            ("ManualMovementDate", null, "дата ручного движения не переносится"),
            ("NumberExecuted", null, "номер исполненного документа не переносится"),
            ("ToDate", null, "срок исполнения не переносится"),
            ("RegisterExecuted", null, "дата регистрации исполнения не переносится"),
            ("About", null, "примечание не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessPostRegisters, "post_registers",
        [
            ("PostRegisterId", null, "идентификатор исходной таблицы"),
            ("NumberRegister", "register_number", null),
            ("RussianPostBatchName", "batch_number", null),
            ("RussianPostShippingPoint", "shipping_point", null),
            ("RussianPostSendingDate", "sending_date", null),
            ("RussianPostLogin", "track_login", null),
            ("RussianPostToken", "track_token", null),
            ("RussianPostPassword", null, "учётные данные не переносятся"),
            ("Address", null, "адрес отправки не переносится"),
            ("DateCreated", null, "дата создания не переносится"),
            ("DateDispatched", null, "дата отправки не переносится"),
            ("DateLastEdit", null, "дата правки не переносится"),
            ("DatePrinted", null, "дата печати не переносится"),
            ("Name", null, "наименование реестра не переносится"),
            ("Title", null, "заголовок реестра не переносится"),
            ("SenderInfo", null, "сведения об отправителе не переносятся"),
            ("SenderNaturalPersonName", null, "ФИО отправителя не переносится"),
            ("Zipcode", null, "индекс отправления не переносится"),
        ]);

        report.Соответствия(LegacyTableNames.AccessSettings, "parameters",
        [
            ("LastIncomingNumber", "parameters.LastIncomingNumber", "переносимый ключ"),
            ("LastOutcomingNumber", "parameters.LastOutgoingNumber", "переносимый ключ"),
            ("CountLetterReg", "parameters.CountLetterReg", "переносимый ключ"),
            ("UseShortname", "parameters.UseShortname", "флаг печати"),
            ("UseWordWrap", "parameters.UseWordWrap", "флаг печати"),
            ("RevertAddressLineOrder", "parameters.RevertAddressLineOrder", "флаг печати"),
            ("SortLetterRegAlphabet", "parameters.SortLetterRegAlphabet", "флаг печати"),
            ("SaveXML", "parameters.SaveXML", "переносимый ключ"),
            ("FormRegister", "parameters.FormRegister", "переносимый ключ"),
            ("DefaultEnvelope", "parameters.DefaultEnvelope", "переносимый ключ"),
            ("DefaultNotify", "parameters.DefaultNotify", "переносимый ключ"),
            ("DefaultSystemEnvelope", "parameters.DefaultSystemEnvelope", "переносимый ключ"),
            ("DefaultSystemNotify", "parameters.DefaultSystemNotify", "переносимый ключ"),
            ("DefaultInventory", "parameters.DefaultInventory", "переносимый ключ"),
            ("DefaultPostRegister", "parameters.DefaultPostRegister", "переносимый ключ"),
            ("AddressWrapType", "parameters.AddressWrapType", "переносимый ключ"),
            ("LastUsingExternalSearch", "parameters.LastUsingExternalSearch", "переносимый ключ"),
            ("ContractNumber", "parameters.ContractNumber", "переносимый ключ"),
            ("SortOrder", "parameters.SortOrder", "переносимый ключ"),
            ("UserName", "parameters.UserName", "переносимый ключ"),
            ("NeedName", "parameters.NeedName", "переносимый ключ"),
            ("ProxyLogin", null, "учётные данные не переносятся"),
            ("ProxyPassword", null, "учётные данные не переносятся"),
            ("ProxyPort", null, "параметры прокси не переносятся"),
            ("ProxyServer", null, "параметры прокси не переносятся"),
            ("SmtpPassword", null, "учётные данные не переносятся"),
            ("SmtpServer", null, "параметры почтового сервера не переносятся"),
            ("IsProxyAuthorization", null, "параметры прокси не переносятся"),
            ("IsProxyUse", null, "параметры прокси не переносятся"),
            ("NeedAuthorization", null, "параметры авторизации не переносятся"),
            ("NeedSSL", null, "параметры авторизации не переносятся"),
            ("ServiceLogin", null, "учётные данные не переносятся"),
            ("ServiceMessage", null, "учётные данные не переносятся"),
            ("ServiceMessageType", null, "учётные данные не переносятся"),
            ("RussianPostLogin", null, "учётные данные не переносятся"),
            ("RussianPostPassword", null, "учётные данные не переносятся"),
            ("RussianPostToken", null, "учётные данные не переносятся"),
            ("Filters", null, "сохранённые фильтры не переносятся"),
            ("SettId", null, "идентификатор исходной таблицы"),
        ]);
    }

    /// <summary>Адрес исходной Access-схемы в виде строк справочника <c>Address</c>.</summary>
    /// <param name="zip">Почтовый индекс.</param>
    /// <param name="street">Улица, дом.</param>
    /// <param name="city">Населённый пункт.</param>
    /// <param name="province">Регион.</param>
    private sealed record АдресAccess(string? Индекс, string? Улица, string? Город, string? Регион);
}

/// <summary>
/// Писатель переноса: записывает подготовленный план в целевую базу через
/// репозитории слоя инфраструктуры (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// <para>
/// Записи сливаются с существующей базой по ключу функции 74 (ТЗ §8.5): ИНН, а
/// при его отсутствии — нормализованное наименование и почтовый индекс. Политика
/// слияния задаётся при создании писателя; по умолчанию действует «дополнить
/// пустые поля» — она не затирает значения, заведённые вручную.
/// </para>
/// <para>
/// Счётчики отчёта пополняются по мере записи: исходная строка учитывается один
/// раз, даже если из неё получено несколько целевых записей.
/// </para>
/// </remarks>
public sealed class LegacyTargetWriter
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly ICounterpartyFieldRepository _поляКонтрагентов;
    private readonly IMailRepository _письма;
    private readonly IGroupRepository _группы;
    private readonly IPrintHistoryRepository _историяПечати;
    private readonly IPrintTemplateRepository _макеты;
    private readonly IParameterRepository _параметры;
    private readonly IQrCacheRepository _коды;
    private readonly IInventoryRepository _описи;
    private readonly IInventoryItemRepository _позиции;
    private readonly IDistributionRepository _рассылки;
    private readonly IPostRegisterRepository _реестры;
    private readonly IMailDocumentRepository _документы;
    private readonly IDocumentMovementRepository _движения;
    private readonly MergePolicy _политика;

    private readonly Dictionary<string, long> _контрагентыПоКлючу = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _группыПоИмени = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _описиПоКлючу = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _рассылкиПоКлючу = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _реестрыПоКлючу = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _отправленияПоКлючу = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _перенесённые = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ошибки = new(StringComparer.OrdinalIgnoreCase);

    private long? _текущийОтправитель;

    /// <summary>
    /// Создаёт писатель переноса.
    /// </summary>
    /// <param name="session">Сессия целевой базы данных.</param>
    /// <param name="policy">
    /// Политика слияния с существующими записями (ТЗ §8.5, функция 74).
    /// </param>
    public LegacyTargetWriter(ISqliteSession session, MergePolicy policy = MergePolicy.FillEmpty)
    {
        ArgumentNullException.ThrowIfNull(session);

        _контрагенты = new CounterpartyRepository(session);
        _поляКонтрагентов = new CounterpartyFieldRepository(session);
        _письма = new MailRepository(session);
        _группы = new GroupRepository(session);
        _историяПечати = new PrintHistoryRepository(session);
        _макеты = new PrintTemplateRepository(session);
        _параметры = new ParameterRepository(session);
        _коды = new QrCacheRepository(session);
        _описи = new InventoryRepository(session);
        _позиции = new InventoryItemRepository(session);
        _рассылки = new DistributionRepository(session);
        _реестры = new PostRegisterRepository(session);
        _документы = new MailDocumentRepository(session);
        _движения = new DocumentMovementRepository(session);
        _политика = policy;
    }

    /// <summary>
    /// Записывает план переноса в целевую базу.
    /// </summary>
    /// <param name="plan">План переноса.</param>
    /// <param name="report">Сборщик отчёта.</param>
    /// <param name="source">
    /// Источник данных для колонки <c>counterparties.source</c> (ТЗ §8.12,
    /// функция 75): «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;».
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task WriteAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(report);

        await ЗаписатьГруппыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьКонтрагентовAsync(plan, report, source, cancellationToken).ConfigureAwait(false);
        await ЗаписатьПоляКонтрагентовAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьОписиAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьПозицииОписейAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьРассылкиAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьРеестрыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьПисьмаAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьИсториюПечатиAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьКодыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьПараметрыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьМакетыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьДокументыAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьДвиженияAsync(plan, report, cancellationToken).ConfigureAwait(false);
        await ЗаписатьТекущегоОтправителяAsync(report, cancellationToken).ConfigureAwait(false);

        УчестьНеперенесённые(plan, report);
    }

    /// <summary>Записывает группы исходной базы.</summary>
    private async Task ЗаписатьГруппыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        if (plan.Groups.Count == 0)
        {
            return;
        }

        var группы = (await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();

        foreach (var группа in plan.Groups)
        {
            try
            {
                var существующая = группы.FirstOrDefault(заведённая =>
                    string.Equals(заведённая.Name, группа.Name, StringComparison.OrdinalIgnoreCase));

                long идентификатор;

                if (существующая is null)
                {
                    var новая = new Group
                    {
                        Name = группа.Name,
                        Note = группа.Note,
                        IsClosed = группа.IsClosed,
                        SortOrder = 0,
                    };

                    идентификатор = await _группы.AddAsync(новая, cancellationToken).ConfigureAwait(false);
                    группы.Add(новая);
                    report.ПеренесеноВЦель("groups");
                }
                else
                {
                    идентификатор = существующая.Id;
                    var изменено = false;

                    if (существующая.Note is null && группа.Note is not null)
                    {
                        существующая.Note = группа.Note;
                        изменено = true;
                    }
                    else if (_политика == MergePolicy.Overwrite && группа.Note is not null)
                    {
                        существующая.Note = группа.Note;
                        изменено = true;
                    }

                    if (!существующая.IsClosed && группа.IsClosed)
                    {
                        существующая.IsClosed = true;
                        изменено = true;
                    }

                    if (изменено)
                    {
                        await _группы.UpdateAsync(существующая, cancellationToken).ConfigureAwait(false);
                    }

                    report.ПеренесеноВЦель("groups");
                }

                _группыПоИмени[группа.Name] = идентификатор;

                if (группа.IsDefault)
                {
                    // Признак группы по умолчанию меняется только назначением:
                    // снять его обычным изменением нельзя (§3.1.2).
                    await _группы.SetDefaultAsync(идентификатор, cancellationToken).ConfigureAwait(false);
                }

                УчитыватьПеренос(группа, report, "groups");
            }
#pragma warning disable CA1031 // Ошибка одной группы не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(группа, ошибка, report, "groups");
            }
        }
    }

    /// <summary>Записывает контрагентов исходной базы.</summary>
    private async Task ЗаписатьКонтрагентовAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        string source,
        CancellationToken cancellationToken)
    {
        foreach (var контрагент in plan.Counterparties)
        {
            try
            {
                long идентификатор;
                var индекс = ИндексСлияния(контрагент);

                var существующий = await НайтиПоКлючуAsync(контрагент.Inn, контрагент.Name, индекс, cancellationToken)
                    .ConfigureAwait(false);

                if (существующий is null)
                {
                    var новый = СоздатьКонтрагента(контрагент, source, ГруппаПоИмени(контрагент.GroupName));

                    идентификатор = await _контрагенты.AddAsync(новый, cancellationToken).ConfigureAwait(false);
                    report.ПеренесеноВЦель("counterparties");
                }
                else
                {
                    идентификатор = существующий.Id;

                    if (_политика == MergePolicy.SkipExisting)
                    {
                        report.Предупреждение(
                            $"Контрагент «{контрагент.Name}» уже есть в базе — запись пропущена по политике слияния.");
                    }
                    else
                    {
                        СлитьКонтрагента(
                            существующий,
                            контрагент,
                            source,
                            ГруппаПоИмени(контрагент.GroupName),
                            _политика == MergePolicy.Overwrite);

                        await _контрагенты.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
                        report.ПеренесеноВЦель("counterparties");
                    }
                }

                _контрагентыПоКлючу[контрагент.Key] = идентификатор;

                if (контрагент.IsSender)
                {
                    _текущийОтправитель = идентификатор;
                }

                УчитыватьПеренос(контрагент, report, "counterparties");
            }
#pragma warning disable CA1031 // Ошибка одной записи не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(контрагент, ошибка, report, "counterparties");
            }
        }
    }

    /// <summary>Записывает пользовательские поля контрагентов.</summary>
    private async Task ЗаписатьПоляКонтрагентовAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var контрагент in plan.Counterparties)
        {
            foreach (var поле in контрагент.CustomFields)
            {
                await ЗаписатьПолеКонтрагентаAsync(поле, контрагент.Key, report, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var поле in plan.CounterpartyFields)
        {
            await ЗаписатьПолеКонтрагентаAsync(поле, поле.CounterpartyKey, report, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Записывает одно пользовательское поле контрагента.</summary>
    private async Task ЗаписатьПолеКонтрагентаAsync(
        LegacyCounterpartyFieldRecord поле,
        string ключКонтрагента,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!_контрагентыПоКлючу.TryGetValue(ключКонтрагента, out var идентификатор))
            {
                report.ПропущеноЗаписи(поле.SourceTable);
                report.Предупреждение(
                    $"Пользовательское поле «{поле.Name}» не перенесено: контрагент исходной базы не найден.");
                return;
            }

            var существующие = await _поляКонтрагентов
                .GetByCounterpartyAsync(идентификатор, cancellationToken)
                .ConfigureAwait(false);

            var существующее = существующие.FirstOrDefault(заведённое =>
                string.Equals(заведённое.FieldName, поле.Name, StringComparison.OrdinalIgnoreCase));

            if (существующее is null)
            {
                await _поляКонтрагентов.AddAsync(
                    new CounterpartyField
                    {
                        CounterpartyId = идентификатор,
                        FieldName = поле.Name,
                        FieldValue = поле.Value,
                        FieldType = поле.FieldType,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else if (_политика != MergePolicy.SkipExisting
                && (существующее.FieldValue is null || _политика == MergePolicy.Overwrite))
            {
                существующее.FieldValue = поле.Value;
                существующее.FieldType = поле.FieldType;

                await _поляКонтрагентов.UpdateAsync(существующее, cancellationToken).ConfigureAwait(false);
            }

            report.ПеренесеноВЦель("counterparty_fields");
            УчитыватьПеренос(поле, report, "counterparty_fields");
        }
#pragma warning disable CA1031 // Ошибка одного поля не прерывает перенос остальных.
        catch (Exception ошибка)
#pragma warning restore CA1031
        {
            ОшибкаПереноса(поле, ошибка, report, "counterparty_fields");
        }
    }

    /// <summary>Записывает отправления исходной базы.</summary>
    private async Task ЗаписатьПисьмаAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var письмо in plan.Mails)
        {
            try
            {
                var получатель = await РазрешитьПолучателяAsync(письмо, report, cancellationToken).ConfigureAwait(false);

                if (получатель is null)
                {
                    report.ПропущеноЗаписи(письмо.SourceTable);
                    report.Предупреждение(
                        $"Отправление {ОписаниеЗаписи(письмо)} не перенесено: получатель не определён.");
                    continue;
                }

                var отправитель = await РазрешитьОтправителяAsync(письмо, получатель.Value, cancellationToken)
                    .ConfigureAwait(false);

                var почтовыйРеестр = РазрешитьРеестрAsync(письмо.PostRegisterKey);
                var рассылка = РазрешитьРассылкуAsync(письмо.DistributionKey);
                var опись = РазрешитьОписьAsync(письмо.InventoryKey);

                var сущность = new ПочтовоеОтправление
                {
                    CounterpartyId = получатель.Value,
                    SenderId = отправитель,
                    DistributionId = рассылка,
                    InventoryId = опись,
                    PostRegisterId = почтовыйРеестр,
                    EnvelopeType = письмо.EnvelopeType,
                    NoticeType = письмо.NoticeType,
                    CopyCount = Math.Max(1, письмо.CopyCount),
                    IsVip = письмо.IsVip,
                    Note = письмо.Note,
                    DeclaredValue = письмо.DeclaredValue,
                    Weight = письмо.Weight,
                    Fee = письмо.Fee,
                    Status = письмо.Status,
                    NeedNotification = письмо.NeedNotification,
                    NeedReturnAddress = письмо.NeedReturnAddress,
                    MailCategory = письмо.MailCategory,
                    DateCreated = письмо.DateCreated,
                    DatePrinted = письмо.DatePrinted,
                    DateDispatched = письмо.DateDispatched,
                    DateConfirmation = письмо.DateConfirmation,
                    DateCanceled = письмо.DateCanceled,
                    IncomingNumber = письмо.IncomingNumber,
                    OutgoingNumber = письмо.OutgoingNumber,
                    RussianPostId = письмо.RussianPostId,
                    RecipientAddress = письмо.RecipientAddress,
                };

                var идентификатор = await _письма.AddAsync(сущность, cancellationToken).ConfigureAwait(false);
                _отправленияПоКлючу[КлючОтправления(письмо)] = идентификатор;

                report.ПеренесеноВЦель("mails");
                УчитыватьПеренос(письмо, report, "mails");
            }
#pragma warning disable CA1031 // Ошибка одного отправления не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(письмо, ошибка, report, "mails");
            }
        }
    }

    /// <summary>Записывает журнал печати исходной базы.</summary>
    private async Task ЗаписатьИсториюПечатиAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var запись in plan.PrintHistories)
        {
            try
            {
                var сущность = new PrintHistory
                {
                    PrintedAt = запись.PrintedAt == default ? DateTime.UtcNow : запись.PrintedAt,
                    RecipientAddressSnapshot = запись.RecipientAddressSnapshot,
                    SenderAddressSnapshot = запись.SenderAddressSnapshot,
                    CopyCount = Math.Max(1, запись.CopyCount),
                    IsVip = запись.IsVip,
                    Note = запись.Note,
                    TemplateDescription = запись.TemplateDescription,
                };

                await _историяПечати.AddAsync(сущность, cancellationToken).ConfigureAwait(false);

                report.ПеренесеноВЦель("print_history");
                УчитыватьПеренос(запись, report, "print_history");
            }
#pragma warning disable CA1031 // Ошибка одной записи не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(запись, ошибка, report, "print_history");
            }
        }
    }

    /// <summary>Записывает кэш QR-кодов исходной базы.</summary>
    private async Task ЗаписатьКодыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var запись in plan.QrCaches)
        {
            try
            {
                var существующий = await _коды
                    .GetByPayloadHashAsync(запись.PayloadHash, cancellationToken)
                    .ConfigureAwait(false);

                if (существующий is null)
                {
                    await _коды.AddAsync(
                        new QrCache
                        {
                            PayloadHash = запись.PayloadHash,
                            ImageBlob = запись.ImageBlob,
                        },
                        cancellationToken).ConfigureAwait(false);

                    report.ПеренесеноВЦель("qr_cache");
                }

                УчитыватьПеренос(запись, report, "qr_cache");
            }
#pragma warning disable CA1031 // Ошибка одной записи не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(запись, ошибка, report, "qr_cache");
            }
        }
    }

    /// <summary>Записывает описи вложений исходной базы.</summary>
    private async Task ЗаписатьОписиAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var опись in plan.Inventories)
        {
            try
            {
                long? получатель = null;

                if (опись.CounterpartyKey is not null
                    && _контрагентыПоКлючу.TryGetValue(опись.CounterpartyKey, out var идентификатор))
                {
                    получатель = идентификатор;
                }

                var сущность = new Inventory
                {
                    Name = опись.Name,
                    CounterpartyId = получатель,
                    Destination = опись.Destination,
                    Note = опись.Note,
                    CreatedAt = опись.CreatedAt == default ? DateTime.UtcNow : опись.CreatedAt,
                };

                var идентификаторОписи = await _описи.AddAsync(сущность, cancellationToken).ConfigureAwait(false);
                _описиПоКлючу[опись.Key] = идентификаторОписи;

                report.ПеренесеноВЦель("inventories");
                УчитыватьПеренос(опись, report, "inventories");
            }
#pragma warning disable CA1031 // Ошибка одной записи не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(опись, ошибка, report, "inventories");
            }
        }
    }

    /// <summary>Записывает позиции описей вложений исходной базы.</summary>
    private async Task ЗаписатьПозицииОписейAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var позиция in plan.InventoryItems)
        {
            try
            {
                if (!_описиПоКлючу.TryGetValue(позиция.InventoryKey, out var опись))
                {
                    report.ПропущеноЗаписи(позиция.SourceTable);
                    report.Предупреждение(
                        $"Позиция «{позиция.Name}» не перенесена: опись исходной базы не найдена.");
                    continue;
                }

                var сущность = new InventoryItem
                {
                    InventoryId = опись,
                    Name = позиция.Name,
                    Quantity = Math.Max(1, позиция.Quantity),
                    Cost = позиция.Cost,
                    Note = позиция.Note,
                    CreatedAt = позиция.CreatedAt == default ? DateTime.UtcNow : позиция.CreatedAt,
                };

                await _позиции.AddAsync(сущность, cancellationToken).ConfigureAwait(false);

                report.ПеренесеноВЦель("inventory_items");
                УчитыватьПеренос(позиция, report, "inventory_items");
            }
#pragma warning disable CA1031 // Ошибка одной позиции не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(позиция, ошибка, report, "inventory_items");
            }
        }
    }

    /// <summary>Записывает параметры исходной базы.</summary>
    private async Task ЗаписатьПараметрыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var параметр in plan.Parameters)
        {
            try
            {
                var существующий = await _параметры
                    .GetByNameAsync(параметр.Name, cancellationToken)
                    .ConfigureAwait(false);

                if (существующий is null)
                {
                    await _параметры.AddAsync(
                        new Parameter { Name = параметр.Name, Value = параметр.Value },
                        cancellationToken).ConfigureAwait(false);
                }
                else if (_политика != MergePolicy.SkipExisting
                    && (существующий.Value is null || _политика == MergePolicy.Overwrite))
                {
                    существующий.Value = параметр.Value;

                    await _параметры.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
                }

                report.ПеренесеноВЦель("parameters");
                УчитыватьПеренос(параметр, report, "parameters");
            }
#pragma warning disable CA1031 // Ошибка одного параметра не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(параметр, ошибка, report, "parameters");
            }
        }
    }

    /// <summary>Записывает макеты печати исходной базы.</summary>
    private async Task ЗаписатьМакетыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        if (plan.PrintTemplates.Count == 0)
        {
            return;
        }

        var макеты = await _макеты.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        foreach (var макет in plan.PrintTemplates)
        {
            try
            {
                var существующий = макеты.FirstOrDefault(заведённый =>
                    string.Equals(заведённый.Name, макет.Name, StringComparison.OrdinalIgnoreCase));

                if (существующий is null)
                {
                    await _макеты.AddAsync(
                        new PrintTemplate
                        {
                            Name = макет.Name,
                            Kind = макет.Kind,
                            Format = макет.Format,
                            WidthMm = макет.WidthMm,
                            HeightMm = макет.HeightMm,
                            Orientation = макет.Orientation,
                            FilePath = макет.FilePath,
                            IsBuiltin = false,
                            Country = EnvelopeFormatCatalog.DefaultCountryCode,
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                else if (_политика != MergePolicy.SkipExisting)
                {
                    существующий.Kind = макет.Kind;
                    существующий.Format = макет.Format;
                    существующий.WidthMm = макет.WidthMm;
                    существующий.HeightMm = макет.HeightMm;
                    существующий.Orientation = макет.Orientation;

                    if (существующий.FilePath.Length == 0 || _политика == MergePolicy.Overwrite)
                    {
                        существующий.FilePath = макет.FilePath;
                    }

                    await _макеты.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
                }

                report.ПеренесеноВЦель("print_templates");
                УчитыватьПеренос(макет, report, "print_templates");
            }
#pragma warning disable CA1031 // Ошибка одного макета не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(макет, ошибка, report, "print_templates");
            }
        }
    }

    /// <summary>Записывает рассылки исходной базы.</summary>
    private async Task ЗаписатьРассылкиAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var рассылка in plan.Distributions)
        {
            try
            {
                long? отправитель = null;

                if (рассылка.SenderKey is not null
                    && _контрагентыПоКлючу.TryGetValue(рассылка.SenderKey, out var идентификатор))
                {
                    отправитель = идентификатор;
                }

                var сущность = new Distribution
                {
                    Name = рассылка.Name,
                    SenderId = отправитель,
                    Kind = рассылка.Kind,
                    Note = рассылка.Note,
                    State = рассылка.State,
                    CreatedAt = рассылка.CreatedAt == default ? DateTime.UtcNow : рассылка.CreatedAt,
                    DispatchedAt = рассылка.DispatchedAt,
                };

                var идентификаторРассылки = await _рассылки.AddAsync(сущность, cancellationToken).ConfigureAwait(false);
                _рассылкиПоКлючу[рассылка.Key] = идентификаторРассылки;

                report.ПеренесеноВЦель("distributions");
                УчитыватьПеренос(рассылка, report, "distributions");
            }
#pragma warning disable CA1031 // Ошибка одной рассылки не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(рассылка, ошибка, report, "distributions");
            }
        }
    }

    /// <summary>Записывает почтовые реестры исходной базы.</summary>
    private async Task ЗаписатьРеестрыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        if (plan.PostRegisters.Count == 0)
        {
            return;
        }

        var реестры = await _реестры.GetAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var реестр in plan.PostRegisters)
        {
            try
            {
                var существующий = реестры.FirstOrDefault(заведённый =>
                    string.Equals(заведённый.RegisterNumber, реестр.RegisterNumber, StringComparison.OrdinalIgnoreCase));

                if (существующий is null)
                {
                    var сущность = new PostRegister
                    {
                        RegisterNumber = реестр.RegisterNumber,
                        BatchNumber = реестр.BatchNumber,
                        ShippingPoint = реестр.ShippingPoint,
                        TrackLogin = реестр.TrackLogin,
                        TrackToken = реестр.TrackToken,
                        SendingDate = реестр.SendingDate,
                    };

                    var идентификатор = await _реестры.AddAsync(сущность, cancellationToken).ConfigureAwait(false);
                    _реестрыПоКлючу[реестр.Key] = идентификатор;
                    _реестрыПоКлючу[НомерРеестра(реестр.RegisterNumber)] = идентификатор;
                }
                else
                {
                    _реестрыПоКлючу[реестр.Key] = существующий.Id;
                    _реестрыПоКлючу[НомерРеестра(реестр.RegisterNumber)] = существующий.Id;
                }

                report.ПеренесеноВЦель("post_registers");
                УчитыватьПеренос(реестр, report, "post_registers");
            }
#pragma warning disable CA1031 // Ошибка одного реестра не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(реестр, ошибка, report, "post_registers");
            }
        }
    }

    /// <summary>Записывает документы отправлений исходной базы.</summary>
    private async Task ЗаписатьДокументыAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var документ in plan.MailDocuments)
        {
            try
            {
                if (!_отправленияПоКлючу.TryGetValue(документ.MailKey, out var отправление))
                {
                    report.ПропущеноЗаписи(документ.SourceTable);
                    report.Предупреждение(
                        $"Документ «{документ.DocumentNumber}» не перенесён: отправление исходной базы не найдено.");
                    continue;
                }

                await _документы.AddAsync(
                    new MailDocument { MailId = отправление, DocNumber = документ.DocumentNumber },
                    cancellationToken).ConfigureAwait(false);

                report.ПеренесеноВЦель("mail_documents");
                УчитыватьПеренос(документ, report, "mail_documents");
            }
#pragma warning disable CA1031 // Ошибка одного документа не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(документ, ошибка, report, "mail_documents");
            }
        }
    }

    /// <summary>Записывает движения документов исходной базы.</summary>
    private async Task ЗаписатьДвиженияAsync(
        LegacyMigrationPlan plan,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        foreach (var движение in plan.DocumentMovements)
        {
            try
            {
                if (!_отправленияПоКлючу.TryGetValue(движение.MailKey, out var отправление))
                {
                    report.ПропущеноЗаписи(движение.SourceTable);
                    report.Предупреждение(
                        $"Движение документа не перенесено: отправление исходной базы не найдено ({движение.MailKey}).");
                    continue;
                }

                await _движения.AddAsync(
                    new DocumentMovement
                    {
                        MailId = отправление,
                        MovedAt = движение.MovedAt == default ? DateTime.UtcNow : движение.MovedAt,
                        TargetDepartment = движение.TargetDepartment,
                        Executor = движение.Executor,
                        Action = движение.Action,
                    },
                    cancellationToken).ConfigureAwait(false);

                report.ПеренесеноВЦель("document_movements");
                УчитыватьПеренос(движение, report, "document_movements");
            }
#pragma warning disable CA1031 // Ошибка одного движения не прерывает перенос остальных.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                ОшибкаПереноса(движение, ошибка, report, "document_movements");
            }
        }
    }

    /// <summary>
    /// Сохраняет идентификатор текущего отправителя (ТЗ §8.1, функция 16) —
    /// он переносится из исходной базы вместе с флагом отправителя.
    /// </summary>
    private async Task ЗаписатьТекущегоОтправителяAsync(
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        if (_текущийОтправитель is null)
        {
            return;
        }

        try
        {
            var значение = _текущийОтправитель.Value.ToString(CultureInfo.InvariantCulture);
            var существующий = await _параметры
                .GetByNameAsync(LegacyDataMapper.КлючТекущегоОтправителя, cancellationToken)
                .ConfigureAwait(false);

            if (существующий is null)
            {
                await _параметры.AddAsync(
                    new Parameter { Name = LegacyDataMapper.КлючТекущегоОтправителя, Value = значение },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                существующий.Value = значение;

                await _параметры.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
            }

            report.Предупреждение(
                $"Текущий отправитель перенесён: идентификатор {значение} сохранён в параметре "
                + $"«{LegacyDataMapper.КлючТекущегоОтправителя}».");
        }
#pragma warning disable CA1031 // Ошибка сохранения параметра не прерывает перенос.
        catch (Exception ошибка)
#pragma warning restore CA1031
        {
            report.Предупреждение(
                $"Текущий отправитель не сохранён: {ошибка.Message}");
        }
    }

    /// <summary>Ищет контрагента по ключу слияния функции 74.</summary>
    private async Task<Counterparty?> НайтиПоКлючуAsync(
        string? inn,
        string name,
        string? zip,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var найденный = await _контрагенты
            .FindByMergeKeyAsync(inn, name, zip, cancellationToken)
            .ConfigureAwait(false);

        if (найденный is not null)
        {
            return найденный;
        }

        var нормализованное = НормализоватьКлюч(name);

        if (нормализованное.Length == 0 || string.Equals(нормализованное, name, StringComparison.Ordinal))
        {
            return null;
        }

        return await _контрагенты
            .FindByMergeKeyAsync(inn, нормализованное, zip, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Нормализует часть ключа слияния: обрезка краёв, схлопывание пробелов,
    /// верхний регистр, удаление кавычек (ТЗ §8.5, функция 74).
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <returns>Нормализованное значение ключа.</returns>
    private static string НормализоватьКлюч(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var символы = value
            .Where(символ => символ is not ('"' or '«' or '»' or '„'))
            .Select(char.ToUpperInvariant)
            .ToArray();

        var части = new string(символы).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', части);
    }

    /// <summary>Создаёт контрагента целевой модели по записи исходной базы.</summary>
    /// <param name="record">Запись исходной базы.</param>
    /// <param name="source">Источник данных для колонки <c>source</c>.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <returns>Готовый контрагент.</returns>
    private static Counterparty СоздатьКонтрагента(
        LegacyCounterpartyRecord record,
        string source,
        long? groupId)
    {
        var контрагент = new Counterparty
        {
            Type = record.Type,
            Name = record.Name,
            ShortName = record.ShortName,
            Inn = record.Inn,
            Kpp = record.Kpp,
            Ogrn = record.Ogrn,
            Okfs = record.Okfs,
            Okpo = record.Okpo,
            Okved = record.Okved,
            Bik = record.Bik,
            Ks = record.Ks,
            Rs = record.Rs,
            Rkc = record.Rkc,
            BankName = record.BankName,
            PassportSeries = record.PassportSeries,
            PassportNumber = record.PassportNumber,
            PassportIssuedBy = record.PassportIssuedBy,
            PassportIssuedDate = record.PassportIssuedDate,
            BirthDate = record.BirthDate,
            BirthPlace = record.BirthPlace,
            Position = record.Position,
            DirectorFio = record.DirectorFio,
            Email = record.Email,
            Phone = record.Phone,
            SpecialNotes = record.SpecialNotes,
            Logo = record.Logo,
            GroupId = groupId,
            IsVip = record.IsVip,
            IsSender = record.IsSender,
            Source = source,
        };

        контрагент.SetAddress(AddressMainType.Legal, record.LegalAddress, false);
        контрагент.SetAddress(AddressMainType.Postal, record.PostalAddress, false);
        контрагент.SetAddress(AddressMainType.Actual, record.ActualAddress, false);
        контрагент.AddressMainType = record.MainAddressType;

        return контрагент;
    }

    /// <summary>
    /// Сливает запись исходной базы с существующим контрагентом по политике
    /// слияния (ТЗ §8.5, функция 74).
    /// </summary>
    /// <param name="existing">Существующий контрагент.</param>
    /// <param name="record">Запись исходной базы.</param>
    /// <param name="source">Источник данных.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    private static void СлитьКонтрагента(
        Counterparty existing,
        LegacyCounterpartyRecord record,
        string source,
        long? groupId,
        bool перезаписать)
    {
        existing.ShortName = Значение(existing.ShortName, record.ShortName, перезаписать);
        existing.Inn = Значение(existing.Inn, record.Inn, перезаписать);
        existing.Kpp = Значение(existing.Kpp, record.Kpp, перезаписать);
        existing.Ogrn = Значение(existing.Ogrn, record.Ogrn, перезаписать);
        existing.Okfs = Значение(existing.Okfs, record.Okfs, перезаписать);
        existing.Okpo = Значение(existing.Okpo, record.Okpo, перезаписать);
        existing.Okved = Значение(existing.Okved, record.Okved, перезаписать);
        existing.Bik = Значение(existing.Bik, record.Bik, перезаписать);
        existing.Ks = Значение(existing.Ks, record.Ks, перезаписать);
        existing.Rs = Значение(existing.Rs, record.Rs, перезаписать);
        existing.Rkc = Значение(existing.Rkc, record.Rkc, перезаписать);
        existing.BankName = Значение(existing.BankName, record.BankName, перезаписать);
        existing.PassportSeries = Значение(existing.PassportSeries, record.PassportSeries, перезаписать);
        existing.PassportNumber = Значение(existing.PassportNumber, record.PassportNumber, перезаписать);
        existing.PassportIssuedBy = Значение(existing.PassportIssuedBy, record.PassportIssuedBy, перезаписать);
        existing.PassportIssuedDate = Значение(existing.PassportIssuedDate, record.PassportIssuedDate, перезаписать);
        existing.BirthDate = Значение(existing.BirthDate, record.BirthDate, перезаписать);
        existing.BirthPlace = Значение(existing.BirthPlace, record.BirthPlace, перезаписать);
        existing.Position = Значение(existing.Position, record.Position, перезаписать);
        existing.DirectorFio = Значение(existing.DirectorFio, record.DirectorFio, перезаписать);
        existing.Email = Значение(existing.Email, record.Email, перезаписать);
        existing.Phone = Значение(existing.Phone, record.Phone, перезаписать);
        existing.SpecialNotes = Значение(existing.SpecialNotes, record.SpecialNotes, перезаписать);

        if (existing.GroupId is null && groupId is not null)
        {
            existing.GroupId = groupId;
        }

        if (record.IsVip)
        {
            existing.IsVip = true;
        }

        if (record.IsSender)
        {
            existing.IsSender = true;
        }

        if (existing.Logo is null || (перезаписать && record.Logo is not null))
        {
            existing.Logo = record.Logo;
        }

        if (existing.Source is null)
        {
            existing.Source = source;
        }

        СлитьАдрес(existing, AddressMainType.Legal, record.LegalAddress, перезаписать);
        СлитьАдрес(existing, AddressMainType.Postal, record.PostalAddress, перезаписать);
        СлитьАдрес(existing, AddressMainType.Actual, record.ActualAddress, перезаписать);
    }

    /// <summary>Сливает адресный срез контрагента.</summary>
    private static void СлитьАдрес(
        Counterparty existing,
        AddressMainType type,
        Address? address,
        bool перезаписать)
    {
        if (address is null)
        {
            return;
        }

        var текущий = existing.GetAddress(type);

        if (текущий is null || текущий.IsEmpty || перезаписать)
        {
            existing.SetAddress(type, address, false);
        }
    }

    /// <summary>Выбирает значение при слиянии записей.</summary>
    /// <param name="существующее">Значение существующей записи.</param>
    /// <param name="импортное">Значение импортируемой записи.</param>
    /// <param name="перезаписать">Перезаписывать непустые значения.</param>
    /// <returns>Значение, которое сохраняется в существующей записи.</returns>
    private static string? Значение(string? существующее, string? импортное, bool перезаписать)
    {
        if (импортное is null)
        {
            return существующее;
        }

        if (!перезаписать)
        {
            return существующее ?? импортное;
        }

        return импортное;
    }

    /// <summary>Выбирает значение при слиянии записей (значения типовых типов).</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="существующее">Значение существующей записи.</param>
    /// <param name="импортное">Значение импортируемой записи.</param>
    /// <param name="перезаписать">Перезаписывать непустые значения.</param>
    /// <returns>Значение, которое сохраняется в существующей записи.</returns>
    private static T? Значение<T>(T? существующее, T? импортное, bool перезаписать)
        where T : struct
    {
        if (импортное is null)
        {
            return существующее;
        }

        if (!перезаписать)
        {
            return существующее ?? импортное;
        }

        return импортное;
    }

    /// <summary>Определяет получателя отправления.</summary>
    private async Task<long?> РазрешитьПолучателяAsync(
        LegacyMailRecord mail,
        MigrationReportBuilder report,
        CancellationToken cancellationToken)
    {
        if (mail.CounterpartyKey is not null
            && _контрагентыПоКлючу.TryGetValue(mail.CounterpartyKey, out var идентификатор))
        {
            return идентификатор;
        }

        if (string.IsNullOrWhiteSpace(mail.RecipientName))
        {
            return null;
        }

        var имя = mail.RecipientName.Trim();
        var индекс = mail.RecipientAddress?.Zip;

        var существующий = await НайтиПоКлючуAsync(null, имя, индекс, cancellationToken).ConfigureAwait(false);

        if (существующий is not null)
        {
            return существующий.Id;
        }

        var новый = new Counterparty
        {
            Name = имя,
            Source = "Импорт (получатель по адресу отправления)",
        };

        новый.SetAddress(AddressMainType.Postal, mail.RecipientAddress, false);

        var новыйИдентификатор = await _контрагенты.AddAsync(новый, cancellationToken).ConfigureAwait(false);
        report.ПеренесеноВЦель("counterparties");
        report.Предупреждение(
            $"Создан контрагент «{имя}» по адресу отправления: в исходной базе отдельной записи нет.");

        return новыйИдентификатор;
    }

    /// <summary>Определяет отправителя отправления.</summary>
    private async Task<long> РазрешитьОтправителяAsync(
        LegacyMailRecord mail,
        long получатель,
        CancellationToken cancellationToken)
    {
        if (mail.SenderKey is not null
            && _контрагентыПоКлючу.TryGetValue(mail.SenderKey, out var идентификатор))
        {
            return идентификатор;
        }

        if (_текущийОтправитель is not null)
        {
            return _текущийОтправитель.Value;
        }

        if (!string.IsNullOrWhiteSpace(mail.SenderName))
        {
            var существующий = await НайтиПоКлючуAsync(null, mail.SenderName.Trim(), null, cancellationToken)
                .ConfigureAwait(false);

            if (существующий is not null)
            {
                return существующий.Id;
            }
        }

        return получатель;
    }

    /// <summary>Определяет почтовый реестр отправления.</summary>
    private long? РазрешитьРеестрAsync(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return _реестрыПоКлючу.TryGetValue(key, out var идентификатор) ? идентификатор : null;
    }

    /// <summary>Определяет рассылку отправления.</summary>
    private long? РазрешитьРассылкуAsync(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return _рассылкиПоКлючу.TryGetValue(key, out var идентификатор) ? идентификатор : null;
    }

    /// <summary>Определяет опись вложений отправления.</summary>
    private long? РазрешитьОписьAsync(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return _описиПоКлючу.TryGetValue(key, out var идентификатор) ? идентификатор : null;
    }

    /// <summary>Возвращает индекс для ключа слияния контрагента.</summary>
    /// <param name="record">Запись исходной базы.</param>
    /// <returns>Почтовый индекс либо <see langword="null"/>.</returns>
    private static string? ИндексСлияния(LegacyCounterpartyRecord record)
        => record.PostalAddress?.Zip ?? record.LegalAddress?.Zip;

    /// <summary>Возвращает идентификатор группы по её наименованию.</summary>
    /// <param name="name">Наименование группы.</param>
    /// <returns>Идентификатор группы либо <see langword="null"/>.</returns>
    private long? ГруппаПоИмени(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _группыПоИмени.TryGetValue(name.Trim(), out var идентификатор) ? идентификатор : null;
    }

    /// <summary>Учитывает перенос исходной записи в счётчиках отчёта.</summary>
    /// <param name="record">Запись исходной базы.</param>
    /// <param name="report">Сборщик отчёта.</param>
    /// <param name="targetTable">Целевая таблица.</param>
    private void УчитыватьПеренос(ILegacySourceRecord record, MigrationReportBuilder report, string targetTable)
    {
        var ключ = КлючЗаписи(record);

        if (_перенесённые.Add(ключ))
        {
            report.ПеренесеноЗаписи(record.SourceTable);
        }
    }

    /// <summary>Фиксирует ошибку переноса записи.</summary>
    private void ОшибкаПереноса(
        ILegacySourceRecord record,
        Exception ошибка,
        MigrationReportBuilder report,
        string targetTable)
    {
        _ошибки.Add(КлючЗаписи(record));

        report.ОшибокЗаписи(record.SourceTable);
        report.Предупреждение(
            $"Таблица «{record.SourceTable}»: запись {ОписаниеЗаписи(record)} не перенесена — {ошибка.Message}");
    }

    /// <summary>
    /// Учитывает записи исходных таблиц, которые не были перенесены: они
    /// считаются пропущенными (ТЗ §8.9, функции 117 и 118).
    /// </summary>
    private void УчестьНеперенесённые(LegacyMigrationPlan plan, MigrationReportBuilder report)
    {
        foreach (var (таблица, ключи) in СобратьКлючи(plan))
        {
            foreach (var ключ in ключи)
            {
                var полныйКлюч = $"{таблица}|{ключ}";

                if (_перенесённые.Contains(полныйКлюч) || _ошибки.Contains(полныйКлюч))
                {
                    continue;
                }

                report.ПропущеноЗаписи(таблица);
            }
        }
    }

    /// <summary>Собирает ключи записей исходных таблиц, присутствующих в плане.</summary>
    private static Dictionary<string, HashSet<string>> СобратьКлючи(LegacyMigrationPlan plan)
    {
        var ключи = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Добавить(ILegacySourceRecord запись)
        {
            if (!ключи.TryGetValue(запись.SourceTable, out var множество))
            {
                множество = [];
                ключи[запись.SourceTable] = множество;
            }

            множество.Add(запись.SourceKey);
        }

        foreach (var запись in plan.Counterparties) Добавить(запись);
        foreach (var запись in plan.CounterpartyFields) Добавить(запись);
        foreach (var запись in plan.Mails) Добавить(запись);
        foreach (var запись in plan.PrintHistories) Добавить(запись);
        foreach (var запись in plan.Groups) Добавить(запись);
        foreach (var запись in plan.PrintTemplates) Добавить(запись);
        foreach (var запись in plan.Parameters) Добавить(запись);
        foreach (var запись in plan.QrCaches) Добавить(запись);
        foreach (var запись in plan.Inventories) Добавить(запись);
        foreach (var запись in plan.InventoryItems) Добавить(запись);
        foreach (var запись in plan.Distributions) Добавить(запись);
        foreach (var запись in plan.PostRegisters) Добавить(запись);
        foreach (var запись in plan.MailDocuments) Добавить(запись);
        foreach (var запись in plan.DocumentMovements) Добавить(запись);

        return ключи;
    }

    /// <summary>Формирует ключ записи для учёта переноса.</summary>
    private static string КлючЗаписи(ILegacySourceRecord record) => $"{record.SourceTable}|{record.SourceKey}";

    /// <summary>Формирует ключ отправления для связей внутри плана.</summary>
    private static string КлючОтправления(LegacyMailRecord mail) => $"{mail.SourceTable}:{mail.SourceKey}";

    /// <summary>Формирует ключ реестра по его номеру.</summary>
    private static string НомерРеестра(string number) => $"номер:{number}";

    /// <summary>Формирует текстовое описание записи для отчёта.</summary>
    private static string ОписаниеЗаписи(ILegacySourceRecord record)
        => string.IsNullOrWhiteSpace(record.SourceKey) ? "без идентификатора" : record.SourceKey;
}
