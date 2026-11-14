using Picket302.Domain.Entities;

namespace Picket302.Shared.Dtos;

/// <summary>
/// Шаблон конверта или бланка в общей базе.
/// </summary>
/// <remarks>
/// <see cref="FilePath"/> в ответе **не передаётся**: на сервере пакет шаблона
/// лежит в защищённом каталоге, и клиент получает его по идентификатору пакета
/// и версии.
/// </remarks>
/// <param name="Id">Идентификатор шаблона.</param>
/// <param name="Name">Наименование шаблона.</param>
/// <param name="Kind">Вид шаблона.</param>
/// <param name="Format">Формат.</param>
/// <param name="WidthMm">Ширина в миллиметрах.</param>
/// <param name="HeightMm">Высота в миллиметрах.</param>
/// <param name="Orientation">Ориентация.</param>
/// <param name="IsBuiltin">Признак встроенного шаблона поставки.</param>
/// <param name="Country">Страна.</param>
/// <param name="PackageId">
/// Идентификатор управляемого пакета шаблона на сервере; пусто, если шаблон
/// не связан с файловым пакетом.
/// </param>
/// <param name="PackageVersion">
/// Версия пакета шаблона: клиент запрашивает именно эту версию, чтобы
/// предпросмотр и печать шли по одному и тому же содержимому.
/// </param>
/// <param name="ContentHash">
/// SHA-256 содержимого пакета в виде шестнадцатеричной строки; пусто, если
/// пакета нет.
/// </param>
/// <param name="Version">Маркер версии строки шаблона.</param>
public sealed record PrintTemplateDto(
    long Id,
    string Name,
    string Kind,
    string Format,
    decimal WidthMm,
    decimal HeightMm,
    TemplateOrientation Orientation,
    bool IsBuiltin,
    string Country,
    string? PackageId,
    long PackageVersion,
    string? ContentHash,
    long Version);

/// <summary>
/// Поле бланка в общей базе.
/// </summary>
/// <param name="Id">Идентификатор поля.</param>
/// <param name="TemplateId">Шаблон, которому принадлежит поле.</param>
/// <param name="Name">Имя поля.</param>
/// <param name="Kind">Вид поля.</param>
/// <param name="Page">Номер страницы.</param>
/// <param name="XMm">Смещение по горизонтали.</param>
/// <param name="YMm">Смещение по вертикали.</param>
/// <param name="LengthMm">Длина.</param>
/// <param name="HeightMm">Высота.</param>
/// <param name="FontName">Имя шрифта.</param>
/// <param name="FontHeightPt">Кегль в пунктах.</param>
/// <param name="FontHeightMinPt">Минимальный кегль.</param>
/// <param name="FontHeightMaxPt">Максимальный кегль.</param>
/// <param name="IsBold">Признак полужирного начертания.</param>
/// <param name="IsItalic">Признак курсивного начертания.</param>
/// <param name="ColorRgb">Цвет в формате RGB.</param>
/// <param name="Align">Выравнивание.</param>
/// <param name="MaxChars">Предельное число знаков.</param>
/// <param name="IsVisible">Признак видимости.</param>
/// <param name="IsPrintable">Признак печати.</param>
/// <param name="ZoneRole">Роль зоны.</param>
/// <param name="ClipartId">Идентификатор клипарта.</param>
/// <param name="Version">Маркер версии поля.</param>
public sealed record BlankFieldDto(
    long Id,
    long TemplateId,
    string Name,
    string Kind,
    int Page,
    decimal XMm,
    decimal YMm,
    decimal LengthMm,
    decimal HeightMm,
    string FontName,
    decimal FontHeightPt,
    decimal FontHeightMinPt,
    decimal FontHeightMaxPt,
    bool IsBold,
    bool IsItalic,
    int ColorRgb,
    FieldAlignment Align,
    int MaxChars,
    bool IsVisible,
    bool IsPrintable,
    string? ZoneRole,
    string? ClipartId,
    long Version);

