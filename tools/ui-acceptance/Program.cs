using System.Drawing;
using Picket302.App;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure;

// Acceptance-harness Picket302: показывает реальное главное окно на ЯВНО
// переданном изолированном профиле. Режимы:
//   emptybook <профиль>      — пустая адресная книга (создатьДемоБазу: false)
//   printflow <профиль>      — синтетические отправитель+получатель для F5/F6
//
// Гард: отказ при совпадении профиля с производственным каталогом
// пользователя. Никакой зависимости от переменных APPDATA/LOCALAPPDATA —
// путь профиля известен до создания состава.

if (args.Length < 2)
{
    Console.Error.WriteLine("Использование: UiAcceptance <emptybook|printflow> <кореньПрофиля> [keep]");
    return 2;
}

var режим = args[0];
var кореньПрофиля = Path.GetFullPath(args[1]);
var сохранятьПрофиль = args.Length > 2 && args[2] == "keep";

if (режим is not ("emptybook" or "printflow"))
{
    Console.Error.WriteLine($"Неизвестный режим: {режим}");
    return 2;
}

// --- Гард изоляции ----------------------------------------------------
var производственный = Path.GetFullPath(Picket302ProfilePaths.ПоУмолчанию().DataDirectory)
    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var проверяемый = кореньПрофиля.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

if (string.Equals(производственный, проверяемый, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(
        $"ОТКАЗ: корень профиля совпадает с производственным каталогом {производственный}. "
        + "Acceptance-прогоны обязаны работать с временным каталогом.");
    return 3;
}

var пути = Picket302ProfilePaths.ДляКорня(кореньПрофиля);

Directory.CreateDirectory(пути.DataDirectory);
Directory.CreateDirectory(пути.LogsDirectory);
Directory.CreateDirectory(пути.SecretsDirectory);

Console.Out.WriteLine($"PROFILE {пути.DataDirectory}");
Console.Out.Flush();

ApplicationConfiguration.Initialize();
Application.SetCompatibleTextRenderingDefault(false);

AppServices? состав = null;
MainShell? окно = null;

try
{
    // Пункт B/A: демо-засев никогда не выполняется в acceptance-профиле.
    состав = await AppServices.СоздатьAsync(пути, создатьДемоБазу: false);

    if (режим == "printflow")
    {
        await ЗасеятьОтправителяИПолучателя(состав);
    }

    окно = new MainShell(состав)
    {
        StartPosition = FormStartPosition.Manual,
        WindowState = FormWindowState.Normal,
    };

    await окно.ИнициализироватьAsync();

    // Чтение команд — в фоне; исполнение на UI-потоке через BeginInvoke.
    _ = Task.Run(async () =>
    {
        while (await Console.In.ReadLineAsync() is { } команда)
        {
            var текст = команда.Trim().ToUpperInvariant();

            if (текст == "CLOSE")
            {
                окно!.BeginInvoke(() => окно.Close());
                break;
            }

            окно!.BeginInvoke(() => Выполнить(окно, текст));
        }
    });

    окно.Shown += (_, _) =>
    {
        Console.Out.WriteLine($"READY {окно.Handle} {Environment.ProcessId}");
        Console.Out.Flush();
    };

    Application.Run(окно);

    Console.Out.WriteLine("DONE");
    Console.Out.Flush();

    return 0;
}
catch (Exception ошибка)
{
    Console.Error.WriteLine(ошибка);
    return 1;
}
finally
{
    окно?.Dispose();

    if (состав is not null)
    {
        await состав.DisposeAsync();
    }

    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    if (!сохранятьПрофиль)
    {
        for (var попытка = 0; попытка < 5; попытка++)
        {
            try
            {
                if (Directory.Exists(кореньПрофиля))
                {
                    Directory.Delete(кореньПрофиля, recursive: true);
                }

                break;
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
        }
    }
    else
    {
        Console.Out.WriteLine($"PROFILE-KEPT {пути.DataDirectory}");
    }
}

static void Выполнить(MainShell окно, string команда)
{
    switch (команда)
    {
        case "SELECT":
            var таблица = окно.ТаблицаПолучателей;

            if (таблица.Rows.Count > 0 && !таблица.Rows[0].IsNewRow)
            {
                таблица.Rows[0].Selected = true;
                Console.Out.WriteLine("OK SELECT");
            }
            else
            {
                Console.Out.WriteLine("FAIL SELECT: таблица пуста");
            }

            break;

        case "F5":
            if (Нажать(окно, "Печать текущего конверта"))
            {
                Console.Out.WriteLine("OK F5");
            }

            break;

        case "F6":
            if (Нажать(окно, "Предпросмотр и печать"))
            {
                Console.Out.WriteLine("OK F6");
            }

            break;

        default:
            Console.Out.WriteLine($"FAIL UNKNOWN {команда}");
            break;
    }

    Console.Out.Flush();
}

static bool Нажать(MainShell окно, string текст)
{
    var стек = new Stack<Control>();
    стек.Push(окно);

    while (стек.Count > 0)
    {
        var текущий = стек.Pop();

        if (текущий is Button кнопка && кнопка.Text == текст)
        {
            кнопка.PerformClick();

            return true;
        }

        foreach (Control дочерний in текущий.Controls)
        {
            стек.Push(дочерний);
        }
    }

    Console.Out.WriteLine($"FAIL BUTTON NOT FOUND: {текст}");
    Console.Out.Flush();

    return false;
}

static async Task ЗасеятьОтправителяИПолучателя(AppServices состав)
{
    var отправитель = await состав.Контрагенты.AddAsync(new Counterparty
    {
        Type = CounterpartyType.LegalEntity,
        Name = "ООО «Отправитель Приёмки»",
        Inn = "7707083893",
        IsActive = true,
        IsSender = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            ["ООО «Отправитель Приёмки»", "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    });

    await состав.Контрагенты.AddAsync(new Counterparty
    {
        Type = CounterpartyType.LegalEntity,
        Name = "ООО «Получатель Приёмки»",
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "125009",
            ["ООО «Получатель Приёмки»", "Отдел писем", "ул. Тверская, 7", string.Empty, "г. Москва", string.Empty]).Value,
    });

    // Текущий отправитель сохраняется в настройках интерфейса изолированного
    // профиля: печатный поток F5 требует выбранного отправителя.
    состав.ТекущийОтправитель.SetGlobalSender(отправитель);
    состав.ТекущийОтправитель.Load();

    // Конвертный шаблон: печать требует выбранного шаблона; в пустом
    // профиле список шаблонов пуст, поэтому acceptance-сценарий сеет
    // единственный шаблон явно.
    await состав.ШаблоныПечати.AddAsync(new PrintTemplate
    {
        Name = "Конверт C6 (приёмка)",
        Kind = PrintTemplateKind.Envelope,
        Format = "C6",
        WidthMm = 324m,
        HeightMm = 229m,
    });

    Console.Out.WriteLine("SEEDED");
    Console.Out.Flush();
}
