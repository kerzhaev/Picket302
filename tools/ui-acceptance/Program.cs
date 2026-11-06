using System.Drawing;
using Picket302.App;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure;

// Широкий перехват — осознанный контракт CLI: harness не падает без
// диагностики, а печатает ошибку и возвращает код 1, чтобы драйвер
// зафиксировал FAIL и снял отпечатки реального профиля.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "CLI-контракт harness: любое исключение превращается в код возврата 1 с диагностикой в stderr")]

// Acceptance-harness Picket302: показывает реальное главное окно на ЯВНО
// переданном изолированном профиле. Режимы:
//   emptybook <профиль>      — пустая адресная книга (создатьДемоБазу: false)
//   printflow <профиль>      — синтетические отправитель+получатель для F5/F6
//
// Гард изоляции: путь профиля проверяется ДО создания каких-либо каталогов
// и состава. Проверяются и сам корень, и рассчитанный
// Picket302ProfilePaths.ДляКорня(корень).DataDirectory: корень может
// совпадать с родителем производственного каталога и превращаться в него.

if (args.Length < 2)
{
    Console.Error.WriteLine("Использование: UiAcceptance <emptybook|printflow> <кореньПрофиля> [keep]");
    return 2;
}

var режим = args[0];
var кореньПрофиля = Path.GetFullPath(args[1]);

if (режим is not ("emptybook" or "printflow"))
{
    Console.Error.WriteLine($"Неизвестный режим: {режим}");
    return 2;
}

// --- Гард изоляции (до создания каталогов и состава) ------------------
var пути = Picket302ProfilePaths.ДляКорня(кореньПрофиля);
var производственный = ProfileIsolationGuard.ПроизводственныйКаталог();

// Отказ 1: сам корень уже является производственным каталогом данных.
var причинаКорня = ProfileIsolationGuard.ПричинаОтказа(кореньПрофиля, производственный);

// Отказ 2: ДляКорня(корень) превращается в производственный каталог
// (корень — родитель производственного каталога или совпадает с ним).
var причинаРасчёта = ProfileIsolationGuard.ПричинаОтказа(пути.DataDirectory, производственный);

if (причинаКорня is not null || причинаРасчёта is not null)
{
    Console.Error.WriteLine("ОТКАЗ: профиль изоляции совпадает с производственным.");
    Console.Error.WriteLine($"  переданный корень: {кореньПрофиля}");
    Console.Error.WriteLine($"  рассчитанный DataDirectory: {пути.DataDirectory}");
    Console.Error.WriteLine($"  производственный DataDirectory: {производственный}");

    if (причинаКорня is not null)
    {
        Console.Error.WriteLine($"  причина (корень): {причинаКорня}");
    }

    if (причинаРасчёта is not null)
    {
        Console.Error.WriteLine($"  причина (расчёт): {причинаРасчёта}");
    }

    return 3;
}

Console.Out.WriteLine($"PROFILE {пути.DataDirectory}");
Console.Out.Flush();

var сохранятьПрофиль = args.Length > 2 && args[2] == "keep";

Directory.CreateDirectory(пути.DataDirectory);
Directory.CreateDirectory(пути.LogsDirectory);
Directory.CreateDirectory(пути.SecretsDirectory);

ApplicationConfiguration.Initialize();
Application.SetCompatibleTextRenderingDefault(false);

AppServices? состав = null;
MainShell? окно = null;
var основнойКод = 0;
var cleanupКод = 0;

try
{
    // Пункт B/A: демо-засев никогда не выполняется в acceptance-профиле.
    состав = await AppServices.СоздатьAsync(пути, создатьДемоБазу: false).ConfigureAwait(false);

    if (режим == "printflow")
    {
        await ЗасеятьОтправителяИПолучателя(состав).ConfigureAwait(false);
    }

    окно = new MainShell(состав)
    {
        StartPosition = FormStartPosition.Manual,
        WindowState = FormWindowState.Normal,
    };

    await окно.ИнициализироватьAsync().ConfigureAwait(false);

    // Чтение команд — в фоне; исполнение на UI-потоке через BeginInvoke.
    _ = Task.Run(async () =>
    {
        while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } команда)
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
}
catch (Exception ошибка)
{
    // CLI-контракт: harness не падает без диагностики — печатает ошибку и
    // возвращает 1, чтобы драйвер зафиксировал FAIL и снял отпечатки.
    Console.Error.WriteLine(ошибка);
    основнойКод = 1;
}

