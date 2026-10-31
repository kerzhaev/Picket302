namespace Picket302.Printing.Elements;

/// <summary>
/// Вид отметки о классификации отправления (ТЗ §8.1, функция 26: «Отметки
/// „Заказное“, „Заказное с уведомлением“ — печатаются на бланке»).
/// </summary>
public enum ServiceStampKind
{
    /// <summary>Простое отправление — отметка не печатается.</summary>
    None = 0,

    /// <summary>«Заказное».</summary>
    Registered = 1,

    /// <summary>«Заказное с уведомлением».</summary>
    RegisteredWithNotification = 2,
}

/// <summary>
/// Отрисовка отметок «Заказное» / «С уведомлением» на бланке конверта
/// (ТЗ §8.1, функция 26; задание этапа: рамка 10 мм, кегль 12pt, полужирный).
/// </summary>
public sealed class ServiceStampRenderer
{
    /// <summary>Сторона рамки отметки, мм.</summary>
    public const decimal FrameSizeMm = 10m;

    /// <summary>Кегль надписи отметки, пунктов.</summary>
    public const decimal FontSizePt = 12m;

    /// <summary>Гарнитура надписи отметки.</summary>
    public const string FontName = "Tahoma";

    /// <summary>Текст отметки «Заказное».</summary>
    public const string RegisteredText = "Заказное";

    /// <summary>Текст отметки «С уведомлением».</summary>
    public const string NotificationText = "С уведомлением";

    /// <summary>
    /// Возвращает текст отметки по классификации отправления.
    /// </summary>
    /// <param name="isRegistered">Признак «заказное».</param>
    /// <param name="needNotification">Признак «нужно уведомление».</param>
    /// <returns>Текст отметки либо <see langword="null"/>, если отметка не печатается.</returns>
    public static string? ResolveText(bool isRegistered, bool needNotification)
    {
        if (!isRegistered)
        {
            return null;
        }

        return needNotification ? NotificationText : RegisteredText;
    }

    /// <summary>
    /// Отрисовывает отметку: надпись 12pt bold в рамке 10×10 мм.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="stamp">Вид отметки.</param>
    /// <param name="xMm">Положение рамки по горизонтали, мм.</param>
    /// <param name="yMm">Положение рамки по вертикали, мм.</param>
    public void Render(IGraphicsContext context, ServiceStampKind stamp, float xMm, float yMm)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (stamp == ServiceStampKind.None)
        {
            return;
        }

        var текст = stamp == ServiceStampKind.RegisteredWithNotification
            ? NotificationText
            : RegisteredText;

        var сторона = (float)FrameSizeMm;

        context.DrawRectangleMm(xMm, yMm, сторона, сторона);
        context.DrawStringMm(
            текст,
            FontName,
            (float)FontSizePt,
            bold: true,
            xMm,
            yMm);
    }

    /// <summary>
    /// Отрисовывает отметку по классификации отправления.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="isRegistered">Признак «заказное» (mails.mail_category / notice).</param>
    /// <param name="needNotification">Признак «нужно уведомление» (mails.need_notification).</param>
    /// <param name="xMm">Положение рамки по горизонтали, мм.</param>
    /// <param name="yMm">Положение рамки по вертикали, мм.</param>
    public void Render(
        IGraphicsContext context,
        bool isRegistered,
        bool needNotification,
        float xMm,
        float yMm)
    {
        var вид = needNotification && isRegistered
            ? ServiceStampKind.RegisteredWithNotification
            : isRegistered
                ? ServiceStampKind.Registered
                : ServiceStampKind.None;

        Render(context, вид, xMm, yMm);
    }
}
