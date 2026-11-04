using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог печати конвертов (ТЗ §7.2, §8.6, функция 79): выбор шаблона и
/// принтера, параметры задания.
/// </summary>
/// <remarks>
/// Перечень шаблонов показывается только при включённой опции «Показывать
/// список шаблонов перед печатью» (ТЗ §8.6, функция 82); при выключенной опции
/// печать выполняется последним использованным шаблоном.
/// </remarks>
public sealed class FormEnvelopePrint : Form
{
    private readonly ComboBox _шаблоны = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ComboBox _принтеры = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _дуплекс = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _паритет = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _копии = new() { Minimum = 1, Maximum = 9999, Value = 1, Width = 80 };
    private readonly CheckBox _зафиксироватьA4 = new() { Text = "Зафиксировать размер A4", AutoSize = true };
    private readonly CheckBox _размеченный = new() { Text = "Размеченный типографский конверт", AutoSize = true };
    private readonly CheckBox _сЛоготипом = new() { Text = "С логотипом", AutoSize = true };
    private readonly CheckBox _границыПечати = new() { Text = "Показывать границы печати", AutoSize = true };
    private readonly CheckBox _показатьСписокШаблонов = new()
    {
        Text = "Показывать список шаблонов перед печатью",
        AutoSize = true,
    };

    private readonly CheckBox _подавитьПредупреждение = new()
    {
        Text = PreprintedMode.SuppressCaption,
        AutoSize = true,
    };

    private readonly Label _надписьШаблона = new() { Text = "Шаблон:", AutoSize = true };
    private readonly Label _предупреждениеРазлиновки = new()
    {
        Text = PreprintedMode.Warning,
        AutoSize = false,
        ForeColor = Color.Firebrick,
    };

    private readonly List<PrintTemplate> _шаблоныСписка = new();
    private readonly List<PrinterDescriptor> _принтерыСписка = new();
    private readonly PrinterManager? _службаПринтеров;
    private readonly bool _показыватьПредупреждениеРазлиновки;

    /// <summary>Признак показа перечня шаблонов в диалоге.</summary>
    private bool _переченьШаблоновПоказывается;

    /// <summary>Признак показа предупреждения о соответствии ГОСТ Р 51506-99.</summary>
    private bool _предупреждениеРазлиновкиПоказывается;

