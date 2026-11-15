using Picket302.App.Shared;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Незавершённые операции рабочего места.
/// </summary>
/// <remarks>
/// <para>
/// Операция становится незавершённой, когда ответ сервера потерян при
/// сетевом сбое: запрос был отправлен, исход неизвестен. Повтор из этого
/// окна выполняется с <b>прежним</b> идентификатором операции и прежним
/// телом запроса — служба вернёт прежний результат и не создаст дубль.
/// Создание той же записи заново (новая операция) наоборот привело бы ко
/// второму контрагенту, письму или рассылке.
/// </para>
/// <para>
/// Сведения хранятся ограниченный срок; просроченные записи удаляются и в
/// списке не появляются — их исход остаётся неизвестным, повтор невозможен.
/// </para>
/// </remarks>
public sealed class FormSharedPendingOperations : Form
{
    private readonly IDataGateway _данные;
    private readonly ListView _список;
    private readonly Button _повторить;
    private bool _повторУспешно;

    /// <summary>
    /// Создаёт окно незавершённых операций.
    /// </summary>
    /// <param name="данные">Шлюз данных: чтение списка и выполнение повтора.</param>
    public FormSharedPendingOperations(IDataGateway данные)
    {
        ArgumentNullException.ThrowIfNull(данные);

        _данные = данные;

        Text = "Незавершённые операции";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 320);

        var пояснение = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(12, 8, 12, 0),
            Text = "Исход этих операций неизвестен: ответ сервера был потерян, операция могла быть "
                + "выполнена. Повтор отправляет команду с прежним идентификатором — дубль не возникнет.",
        };

        _список = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
        };

        _список.Columns.Add("Время", 140);
        _список.Columns.Add("Команда", 200);
        _список.Columns.Add("Идентификатор операции", 190);

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(12, 8, 12, 8),
        };

        _повторить = new Button
        {
            Text = "Повторить",
            Width = 110,
            DialogResult = DialogResult.None,
        };

        var закрыть = new Button
        {
            Text = "Закрыть",
            Width = 110,
            DialogResult = DialogResult.Cancel,
        };

        _повторить.Click += (_, _) => ПовторитьВыделенные();

        кнопки.Controls.Add(закрыть);
        кнопки.Controls.Add(_повторить);

        Controls.Add(_список);
        Controls.Add(кнопки);
        Controls.Add(пояснение);

        AcceptButton = _повторить;
        CancelButton = закрыть;

        Заполнить();
    }

    /// <summary>Признак того, что хотя бы одна операция была повторена.</summary>
    public bool ПовторУспешно => _повторУспешно;

    /// <summary>
    /// Открывает окно незавершённых операций.
    /// </summary>
    /// <param name="владелец">Окно-владелец; может быть пустым.</param>
    /// <param name="данные">Шлюз данных.</param>
    /// <returns>Признак успешного повтора хотя бы одной операции.</returns>
    public static bool Показать(IWin32Window? владелец, IDataGateway данные)
    {
        using var окно = new FormSharedPendingOperations(данные);

        if (владелец is null)
        {
            окно.ShowDialog();
        }
        else
        {
            окно.ShowDialog(владелец);
        }

        return окно.ПовторУспешно;
    }

    /// <summary>Заполняет список незавершёнными операциями.</summary>
    private void Заполнить()
    {
        _список.BeginUpdate();
        _список.Items.Clear();

        foreach (var операция in _данные.НезавершённыеОперации)
        {
            var строка = new ListViewItem(операция.НачалоUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture))
            {
                Tag = операция.Идентификатор,
            };

            строка.SubItems.Add(операция.Команда);
            строка.SubItems.Add(операция.Идентификатор);

            _список.Items.Add(строка);
        }

        _список.EndUpdate();

        _повторить.Enabled = _список.Items.Count > 0;
    }

    /// <summary>Повторяет выделенные операции.</summary>
    private async void ПовторитьВыделенные()
    {
        if (_список.SelectedItems.Count == 0)
        {
            return;
        }

        _повторить.Enabled = false;

        try
        {
            foreach (var строка in _список.SelectedItems.Cast<ListViewItem>().ToArray())
            {
                var идентификатор = (string)строка.Tag!;

                try
                {
                    await _данные
                        .ПовторитьНезавершённуюAsync(идентификатор)
                        .ConfigureAwait(true);

                    _повторУспешно = true;
                    строка.Remove();
                }
                catch (SharedOutcomeUnknownException ошибка)
                {
                    // Ответ снова потерян: запись остаётся, повтор можно
                    // повторить позже.
                    MessageBox.Show(
                        this,
                        ошибка.Message,
                        "Незавершённые операции",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch (SharedModeException ошибка)
                {
                    // Служба ответила содержательным отказом: исход известен,
                    // повторять бессмысленно — запись уже удалена шлюзом.
                    строка.Remove();

                    MessageBox.Show(
                        this,
                        "Операция повторена, но служба ответила отказом: " + ошибка.Message,
                        "Незавершённые операции",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }
        finally
        {
            _повторить.Enabled = _список.Items.Count > 0;
        }
    }
}
