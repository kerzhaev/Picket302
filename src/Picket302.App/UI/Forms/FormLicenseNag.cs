using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.Licensing;

namespace Picket302.App.UI.Forms;

/// <summary>
/// NAG-окно при запуске в пробном режиме или по истечении срока
/// (ТЗ §8.10, функция 119: «Истёкший trial — NAG-окно «Пробный период
/// закончился», «Осталось дней: 0», кнопки «Купить сейчас» / «Ввести ключ»»).
/// </summary>
/// <remarks>
/// Сообщение и состав кнопок доступны и без запуска окна: текст формирует
/// доменный контракт <see cref="LicenseStatus.BuildNagMessage"/>.
/// </remarks>
public sealed class FormLicenseNag : Form
{
    /// <summary>Надпись кнопки перехода к покупке.</summary>
    public const string КнопкаПокупки = "Купить сейчас";

    /// <summary>Надпись кнопки ввода ключа.</summary>
    public const string КнопкаВводаКлюча = "Ввести ключ";

    private readonly Button _купить;
    private readonly Button _ввестиКлюч;

    /// <summary>
    /// Создаёт NAG-окно по статусу лицензии.
    /// </summary>
    /// <param name="статус">Статус лицензии.</param>
    /// <exception cref="ArgumentNullException">Статус не задан.</exception>
    public FormLicenseNag(LicenseStatus статус)
    {
        ArgumentNullException.ThrowIfNull(статус);

        MessageText = статус.BuildNagMessage();

        Text = "Пробный период программы";
        Font = new Font("Tahoma", 8f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(440, 150);

        var текст = new Label
        {
            Text = MessageText,
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(416, 64),
        };

        var пояснение = new Label
        {
            Text = "Чтение данных остаётся доступным. Создание записей и печать партий "
                + "возобновляются после ввода ключа.",
            AutoSize = false,
            Location = new Point(12, 76),
            Size = new Size(416, 32),
        };

        _купить = new Button
        {
            Text = КнопкаПокупки,
            Size = new Size(110, 28),
            Location = new Point(200, 112),
        };

        _купить.Click += (_, _) =>
        {
            ВыбранноеДействие = КнопкаПокупки;
            DialogResult = DialogResult.OK;
            Close();
        };

        _ввестиКлюч = new Button
        {
            Text = КнопкаВводаКлюча,
            Size = new Size(110, 28),
            Location = new Point(318, 112),
        };

        _ввестиКлюч.Click += (_, _) =>
        {
            ВыбранноеДействие = КнопкаВводаКлюча;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[] { текст, пояснение, _купить, _ввестиКлюч });

        AcceptButton = _ввестиКлюч;
        CancelButton = _ввестиКлюч;
    }

    /// <summary>
    /// Текст сообщения NAG-окна: состояние и число оставшихся дней.
    /// </summary>
    public string MessageText { get; }

    /// <summary>
    /// Состав кнопок NAG-окна.
    /// </summary>
    public IReadOnlyList<string> Buttons { get; } = [КнопкаПокупки, КнопкаВводаКлюча];

    /// <summary>
    /// Действие, выбранное оператором; <see langword="null"/> — окно закрыто
    /// без выбора.
    /// </summary>
    public string? ВыбранноеДействие { get; private set; }
}