    /// <summary>
    /// Создаёт диалог печати.
    /// </summary>
    /// <param name="шаблоны">Доступные шаблоны конвертов и бланков.</param>
    /// <param name="принтеры">Доступные принтеры.</param>
    /// <param name="показатьСписокШаблонов">
    /// Признак показа списка шаблонов (опция профиля «Показывать список
    /// шаблонов перед печатью»).
    /// </param>
    /// <param name="копий">Число копий по умолчанию.</param>
    /// <param name="последнийШаблон">
    /// Последний использованный шаблон: применяется автоматически, когда
    /// список шаблонов не показывается.
    /// </param>
    /// <param name="службаПринтеров">Менеджер принтеров для кнопки «Настройки принтера».</param>
    /// <param name="показыватьПредупреждениеРазлиновки">
    /// Признак показа предупреждения о соответствии ГОСТ Р 51506-99 в режиме
    /// разлинованного конверта.
    /// </param>
    public FormEnvelopePrint(
        IReadOnlyList<PrintTemplate> шаблоны,
        IReadOnlyList<PrinterDescriptor> принтеры,
        bool показатьСписокШаблонов,
        int копий = 1,
        PrintTemplate? последнийШаблон = null,
        PrinterManager? службаПринтеров = null,
        bool показыватьПредупреждениеРазлиновки = true)
    {
        Text = "Печать";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(600, 430);

        _службаПринтеров = службаПринтеров;
        _показыватьПредупреждениеРазлиновки = показыватьПредупреждениеРазлиновки;

        var надписьПринтера = new Label { Text = "Принтер:", AutoSize = true, Location = new Point(12, 50) };
        var надписьКопий = new Label { Text = "Копий:", AutoSize = true, Location = new Point(12, 84) };
        var надписьДуплекса = new Label { Text = "Двусторонняя печать:", AutoSize = true, Location = new Point(12, 118) };
        var надписьПаритета = new Label { Text = "Печать страниц:", AutoSize = true, Location = new Point(12, 152) };

        _надписьШаблона.Location = new Point(12, 16);
        _шаблоны.Location = new Point(140, 12);
        _принтеры.Location = new Point(140, 46);
        _копии.Location = new Point(140, 80);
        _дуплекс.Location = new Point(140, 114);
        _паритет.Location = new Point(140, 148);

        var кнопкаНастройкиПринтера = new Button
        {
            Text = "Настройки принтера",
            Size = new Size(160, 26),
            Location = new Point(428, 44),
        };

        кнопкаНастройкиПринтера.Click += (_, _) => ОткрытьНастройкиПринтера();

        foreach (var шаблон in шаблоны.Where(шаблон => шаблон.Kind == PrintTemplateKind.Envelope))
        {
            _шаблоныСписка.Add(шаблон);
            _шаблоны.Items.Add($"{шаблон.Name} ({шаблон.WidthMm:0}×{шаблон.HeightMm:0} мм)");
        }

        foreach (var принтер in принтеры)
        {
            _принтерыСписка.Add(принтер);
            _принтеры.Items.Add($"{принтер.Name} — {принтер.State}");
        }

        _дуплекс.Items.AddRange(new object[] { "Односторонняя", "Двусторонняя", "Двухпроходная вручную" });
        _дуплекс.SelectedIndex = 1;

        _паритет.Items.AddRange(new object[] { "Всех страниц", "Только нечётных", "Только чётных" });
        _паритет.SelectedIndex = 0;

        _копии.Value = Math.Max(1, Math.Min(копий, 9999));

        ВыбратьШаблон(последнийШаблон);

        if (_принтеры.Items.Count > 0)
        {
            _принтеры.SelectedIndex = 0;
        }

        _зафиксироватьA4.Location = new Point(140, 182);
        _размеченный.Location = new Point(140, 206);
        _показатьСписокШаблонов.Location = new Point(140, 230);
        _сЛоготипом.Location = new Point(140, 254);
        _границыПечати.Location = new Point(140, 278);

        _предупреждениеРазлиновки.Location = new Point(12, 304);
        _предупреждениеРазлиновки.Size = new Size(576, 44);
        _подавитьПредупреждение.Location = new Point(140, 352);

        var кнопкаПечать = new Button { Text = "Печать", DialogResult = DialogResult.OK, Size = new Size(96, 28) };
        кнопкаПечать.Location = new Point(400, 388);

        var кнопкаОтмена = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Size = new Size(96, 28) };
        кнопкаОтмена.Location = new Point(504, 388);

        Controls.AddRange(new Control[]
        {
            _надписьШаблона, надписьПринтера, надписьКопий, надписьДуплекса, надписьПаритета,
            _шаблоны, _принтеры, кнопкаНастройкиПринтера, _копии, _дуплекс, _паритет,
            _зафиксироватьA4, _размеченный, _показатьСписокШаблонов, _сЛоготипом, _границыПечати,
            _предупреждениеРазлиновки, _подавитьПредупреждение,
            кнопкаПечать, кнопкаОтмена,
        });

        AcceptButton = кнопкаПечать;
        CancelButton = кнопкаОтмена;

        _показатьСписокШаблонов.Checked = показатьСписокШаблонов;
        _показатьСписокШаблонов.CheckedChanged += (_, _) => ОбновитьВидимостьШаблонов();
        _размеченный.CheckedChanged += (_, _) => ОбновитьПредупреждениеРазлиновки();

