namespace Picket302.Domain.Contracts;

/// <summary>
/// Вкладки главного окна приложения (ТЗ §8.12, функция 152: «Четыре вкладки
/// главного окна»).
/// </summary>
public enum ShellTab
{
    /// <summary>«Адресная книга» — таблица контрагентов, дерево категорий, поиск.</summary>
    AddressBook = 0,

    /// <summary>«Конверты» — каталог бланков.</summary>
    Envelopes = 1,

    /// <summary>«Рассылки» — список рассылок: новая, печать, сохранённые и отправленные.</summary>
    Distributions = 2,

    /// <summary>«Отправленные» — сведения об отправленных конвертах.</summary>
    Sent = 3,
}
