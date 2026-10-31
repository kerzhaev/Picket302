namespace Picket302.Domain.Contracts;

/// <summary>
/// Область выборочной очистки базы (ТЗ §8.11, функция 137; §8.12, функция 153:
/// «Демо-база и очистка базы»).
/// </summary>
public enum ClearScope
{
    /// <summary>«Категории контрагентов» — справочник групп (функция 138).</summary>
    CounterpartyCategories = 0,

    /// <summary>«База рассылок» — рассылки и входящие в них письма.</summary>
    Distributions = 1,

    /// <summary>«База контрагентов» — карточки контрагентов и их поля.</summary>
    Counterparties = 2,

    /// <summary>«База отправленных писем» — письма и журнал напечатанных конвертов.</summary>
    SentMails = 3,
}
