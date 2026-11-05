using System.Drawing;
using System.Windows.Forms;

using Picket302.Infrastructure.Deployment;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Мастер установки на USB-флешку (ТЗ §10.6, функции 105 и 130).
/// </summary>
/// <remarks>
/// <para>
/// Мастер предлагает два режима: «Всё целиком» (программа, переносная СУБД и
/// база данных) и «Только база данных» (рекомендуемый режим: программа остаётся
/// на рабочих станциях). Перед установкой показывается требуемый и доступный
/// объём памяти флешки.
/// </para>
/// <para>
/// Форма не содержит собственной логики установки: расчёт и копирование
/// выполняет <see cref="UsbInstaller"/>, поэтому поведение мастера проверяется
/// модульными тестами без открытия окна.
/// </para>
/// </remarks>
public sealed class FormUsbInstallWizard : Form
{
    private readonly UsbInstaller _установщик;
    private readonly RadioButton _всёЦеликом = new();
    private readonly RadioButton _толькоБаза = new();
    private readonly TextBox _папкаUsb = new();
    private readonly Label _требуется = new();
    private readonly Label _доступно = new();
    private readonly TextBox _вывод = new();

    /// <summary>
    /// Создаёт мастер установки на USB-флешку.
    /// </summary>
    /// <param name="установщик">Установщик, выполняющий расчёт и копирование.</param>
    public FormUsbInstallWizard(UsbInstaller установщик)
    {
        ArgumentNullException.ThrowIfNull(установщик);

        _установщик = установщик;

        Text = "Установка на USB-флешку";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(620, 360);

        ПостроитьРежимы();
        ПостроитьПапку();
        ПостроитьОбъём();
        ПостроитьРезультат();
        ПостроитьКнопки();

        _всёЦеликом.Checked = true;
        _толькоБаза.Checked = false;
    }

    /// <summary>
    /// Создаёт мастер установки на USB-флешку для указанной базы данных.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных текущей установки.</param>
    /// <param name="каталогПрограммы">
    /// Каталог поставки программы; по умолчанию — каталог исполняемого файла.
    /// </param>
    public FormUsbInstallWizard(string путьБазы, string? каталогПрограммы = null)
        : this(new UsbInstaller(путьБазы, каталогПрограммы))
    {
    }

    /// <summary>Выбранный режим установки.</summary>
    public UsbInstallMode Режим => _толькоБаза.Checked ? UsbInstallMode.DatabaseOnly : UsbInstallMode.Full;

    /// <summary>Указанная папка флешки.</summary>
    public string ПапкаUsb => _папкаUsb.Text.Trim();

    /// <summary>Последний рассчитанный план установки.</summary>
    public UsbInstallPlan? План { get; private set; }

    /// <summary>Последний результат установки.</summary>
    public UsbInstallResult? Результат { get; private set; }

    /// <summary>
    /// Указывает папку флешки и пересчитывает план установки.
    /// </summary>
    /// <param name="путь">Путь к папке флешки.</param>
    /// <returns>План установки либо <see langword="null"/>, если папка не указана.</returns>
    public UsbInstallPlan? УказатьПапку(string путь)
    {
        _папкаUsb.Text = путь;

        return ПересчитатьПлан();
    }

    /// <summary>
    /// Выбирает режим установки и пересчитывает план установки.
    /// </summary>
    /// <param name="режим">Режим установки.</param>
    /// <returns>План установки либо <see langword="null"/>, если папка не указана.</returns>
    public UsbInstallMode ЗадатьРежим(UsbInstallMode режим)
    {
        _всёЦеликом.Checked = режим == UsbInstallMode.Full;
        _толькоБаза.Checked = режим == UsbInstallMode.DatabaseOnly;

        ПересчитатьПлан();

        return Режим;
    }

    /// <summary>
    /// Пересчитывает план установки для указанной папки флешки.
    /// </summary>
    /// <returns>План установки либо <see langword="null"/>, если папка не указана.</returns>
    public UsbInstallPlan? ПересчитатьПлан()
    {
        var папка = ПапкаUsb;

        if (папка.Length == 0)
        {
            План = null;
            _требуется.Text = "Требуется: —";
            _доступно.Text = "Доступно: —";

            return null;
        }

        try
        {
            План = _установщик.PlanInstall(папка, Режим);
        }
        catch (IOException)
        {
            План = null;
            _требуется.Text = "Требуется: —";
            _доступно.Text = "Доступно: —";

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            План = null;
            _требуется.Text = "Требуется: —";
            _доступно.Text = "Доступно: —";

            return null;
        }

        _требуется.Text = $"Требуется: {UsbInstaller.ФорматОбъёма(План.RequiredBytes)}";
        _доступно.Text = $"Доступно: {UsbInstaller.ФорматОбъёма(План.AvailableBytes)}";

        if (!План.IsEnoughSpace)
        {
            _доступно.Text += " — места недостаточно, освободите место на флешке";
        }

        return План;
    }

