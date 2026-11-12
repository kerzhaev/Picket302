using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Domain.Entities;
using Picket302.Domain.Postal;
using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог печати конвертов (ТЗ §7.2, §8.6, функция 79): выбор шаблона и
/// принтера, параметры задания.
/// </summary>
/// <remarks>
/// <para>
/// Перечень шаблонов показывается только при включённой опции «Показывать
/// список шаблонов перед печатью» (ТЗ §8.6, функция 82); при выключенной опции
/// печать выполняется последним использованным шаблоном.
/// </para>
/// <para>
/// Компоновка построена на <see cref="TableLayoutPanel"/> и
/// <see cref="FlowLayoutPanel"/> без абсолютных координат (редизайн
/// 2026-11-05, этап 3): диалог переносит длинные имена принтеров, шаблонов и
/// текст предупреждения без наложения на кнопки.
/// </para>
/// </remarks>
public sealed class FormEnvelopePrint : Form
{
    private const string ТекстОтсутствияПринтеров = "Принтеры не найдены";

    private readonly ComboBox _шаблоны = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _принтеры = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _дуплекс = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _паритет = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _копии = new() { Minimum = 1, Maximum = 9999, Value = 1, Dock = DockStyle.Fill };
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

    private readonly Label _надписьШаблона = new() { Text = "Шаблон конверта:", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _предупреждениеРазлиновки = new()
    {
        Text = PreprintedMode.Warning,
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        ForeColor = Color.Firebrick,
        Dock = DockStyle.Top,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(4, 2, 4, 2),
    };

    /// <summary>Строка «Шаблон конверта» — скрывается целиком вместе с полем.</summary>
    private readonly TableLayoutPanel _строкаШаблона = new();

    /// <summary>Панель предупреждения: текст и флаг подавления рядом.</summary>
    private readonly TableLayoutPanel _панельПредупреждения = new();

    private readonly ToolTip _подсказки = new();
    private readonly List<PrintTemplate> _шаблоныСписка = new();
    private readonly List<PrinterDescriptor> _принтерыСписка = new();
    private readonly PrinterManager? _службаПринтеров;
    private readonly bool _показыватьПредупреждениеРазлиновки;
    private readonly Button _кнопкаНастройкиПринтера = new() { Text = "Настройки принтера", AutoSize = true };

    /// <summary>
    /// Калибровка пары «принтер + формат» (ТЗ §8.4, функция 45):
    /// смещение с шагом 0,1 мм, применяется в предпросмотре и печати.
    /// </summary>
    private readonly Button _кнопкаКалибровки = new() { Text = "Калибровка…", AutoSize = true };

    /// <summary>Профиль калибровочных смещений (NULL — калибровка недоступна).</summary>
    private readonly PrinterOffsetProfile? _профильКалибровки;

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
        bool показыватьПредупреждениеРазлиновки = true,
        PrinterOffsetProfile? профильКалибровки = null,
        string? последнийПринтер = null)
    {
        Text = "Печать";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(560, 240);

        _службаПринтеров = службаПринтеров;
        _профильКалибровки = профильКалибровки;
        _показыватьПредупреждениеРазлиновки = показыватьПредупреждениеРазлиновки;

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

        // Понятное состояние без принтеров (этап 3, п. 8): диалог не падает,
        // поле называет состояние, кнопка настроек недоступна с подсказкой.
        if (_принтеры.Items.Count == 0)
        {
            _принтеры.Items.Add(ТекстОтсутствияПринтеров);
            _кнопкаНастройкиПринтера.Enabled = false;
            _подсказки.SetToolTip(
                _кнопкаНастройкиПринтера,
                "Принтеры не найдены: подключите принтер и повторите печать.");
        }

        _дуплекс.Items.AddRange(new object[] { "Односторонняя", "Двусторонняя", "Двухпроходная вручную" });
        _дуплекс.SelectedIndex = 1;

        _паритет.Items.AddRange(new object[] { "Всех страниц", "Только нечётных", "Только чётных" });
        _паритет.SelectedIndex = 0;

        _копии.Value = Math.Max(1, Math.Min(копий, 9999));

        // ТЗ §8.5, функция 35: последний принтер восстанавливается, если он
        // ещё доступен в системе (пропавший принтер — безопасный откат к
        // принтеру по умолчанию).
        if (!string.IsNullOrWhiteSpace(последнийПринтер))
        {
            var индекс = -1;
            for (var позиция = 0; позиция < _принтеры.Items.Count; позиция++)
            {
                if (string.Equals(
                        _принтеры.Items[позиция]?.ToString(),
                        последнийПринтер,
                        StringComparison.OrdinalIgnoreCase))
                {
                    индекс = позиция;
                    break;
                }
            }

            if (индекс >= 0)
            {
                _принтеры.SelectedIndex = индекс;
            }
        }

        ВыбратьШаблон(последнийШаблон);

        if (_принтерыСписка.Count > 0)
        {
            _принтеры.SelectedIndex = 0;
        }

        var кнопкаПечать = new Button
        {
            Text = "Печать",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            TabIndex = 0,
        };
        кнопкаПечать.AccessibleName = "Печать";
        _подсказки.SetToolTip(кнопкаПечать, "Начать печать с выбранными параметрами");

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            TabIndex = 1,
        };
        кнопкаОтмена.AccessibleName = "Отмена печати";
        _подсказки.SetToolTip(кнопкаОтмена, "Закрыть диалог без печати");