// --- Cleanup fail-closed: каждый шаг независимо, итог учитывается ------
try
{
    try
    {
        окно?.Dispose();
    }
    catch (Exception ошибка)
    {
        Console.Error.WriteLine($"CLEANUP: ошибка утилизации окна: {ошибка.Message}");
        cleanupКод = 1;
    }

    try
    {
        if (состав is not null)
        {
            await состав.DisposeAsync().ConfigureAwait(false);
        }
    }
    catch (Exception ошибка)
    {
        Console.Error.WriteLine($"CLEANUP: ошибка утилизации состава: {ошибка.Message}");
        cleanupКод = 1;
    }

    try
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
    catch (Exception ошибка)
    {
        Console.Error.WriteLine($"CLEANUP: ошибка очистки пула соединений: {ошибка.Message}");
        cleanupКод = 1;
    }

    if (сохранятьПрофиль)
    {
        Console.Out.WriteLine($"PROFILE-KEPT {пути.DataDirectory}");
        Console.Out.WriteLine("CLEANUP: intentionally kept");
    }
    else
    {
        var удалён = false;

        try
        {
            for (var попытка = 0; попытка < 5; попытка++)
            {
                if (!Directory.Exists(кореньПрофиля))
                {
                    удалён = true;
                    break;
                }

                try
                {
                    Directory.Delete(кореньПрофиля, recursive: true);
                }
                catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                }
            }

            удалён = !Directory.Exists(кореньПрофиля);
        }
        catch (Exception ошибка)
        {
            Console.Error.WriteLine($"CLEANUP: ошибка удаления временного профиля: {ошибка.Message}");
        }

        if (удалён)
        {
            Console.Out.WriteLine("CLEANUP PASS: True");
        }
        else
        {
            cleanupКод = 1;
            Console.Error.WriteLine($"CLEANUP PASS: False");
            Console.Error.WriteLine($"CLEANUP: каталог остался: {кореньПрофиля}");
            Console.Error.WriteLine("CLEANUP: причина — файлы заняты процессом или доступ запрещён");
        }
    }
}
finally
{
    // Итог: нормальный режим завершается нулём только при успешном
    // сценарии И успешном cleanup.
    var итог = Math.Max(основнойКод, cleanupКод);

    Console.Out.WriteLine($"EXIT {итог}");
    Console.Out.Flush();

    Environment.ExitCode = итог;
}

return Environment.ExitCode;

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

        case "COUNTS":
            // Счётчики и состав таблиц: драйвер проверяет пустое состояние
            // без UIA (строка состояния и число строк обеих таблиц).
            Console.Out.WriteLine(
                $"COUNTS всего={ИзвлечьЧисло(окно.СчётчикВсего.Text)} "
                + $"отправителей={ЧислоСтрок(окно.ТаблицаОтправителей)} "
                + $"получателей={ЧислоСтрок(окно.ТаблицаПолучателей)}");
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

static int ЧислоСтрок(DataGridView таблица)
{
    var число = 0;

    foreach (DataGridViewRow строка in таблица.Rows)
    {
        if (!строка.IsNewRow)
        {
            число++;
        }
    }

    return число;
}

static string ИзвлечьЧисло(string текст)
{
    var фрагменты = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    foreach (var фрагмент in фрагменты)
    {
        if (long.TryParse(фрагмент, out _))
        {
            return фрагмент;
        }
    }

    return "0";
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
    }).ConfigureAwait(false);

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
    }).ConfigureAwait(false);

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
    }).ConfigureAwait(false);

    Console.Out.WriteLine("SEEDED");
    Console.Out.Flush();
}