        ОбновитьВидимостьШаблонов();
        ОбновитьПредупреждениеРазлиновки();
        EnvelopeUiTheme.ОформитьОкно(this);
    }

    /// <summary>
    /// Признак показа списка шаблонов перед печатью (значение опции профиля на
    /// момент открытия диалога с возможностью изменения оператором).
    /// </summary>
    public bool ПоказатьСписокШаблонов => _показатьСписокШаблонов.Checked;

    /// <summary>
    /// Признак показа перечня шаблонов в диалоге: при выключенной опции
    /// «Показывать список шаблонов перед печатью» перечень скрыт, а печать
    /// выполняется последним использованным шаблоном.
    /// </summary>
    public bool ПереченьШаблоновПоказывается => _переченьШаблоновПоказывается;

    /// <summary>
    /// Признак показа предупреждения о соответствии ГОСТ Р 51506-99 вместе с
    /// флагом «Больше не показывать это сообщение» (ТЗ §8.6, функция 89).
    /// </summary>
    public bool ПредупреждениеРазлиновкиПоказывается => _предупреждениеРазлиновкиПоказывается;

    /// <summary>
    /// Признак подавления предупреждения о соответствии ГОСТ Р 51506-99
    /// (ТЗ §8.6, функция 89).
    /// </summary>
    public bool ПодавитьПредупреждениеРазлиновки => _подавитьПредупреждение.Checked;

    /// <summary>Выбранный шаблон.</summary>
    public PrintTemplate? ВыбранныйШаблон =>
        _шаблоны.SelectedIndex >= 0 ? _шаблоныСписка[_шаблоны.SelectedIndex] : null;

    /// <summary>Выбранный принтер.</summary>
    public PrinterDescriptor? ВыбранныйПринтер =>
        _принтеры.SelectedIndex >= 0 ? _принтерыСписка[_принтеры.SelectedIndex] : null;

    /// <summary>Признак показа границ печатной области в предпросмотре.</summary>
    public bool ПоказатьГраницыПечати => _границыПечати.Checked;

    /// <summary>
    /// Формирует параметры печатной операции.
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <returns>Параметры печати.</returns>
    public PrintRequest СформироватьЗапрос(IReadOnlyList<long> mailIds)
    {
        var режимДуплекса = _дуплекс.SelectedIndex switch
        {
            1 => DuplexMode.Duplex,
            2 => DuplexMode.ManualTwoPass,
            _ => DuplexMode.Simplex,
        };

        var паритет = _паритет.SelectedIndex switch
        {
            1 => PageParity.OddOnly,
            2 => PageParity.EvenOnly,
            _ => PageParity.All,
        };

        return new PrintRequest
        {
            MailIds = mailIds,
            TemplateId = ВыбранныйШаблон?.Id,
            PrinterName = ВыбранныйПринтер?.Name ?? string.Empty,
            Duplex = режимДуплекса,
            Parity = паритет,
            Copies = (int)_копии.Value,
            FixA4 = _зафиксироватьA4.Checked,
            Preprinted = _размеченный.Checked,
            WithLogo = _сЛоготипом.Checked,
            ShowPreview = true,
        };
    }

    /// <summary>
    /// Выбирает шаблон: последний использованный, если он доступен, иначе
    /// первый из списка.
    /// </summary>
    /// <param name="последнийШаблон">Последний использованный шаблон.</param>
    private void ВыбратьШаблон(PrintTemplate? последнийШаблон)
    {
        if (_шаблоны.Items.Count == 0)
        {
            return;
        }

        if (последнийШаблон is not null)
        {
            var индекс = _шаблоныСписка.FindIndex(шаблон => шаблон.Id == последнийШаблон.Id);

            if (индекс >= 0)
            {
                _шаблоны.SelectedIndex = индекс;
                return;
            }
        }

        _шаблоны.SelectedIndex = 0;
    }

    /// <summary>
    /// Показывает или скрывает перечень шаблонов согласно опции «Показывать
    /// список шаблонов перед печатью».
    /// </summary>
    private void ОбновитьВидимостьШаблонов()
    {
        var показывать = _показатьСписокШаблонов.Checked;

        _переченьШаблоновПоказывается = показывать;
        _шаблоны.Visible = показывать;
        _надписьШаблона.Visible = показывать;
    }

    /// <summary>
    /// Показывает или скрывает предупреждение о соответствии ГОСТ Р 51506-99 и
    /// сопровождающий его флаг «Больше не показывать это сообщение».
    /// </summary>
    private void ОбновитьПредупреждениеРазлиновки()
    {
        var показывать = _размеченный.Checked && _показыватьПредупреждениеРазлиновки;

        _предупреждениеРазлиновкиПоказывается = показывать;
        _предупреждениеРазлиновки.Visible = показывать;
        _подавитьПредупреждение.Visible = показывать;
    }

    /// <summary>
    /// Открывает системный диалог свойств выбранного принтера (ТЗ §8.6,
    /// функция 78).
    /// </summary>
    private void ОткрытьНастройкиПринтера()
    {
        var принтер = ВыбранныйПринтер;

        if (_службаПринтеров is null || принтер is null)
        {
            MessageBox.Show(
                this,
                "Выберите принтер, настройки которого нужно открыть.",
                "Настройки принтера",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        _службаПринтеров.ShowPrinterProperties(принтер.Name);
    }
}
