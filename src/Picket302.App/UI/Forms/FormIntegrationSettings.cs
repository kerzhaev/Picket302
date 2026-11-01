using System.Drawing;
using System.Windows.Forms;

using Microsoft.Win32;

using Picket302.ComAutomation;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Страница настроек интеграции: регистрация COM-сервера автоматизации и
/// настройки печати конвертов из реестра (ТЗ §9.3, функция 114: «Регистрация
/// ProgID PEPro.Application и интерфейса IPEProApplication в
/// HKCU\Software\Classes (без прав администратора)»).
/// </summary>
/// <remarks>
/// Регистрация выполняется в ветке пользователя <c>HKCU\Software\Classes</c>,
/// поэтому права администратора не требуются. Настройки печати читаются
/// COM-сервером из <c>HKCU\Software\Picket302\PrintEnvelope</c>; отсутствующее
/// значение трактуется как пустая строка.
/// </remarks>
public sealed class FormIntegrationSettings : Form
{
    /// <summary>Ветка реестра с настройками печати конвертов.</summary>
    public const string PrintEnvelopeKey = @"Software\Picket302\PrintEnvelope";

    private readonly Label _состояние = new();
    private readonly TextBox _отправительИмя = new();
    private readonly TextBox _отправительАдрес = new();
    private readonly TextBox _отправительИндекс = new();
    private readonly TextBox _получательИмя = new();
    private readonly TextBox _получательАдрес = new();
    private readonly TextBox _получательИндекс = new();

    /// <summary>
    /// Создаёт страницу настроек интеграции.
    /// </summary>
    public FormIntegrationSettings()
    {
        Text = "Настройки интеграции";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 400);

        ПостроитьРазделCom();
        ПостроитьРазделПечати();
        ПостроитьКнопки();

