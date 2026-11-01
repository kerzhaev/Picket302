using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Ворота лицензии поверх автомата состояний (ТЗ §8.10, функция 119).
/// </summary>
/// <remarks>
/// <para>
/// Реализация переводит состояние автомата в решение по операции: пока
/// действует лицензия либо не исчерпан лимит пробного периода, операция
/// разрешена; истёкший срок или исчерпанный лимит объёма дают запрет с
/// понятным сообщением и числом оставшихся дней.
/// </para>
/// <para>
/// Состояние пересчитывается на каждой проверке, поэтому изменение системной
/// даты или наполнение базы учитываются немедленно (ТЗ §8.10: «Перевод
/// системной даты за границу срока переводит приложение в режим истечения
/// без потери данных»).
/// </para>
/// </remarks>
public sealed class LicenseStateMachineGate : ILicenseGate
{
    private readonly LicenseStateMachine _автомат;

    /// <summary>Создаёт ворота лицензии поверх автомата состояний.</summary>
    /// <param name="автомат">Автомат состояний лицензии.</param>
    /// <exception cref="ArgumentNullException">Автомат не задан.</exception>
    public LicenseStateMachineGate(LicenseStateMachine автомат)
    {
        ArgumentNullException.ThrowIfNull(автомат);

        _автомат = автомат;
    }

    /// <inheritdoc />
    public LicenseStatus ТекущийСтатус => _автомат.Status;

    /// <inheritdoc />
    public LicenseGateDecision Проверить(LicenseOperation операция)
    {
        var состояние = _автомат.EvaluateState();
        var статус = _автомат.Status;

        if (состояние is LicenseState.Licensed or LicenseState.Trial)
        {
            return LicenseGateDecision.Разрешить(состояние, статус.DaysRemaining);
        }

        var лимиты = _автомат.ТекущиеЛимиты;
        var объём = _автомат.ТекущийОбъём();

        var лимитИсчерпан =
            (лимиты.MaxCounterparties is int пределКонтрагентов && объём.Контрагенты >= пределКонтрагентов)
            || (лимиты.MaxDocuments is int пределДокументов && объём.Документы >= пределДокументов);

        var сообщение = лимитИсчерпан
            ? $"Исчерпан лимит пробного периода: контрагентов {объём.Контрагенты} "
                + $"из {Предел(лимиты.MaxCounterparties)}, документов {объём.Документы} "
                + $"из {Предел(лимиты.MaxDocuments)}. {Описание(операция)} недоступно. Осталось дней: 0."
            : $"Пробный период закончился. {Описание(операция)} недоступно. Осталось дней: 0.";

        return LicenseGateDecision.Запретить(состояние, 0, сообщение);
    }

    /// <summary>
    /// Название блокируемой операции в сообщении оператору.
    /// </summary>
    /// <param name="операция">Операция.</param>
    /// <returns>Текст названия операции.</returns>
    private static string Описание(LicenseOperation операция) => операция switch
    {
        LicenseOperation.ПечатьПартий => "Печать партий",
        _ => "Создание записей",
    };

    /// <summary>
    /// Формирует текст предела для сообщения: число либо «без ограничения».
    /// </summary>
    /// <param name="предел">Предел лимита.</param>
    /// <returns>Текст предела.</returns>
    private static string Предел(int? предел) => предел is int значение
        ? значение.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "без ограничения";
}
