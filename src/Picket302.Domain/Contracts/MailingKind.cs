namespace Picket302.Domain.Contracts;

/// <summary>
/// Режим формирования рассылки (ТЗ §8.12, функция 155: «Формирование рассылки
/// по категориям»).
/// </summary>
public enum MailingKind
{
    /// <summary>«Рассылка на категории» — выбор нескольких категорий.</summary>
    ByCategories = 0,

    /// <summary>«Рассылка на категорию» — выбор одной категории.</summary>
    ByCategory = 1,
}
