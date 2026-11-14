namespace Picket302.Shared.Protocol;

/// <summary>
/// Вид общей сущности, для которой ведётся маркер версии.
/// </summary>
/// <remarks>
/// Маркер версии хранится в отдельной таблице службы
/// (<c>shared_row_versions</c>), а не в строках самих сущностей: так
/// существующие репозитории и локальный режим остаются неизменными, а
/// проверка версии работает только там, где она нужна.
/// </remarks>
public enum SharedEntityKind
{
    /// <summary>Контрагент.</summary>
    Counterparty = 0,

    /// <summary>Пользовательское поле контрагента.</summary>
    CounterpartyField = 1,

    /// <summary>Группа (категория) адресной книги.</summary>
    Group = 2,

    /// <summary>Папка отправлений.</summary>
    Folder = 3,

    /// <summary>Отправление.</summary>
    Mail = 4,

    /// <summary>Рассылка.</summary>
    Distribution = 5,

    /// <summary>Шаблон конверта или бланка.</summary>
    PrintTemplate = 6,

    /// <summary>Поле бланка.</summary>
    BlankField = 7,
}