/// <summary>
/// Реестр печатных сессий в общей базе.
/// </summary>
/// <param name="Id">Идентификатор сессии печати.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
/// <param name="TemplateId">Использованный шаблон.</param>
/// <param name="PrinterName">Имя принтера рабочего места.</param>
/// <param name="PaperSizeName">Наименование формата бумаги.</param>
/// <param name="PaperWidthMm">Ширина бумаги.</param>
/// <param name="PaperHeightMm">Высота бумаги.</param>
/// <param name="FontInfo">Сведения о шрифте.</param>
/// <param name="Copies">Число экземпляров.</param>
/// <param name="PrintMode">Режим печати.</param>
/// <param name="Status">Статус печатной операции.</param>
/// <param name="ErrorText">Текст ошибки.</param>
/// <param name="DeviceId">Устройство-клиент, выполнившее печать.</param>
/// <remarks>
/// Снимок настроек принтера (<c>DevmodeSnapshot</c>) в общий журнал не
/// передаётся: это содержимое памяти драйвера принтера конкретного рабочего
/// места, оно не имеет смысла на другом компьютере и может содержать данные
/// о принтере. Снимок остаётся в локальном журнале рабочего места.
/// </remarks>
public sealed record PrintJobDto(
    long Id,
    DateTime CreatedAt,
    long? TemplateId,
    string PrinterName,
    string PaperSizeName,
    decimal PaperWidthMm,
    decimal PaperHeightMm,
    string? FontInfo,
    int Copies,
    string PrintMode,
    string Status,
    string? ErrorText,
    string? DeviceId);

/// <summary>
/// Запись журнала печати в общей базе.
/// </summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="PrintedAt">Момент печати (UTC).</param>
/// <param name="MailId">Отправление.</param>
/// <param name="RecipientAddressSnapshot">Снимок адреса получателя.</param>
/// <param name="SenderAddressSnapshot">Снимок адреса отправителя.</param>
/// <param name="CopyCount">Число экземпляров.</param>
/// <param name="IsVip">Признак «избранный адрес».</param>
/// <param name="Note">Примечание.</param>
/// <param name="TemplateDescription">Описание использованного шаблона.</param>
/// <param name="PrintJobId">Реестр печатных сессий.</param>
/// <param name="DeviceId">Устройство-клиент, выполнившее печать.</param>
/// <param name="Outcome">
/// Исход печатной операции: «Prepared», «Spooled», «Error» или «Unknown».
/// Пусто — исход определялся самой программой (одиночный режим).
/// </param>
public sealed record PrintHistoryDto(
    long Id,
    DateTime PrintedAt,
    long? MailId,
    string RecipientAddressSnapshot,
    string SenderAddressSnapshot,
    int CopyCount,
    bool IsVip,
    string? Note,
    string TemplateDescription,
    long? PrintJobId,
    string? DeviceId,
    string? Outcome = null);

/// <summary>
/// Данные отправления для печати.
/// </summary>
/// <param name="Id">Идентификатор отправления.</param>
/// <param name="Values">
/// Значения полей по тегам шаблона; набор формируется службой из общей базы.
/// </param>
/// <param name="LogoBytes">Логотип получателя.</param>
public sealed record PrintableItemDto(
    long Id,
    IReadOnlyDictionary<string, string> Values,
    byte[]? LogoBytes);

/// <summary>
/// Метаданные управляемого пакета шаблона на сервере.
/// </summary>
/// <param name="PackageId">Идентификатор пакета.</param>
/// <param name="Name">Наименование пакета.</param>
/// <param name="Version">Текущая версия пакета.</param>
/// <param name="ContentHash">SHA-256 содержимого текущей версии.</param>
/// <param name="SizeBytes">Размер содержимого в байтах.</param>
/// <param name="IsBuiltin">Признак встроенного пакета поставки.</param>
/// <param name="UpdatedAt">Момент последнего изменения (UTC).</param>
public sealed record TemplatePackageDto(
    string PackageId,
    string Name,
    long Version,
    string ContentHash,
    long SizeBytes,
    bool IsBuiltin,
    DateTime UpdatedAt);