    /// <summary>
    /// Выполняет установку на указанную флешку.
    /// </summary>
    /// <returns>
    /// Результат установки; при незаполненной папке —
    /// <see langword="null"/>.
    /// </returns>
    public UsbInstallResult? ВыполнитьУстановку()
    {
        var папка = ПапкаUsb;

        if (папка.Length == 0)
        {
            _вывод.Text = "Укажите папку флешки.";

            return null;
        }

        var план = ПересчитатьПлан();

        if (план is not null && !план.IsEnoughSpace)
        {
            Результат = new UsbInstallResult(
                false,
                $"Недостаточно места на USB-флешке: требуется {UsbInstaller.ФорматОбъёма(план.RequiredBytes)}, "
                    + $"доступно {UsbInstaller.ФорматОбъёма(план.AvailableBytes)}. "
                    + "Освободите место на флешке и повторите установку.",
                null,
                план.RequiredBytes,
                план.AvailableBytes);

            _вывод.Text = Результат.Error;

            return Результат;
        }

        Результат = _установщик.InstallToUsb(папка, Режим);

        _вывод.Text = Результат.IsSuccess
            ? $"Установка выполнена. Программа и база размещены: {Результат.InstalledPath}."
            : Результат.Error;

        return Результат;
    }

    /// <summary>
    /// Строит переключатели режима установки.
    /// </summary>
    private void ПостроитьРежимы()
    {
        var заголовок = new Label
        {
            Text = "Режим установки:",
            AutoSize = true,
            Location = new Point(10, 10),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        _всёЦеликом.Text = "Всё целиком (программа, переносная СУБД и база данных)";
        _всёЦеликом.AutoSize = true;
        _всёЦеликом.Location = new Point(24, 34);

        _толькоБаза.Text = "Только база данных (рекомендуется: программа остаётся на рабочих станциях)";
        _толькоБаза.AutoSize = true;
        _толькоБаза.Location = new Point(24, 56);

        _всёЦеликом.CheckedChanged += (_, _) => ПересчитатьПлан();
        _толькоБаза.CheckedChanged += (_, _) => ПересчитатьПлан();

        Controls.Add(заголовок);
        Controls.Add(_всёЦеликом);
        Controls.Add(_толькоБаза);
    }

    /// <summary>
    /// Строит выбор папки флешки.
    /// </summary>
    private void ПостроитьПапку()
    {
        var метка = new Label
        {
            Text = "Папка на флешке:",
            AutoSize = true,
            Location = new Point(10, 86),
        };

        _папкаUsb.Location = new Point(130, 83);
        _папкаUsb.Width = 350;

        var обзор = new Button
        {
            Text = "Обзор…",
            Size = new Size(90, 24),
            Location = new Point(490, 81),
        };

        обзор.Click += (_, _) => ВыбратьПапкуДиалогом();

        _папкаUsb.TextChanged += (_, _) => ПересчитатьПлан();

        Controls.Add(метка);
        Controls.Add(_папкаUsb);
        Controls.Add(обзор);
    }

    /// <summary>
    /// Строит сводку по объёму флешки.
    /// </summary>
    private void ПостроитьОбъём()
    {
        var заголовок = new Label
        {
            Text = "Объём данных:",
            AutoSize = true,
            Location = new Point(10, 118),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        _требуется.AutoSize = true;
        _требуется.Location = new Point(24, 140);

        _доступно.AutoSize = true;
        _доступно.Location = new Point(24, 160);

        Controls.Add(заголовок);
        Controls.Add(_требуется);
        Controls.Add(_доступно);
    }

    /// <summary>
    /// Строит поле вывода результата установки.
    /// </summary>
    private void ПостроитьРезультат()
    {
        var метка = new Label
        {
            Text = "Результат:",
            AutoSize = true,
            Location = new Point(10, 186),
        };

        _вывод.Location = new Point(10, 206);
        _вывод.Size = new Size(600, 100);
        _вывод.Multiline = true;
        _вывод.ReadOnly = true;
        _вывод.ScrollBars = ScrollBars.Vertical;

        Controls.Add(метка);
        Controls.Add(_вывод);
    }

    /// <summary>
    /// Строит нижние кнопки мастера.
    /// </summary>
    private void ПостроитьКнопки()
    {
        var установить = new Button
        {
            Text = "Установить",
            Size = new Size(120, 24),
            Location = new Point(10, 320),
        };

        установить.Click += (_, _) => ВыполнитьУстановку();

        var закрыть = new Button
        {
            Text = "Закрыть",
            Size = new Size(100, 24),
            Location = new Point(510, 320),
            DialogResult = DialogResult.OK,
        };

        Controls.Add(установить);
        Controls.Add(закрыть);

        AcceptButton = установить;
        CancelButton = закрыть;
    }

    /// <summary>
    /// Открывает диалог выбора папки флешки.
    /// </summary>
    private void ВыбратьПапкуДиалогом()
    {
        using var диалог = new FolderBrowserDialog
        {
            Description = "Выберите папку на USB-флешке",
            UseDescriptionForTitle = true,
        };

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            _папкаUsb.Text = диалог.SelectedPath;
        }
    }
}