        ЗагрузитьНастройкиПечати();
        ОбновитьСостояниеРегистрации();
    }

    /// <summary>
    /// Строит раздел регистрации COM-сервера.
    /// </summary>
    private void ПостроитьРазделCom()
    {
        var заголовок = new Label
        {
            Text = "COM-сервер автоматизации (ProgID PEPro.Application)",
            AutoSize = true,
            Location = new Point(10, 10),
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        _состояние.AutoSize = false;
        _состояние.Location = new Point(10, 30);
        _состояние.Size = new Size(540, 32);

        var зарегистрировать = new Button
        {
            Text = "Зарегистрировать COM-сервер",
            Size = new Size(180, 24),
            Location = new Point(10, 66),
        };

        зарегистрировать.Click += (_, _) => ЗарегистрироватьСервер();

        var удалить = new Button
        {
            Text = "Удалить регистрацию",
            Size = new Size(160, 24),
            Location = new Point(200, 66),
        };

        удалить.Click += (_, _) => УдалитьРегистрацию();

        Controls.Add(заголовок);
        Controls.Add(_состояние);
        Controls.Add(зарегистрировать);
        Controls.Add(удалить);
    }

    /// <summary>
    /// Строит раздел настроек печати конвертов.
    /// </summary>
    private void ПостроитьРазделПечати()
    {
        var заголовок = new Label
        {
            Text = "Настройки печати конвертов (HKCU\\Software\\Picket302\\PrintEnvelope)",
            AutoSize = true,
            Location = new Point(10, 104),
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        Controls.Add(заголовок);

        var метки = new (string Заголовок, TextBox Поле)[]
        {
            ("Отправитель — имя:", _отправительИмя),
            ("Отправитель — адрес:", _отправительАдрес),
            ("Отправитель — индекс:", _отправительИндекс),
            ("Получатель — имя:", _получательИмя),
            ("Получатель — адрес:", _получательАдрес),
            ("Получатель — индекс:", _получательИндекс),
        };

        var строка = 0;

        foreach (var (текст, поле) in метки)
        {
            var метка = new Label
            {
                Text = текст,
                AutoSize = true,
                Location = new Point(10, 130 + (строка * 26)),
            };

            поле.Location = new Point(180, 127 + (строка * 26));
            поле.Width = 360;

            Controls.Add(метка);
            Controls.Add(поле);

            строка++;
        }

        var сохранить = new Button
        {
            Text = "Сохранить настройки",
            Size = new Size(160, 24),
            Location = new Point(180, 290),
        };

        сохранить.Click += (_, _) => СохранитьНастройкиПечати();

        Controls.Add(сохранить);
    }

    /// <summary>
    /// Строит нижние кнопки формы.
    /// </summary>
    private void ПостроитьКнопки()
    {
        var закрыть = new Button
        {
            Text = "Закрыть",
            Size = new Size(100, 24),
            Location = new Point(440, 360),
            DialogResult = DialogResult.OK,
        };

        Controls.Add(закрыть);
        AcceptButton = закрыть;
        CancelButton = закрыть;
    }

    /// <summary>
    /// Регистрирует COM-сервер в ветке классов пользователя.
    /// </summary>
    private void ЗарегистрироватьСервер()
    {
        try
        {
            var регистрация = new ComRegistration();
            регистрация.Register();
            ОбновитьСостояниеРегистрации();

            MessageBox.Show(
                this,
                "COM-сервер зарегистрирован в HKCU\\Software\\Classes.\n" +
                "Регистрация выполняется без прав администратора.",
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось зарегистрировать COM-сервер: {ошибка.Message}",
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Удаляет регистрацию COM-сервера из ветки классов пользователя.
    /// </summary>
    private void УдалитьРегистрацию()
    {
        try
        {
            var регистрация = new ComRegistration();
            регистрация.Unregister();
            ОбновитьСостояниеРегистрации();
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось удалить регистрацию COM-сервера: {ошибка.Message}",
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Обновляет надпись о состоянии регистрации COM-сервера.
    /// </summary>
    private void ОбновитьСостояниеРегистрации()
    {
        var зарегистрирован = ComRegistration.KeyExists(
            $@"{ComRegistration.DefaultClassesRoot}\{ComAutomationIds.ProgId}");

        _состояние.Text = зарегистрирован
            ? "Регистрация присутствует: ProgID PEPro.Application доступен текущему пользователю."
            : "Регистрация отсутствует. COM-сервер не зарегистрирован.";
    }

    /// <summary>
    /// Загружает настройки печати конвертов из реестра.
    /// </summary>
    private void ЗагрузитьНастройкиПечати()
    {
        _отправительИмя.Text = Прочитать("FromName");
        _отправительАдрес.Text = Прочитать("FromAddr");
        _отправительИндекс.Text = Прочитать("FromIndex");
        _получательИмя.Text = Прочитать("ToName");
        _получательАдрес.Text = Прочитать("ToAddr");
        _получательИндекс.Text = Прочитать("ToIndex");
    }

    /// <summary>
    /// Сохраняет настройки печати конвертов в реестр.
    /// </summary>
    private void СохранитьНастройкиПечати()
    {
        try
        {
            using var ключ = Registry.CurrentUser.CreateSubKey(PrintEnvelopeKey, writable: true)
                ?? throw new InvalidOperationException(
                    $"Не удалось открыть ключ реестра HKCU\\{PrintEnvelopeKey}.");

            ключ.SetValue("FromName", _отправительИмя.Text, RegistryValueKind.String);
            ключ.SetValue("FromAddr", _отправительАдрес.Text, RegistryValueKind.String);
            ключ.SetValue("FromIndex", _отправительИндекс.Text, RegistryValueKind.String);
            ключ.SetValue("ToName", _получательИмя.Text, RegistryValueKind.String);
            ключ.SetValue("ToAddr", _получательАдрес.Text, RegistryValueKind.String);
            ключ.SetValue("ToIndex", _получательИндекс.Text, RegistryValueKind.String);

            MessageBox.Show(
                this,
                "Настройки печати конвертов сохранены.",
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось сохранить настройки печати: {ошибка.Message}",
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Читает значение настройки печати; отсутствующее значение — пустая строка.
    /// </summary>
    /// <param name="имя">Имя параметра.</param>
    /// <returns>Значение параметра либо пустая строка.</returns>
    private static string Прочитать(string имя) =>
        ComRegistration.ReadValue(PrintEnvelopeKey, имя) ?? string.Empty;
}
