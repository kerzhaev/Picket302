using System.Drawing;
using Picket302.App;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure;
using Picket302.Printing.Preview;

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

// Контракт кодировки вывода задаётся ДО первого вывода и не зависит от
// платформенной OEM-кодировки консоли: StreamWriter над
// OpenStandardOutput/OpenStandardError с UTF8Encoding(false) пишет UTF-8
// без BOM в перенаправленные трубы (harness всегда запускается драйвером
// с RedirectStandardOutput/Error; консольного окна может не быть вовсе,
// поэтому Console.OutputEncoding через SetConsoleOutputCP не используется).
Console.SetOut(new StreamWriter(
    Console.OpenStandardOutput(),
    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true,
});
Console.SetError(new StreamWriter(
    Console.OpenStandardError(),
    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true,
});

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

            окно!.BeginInvoke(() => Выполнить(окно, текст, состав!));
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

static void Выполнить(MainShell окно, string команда, AppServices состав)
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

        case "SELECTLAST":
            // Сценарий предпросмотра: печатается ПОСЛЕДНЯЯ добавленная
            // запись (получатель сеется после отправителя). Выбор строки 0
            // при порядке вставки брал отправителя, и в поля «Кому»
            // попадали данные отправителя.
            var таблицаПоследних = окно.ТаблицаПолучателей;
            var индексПоследней = -1;

            for (var строка = 0; строка < таблицаПоследних.Rows.Count; строка++)
            {
                if (!таблицаПоследних.Rows[строка].IsNewRow)
                {
                    индексПоследней = строка;
                }
            }

            if (индексПоследней >= 0)
            {
                таблицаПоследних.Rows[индексПоследней].Selected = true;
                var имяПоследней = Convert.ToString(
                    таблицаПоследних.Rows[индексПоследней].Cells["Получатель"].Value);
                Console.Out.WriteLine($"OK SELECTLAST {имяПоследней}");
            }
            else
            {
                Console.Out.WriteLine("FAIL SELECTLAST: таблица пуста");
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

        case "SEEDFIELDS":
            // Сценарий предпросмотра: в шаблон приёмки добавляются реальные
            // поля бланка ([edt_str_to1], [edt_str_to2], [edtIndeks_to]) —
            // без них renderer выводит пустой бланк, и «белый preview» не
            // доказывает работу рендеринга.
            ЗасеятьПоляБланка(состав);
            break;

        case "PREVIEWSCAN":
            // Сценарий предпросмотра: пиксельный скан страницы предпросмотра
            // (тот же метафайл, что рисует холст окна, но без декоративных
            // рамок) — доказательство, что renderer реально вывел текст
            // адресата и индекс.
            СканПредпросмотра();
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

/// <summary>
/// Добавляет в шаблон приёмки реальные поля бланка сценария предпросмотра:
/// две строки адресата ([edt_str_to1], [edt_str_to2]) и индекс получателя
/// ([edtIndeks_to]). Значения полей привязываются к данным отправлений по
/// тегам шаблона (словарь полей ТЗ §3.2); координаты лежат внутри бланка
/// C6-раскладки приёмки 324×229 мм и вдали от краёв печатной области.
/// Повторный вызов идемпотентен: существующие поля не дублируются.
/// </summary>
static void ЗасеятьПоляБланка(AppServices состав)
{
    var шаблоны = состав.ШаблоныПечати.GetAllAsync().GetAwaiter().GetResult();
    var шаблон = шаблоны.FirstOrDefault(элемент => элемент.Name == "Конверт C6 (приёмка)");

    if (шаблон is null)
    {
        Console.Out.WriteLine("FAIL SEEDFIELDS: шаблон приёмки не найден");
        return;
    }

    var существующие = состав.ПоляШаблонов.GetByTemplateAsync(шаблон.Id).GetAwaiter().GetResult();

    if (существующие.Count > 0)
    {
        Console.Out.WriteLine($"OK SEEDFIELDS шаблон={шаблон.Id} полей={существующие.Count} (уже заданы)");
        return;
    }

    // Печатная область принтера по умолчанию (для формата C6): координаты
    // полей сеются ВНУТРИ неё, чтобы проверка «текст не выходит за печатную
    // область» была проверкой рендеринга, а не столкновением с координатами
    // посева. Fallback — размер бланка (как в PrintPreviewBuilder).
    var ширинаPrintableМм = шаблон.WidthMm;
    var высотаPrintableМм = шаблон.HeightMm;
    var принтерПоУмолчанию = состав.СлужбаПринтеров.GetDefaultPrinter();

    if (принтерПоУмолчанию is not null)
    {
        try
        {
            var сведения = состав.СлужбаПринтеров.GetDeviceInfo(
                принтерПоУмолчанию.Name,
                Picket302.Printing.Metrics.PrinterDeviceInfo.PaperSizeCodeByFormat(шаблон.Format));

            if (сведения.PrintableWidthMm > 0)
            {
                ширинаPrintableМм = сведения.PrintableWidthMm;
                высотаPrintableМм = сведения.PrintableHeightMm;
            }
        }
        catch
        {
            // Драйвер недоступен — fallback на размер бланка.
        }
    }

    // Блок адресата: 80×8 мм, правый нижний сектор зоны «печатная область
    // ∩ бланк» (драйвер может вернуть печатную область крупнее бланка —
    // например, A4-портрет для кода C6: пересечение защищает от выхода за
    // страницу); индекс: 40×12 мм, левый верхний сектор бланка.
    var ширина = Math.Min(ширинаPrintableМм, шаблон.WidthMm);
    var высота = Math.Min(высотаPrintableМм, шаблон.HeightMm);
    var правоБлока = ширина - 10m;
    var лево = правоБлока > 90m ? правоБлока - 80m : 10m;
    var низБлока = высота - 10m;
    var верх1 = низБлока > 22m ? низБлока - 22m : 10m;
    var верх2 = низБлока > 10m ? низБлока - 10m : 10m;

    var поля = new[]
    {
        new BlankField
        {
            TemplateId = шаблон.Id,
            Name = "[edt_str_to1]",
            Kind = BlankFieldKind.Text,
            Page = 1,
            XMm = лево,
            YMm = верх1,
            LengthMm = 80m,
            HeightMm = 8m,
            ZoneRole = "BottomRecipient",
        },
        new BlankField
        {
            TemplateId = шаблон.Id,
            Name = "[edt_str_to2]",
            Kind = BlankFieldKind.Text,
            Page = 1,
            XMm = лево,
            YMm = верх2,
            LengthMm = 80m,
            HeightMm = 8m,
            ZoneRole = "BottomRecipient",
        },
        // Индекс — ТЕКСТОВОЕ поле с тегом [edtIndeks_to]: машиночитаемый
        // кодовый штамп (Kind=post_index) требует системного шрифта ZIPcode
        // (ТЗ §6.3; подмена/фолбэк запрещены), которого на host-машине
        // приёмки нет — PostIndexRenderer корректно блокирует печать
        // исключением PostIndexBlockedException и понятным сообщением.
        // Читаемые цифры индекса тем же тегом доказывают отрисовку поля
        // без подмены шрифта.
        new BlankField
        {
            TemplateId = шаблон.Id,
            Name = "[edtIndeks_to]",
            Kind = BlankFieldKind.Text,
            Page = 1,
            XMm = 20m,
            YMm = 30m,
            LengthMm = 40m,
            HeightMm = 12m,
            ZoneRole = "PostIndexZone",
        },
    };

    foreach (var поле in поля)
    {
        состав.ПоляШаблонов.AddAsync(поле).GetAwaiter().GetResult();
    }

    Console.Out.WriteLine(
        $"OK SEEDFIELDS шаблон={шаблон.Id} полей={поля.Length} printable={ширинаPrintableМм}x{высотаPrintableМм}мм " +
        $"to1={лево},{верх1} to2={лево},{верх2}");
    Console.Out.Flush();
}

/// <summary>
/// Пиксельный скан страницы открытого окна предпросмотра. Страница
/// рендерится в растр 4 px/мм напрямую из метафайла предпросмотра (без
/// декоративных рамок холста: чёрной рамки страницы, красной пунктирной
/// печатной области и синих контуров полей), после чего подсчитываются
/// тёмные пиксели (яркость &lt; 128): всего, вне печатной области и внутри
/// каждого поля бланка. Пустой белый холст даёт dark≈0 — это FAIL, а не
/// PASS (доказательство рендеринга 2026-11-08).
/// </summary>
static void СканПредпросмотра()
{
    var форма = Application.OpenForms
        .OfType<Form>()
        .FirstOrDefault(открытое => открытое.GetType().Name == "FormPrintPreview");

    if (форма is null)
    {
        Console.Out.WriteLine("PREVIEW-SCAN FAIL окно-предпросмотра-не-найдено");
        return;
    }

    var документ = форма.GetType()
        .GetField("_документ", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.GetValue(форма) as PreviewDocument;

    if (документ is null)
    {
        Console.Out.WriteLine("PREVIEW-SCAN FAIL документ-недоступен");
        return;
    }

    var пикселейНаМм = 4f;
    var ширина = Math.Max((int)Math.Ceiling(документ.PageSizeMm.Width * пикселейНаМм), 1);
    var высота = Math.Max((int)Math.Ceiling(документ.PageSizeMm.Height * пикселейНаМм), 1);

    using var страница = new Bitmap(ширина, высота);

    using (var графика = Graphics.FromImage(страница))
    {
        графика.Clear(Color.White);

        if (документ.Pages.Count > 0)
        {
            // Исходный прямоугольник в ПИКСЕЛЯХ метафайла: метафайл создаётся
            // с рамкой в пикселях, согласованной с опорным растровым
            // изображением (PrintPreviewEngine.СоздатьМетафайл); источник в
            // миллиметрах искажал воспроизведение ×1.1754 (регрессия
            // доказательства рендеринга 2026-11-08).
            графика.DrawImage(
                документ.Pages[0],
                new Rectangle(0, 0, ширина, высота),
                new RectangleF(0f, 0f, документ.Pages[0].Width, документ.Pages[0].Height),
                GraphicsUnit.Pixel);
        }
    }

    // Контракт размера холста: скан обязан совпадать с расчётом по размеру
    // страницы — иначе холст обрезан или исказён.
    var размерыТочны = страница.Width == ширина && страница.Height == высота;

    var печатная = документ.PrintableAreaMm.Width > 0 && документ.PrintableAreaMm.Height > 0
        ? new Rectangle(
            (int)(документ.PrintableAreaMm.X * пикселейНаМм),
            (int)(документ.PrintableAreaMm.Y * пикселейНаМм),
            (int)(документ.PrintableAreaMm.Width * пикселейНаМм),
            (int)(документ.PrintableAreaMm.Height * пикселейНаМм))
        : new Rectangle(0, 0, ширина, высота);

    var зоны = документ.Fields
        .Select(поле => (
            поле.Name,
            поле.Kind,
            Прямоугольник: new Rectangle(
                (int)(поле.Bounds.X * пикселейНаМм),
                (int)(поле.Bounds.Y * пикселейНаМм),
                Math.Max((int)(поле.Bounds.Width * пикселейНаМм), 1),
                Math.Max((int)(поле.Bounds.Height * пикселейНаМм), 1))))
        .ToList();

    var тёмноеВПоле = new int[зоны.Count];
    var тёмныхВсего = 0;
    var тёмныхВнеПечатной = 0;

    var данные = страница.LockBits(
        new Rectangle(0, 0, ширина, высота),
        System.Drawing.Imaging.ImageLockMode.ReadOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    try
    {
        var буфер = new byte[данные.Stride * высота];
        System.Runtime.InteropServices.Marshal.Copy(данные.Scan0, буфер, 0, буфер.Length);

        for (var y = 0; y < высота; y++)
        {
            var смещениеСтроки = y * данные.Stride;

            for (var x = 0; x < ширина; x++)
            {
                var синий = буфер[смещениеСтроки + (x * 4)];
                var зелёный = буфер[смещениеСтроки + (x * 4) + 1];
                var красный = буфер[смещениеСтроки + (x * 4) + 2];

                var яркость = (красный * 299 + зелёный * 587 + синий * 114) / 1000;

                if (яркость >= 128)
                {
                    continue;
                }

                тёмныхВсего++;

                if (!печатная.Contains(x, y))
                {
                    тёмныхВнеПечатной++;
                }

                for (var номерЗоны = 0; номерЗоны < зоны.Count; номерЗоны++)
                {
                    if (зоны[номерЗоны].Прямоугольник.Contains(x, y))
                    {
                        тёмноеВПоле[номерЗоны]++;
                    }
                }
            }
        }
    }
    finally
    {
        страница.UnlockBits(данные);
    }

    // Диагностика распределения тёмных пикселей: bbox и плотность по
    // горизонтальным полосам (8 полос сверху вниз).
    var минX = ширина;
    var минY = высота;
    var максX = -1;
    var максY = -1;
    var полосы = new int[8];

    var диагностикаБит = страница.LockBits(
        new Rectangle(0, 0, ширина, высота),
        System.Drawing.Imaging.ImageLockMode.ReadOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    try
    {
        var буферДиагностики = new byte[диагностикаБит.Stride * высота];
        System.Runtime.InteropServices.Marshal.Copy(диагностикаБит.Scan0, буферДиагностики, 0, буферДиагностики.Length);

        for (var y = 0; y < высота; y++)
        {
            var смещение = y * диагностикаБит.Stride;

            for (var x = 0; x < ширина; x++)
            {
                var b = буферДиагностики[смещение + (x * 4)];
                var g = буферДиагностики[смещение + (x * 4) + 1];
                var r = буферДиагностики[смещение + (x * 4) + 2];

                if ((r * 299 + g * 587 + b * 114) / 1000 >= 128)
                {
                    continue;
                }

                if (x < минX) { минX = x; }
                if (x > максX) { максX = x; }
                if (y < минY) { минY = y; }
                if (y > максY) { максY = y; }
                полосы[Math.Min(y * 8 / высота, 7)]++;
            }
        }
    }
    finally
    {
        страница.UnlockBits(диагностикаБит);
    }

    var ббокс = максX < 0 ? "пусто" : $"x={минX}..{максX} y={минY}..{максY}";

    Console.Out.WriteLine(
        $"PREVIEW-SCAN страниц={документ.Pages.Count} canvasW={ширина} canvasH={высота} "
        + $"dimsOk={(размерыТочны ? "true" : "false")} dark={тёмныхВсего} "
        + $"outsidePrintable={тёмныхВнеПечатной} bbox={ббокс} полосы=[{string.Join(",", полосы)}]");

    // Диагностический снимок растра скана (переписывается каждым прогоном):
    // зелёные рамки — границы полей предпросмотра в мм-координатах.
    try
    {
        using var разметка = Graphics.FromImage(страница);
        var пероПолей = new Pen(Color.FromArgb(160, 0, 160, 0), 2f);

        foreach (var поле in зоны)
        {
            разметка.DrawRectangle(пероПолей, поле.Прямоугольник);
        }

        пероПолей.Dispose();
        страница.Save(
            Path.Combine(Path.GetTempPath(), "Picket302PreviewScan.png"),
            System.Drawing.Imaging.ImageFormat.Png);
    }
    catch
    {
        // Диагностика не влияет на исход скана.
    }

    for (var номерЗоны = 0; номерЗоны < зоны.Count; номерЗоны++)
    {
        Console.Out.WriteLine(
            $"PREVIEW-FIELD name={зоны[номерЗоны].Name} kind={зоны[номерЗоны].Kind} dark={тёмноеВПоле[номерЗоны]}");
    }

    Console.Out.Flush();
}
