using System.Globalization;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Форма исключений с готовым текстом для службы поддержки (ТЗ §8.12,
/// функция 163: «форма исключений с готовым текстом для службы поддержки
/// („Просьба сообщить о ней в службу поддержки. Пожалуйста, скопируйте и
/// отправьте сообщение“)»).
/// </summary>
public sealed class FormExceptionReport : Form
{
    /// <summary>
    /// Текст обращения к службе поддержки (ТЗ §8.12, функция 163).
    /// </summary>
    public const string SupportRequestText =
        "В процессе работы программы обнаружена ошибка. "
        + "Просьба сообщить о ней в службу поддержки. "
        + "Пожалуйста, скопируйте и отправьте сообщение.";

    private readonly TextBox _подробности = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 8f),
    };

    private readonly Button _копировать = new();
    private readonly Button _поддержка = new();
    private readonly Button _закрыть = new();
    private readonly Exception _исключение;

    /// <summary>
    /// Создаёт форму отчёта об исключении.
    /// </summary>
    /// <param name="исключение">Исключение, о котором сообщается.</param>
    public FormExceptionReport(Exception исключение)
    {
        ArgumentNullException.ThrowIfNull(исключение);

        _исключение = исключение;

        Text = "Ошибка программы";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ClientSize = new Size(760, 420);

        ПостроитьИнтерфейс();

        _подробности.Text = BuildDetails();
    }

    /// <summary>
    /// Текст обращения к службе поддержки.
    /// </summary>
    public string SupportText => SupportRequestText;

    /// <summary>
    /// Полный текст сообщения для службы поддержки: обращение, сведения о
    /// программе и стек исключения.
    /// </summary>
    public string BuildDetails()
    {
        var строки = new List<string>
        {
            SupportRequestText,
            string.Empty,
            $"Программа: Picket302 {ВерсияПрограммы()}",
            $"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)}",
            $"Пользователь: {Environment.UserName}",
            $"Компьютер: {Environment.MachineName}",
            string.Empty,
            "Текст ошибки: " + _исключение.Message,
            string.Empty,
            "Стек вызовов:",
            _исключение.ToString(),
        };

        return string.Join(Environment.NewLine, строки);
    }

    /// <summary>
    /// Копирует текст сообщения в буфер обмена.
    /// </summary>
    /// <returns>Скопированный текст.</returns>
    public string CopyForSupport()
    {
        var текст = BuildDetails();

        try
        {
            Clipboard.SetText(текст);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Буфер обмена занят другим процессом: текст остаётся в окне.
        }

        return текст;
    }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var заголовок = new Label
        {
            Text = SupportRequestText,
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 8, 8, 0),
        };

        _копировать.Text = "Скопировать для службы поддержки";
        _копировать.Size = new Size(210, 26);
        _копировать.Click += (_, _) => CopyForSupport();

        _поддержка.Text = "Служба поддержки";
        _поддержка.Size = new Size(140, 26);
        _поддержка.Click += (_, _) => ОткрытьСлужбуПоддержки();

        _закрыть.Text = "Закрыть";
        _закрыть.Size = new Size(90, 26);
        _закрыть.DialogResult = DialogResult.Cancel;

        var нижняяПанель = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        нижняяПанель.Controls.Add(_копировать);
        нижняяПанель.Controls.Add(_поддержка);
        нижняяПанель.Controls.Add(_закрыть);

        _копировать.Location = new Point(10, 5);
        _поддержка.Location = new Point(230, 5);
        _закрыть.Location = new Point(650, 5);

        Controls.Add(_подробности);
        Controls.Add(нижняяПанель);
        Controls.Add(заголовок);

        CancelButton = _закрыть;
    }

    /// <summary>
    /// Открывает страницу службы поддержки в браузере.
    /// </summary>
    private void ОткрытьСлужбуПоддержки()
    {
        try
        {
            var процесс = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://support.picket302.local/",
                UseShellExecute = true,
            });

            _ = процесс;
        }
        catch (SystemException)
        {
            MessageBox.Show(
                this,
                "Не удалось открыть страницу службы поддержки. Скопируйте текст ошибки и отправьте его по электронной почте.",
                "Служба поддержки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Возвращает версию программы из сведений о сборке.
    /// </summary>
    private static string ВерсияПрограммы()
        => typeof(FormExceptionReport).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
}
