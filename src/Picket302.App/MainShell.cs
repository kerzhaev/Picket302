namespace Picket302.App;

/// <summary>
/// Главное окно оператора — схема «3+2» (ТЗ §7.1).
/// </summary>
/// <remarks>
/// На данном этапе решение содержит только каркас окна: строка меню с верхними пунктами
/// «Файл / Правка / Вид / Сервис / Печать / Рассылки / Справка», рабочая область
/// и строка состояния. Наполнение рабочей области — панели ОТПРАВИТЕЛЬ, ПОЛУЧАТЕЛЬ,
/// ОТОБРАЖЕНИЕ, ПРИМЕЧАНИЕ и справочники — разворачивается по мере реализации функций
/// адресной книги и печати.
/// </remarks>
public sealed class MainShell : Form
{
    /// <summary>Верхние пункты меню главного окна (ТЗ §7.1).</summary>
    private static readonly (string Заголовок, string Имя)[] ПунктыМеню =
    {
        ("Файл", "menuFile"),
        ("Правка", "menuEdit"),
        ("Вид", "menuView"),
        ("Сервис", "menuService"),
        ("Печать", "menuPrint"),
        ("Рассылки", "menuDistribution"),
        ("Справка", "menuHelp"),
    };

    /// <summary>Плотный шрифт оболочки: 8 pt Tahoma (ТЗ §7.1, §3.5 «Визуальный язык»).</summary>
    private static readonly Font ШрифтОболочки = new("Tahoma", 8f);

    /// <summary>Создаёт главное окно приложения.</summary>
    public MainShell()
    {
        Text = "Picket302";
        Font = ШрифтОболочки;
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1024, 680);

        var mainMenu = new MenuStrip
        {
            Name = "mainMenu",
            Dock = DockStyle.Top,
        };

        foreach (var (заголовок, имя) in ПунктыМеню)
        {
            mainMenu.Items.Add(new ToolStripMenuItem(заголовок) { Name = имя });
        }

        this.MainMenuStrip = mainMenu;

        var workArea = new Panel
        {
            Name = "workArea",
            Dock = DockStyle.Fill,
        };

        var statusStrip = new StatusStrip
        {
            Name = "statusStrip",
        };

        statusStrip.Items.Add(new ToolStripStatusLabel("Всего: 0") { Name = "totalCounter" });
        statusStrip.Items.Add(new ToolStripStatusLabel("Выбрано: 0") { Name = "selectedCounter" });
        statusStrip.Items.Add(new ToolStripStatusLabel("Всего копий: 0") { Name = "copiesCounter" });
        statusStrip.Items.Add(new ToolStripStatusLabel("База: не подключена") { Name = "databaseState" });

        // Порядок добавления задаёт порядок стыковки: последний добавленный элемент
        // стыкуется первым — меню сверху, строка состояния снизу, рабочая область — между ними.
        Controls.Add(workArea);
        Controls.Add(statusStrip);
        Controls.Add(mainMenu);
    }
}