        EnvelopeUiTheme.ОформитьОсновнуюКнопку(кнопкаПечать);

        Controls.Add(ПостроитьКорневуюСетку(кнопкаПечать, кнопкаОтмена));

        AcceptButton = кнопкаПечать;
        CancelButton = кнопкаОтмена;

        _показатьСписокШаблонов.Checked = показатьСписокШаблонов;
        _показатьСписокШаблонов.CheckedChanged += (_, _) => ОбновитьВидимостьШаблонов();
        _размеченный.CheckedChanged += (_, _) => ОбновитьПредупреждениеРазлиновки();

        ДополнитьДоступность();
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
    /// Строит корневую сетку диалога: блоки «Шаблон и принтер», параметры,
    /// дополнительные параметры, предупреждение и панель кнопок
    /// (этап 3, п. 2–5).
    /// </summary>
    /// <param name="кнопкаПечать">Основная кнопка.</param>
    /// <param name="кнопкаОтмена">Вторичная кнопка.</param>
    /// <returns>Корневая панель.</returns>
    private TableLayoutPanel ПостроитьКорневуюСетку(Button кнопкаПечать, Button кнопкаОтмена)
    {
        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        корень.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Строка шаблона скрывается целиком (этап 3, п. 4): строка таблицы
        // авто-высоты сворачивается при скрытии единственного содержимого.
        _строкаШаблона.Dock = DockStyle.Fill;
        _строкаШаблона.ColumnCount = 2;
        _строкаШаблона.AutoSize = true;
        _строкаШаблона.Margin = new Padding(0);
        _строкаШаблона.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _строкаШаблона.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _строкаШаблона.Controls.Add(_надписьШаблона, 0, 0);
        _строкаШаблона.Controls.Add(_шаблоны, 1, 0);
        _шаблоны.MinimumSize = new Size(300, 0);

        _кнопкаНастройкиПринтера.AutoSize = true;
        _кнопкаНастройкиПринтера.AccessibleName = "Настройки принтера";
        _подсказки.SetToolTip(
            _кнопкаНастройкиПринтера,
            "Открыть системные свойства выбранного принтера (ТЗ §8.6, функция 78)");
        _кнопкаНастройкиПринтера.Click += (_, _) => ОткрытьНастройкиПринтера();

        _кнопкаКалибровки.AccessibleName = "Калибровка принтера";
        _подсказки.SetToolTip(
            _кнопкаКалибровки,
            "Калибровочное смещение пары «принтер + формат» с шагом 0,1 мм (ТЗ §8.4, функция 45)");
        _кнопкаКалибровки.Enabled = _профильКалибровки is not null;
        _кнопкаКалибровки.Click += (_, _) => ОткрытьКалибровку();

        var блокШаблонаИПринтера = Секция("Шаблон и принтер", сетка =>
        {
            сетка.RowCount = 3;
            сетка.Controls.Add(_строкаШаблона, 0, 0);
            сетка.SetColumnSpan(_строкаШаблона, 3);
            сетка.Controls.Add(Метка("Принтер:"), 0, 1);
            сетка.Controls.Add(_принтеры, 1, 1);
            сетка.Controls.Add(_кнопкаНастройкиПринтера, 2, 1);
            сетка.Controls.Add(_кнопкаКалибровки, 3, 1);
            _принтеры.MinimumSize = new Size(240, 0);
        });

        var блокПараметров = Секция("Параметры печати", сетка =>
        {
            сетка.RowCount = 3;
            сетка.Controls.Add(Метка("Количество копий:"), 0, 0);
            сетка.Controls.Add(_копии, 1, 0);
            сетка.Controls.Add(Метка("Двусторонняя печать:"), 0, 1);
            сетка.Controls.Add(_дуплекс, 1, 1);
            сетка.Controls.Add(Метка("Печать страниц:"), 0, 2);
            сетка.Controls.Add(_паритет, 1, 2);
            _копии.MaximumSize = new Size(120, 0);
            _дуплекс.MinimumSize = new Size(200, 0);
            _паритет.MinimumSize = new Size(200, 0);
        });

        var флажки = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
        };

