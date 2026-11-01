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
/// Вид уведомления о вручении (ТЗ §8.1, функция 32: «Выбор вида уведомления
/// о вручении»).
/// </summary>
public enum NotificationType
{
    /// <summary>Уведомление не требуется.</summary>
    None = 0,

    /// <summary>Бланк уведомления ф.119 (РФ).</summary>
    Form119 = 1,

    /// <summary>Национальная форма уведомления.</summary>
    National = 2,

    /// <summary>Совмещённый перевод бланка ф.112эп.</summary>
    Form112Ep = 3,
}

/// <summary>
/// Соответствие между видами уведомления и их записью в колонке
/// <c>mails.notice_type</c> (ТЗ §3.1.3; §8.1, функция 32).
/// </summary>
public static class NotificationTypeMapper
{
    /// <summary>Возвращает ключ вида уведомления для записи в базу данных.</summary>
    /// <param name="type">Вид уведомления.</param>
    /// <returns>Ключ вида уведомления.</returns>
    public static string Ключ(this NotificationType type) =>
        type switch
        {
            NotificationType.Form119 => "Form119",
            NotificationType.National => "National",
            NotificationType.Form112Ep => "Form112Ep",
            _ => string.Empty,
        };

    /// <summary>
    /// Определяет вид уведомления по значению колонки <c>mails.notice_type</c>.
    /// </summary>
    /// <param name="значение">Сохранённое значение вида уведомления.</param>
    /// <returns>Вид уведомления; при неизвестном значении — <see cref="NotificationType.None"/>.</returns>
    public static NotificationType Разобрать(string? значение)
    {
        if (string.IsNullOrWhiteSpace(значение))
        {
            return NotificationType.None;
        }

        return значение.Trim().ToLowerInvariant() switch
        {
            "form119" or "119" or "ф.119" or "ф119" => NotificationType.Form119,
            "national" or "национальная" or "нац" => NotificationType.National,
            "form112ep" or "112ep" or "112эп" or "ф.112эп" => NotificationType.Form112Ep,
            _ => NotificationType.None,
        };
    }

    /// <summary>Возвращает название вида уведомления для интерфейса оператора.</summary>
    /// <param name="type">Вид уведомления.</param>
    /// <returns>Название вида уведомления.</returns>
    public static string Название(this NotificationType type) =>
        type switch
        {
            NotificationType.Form119 => "ф.119 (РФ)",
            NotificationType.National => "Национальная форма",
            NotificationType.Form112Ep => "ф.112эп (совмещённый перевод)",
            _ => "Не требуется",
        };
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
