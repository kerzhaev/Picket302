namespace Picket302.Domain.Entities;

/// <summary>
/// Статус почтового отправления (ТЗ §3.1.3, колонка <c>mails.status</c>;
/// §8.1, функция 24 «Жизненный цикл отправления (4 даты)»).
/// </summary>
public enum MailStatus
{
    /// <summary>Черновик.</summary>
    Draft = 0,

    /// <summary>Напечатано.</summary>
    Printed = 1,

    /// <summary>Отправлено.</summary>
    Dispatched = 2,

    /// <summary>Подтверждено.</summary>
    Confirmed = 3,

    /// <summary>Отменено.</summary>
    Canceled = 4,
}

/// <summary>
/// Категория отправления (ТЗ §3.1.3, колонка <c>mails.mail_category</c>;
/// §8.1, функция 23).
/// </summary>
public enum MailCategory
{
    /// <summary>Простое.</summary>
    Simple = 0,

    /// <summary>Заказное.</summary>
    Registered = 1,

    /// <summary>С объявленной ценностью.</summary>
    DeclaredValue = 2,

    /// <summary>1-й класс.</summary>
    FirstClass = 3,
}

/// <summary>
/// Вид рассылки (ТЗ §3.1.10, колонка <c>distributions.kind</c>).
/// </summary>
public enum DistributionKind
{
    /// <summary>Один ко многим.</summary>
    OneToMany = 0,

    /// <summary>Многие к одному.</summary>
    ManyToOne = 1,
}

/// <summary>
/// Состояние рассылки (ТЗ §3.1.10, колонка <c>distributions.state</c>).
/// </summary>
public enum DistributionState
{
    /// <summary>Черновик.</summary>
    Draft = 0,

    /// <summary>Сохранена.</summary>
    Saved = 1,

    /// <summary>Отправлена.</summary>
    Dispatched = 2,
}

/// <summary>
/// Действие с входящим документом (ТЗ §3.1.14, колонка <c>document_movements.action</c>).
/// </summary>
public enum DocumentMovementAction
{
    /// <summary>Приём.</summary>
    Reception = 0,

    /// <summary>Передача на исполнение.</summary>
    Execution = 1,

    /// <summary>В архив.</summary>
    Archived = 2,
}