        флажки.Controls.AddRange(
        [
            _зафиксироватьA4,
            _размеченный,
            _сЛоготипом,
            _границыПечати,
            _показатьСписокШаблонов,
        ]);

        var блокДополнительно = Секция("Дополнительные параметры", сетка =>
        {
            сетка.RowCount = 1;
            сетка.Controls.Add(флажки, 0, 0);
        });

        // Предупреждение переносится и занимает собственный блок: флаг
        // подавления рядом, кнопки не перекрываются (этап 3, п. 4).
        _панельПредупреждения.Dock = DockStyle.Fill;
        _панельПредупреждения.ColumnCount = 1;
        _панельПредупреждения.RowCount = 2;
        _панельПредупреждения.AutoSize = true;
        _панельПредупреждения.Padding = new Padding(8);
        _панельПредупреждения.BackColor = Color.FromArgb(253, 244, 235);
        _панельПредупреждения.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _панельПредупреждения.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _панельПредупреждения.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _панельПредупреждения.Controls.Add(_предупреждениеРазлиновки, 0, 0);
        _панельПредупреждения.Controls.Add(_подавитьПредупреждение, 0, 1);
        _панельПредупреждения.Margin = new Padding(0, 8, 0, 8);

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        };

        кнопки.Controls.AddRange([кнопкаПечать, кнопкаОтмена]);
        кнопки.SetFlowBreak(кнопкаОтмена, true);

        корень.RowCount = 5;
        корень.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        корень.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        корень.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        корень.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        корень.Controls.Add(блокШаблонаИПринтера, 0, 0);
        корень.Controls.Add(блокПараметров, 0, 1);
        корень.Controls.Add(блокДополнительно, 0, 2);
        корень.Controls.Add(_панельПредупреждения, 0, 3);
        корень.Controls.Add(кнопки, 0, 4);

        return корень;
    }

    /// <summary>
    /// Создаёт секцию диалога: группу с заголовком и внутренней таблицей
    /// «метка — поле» без абсолютных координат.
    /// </summary>
    /// <param name="заголовок">Заголовок секции.</param>
    /// <param name="наполнение">Настраивает внутреннюю сетку секции.</param>
    /// <returns>Готовая секция.</returns>
    private static GroupBox Секция(string заголовок, Action<TableLayoutPanel> наполнение)
    {
        var группа = new GroupBox
        {
            Text = заголовок,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 16, 8, 6),
            Margin = new Padding(0, 0, 0, 8),
        };

        var сетка = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Margin = Padding.Empty,
        };

        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        сетка.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        наполнение(сетка);

        for (var строка = 0; строка < Math.Max(1, сетка.RowCount); строка++)
        {
            сетка.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        группа.Controls.Add(сетка);

        return группа;
    }

    /// <summary>Создаёт метку поля секции.</summary>
    /// <param name="текст">Текст метки.</param>
    /// <returns>Метка.</returns>
    private static Label Метка(string текст) => new()
    {
        Text = текст,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
    };

    /// <summary>
    /// Заполняет доступность ключевых полей диалога (этап 8).
    /// </summary>
    private void ДополнитьДоступность()
    {
        _шаблоны.AccessibleName = "Шаблон конверта";
        _шаблоны.AccessibleDescription = "Выбор шаблона конверта или бланка для печати";
        _принтеры.AccessibleName = "Принтер";
        _принтеры.AccessibleDescription = "Выбор принтера для печати конвертов";
        _копии.AccessibleName = "Количество копий";
        _дуплекс.AccessibleName = "Двусторонняя печать";
        _паритет.AccessibleName = "Печать страниц";
        _зафиксироватьA4.AccessibleName = "Зафиксировать размер A4";
        _размеченный.AccessibleName = "Размеченный типографский конверт";
        _сЛоготипом.AccessibleName = "Печать с логотипом";
        _границыПечати.AccessibleName = "Показывать границы печати";
        _показатьСписокШаблонов.AccessibleName = "Показывать список шаблонов перед печатью";
        _подавитьПредупреждение.AccessibleName = "Больше не показывать предупреждение о разлиновке";
        _предупреждениеРазлиновки.AccessibleName = "Предупреждение о соответствии ГОСТ Р 51506-99";

        _шаблоны.TabIndex = 0;
        _принтеры.TabIndex = 1;
        _кнопкаНастройкиПринтера.TabIndex = 2;
        _копии.TabIndex = 3;
        _дуплекс.TabIndex = 4;
        _паритет.TabIndex = 5;
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
    /// список шаблонов перед печатью». Строка шаблона сворачивается целиком,
    /// без пустой дыры (этап 3, п. 4).
    /// </summary>
    private void ОбновитьВидимостьШаблонов()
    {
        var показывать = _показатьСписокШаблонов.Checked;

        _переченьШаблоновПоказывается = показывать;
        _шаблоны.Visible = показывать;
        _надписьШаблона.Visible = показывать;
        _строкаШаблона.Visible = показывать;
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
        _панельПредупреждения.Visible = показывать;
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

    /// <summary>
    /// Открывает калибровку выбранной пары «принтер + формат конверта»
    /// (ТЗ §8.4, функция 45). Смещение применяется в предпросмотре и
    /// печати независимо для каждой пары.
    /// </summary>
    private void ОткрытьКалибровку()
    {
        if (_профильКалибровки is null)
        {
            return;
        }

        var принтер = ВыбранныйПринтер;
        var шаблон = ВыбранныйШаблон;

        if (принтер is null || шаблон is null)
        {
            MessageBox.Show(
                this,
                "Выберите принтер и шаблон конверта для калибровки.",
                "Калибровка принтера",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        if (!System.Enum.TryParse<EnvelopeFormat>(шаблон.Format, true, out var формат))
        {
            MessageBox.Show(
                this,
                $"Формат «{шаблон.Format}» не распознан как формат конверта.",
                "Калибровка принтера",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        using var диалог = new FormPrinterCalibration(_профильКалибровки, принтер.Name, формат);
        диалог.ShowDialog(this);
    }
}
