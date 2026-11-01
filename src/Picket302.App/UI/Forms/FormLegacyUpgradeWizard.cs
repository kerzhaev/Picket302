using System.Drawing;
using System.Windows.Forms;

using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Migration;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Мастер загрузки данных из предыдущей (бесплатной) версии Picket302
/// (ТЗ §8.9, функция 107).
/// </summary>
/// <remarks>
/// <para>
/// Мастер указывает папку базы прежней установки, запускает перенос и показывает
/// отчёт: сколько записей перенесено, сколько пропущено и какие настройки не
/// перенесены.
/// </para>
/// <para>
/// Форма не содержит собственной логики переноса: перенос выполняет
/// <see cref="LegacyUpgradeImporter"/>, поэтому поведение мастера проверяется
/// модульными тестами без открытия окна.
/// </para>
/// </remarks>
public sealed class FormLegacyUpgradeWizard : Form
{
    private readonly LegacyUpgradeImporter _импортёр;
    private readonly TextBox _папка = new();
    private readonly TextBox _вывод = new();

    /// <summary>
    /// Создаёт мастер загрузки данных из прежней версии.
    /// </summary>
    /// <param name="импортёр">Загрузчик данных прежней версии.</param>
    public FormLegacyUpgradeWizard(LegacyUpgradeImporter импортёр)
    {
        ArgumentNullException.ThrowIfNull(импортёр);

        _импортёр = импортёр;

        Text = "Загрузка данных из предыдущей версии";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(640, 420);

        ПостроитьПапку();
        ПостроитьВывод();
        ПостроитьКнопки();
    }

    /// <summary>
    /// Создаёт мастер загрузки данных из прежней версии для указанной сессии.
    /// </summary>
    /// <param name="сессия">Сессия базы данных текущей установки.</param>
    /// <param name="политика">Политика слияния с существующими записями.</param>
    public FormLegacyUpgradeWizard(ISqliteSession сессия, MergePolicy политика = MergePolicy.FillEmpty)
        : this(new LegacyUpgradeImporter(сессия, политика))
    {
    }

    /// <summary>Указанная папка прежней установки.</summary>
    public string ПапкаПрежнейУстановки => _папка.Text.Trim();

    /// <summary>Последний отчёт переноса.</summary>
    public MigrationReport? Отчёт { get; private set; }

    /// <summary>
    /// Указывает папку прежней установки.
    /// </summary>
    /// <param name="путь">Путь к папке прежней установки.</param>
    public void УказатьПапку(string путь) => _папка.Text = путь;

    /// <summary>
    /// Выполняет загрузку данных из прежней версии.
    /// </summary>
    /// <returns>
    /// Отчёт переноса; при незаполненной папке — <see langword="null"/>.
    /// </returns>
    public MigrationReport? ВыполнитьИмпорт()
    {
        var папка = ПапкаПрежнейУстановки;

        if (папка.Length == 0)
        {
            _вывод.Text = "Укажите папку прежней установки.";

            return null;
        }

        try
        {
            Отчёт = _импортёр.ImportFromLegacy(папка);
        }
        catch (LegacyMigrationException ошибка)
        {
            Отчёт = null;
            _вывод.Text = ошибка.Message;

            return null;
        }

        _вывод.Text = Отчёт.ToText();

        return Отчёт;
    }

    /// <summary>
    /// Строит выбор папки прежней установки.
    /// </summary>
    private void ПостроитьПапку()
    {
        var заголовок = new Label
        {
            Text = "Загрузка данных из предыдущей (бесплатной) версии Picket302.",
            AutoSize = true,
            Location = new Point(10, 10),
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        var пояснение = new Label
        {
            Text = "Укажите папку, в которой прежняя версия хранит файл базы данных."
                + " Переносятся адресная книга, группы, пользовательские поля, письма и история печати;",
            AutoSize = true,
            Location = new Point(10, 30),
        };

        var пояснение2 = new Label
        {
            Text = "настройки подключения прежней системы не переносятся.",
            AutoSize = true,
            Location = new Point(10, 46),
        };

        var метка = new Label
        {
            Text = "Папка прежней версии:",
            AutoSize = true,
            Location = new Point(10, 74),
        };

        _папка.Location = new Point(170, 71);
        _папка.Width = 340;

        var обзор = new Button
        {
            Text = "Обзор…",
            Size = new Size(90, 24),
            Location = new Point(520, 69),
        };

        обзор.Click += (_, _) => ВыбратьПапкуДиалогом();

        Controls.Add(заголовок);
        Controls.Add(пояснение);
        Controls.Add(пояснение2);
        Controls.Add(метка);
        Controls.Add(_папка);
        Controls.Add(обзор);
    }

    /// <summary>
    /// Строит поле вывода отчёта переноса.
    /// </summary>
    private void ПостроитьВывод()
    {
        var метка = new Label
        {
            Text = "Отчёт переноса:",
            AutoSize = true,
            Location = new Point(10, 106),
        };

        _вывод.Location = new Point(10, 126);
        _вывод.Size = new Size(620, 250);
        _вывод.Multiline = true;
        _вывод.ReadOnly = true;
        _вывод.ScrollBars = ScrollBars.Both;
        _вывод.WordWrap = true;

        Controls.Add(метка);
        Controls.Add(_вывод);
    }

    /// <summary>
    /// Строит нижние кнопки мастера.
    /// </summary>
    private void ПостроитьКнопки()
    {
        var импортировать = new Button
        {
            Text = "Импортировать",
            Size = new Size(130, 24),
            Location = new Point(10, 386),
        };

        импортировать.Click += (_, _) => ВыполнитьИмпорт();

        var закрыть = new Button
        {
            Text = "Закрыть",
            Size = new Size(100, 24),
            Location = new Point(530, 386),
            DialogResult = DialogResult.OK,
        };

        Controls.Add(импортировать);
        Controls.Add(закрыть);

        AcceptButton = импортировать;
        CancelButton = закрыть;
    }

    /// <summary>
    /// Открывает диалог выбора папки прежней установки.
    /// </summary>
    private void ВыбратьПапкуДиалогом()
    {
        using var диалог = new FolderBrowserDialog
        {
            Description = "Выберите папку прежней установки Picket302",
            UseDescriptionForTitle = true,
        };

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            _папка.Text = диалог.SelectedPath;
        }
    }
}
