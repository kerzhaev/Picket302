using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Infrastructure.Templates;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог выбора графического элемента из локального каталога клипартов
/// (ТЗ §8.10, функция 158): список с крупными значками, предпросмотр
/// выбранного элемента и кнопки подтверждения.
/// </summary>
public sealed class FormClipartPicker : Form
{
    private readonly ClipartLibrary _каталог;
    private readonly ListView _список = new();
    private readonly PictureBox _предпросмотр = new();
    private readonly Label _подсказкаПустоты = new();
    private readonly Button _выбрать = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт диалог выбора клипартa.
    /// </summary>
    /// <param name="каталог">Локальный каталог клипартов.</param>
    public FormClipartPicker(ClipartLibrary каталог)
    {
        ArgumentNullException.ThrowIfNull(каталог);

        _каталог = каталог;

        Text = "Клипарт";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(480, 360);

        ПостроитьИнтерфейс();
        EnvelopeUiTheme.ОформитьОкно(this);
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(_выбрать);

        ЗаполнитьКаталог();
    }

    /// <summary>
    /// Идентификатор выбранного элемента; <see langword="null"/>, если диалог
    /// закрыт без выбора.
    /// </summary>
    public string? ВыбранныйId { get; private set; }

    /// <summary>
    /// PNG-байты выбранного элемента либо <see langword="null"/>.
    /// </summary>
    public byte[]? ВыбранныйPng { get; private set; }

    /// <summary>Строит элементы диалога.</summary>
    private void ПостроитьИнтерфейс()
    {
        var значки = new ImageList
        {
            ImageSize = new Size(48, 48),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        _список.Dock = DockStyle.Fill;
        _список.View = View.LargeIcon;
        _список.LargeImageList = значки;
        _список.MultiSelect = false;
        _список.FullRowSelect = true;
        _список.HideSelection = false;
        _список.ItemSelectionChanged += (_, _) =>
            ОбновитьПредпросмотр(_список.SelectedItems.Cast<ListViewItem>().FirstOrDefault());
        _список.DoubleClick += (_, _) =>
        {
            if (_выбрать.Enabled)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        // Предпросмотр выбранного элемента: та же картинка крупно.
        var панельПредпросмотра = new Panel
        {
            Dock = DockStyle.Right,
            Width = 168,
            Padding = new Padding(8, 8, 8, 8),
        };

        _предпросмотр.Dock = DockStyle.Fill;
        _предпросмотр.SizeMode = PictureBoxSizeMode.Zoom;
        _предпросмотр.BorderStyle = BorderStyle.FixedSingle;
        _предпросмотр.BackColor = SystemColors.Window;

        панельПредпросмотра.Controls.Add(_предпросмотр);

        // Пустой каталог не оставляет диалог сломанным (аудит UX 2026-11-03,
        // G5): поясняем, что каталог пуст и выбора нет.
        _подсказкаПустоты.Text = "Каталог клипартов пуст.";
        _подсказкаПустоты.Dock = DockStyle.Fill;
        _подсказкаПустоты.TextAlign = ContentAlignment.MiddleCenter;
        _подсказкаПустоты.ForeColor = EnvelopeUiTheme.MutedText;
        _подсказкаПустоты.Font = EnvelopeUiTheme.ШрифтТекста(8.5f, FontStyle.Italic);
        _подсказкаПустоты.Visible = false;

        var низ = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 0, 8, 0) };

        _выбрать.Text = "Выбрать";
        _выбрать.AutoSize = true;
        _выбрать.Enabled = false;
        _выбрать.DialogResult = DialogResult.OK;

        _отмена.Text = "Отмена";
        _отмена.AutoSize = true;
        _отмена.DialogResult = DialogResult.Cancel;

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };

        кнопки.Controls.Add(_отмена);
        кнопки.Controls.Add(_выбрать);

        низ.Controls.Add(кнопки);

        Controls.Add(_список);
        Controls.Add(_подсказкаПустоты);
        Controls.Add(панельПредпросмотра);
        Controls.Add(низ);

        AcceptButton = _выбрать;
        CancelButton = _отмена;
    }

    /// <summary>Заполняет список элементами каталога.</summary>
    private void ЗаполнитьКаталог()
    {
        var значки = _список.LargeImageList!;

        foreach (var элемент in _каталог.Items)
        {
            if (!_каталог.TryGet(элемент.Id, out var png))
            {
                continue;
            }

            using var поток = new MemoryStream(png);
            using var рисунок = Image.FromStream(поток);

            значки.Images.Add(элемент.Id, рисунок);

            var пункт = new ListViewItem(элемент.Name)
            {
                ImageKey = элемент.Id,
                Tag = элемент.Id,
                ToolTipText = $"Назначить элемент «{элемент.Name}» логотипом получателя",
            };

            _список.Items.Add(пункт);
        }

        var пусто = _список.Items.Count == 0;
        _подсказкаПустоты.Visible = пусто;
        _список.Visible = !пусто;

        // Подтверждение доступно только после выделения элемента: сам факт
        // непустого каталога выбора не делает.
        _выбрать.Enabled = false;
    }

    /// <summary>
    /// Выделяет элемент каталога по идентификатору и обновляет предпросмотр —
    /// тот же путь выполняется при щелчке по списку.
    /// </summary>
    /// <param name="id">Идентификатор элемента каталога.</param>
    /// <returns><see langword="true"/>, если элемент найден и выделен.</returns>
    public bool ВыбратьЭлемент(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var пункт = _список.Items.Cast<ListViewItem>()
            .FirstOrDefault(элемент => string.Equals(элемент.Tag as string, id, StringComparison.Ordinal));

        if (пункт is not null)
        {
            пункт.Selected = true;
        }

        ОбновитьПредпросмотр(пункт);

        return пункт is not null;
    }

    /// <summary>Обновляет предпросмотр и состояние кнопки выбора.</summary>
    /// <param name="выбранный">Выделенный элемент списка либо <see langword="null"/>.</param>
    private void ОбновитьПредпросмотр(ListViewItem? выбранный)
    {
        _выбрать.Enabled = выбранный is not null;

        if (выбранный?.Tag is string id && _каталог.TryGet(id, out var png))
        {
            ВыбранныйId = id;
            ВыбранныйPng = png;

            // Копия растра отвязывает предпросмотр от потока-источника:
            // GDI+ требует держать поток открытым всё время жизни Image.
            using var поток = new MemoryStream(png);
            using var источник = Image.FromStream(поток);

            _предпросмотр.Image?.Dispose();
            _предпросмотр.Image = new Bitmap(источник);
        }
        else
        {
            ВыбранныйId = null;
            ВыбранныйPng = null;

            _предпросмотр.Image?.Dispose();
            _предпросмотр.Image = null;
        }
    }
}
