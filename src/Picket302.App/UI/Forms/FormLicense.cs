using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Licensing;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Окно «Регистрация программы»: ввод ключа, онлайн-активация, офлайн-файл
/// запроса и снятие ключа (ТЗ §8.10, функции 122 и 123).
/// </summary>
/// <remarks>
/// Окно показывает текущий идентификатор оборудования — по нему оператор
/// обращается в поддержку (ТЗ §8.10, функция 120: «UI: Страница
/// "Регистрация": отображение текущего MachineID для обращения в поддержку»).
/// </remarks>
public sealed class FormLicense : Form
{
    private readonly LicenseManager _менеджер;
    private readonly LicenseStateMachine _автомат;
    private readonly ActivationService _активация;
    private readonly MachineIdentity _машина;

    private readonly TextBox _полеКлюча = new();
    private readonly Label _сводка = new();
    private readonly Label _идентификатор = new();
    private readonly Button _активировать = new();
    private readonly Button _создатьЗапрос = new();
    private readonly Button _применитьОтвет = new();
    private readonly Button _снятьКлюч = new();
    private readonly Button _закрыть = new();

    /// <summary>
    /// Создаёт окно регистрации программы.
    /// </summary>
    /// <param name="менеджер">Менеджер лицензии.</param>
    /// <param name="автомат">Автомат состояний лицензии.</param>
    /// <param name="активация">Служба активации.</param>
    /// <param name="машина">Вычислитель идентификатора оборудования.</param>
    /// <exception cref="ArgumentNullException">Обязательный состав не задан.</exception>
    public FormLicense(
        LicenseManager менеджер,
        LicenseStateMachine автомат,
        ActivationService активация,
        MachineIdentity машина)
    {
        ArgumentNullException.ThrowIfNull(менеджер);
        ArgumentNullException.ThrowIfNull(автомат);
        ArgumentNullException.ThrowIfNull(активация);
        ArgumentNullException.ThrowIfNull(машина);

        _менеджер = менеджер;
        _автомат = автомат;
        _активация = активация;
        _машина = машина;

        Text = "Регистрация программы";
        Font = new Font("Tahoma", 8f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 300);

        ПостроитьИнтерфейс();

        ОбновитьСведения();
    }

    /// <summary>
    /// Идентификатор оборудования текущей машины — для обращения в поддержку.
    /// </summary>
    public string MachineIdText => _машина.GetMachineId(MachineIdVariant.Composite);

    /// <summary>
    /// Сводка состояния лицензии: редакция, срок истечения, владелец.
    /// </summary>
    public string StatusSummary => СобратьСводку(_автомат.Status);

    /// <summary>
    /// Введённый ключ активации.
    /// </summary>
    public string KeyText
    {
        get => _полеКлюча.Text;
        set => _полеКлюча.Text = value;
    }

    /// <summary>
    /// Строит элементы окна.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var надписьКлюча = new Label
        {
            Text = "Ключ:",
            Location = new Point(12, 14),
            Size = new Size(60, 20),
        };

        _полеКлюча.Location = new Point(76, 12);
        _полеКлюча.Size = new Size(432, 22);

        _сводка.Location = new Point(12, 42);
        _сводка.Size = new Size(496, 60);
        _сводка.TextAlign = ContentAlignment.TopLeft;

        _идентификатор.Location = new Point(12, 106);
        _идентификатор.Size = new Size(496, 40);
        _идентификатор.TextAlign = ContentAlignment.TopLeft;

        НастроитьКнопку(_активировать, "Активировать онлайн", new Point(12, 156), АктивироватьОнлайнAsync);
        НастроитьКнопку(_создатьЗапрос, "Создать файл запроса", new Point(180, 156), СоздатьФайлЗапроса);
        НастроитьКнопку(_применитьОтвет, "Применить файл ответа", new Point(348, 156), ПрименитьФайлОтвета);

        НастроитьКнопку(_снятьКлюч, "Снять ключ", new Point(12, 196), СнятьКлюч);

