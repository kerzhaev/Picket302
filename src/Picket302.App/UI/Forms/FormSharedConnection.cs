using Picket302.App.Shared;
using Picket302.Infrastructure.Logging;
using Picket302.Shared.Protocol;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог «Подключение к общей базе» (ТЗ совместного режима: клиент указывает
/// адрес сервера и порт, а также одноразовый код сопряжения).
/// </summary>
/// <remarks>
/// <para>
/// Код сопряжения показывает администратор серверной службы на серверном
/// компьютере. Клиент сверяет фрагмент отпечатка сервера, который входит в
/// код, с сертификатом, который служба предъявляет при TLS-рукопожатии:
/// секрет устройства не передаётся постороннему и проверка сертификата не
/// отключается.
/// </para>
/// <para>
/// После сопряжения реквизиты сохраняются в профиле рабочего места, а секрет
/// устройства — в защищённое хранилище Windows. Ввод пароля здесь не нужен:
/// учётных записей пользователей в общем режиме нет.
/// </para>
/// </remarks>
public sealed class FormSharedConnection : Form
{
    private readonly TextBox _адрес = new();
    private readonly NumericUpDown _порт = new()
    {
        Minimum = 1,
        Maximum = 65535,
        Value = SharedProtocol.DefaultPort,
    };
    private readonly TextBox _код = new();
    private readonly TextBox _имяУстройства = new() { Text = Environment.MachineName };
    private readonly Label _состояние = new() { AutoSize = true };
    private readonly Button _подключить = new() { Text = "Подключиться" };
    private readonly Button _отмена = new() { Text = "Отмена", DialogResult = DialogResult.Cancel };

    private readonly SharedClientProfile _профиль;
    private readonly Action<string> _журнал;
    private readonly Action<string> _журналОшибки;

    /// <summary>
    /// Создаёт диалог подключения к общей базе.
    /// </summary>
    /// <param name="профиль">Профиль подключения к общей базе.</param>
    /// <param name="журнал">Приёмник информационных записей журнала.</param>
    /// <param name="журналОшибки">
    /// Приёмник записей о неудачном сопряжении; при <see langword="null"/> используется
    /// <paramref name="журнал"/>.
    /// </param>
    public FormSharedConnection(
        SharedClientProfile профиль,
        Action<string> журнал,
        Action<string>? журналОшибки = null)
    {
        ArgumentNullException.ThrowIfNull(профиль);
        ArgumentNullException.ThrowIfNull(журнал);

        _профиль = профиль;
        _журнал = журнал;
        _журналОшибки = журналОшибки ?? журнал;

        Text = "Подключение к общей базе";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(420, 220);

        ПостроитьИнтерфейс();
        ЗаполнитьСохранённыеРеквизиты();
    }

    /// <summary>
    /// Реквизиты подключения, сохранённые после сопряжения.
    /// </summary>
    public SharedClientSettings? Результат { get; private set; }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var таблица = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 2,
        };

        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ДобавитьСтроку(таблица, 0, "Адрес сервера:", _адрес);
        ДобавитьСтроку(таблица, 1, "Порт:", _порт);
        ДобавитьСтроку(таблица, 2, "Код сопряжения:", _код);
        ДобавитьСтроку(таблица, 3, "Имя этого компьютера:", _имяУстройства);
        ДобавитьСтроку(таблица, 4, "", _состояние);

        _подключить.Size = new Size(120, 28);
        _подключить.Location = new Point(180, 180);
        _подключить.Click += async (_, _) => await ПодключитьсяAsync().ConfigureAwait(true);

        _отмена.Size = new Size(90, 28);
        _отмена.Location = new Point(310, 180);

        Controls.Add(_подключить);
        Controls.Add(_отмена);
        Controls.Add(таблица);

        AcceptButton = _подключить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Добавляет строку формы в таблицу.
    /// </summary>
    private static void ДобавитьСтроку(TableLayoutPanel таблица, int строка, string текст, Control поле)
    {
        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var метка = new Label
        {
            Text = текст,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        поле.Dock = DockStyle.Fill;

        таблица.Controls.Add(метка, 0, строка);
        таблица.Controls.Add(поле, 1, строка);
    }

    /// <summary>
    /// Заполняет поля сохранёнными реквизитами.
    /// </summary>
    private void ЗаполнитьСохранённыеРеквизиты()
    {
        var реквизиты = _профиль.Прочитать();

        if (!string.IsNullOrWhiteSpace(реквизиты.Host))
        {
            _адрес.Text = реквизиты.Host;
            _порт.Value = реквизиты.Port is > 0 and <= 65535
                ? реквизиты.Port
                : SharedProtocol.DefaultPort;
        }
    }

    /// <summary>
    /// Сопрягает это устройство с серверной службой.
    /// </summary>
    private async Task ПодключитьсяAsync()
    {
        var адрес = _адрес.Text.Trim();

        if (адрес.Length == 0)
        {
            _состояние.Text = "Укажите адрес сервера.";
            return;
        }

        var код = _код.Text.Trim();

        if (код.Length == 0)
        {
            _состояние.Text = "Введите код сопряжения, показанный администратором сервера.";
            return;
        }

        _подключить.Enabled = false;
        _состояние.Text = "Подключение…";

        try
        {
            var (фрагмент, секрет) = SharedFingerprint.РазделитьКод(код);

            if (фрагмент.Length == 0 || секрет.Length == 0)
            {
                _состояние.Text = "Код сопряжения неполный: он состоит из шести групп по четыре знака.";
                return;
            }

            var реквизиты = new SharedClientSettings(адрес, (int)_порт.Value);
            var журнал = new List<string>();

            using var транспорт = SharedTransport.СоздатьДляСопряжения(
                реквизиты,
                фрагмент,
                сообщение => журнал.Add(сообщение));

            var сопряжение = await транспорт
                .СопрячьAsync(код, _имяУстройства.Text.Trim())
                .ConfigureAwait(true);

            // Секрет устройства сохраняется в защищённое хранилище Windows;
            // реквизиты — рядом с профилем. Открытый секрет на диск не пишется.
            _профиль.СохранитьСекрет(сопряжение.Secret);
            _профиль.Сохранить(new SharedClientSettings(
                адрес,
                (int)_порт.Value,
                сопряжение.DeviceId,
                сопряжение.ServerId,
                сопряжение.ServerFingerprint));

            _журнал($"Устройство сопряжено с общей базой {адрес}:{_порт.Value}.");

            Результат = _профиль.Прочитать();

            _состояние.Text = "Устройство сопряжено. Изменения применяются при перезапуске программы.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (SharedModeException ошибка)
        {
            _состояние.Text = ошибка.Message;
            _журналОшибки($"Сопряжение не выполнено: {ошибка.Message}");
        }
        catch (Exception ошибка)
        {
            _состояние.Text = "Подключение не выполнено. Подробности — в журнале программы.";
            _журналОшибки($"Сопряжение не выполнено: {ошибка}");
        }
        finally
        {
            _подключить.Enabled = true;
        }
    }
}
