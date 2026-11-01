using System.Windows.Forms;
using Picket302.Infrastructure.Persistence;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог диагностики «сервер БД не запускается» (ТЗ §8.11, функция 136).
/// </summary>
/// <remarks>
/// Диалог сообщает об осиротевших службах СУБД, остатках прежней установки,
/// и предлагает корректное их удаление с подтверждением.
/// </remarks>
public sealed class FormDbServiceDoctor : Form
{
    private readonly TextBox _отчёт = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
    };

    private readonly Button _удалить = new();
    private readonly Button _закрыть = new();
    private readonly DbServiceDoctor _диагност;

    /// <summary>
    /// Создаёт диалог диагностики.
    /// </summary>
    /// <param name="диагност">Диагност службы СУБД.</param>
    public FormDbServiceDoctor(DbServiceDoctor диагност)
    {
        ArgumentNullException.ThrowIfNull(диагност);

        _диагност = диагност;

        Text = "Диагностика сервера базы данных";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 320);

        ПостроитьИнтерфейс();

        Отчёт = _диагност.DiagnoseDbService();

        ЗаполнитьОтчёт(Отчёт);
    }

    /// <summary>Отчёт диагностики.</summary>
    public DiagnosisReport Отчёт { get; private set; }

    /// <summary>
    /// Удаляет выбранную осиротевшую службу после подтверждения пользователя.
    /// </summary>
    /// <param name="serviceName">Имя удаляемой службы.</param>
    /// <param name="подтверждено">
    /// Признак того, что пользователь подтвердил удаление.
    /// </param>
    /// <returns>Результат удаления.</returns>
    public ServiceRemovalResult УдалитьСлужбу(string serviceName, bool подтверждено)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!подтверждено)
        {
            return ServiceRemovalResult.Failure(
                serviceName,
                $"Удаление службы «{serviceName}» не подтверждено пользователем.");
        }

        var результат = _диагност.DeleteService(serviceName);

        Отчёт = _диагност.DiagnoseDbService();

        ЗаполнитьОтчёт(Отчёт);

        return результат;
    }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        _удалить.Text = "Удалить осиротевшую службу";
        _удалить.Size = new Size(190, 24);
        _удалить.Location = new Point(12, 288);
        _удалить.Click += (_, _) => ПодтвердитьУдаление();

        _закрыть.Text = "Закрыть";
        _закрыть.Size = new Size(90, 24);
        _закрыть.Location = new Point(456, 288);
        _закрыть.DialogResult = DialogResult.Cancel;

        Controls.Add(_отчёт);
        Controls.Add(_удалить);
        Controls.Add(_закрыть);

        CancelButton = _закрыть;
    }

    /// <summary>
    /// Запрашивает подтверждение и удаляет осиротевшую службу.
    /// </summary>
    private void ПодтвердитьУдаление()
    {
        var осиротевшие = Отчёт.OrphanedServices;

        if (осиротевшие.Count == 0)
        {
            MessageBox.Show(this, "Осиротевших служб не обнаружено.", "Диагностика");
            return;
        }

        var имя = осиротевшие[0].ServiceName;

        var подтверждение = MessageBox.Show(
            this,
            $"Удалить службу «{имя}» командой sc delete?\r\n"
            + "Служба является остатком прежней установки и блокирует запуск сервера базы данных.\r\n"
            + "Для удаления требуются права администратора.",
            "Диагностика",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (подтверждение != DialogResult.Yes)
        {
            return;
        }

        var результат = УдалитьСлужбу(имя, подтверждено: true);

        MessageBox.Show(
            this,
            результат.IsSuccess
                ? $"Служба «{имя}» удалена. Сервер базы данных можно запускать."
                : результат.Error,
            "Диагностика");
    }

    /// <summary>
    /// Выводит отчёт диагностики в текстовое поле.
    /// </summary>
    private void ЗаполнитьОтчёт(DiagnosisReport отчёт)
    {
        var строки = new List<string> { отчёт.Message };

        foreach (var служба in отчёт.OrphanedServices)
        {
            строки.Add(string.Empty);
            строки.Add($"Служба: {служба.ServiceName}");
            строки.Add($"  Отображаемое имя: {служба.DisplayName}");
            строки.Add($"  Состояние: {служба.State}");
            строки.Add($"  Исполняемый файл: {служба.ImagePath}");
        }

        _отчёт.Text = string.Join(Environment.NewLine, строки);
        _удалить.Enabled = отчёт.HasOrphanedServices;
    }
}