        _закрыть.Text = "Закрыть";
        _закрыть.Size = new Size(110, 26);
        _закрыть.Location = new Point(398, 196);
        _закрыть.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(
        [
            надписьКлюча,
            _полеКлюча,
            _сводка,
            _идентификатор,
            _активировать,
            _создатьЗапрос,
            _применитьОтвет,
            _снятьКлюч,
            _закрыть,
        ]);

        CancelButton = _закрыть;
    }

    /// <summary>
    /// Настраивает кнопку окна.
    /// </summary>
    /// <param name="кнопка">Кнопка.</param>
    /// <param name="текст">Надпись кнопки.</param>
    /// <param name="положение">Положение кнопки.</param>
    /// <param name="обработчик">Обработчик нажатия.</param>
    private static void НастроитьКнопку(Button кнопка, string текст, Point положение, Action обработчик)
    {
        кнопка.Text = текст;
        кнопка.Size = new Size(160, 26);
        кнопка.Location = положение;
        кнопка.Click += (_, _) => обработчик();
    }

    /// <summary>
    /// Обновляет сведения о лицензии и идентификаторе оборудования.
    /// </summary>
    private void ОбновитьСведения()
    {
        _сводка.Text = StatusSummary;

        var идентификатор = new StringBuilder();

        идентификатор.AppendLine("Идентификатор оборудования (для обращения в поддержку):");
        идентификатор.Append(MachineIdText);

        _идентификатор.Text = идентификатор.ToString();
    }

    /// <summary>
    /// Формирует сводку состояния лицензии.
    /// </summary>
    /// <param name="статус">Статус лицензии.</param>
    /// <returns>Текст сводки.</returns>
    private static string СобратьСводку(LicenseStatus статус)
    {
        var сводка = new StringBuilder();

        сводка.AppendLine("Редакция: " + Редакции.Название(статус.Edition));
        сводка.AppendLine("Состояние: " + НазваниеСостояния(статус.State));
        сводка.Append("Действует до: " + СрокДействия(статус.ExpiresAtUtc));

        if (!string.IsNullOrWhiteSpace(статус.Owner))
        {
            сводка.AppendLine();
            сводка.Append("Владелец: " + статус.Owner);
        }

        if (!string.IsNullOrWhiteSpace(статус.Message))
        {
            сводка.AppendLine();
            сводка.Append(статус.Message);
        }

        return сводка.ToString();
    }

    /// <summary>
    /// Возвращает отображаемое название состояния лицензии.
    /// </summary>
    /// <param name="состояние">Состояние лицензии.</param>
    /// <returns>Название состояния.</returns>
    private static string НазваниеСостояния(LicenseState состояние) => состояние switch
    {
        LicenseState.Licensed => "лицензия получена",
        LicenseState.Expired => "пробный период закончился",
        _ => "пробный период",
    };

    /// <summary>
    /// Возвращает отображаемый срок действия лицензии.
    /// </summary>
    /// <param name="истечение">Момент истечения срока (UTC).</param>
    /// <returns>Текст срока.</returns>
    private static string СрокДействия(DateTime? истечение) => истечение is DateTime момент
        ? момент.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
        : "не ограничен";

    /// <summary>
    /// Активирует лицензию через сервер активации.
    /// </summary>
    private async void АктивироватьОнлайнAsync()
    {
        _активировать.Enabled = false;

        try
        {
            var результат = await _активация.ActivateOnlineAsync(_полеКлюча.Text).ConfigureAwait(true);

            ПоказатьРезультат(результат);
        }
        finally
        {
            _активировать.Enabled = true;

            ОбновитьСведения();
        }
    }

    /// <summary>
    /// Формирует офлайн-файл запроса на регистрацию.
    /// </summary>
    private void СоздатьФайлЗапроса()
    {
        try
        {
            _активация.Ключ = _полеКлюча.Text;

            var путь = _активация.BuildOfflineRequest();

            MessageBox.Show(
                this,
                "Файл запроса создан:" + Environment.NewLine + путь
                    + Environment.NewLine + Environment.NewLine
                    + "Передайте его в поддержку и дождитесь ответного файла активации.",
                "Регистрация программы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception исключение) when (исключение is InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                "Не удалось создать файл запроса: " + исключение.Message,
                "Регистрация программы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Применяет ответный файл активации.
    /// </summary>
    private void ПрименитьФайлОтвета()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Файл ответа активации",
            Filter = "Файлы ответа (*.json)|*.json|Все файлы (*.*)|*.*",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var результат = _активация.ApplyOfflineResponse(диалог.FileName);

        ПоказатьРезультат(результат);

        ОбновитьСведения();
    }

    /// <summary>
    /// Снимает ключ с подтверждением и предупреждением о переходе в пробный режим.
    /// </summary>
    private void СнятьКлюч()
    {
        var подтверждение = MessageBox.Show(
            this,
            "Снять ключ? Программа вернётся в пробный режим. Пользовательские данные сохранятся.",
            "Регистрация программы",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (подтверждение != DialogResult.Yes)
        {
            return;
        }

        _менеджер.RemoveKey();

        ОбновитьСведения();

        MessageBox.Show(
            this,
            "Ключ снят. Программа работает в пробном режиме.",
            "Регистрация программы",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Показывает результат операции активации.
    /// </summary>
    /// <param name="результат">Результат активации.</param>
    private void ПоказатьРезультат(ActivationResult результат)
    {
        if (результат.IsSuccess)
        {
            MessageBox.Show(
                this,
                "Активация выполнена. Действующая редакция: "
                    + Редакции.Название(результат.Status?.Edition ?? Edition.Free) + ".",
                "Регистрация программы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        MessageBox.Show(
            this,
            результат.Error ?? "Активация не выполнена.",
            "Регистрация программы",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

/// <summary>
/// Окно преимуществ редакции (ТЗ §8.10, функция 126: «При попытке
/// использовать возможность старшей редакции показывается окно преимуществ со
/// списком того, что даёт переход, а не сухое "недоступно"»).
/// </summary>
public sealed class FormEditionAdvantages : Form
{
    /// <summary>
    /// Создаёт окно преимуществ перехода в старшую редакцию.
    /// </summary>
    /// <param name="текущаяРедакция">Действующая редакция.</param>
    /// <param name="целеваяРедакция">Редакция, переход в которую рассматривается.</param>
    public FormEditionAdvantages(Edition текущаяРедакция, Edition целеваяРедакция)
    {
        ТекущаяРедакция = текущаяРедакция;
        ЦелеваяРедакция = целеваяРедакция;

        Advantages = Редакции.Описание(целеваяРедакция).Advantages;
        CurrentAdvantages = Редакции.Описание(текущаяРедакция).Advantages;

        MessageText = "Возможности редакции " + Редакции.Название(целеваяРедакция)
            + " недоступны в редакции " + Редакции.Название(текущаяРедакция)
            + ". Переход открывает:";

        Text = "Преимущества редакции " + Редакции.Название(целеваяРедакция);
        Font = new Font("Tahoma", 8f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 260);

        var заголовок = new Label
        {
            Text = MessageText,
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(436, 40),
        };

        var список = new ListBox
        {
            Location = new Point(12, 56),
            Size = new Size(436, 140),
            IntegralHeight = false,
        };

        foreach (var преимущество in Advantages)
        {
            список.Items.Add("— " + преимущество);
        }

        var кнопка = new Button
        {
            Text = "Закрыть",
            Size = new Size(96, 26),
            Location = new Point(352, 214),
            DialogResult = DialogResult.OK,
        };

        Controls.AddRange(new Control[] { заголовок, список, кнопка });

        AcceptButton = кнопка;
        CancelButton = кнопка;
    }

    /// <summary>Действующая редакция.</summary>
    public Edition ТекущаяРедакция { get; }

    /// <summary>Редакция, переход в которую рассматривается.</summary>
    public Edition ЦелеваяРедакция { get; }

    /// <summary>
    /// Перечень преимуществ целевой редакции.
    /// </summary>
    public IReadOnlyList<string> Advantages { get; }

    /// <summary>
    /// Перечень преимуществ действующей редакции.
    /// </summary>
    public IReadOnlyList<string> CurrentAdvantages { get; }

    /// <summary>
    /// Пояснение для оператора.
    /// </summary>
    public string MessageText { get; }
}
