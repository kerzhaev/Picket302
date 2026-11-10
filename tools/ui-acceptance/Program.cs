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
//   noaddress <профиль>      — отправитель с адресом + получатель БЕЗ адреса
//                              (сценарий «получатель без адреса», ТЗ §11.4
//                              приёмки): печать обязана показать понятное
//                              сообщение, не создать отправление и не
//                              сформировать задание на печать.
//   screens <профиль>        — снимки экранов матрицы визуального сравнения:
//                              открывает рабочие диалоги по одному, дожидаясь
//                              команды оператора «СЛЕДУЮЩИЙ» перед закрытием.
//   PICKET302_ACCEPTANCE_NONACTIVATING=1 — для screenshots/DPI: окна
//                              создаются с WS_EX_NOACTIVATE, не становятся
//                              foreground и не вызывают BringToFront/Activate.
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
// Для обратного канала действует тот же явный контракт: Windows PowerShell
// 5.1 при первом обращении к Process.StandardInput может отправить UTF-8 BOM,
// а без фиксированной кодировки перенаправленный stdin декодируется OEM-кодовой
// страницей и превращает даже ASCII-команду в «Я╗┐COUNTS». Драйверы формируют
// команды в UTF-8; Windows PowerShell 5.1 может добавить один BOM при первом
// обращении к StandardInput, поэтому harness явно читает stdin как UTF-8 и
// принимает этот маркер только в начале протокола.
Console.InputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
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
    Console.Error.WriteLine("Использование: UiAcceptance <emptybook|printflow|noaddress> <кореньПрофиля> [keep]");
    return 2;
}

var режим = args[0];
var кореньПрофиля = Path.GetFullPath(args[1]);

static bool НеактивирующийРежим()
    => string.Equals(
        Environment.GetEnvironmentVariable("PICKET302_ACCEPTANCE_NONACTIVATING"),
        "1",
        StringComparison.Ordinal);

if (режим is not ("emptybook" or "printflow" or "noaddress" or "screens" or "screens2" or "noprinter"))
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

// Журнал profile-sensitive операции (stage3.1, §6.3): PID и границы гарда
// в stdout каждого прогона — расследование внешних изменений профиля
// получает явную связь «прогон ↔ процесс ↔ корень».
Console.Out.WriteLine(
    $"GUARD PASS pid={Environment.ProcessId} root={пути.DataDirectory} "
    + $"process={Environment.ProcessPath ?? "UiAcceptance"}");
Console.Out.Flush();

var сохранятьПрофиль = args.Length > 2 && args[2] == "keep";

Directory.CreateDirectory(пути.DataDirectory);
Directory.CreateDirectory(пути.LogsDirectory);
Directory.CreateDirectory(пути.SecretsDirectory);

ApplicationConfiguration.Initialize();
Application.SetCompatibleTextRenderingDefault(false);

// Fail-closed для acceptance (регрессия 2026-11-08): непредвиденное
// исключение в потоке UI по умолчанию показывает МОДАЛЬНЫЙ диалог
// ThreadException — он блокирует поток UI навсегда, драйвер не получает
// ни stdout-маркеров, ни выхода, и приёмка виснет (зарегистрировано при
// прогоне снимков экрана: диалог исключения ImageList держал процесс,
// пока он не был найден и завершён вручную). Режим ThrowException
// пробрасывает исключение из цикла сообщений: его ловит верхний
// try/catch этого файла — процесс печатает диагностику в stderr и
// завершается с кодом 1, гарантированно проходя очистку профиля.
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

AppServices? состав = null;
MainShell? окно = null;
var основнойКод = 0;
var cleanupКод = 0;
var неактивирующийПерехват = IntPtr.Zero;

try
{
    // Пункт B/A: демо-засев никогда не выполняется в acceptance-профиле.
    // Изоляция реестра (stage3.1, контейнирование ENV3-02-соседнего обхода):
    // составное хранилище отправителя по умолчанию пишет SenderId и в
    // HKCU\Software\PEPro\CurrentSender — реальную машину. Все режимы
    // acceptance работают с NullCurrentSenderStore: реестровая ветка остаётся
    // за производственной установкой. Режим noprinter подменяет принтерный
    // менеджер производным классом (null вместо принтера по умолчанию) —
    // сценарий «нет принтера» без изменения системных настроек стенда.
    var принтерныйМенеджер = режим == "noprinter"
        ? new ПустойМенеджерПринтеров()
        : null;
    состав = await AppServices
        .СоздатьAsync(
            пути,
            создатьДемоБазу: false,
            службаПринтеров: принтерныйМенеджер,
            реестрОтправителяИзолирован: true)
        .ConfigureAwait(false);

    if (режим == "printflow" || режим == "noprinter")
    {
        await ЗасеятьОтправителяИПолучателя(состав).ConfigureAwait(false);
    }

    if (режим == "noaddress")
    {
        await ЗасеятьПолучателяБезАдреса(состав).ConfigureAwait(false);
    }

    if (режим == "screens" || режим == "screens2")
    {
        // Снимки экранов матрицы: наполняем адресную книгу демонстрационными
        // данными (в отличие от emptybook) — иначе половина поверхностей
        // (карточка, длинные значения) нечего показывать. Шаблон печати
        // сеется командой SEEDTEMPLATE по требованию: сценарий «нет шаблона»
        // выполняется на профиле БЕЗ шаблонов.
        await ЗасеятьДемонстрационныеДанные(состав).ConfigureAwait(false);
    }

    окно = new MainShell(состав)
    {
        StartPosition = FormStartPosition.Manual,
        WindowState = FormWindowState.Normal,
    };

    ПодготовитьНеактивирующееОкно(окно);

    await окно.ИнициализироватьAsync().ConfigureAwait(false);

    if (НеактивирующийРежим())
    {
        // Флаг WS_EX_NOACTIVATE на известных формах не покрывает нативные
        // MessageBox и модальные формы, созданные глубоко внутри MainShell
        // во время F5/F6. Thread-local CBT hook срабатывает до активации
        // любого нового окна именно этого UI-потока; обычный запуск без
        // переменной окружения этот путь не использует.
        неактивирующийПерехват = Платформенно.УстановитьНеактивирующийПерехват();
    }

    // Чтение команд — в фоне; исполнение на UI-потоке через BeginInvoke.
    _ = Task.Run(async () =>
    {
        while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } команда)
        {
            // Windows PowerShell 5.1 materializes Process.StandardInput as a
            // UTF-8 StreamWriter that emits a BOM on its first access. Even
            // when the caller writes the command bytes through BaseStream,
            // that marker can arrive before the first command. It is valid
            // only at the beginning of this protocol, so remove it before
            // dispatching instead of treating the command as UNKNOWN.
            var текст = команда.Trim().TrimStart('\uFEFF');

            // Команда — первый токен в верхнем регистре; аргумент сохраняется
            // как есть (имя диалога регистрозависимо).
            var разделитель = текст.IndexOf(' ');
            var имяКоманды = (разделитель < 0 ? текст : текст[..разделитель]).ToUpperInvariant();
            var аргумент = разделитель < 0 ? string.Empty : текст[(разделитель + 1)..].Trim();

            if (имяКоманды == "CLOSE")
            {
                окно!.BeginInvoke(() => окно.Close());
                break;
            }

            окно!.BeginInvoke(() => Выполнить(окно, имяКоманды, состав!, аргумент));
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
        if (неактивирующийПерехват != IntPtr.Zero)
        {
            Платформенно.СнятьНеактивирующийПерехват(неактивирующийПерехват);
            неактивирующийПерехват = IntPtr.Zero;
        }
    }
    catch (Exception ошибка)
    {
        Console.Error.WriteLine($"CLEANUP: ошибка снятия неактивирующего перехвата: {ошибка.Message}");
        cleanupКод = 1;
    }

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

static void Выполнить(MainShell окно, string команда, AppServices состав, string аргумент = "")
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
                var имяПоследней = таблицаПоследних.Rows[индексПоследней]
                    .Cells["Получатель"].Value?.ToString() ?? string.Empty;
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

        case "SELECTNOADDR":
            // Сценарий «получатель без адреса»: выбирается ИМЕННО та строка,
            // у контрагента которой нет основного адреса. Выбор по признаку
            // (а не по порядку строк) делает сценарий детерминированным и не
            // зависит от порядка вставки или сортировки.
            //
            // ВАЖНО: снимается выделение со ВСЕХ остальных строк. Иначе
            // DataGridView оставляет автоматически выделенной первую строку
            // (отправителя, у которого адрес ЕСТЬ), и F6 создала бы
            // отправление для него — сценарий проверял бы не то, что
            // задумано, а «печать частично прошла».
            var таблицаБезАдреса = окно.ТаблицаПолучателей;
            var найденБезАдреса = false;

            таблицаБезАдреса.ClearSelection();

            for (var строка = 0; строка < таблицаБезАдреса.Rows.Count; строка++)
            {
                if (таблицаБезАдреса.Rows[строка].IsNewRow)
                {
                    continue;
                }

                if (таблицаБезАдреса.Rows[строка].Tag is not long идентификатор)
                {
                    continue;
                }

                var контрагент = состав.Контрагенты.GetByIdAsync(идентификатор).GetAwaiter().GetResult();

                if (контрагент is null || контрагент.GetAddress(контрагент.AddressMainType) is not null)
                {
                    continue;
                }

                таблицаБезАдреса.Rows[строка].Selected = true;
                var имя = таблицаБезАдреса.Rows[строка].Cells["Получатель"].Value?.ToString() ?? string.Empty;
                var выделено = таблицаБезАдреса.SelectedRows.Count;
                Console.Out.WriteLine($"OK SELECTNOADDR {имя} выделено={выделено}");
                найденБезАдреса = true;
                break;
            }

            if (!найденБезАдреса)
            {
                Console.Out.WriteLine("FAIL SELECTNOADDR: получатель без адреса не найден");
            }

            break;

        case "SHOW":
            // Открыть рабочий диалог для снимка экрана. Диалог открывается
            // НЕ модально: окно остаётся живым, пока драйвер не пришлёт
            // команду закрытия — иначе снимок невозможно снять.
            ПоказатьДиалог(окно, состав, аргумент);
            break;

        case "HIDE":
            // Закрыть последний открытый диалог снимков.
            СнимкиЭкранов.ПоследнееМеню?.Close();
            СнимкиЭкранов.ПоследнееМеню = null;
            СнимкиЭкранов.ПоследнийДиалог?.Close();
            СнимкиЭкранов.ПоследнийДиалог = null;
            Console.Out.WriteLine("OK HIDE");
            Console.Out.Flush();
            break;

        case "FILTERMENU":
            // Открыть настоящее вложенное меню «Вид → Фильтры».
            ПоказатьМенюФильтров(окно);
            break;

        case "QUICKFILTERMENU":
            // Открыть тот же набор быстрых фильтров через кнопку над таблицей.
            ПоказатьМенюБыстрыхФильтров(окно);
            break;

        case "PRINTMENU":
            // Открыть настоящее меню «Печать» с командами F5/F6/F7/F8.
            ПоказатьМенюПечати(окно);
            break;

        case "TABLEMENU":
            // Открыть настоящее контекстное меню строки DataGridView.
            ПоказатьМенюТаблицы(окно);
            break;

        case "MAILCOUNT":
            // Сценарий «получатель без адреса»: доказательство, что печать
            // не создала частичную запись отправления.
            СчитатьОтправления(состав);
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

        case "SETTAB":
            // Смена вкладки главного окна («Отправленные» — 3). Драйвер
            // доказывает состояние вкладки командой SENTINFO после
            // асинхронной загрузки.
            if (!int.TryParse(аргумент, out var номерВкладки)
                || номерВкладки < 0
                || номерВкладки >= окно.Вкладки.TabPages.Count)
            {
                Console.Out.WriteLine($"FAIL SETTAB: неверный индекс '{аргумент}'");
                break;
            }

            окно.Вкладки.SelectedIndex = номерВкладки;
            Console.Out.WriteLine(
                $"OK SETTAB {номерВкладки} «{окно.Вкладки.TabPages[номерВкладки].Text}»");
            break;

        case "SENTINFO":
            // Вкладка «Отправленные»: число строк таблицы, признак подсказки
            // пустого дерева и выделение — доказательства состояний 7.1/7.2.
            {
                var таблицаПисем = (DataGridView?)typeof(MainShell)
                    .GetField("_таблицаОтправленных", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(окно);

                if (таблицаПисем is null)
                {
                    Console.Out.WriteLine("FAIL SENTINFO: таблица не найдена");
                    break;
                }

                var строк = 0;
                string? первое = null;

                foreach (DataGridViewRow строка in таблицаПисем.Rows)
                {
                    if (строка.IsNewRow)
                    {
                        continue;
                    }

                    строк++;

                    if (первое is null)
                    {
                        первое = строка.Cells["Получатель"].Value?.ToString();
                    }
                }

                Console.Out.WriteLine(
                    $"SENTINFO строк={строк} подсказка={окно.ПоказанаПодсказкаПустыхПапок} "
                    + $"выделено={таблицаПисем.SelectedRows.Count} первая={первое ?? "-"}");
            }

            break;

        case "SEEDTEMPLATE":
            // Сеет конвертный шаблон приёмки по требованию (idempotent):
            // сценарий «нет шаблона» выполняется ДО сеева, сценарий
            // «блокировка размера бумаги» — ПОСЛЕ него.
            ЗасеятьШаблонПриёмки(состав);
            break;

        case "CLIPSELECT":
            // Смоук-регрессия UX3-01: выбор значка и подтверждение в открытом
            // диалоге клипарта. Падение на ленивом чтении ImageList здесь
            // означает FAIL.
            ВыбратьКлипарт();
            break;

        case "GRIDRESET":
            // «Сбросить» в настройке вида таблиц: значение оформления до и
            // после нажатия кнопки — доказательство 7.8.
            СброситьОформление();
            break;

        case "SETDPI":
            // Программная симуляция DPI (DPI_SIMULATION, НЕ OS-DPI): реальный
            // конвейер PerMonitorV2 — форме отправляется WM_DPICHANGED, и
            // WinForms сам перемасштабирует шрифты и контролы. OS-DPI стенд на
            // host-машине недоступен, поэтому флаг остаётся
            // DPI_SIMULATION_*_VERIFIED, а не DPI_RUNTIME_*.
            if (!int.TryParse(аргумент, out var процентDpi)
                || процентDpi < 100
                || процентDpi > 300)
            {
                Console.Out.WriteLine($"FAIL SETDPI: неверный процент '{аргумент}'");
                break;
            }

            // 100 % — тоже РЕАЛЬНЫЙ переход (обратный WM_DPICHANGED на 96),
            // а не отключение симуляции: без него возврат не доказан.
            СнимкиЭкранов.ЦелевойDpi = (int)Math.Round(96m * процентDpi / 100m);

            foreach (var форма in Application.OpenForms.OfType<Form>().ToList())
            {
                ПрименитьЦельDpi(форма);
            }

            Console.Out.WriteLine($"OK SETDPI {процентDpi} target={СнимкиЭкранов.ЦелевойDpi}");
            break;

        case "LAYOUTCHECK":
            // Критерии DPI-PASS §8.3: отрицательные/нулевые размеры видимых
            // контролов и контролы, вышедшие за границы родителя.
            ПроверитьКомпоновку();
            break;

        case "F7":
            // Прямая печать без предпросмотра (ТЗ §8.6): единственный
            // acceptance-путь, при котором задание доходит до конвейера
            // валидации — BLOCKED_SIZE диалог «Проверка размера бумаги».
            var пунктF7 = НайтиПунктМеню(окно, "Выделенные без предпросмотра (F7)");

            if (пунктF7 is not null)
            {
                пунктF7.PerformClick();
                Console.Out.WriteLine("OK F7");
            }
            else
            {
                Console.Out.WriteLine("FAIL F7: пункт меню не найден");
            }

            break;

        default:
            Console.Out.WriteLine($"FAIL UNKNOWN {команда}");
            break;
    }

    Console.Out.Flush();
}

/// <summary>
/// Сценарий «получатель без адреса»: отправитель заводится с полным адресом
/// (иначе печать остановится раньше, на проверке отправителя), а получатель
/// — БЕЗ основного адреса. Именно этот получатель выбирается в адресной
/// книге, поэтому и F5, и F6 обязаны показать понятное сообщение и НЕ
/// создать отправление.
///
/// Требование сценария (ТЗ §11.4): печать без адреса не должна приводить к
/// зависанию, к частично созданной записи и к заданию на печать.
/// </summary>
static async Task ЗасеятьПолучателяБезАдреса(AppServices состав)
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

    // Получатель БЕЗ адреса: PostalAddress не задаётся вовсе, поэтому
    // контрагент.GetAddress(AddressMainType) возвращает null — ровно то
    // состояние, которое обязано обрабатываться сообщением, а не печатью.
    await состав.Контрагенты.AddAsync(new Counterparty
    {
        Type = CounterpartyType.LegalEntity,
        Name = "ООО «Получатель Без Адреса»",
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    }).ConfigureAwait(false);

    состав.ТекущийОтправитель.SetGlobalSender(отправитель);
    состав.ТекущийОтправитель.Load();

    // Шаблон нужен, чтобы сценарий дошёл до печатного конвейера в тех случаях,
    // когда адрес у получателя всё же окажется задан (проверка «печать не
    // началась» должна отличаться от «шаблона нет»).
    await состав.ШаблоныПечати.AddAsync(new PrintTemplate
    {
        Name = "Конверт C6 (приёмка)",
        Kind = PrintTemplateKind.Envelope,
        Format = "C6",
        WidthMm = 324m,
        HeightMm = 229m,
    }).ConfigureAwait(false);

    Console.Out.WriteLine("SEEDED-NOADDRESS");
    Console.Out.Flush();
}

/// <summary>
/// Наполняет изолированный профиль демонстрационными данными для снимков
/// экранов матрицы визуального сравнения: отправитель, получатели с разными
/// адресами, один — с очень длинными значениями, группы и шаблон печати.
/// Данные живут ТОЛЬКО в изолированном профиле драйвера.
/// </summary>
static async Task ЗасеятьДемонстрационныеДанные(AppServices состав)
{
    var отправитель = await состав.Контрагенты.AddAsync(new Counterparty
    {
        Type = CounterpartyType.LegalEntity,
        Name = "ООО «Отправитель Приёмки»",
        ShortName = "Отправитель",
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
        ShortName = "Получатель",
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "125009",
            ["ООО «Получатель Приёмки»", "Отдел писем", "ул. Тверская, 7", string.Empty, "г. Москва", string.Empty]).Value,
    }).ConfigureAwait(false);

    // Очень длинные значения: длинное наименование, длинная строка адреса и
    // длинный индекс — проверка читаемости без обрезания.
    await состав.Контрагенты.AddAsync(new Counterparty
    {
        Type = CounterpartyType.LegalEntity,
        Name = "Общество с ограниченной ответственностью «Промышленно-внедренческая компания «Восток-Запад-Инвест»",
        ShortName = "Восток-Запад-Инвест",
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "125009",
            [
                "Общество с ограниченной ответственностью «Промышленно-внедренческая компания «Восток-Запад-Инвест»",
                "Производственно-сбытовое управление международных поставок",
                "ул. Тверская, дом 7, строение 3, этаж 4, офис 412",
                "Деловой центр «Мельниковская площадь»",
                "г. Москва",
                string.Empty,
            ]).Value,
    }).ConfigureAwait(false);

    состав.ТекущийОтправитель.SetGlobalSender(отправитель);
    состав.ТекущийОтправитель.Load();

    // Шаблон печати НЕ сеется: сценарий «нет шаблона» (S4) обязан видеть
    // пустой список; шаблон для «блокировки размера» (S5) сеется командой
    // SEEDTEMPLATE по требованию драйвера.
    Console.Out.WriteLine("SEEDED-SCREENS");
    Console.Out.Flush();
}


/// <summary>
/// Открывает рабочий диалог для снимка экрана. Диалог открывается НЕ
/// модально и остаётся живым до команды HIDE.
/// </summary>
static void ПоказатьДиалог(MainShell окно, AppServices состав, string имя)
{
    try
    {
        СнимкиЭкранов.ПоследнийДиалог?.Close();
        СнимкиЭкранов.ПоследнийДиалог = null;

        Form? диалог = имя.ToLowerInvariant() switch
        {
            // Карточка открывается для РЕАЛЬНОГО контрагента: иначе снимок
            // показывает пустую форму, а не рабочее состояние.
            "counterparty" => new Picket302.App.UI.Forms.FormCounterpartyEdit(
                состав.Контрагенты.GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 10))
                    .GetAwaiter().GetResult().Items.FirstOrDefault()),
            "mailedit" => new Picket302.App.UI.Forms.FormMailEdit(),
            "gridappearance" => new Picket302.App.UI.Forms.FormGridAppearance(
                // НЕОБЫЧНОЕ оформление (stage3.1, сценарий 7.8): сброс обязан
                // вернуть значения по умолчанию — доказуемо только при
                // отличном от дефолтного входном состоянии.
                new Picket302.Domain.Contracts.GridAppearance
                {
                    FocusColor = 255,
                    SelectedColor = 65280,
                    HintColor = 8421376,
                    ZebraEnabled = true,
                    HeadersVisible = true,
                    FontSize = 10f,
                    RowHeight = 22,
                }),
            "blankeditor" => new Picket302.App.UI.Forms.FormBlankEditor(),
            "clipartpicker" => new Picket302.App.UI.Forms.FormClipartPicker(
                new Picket302.Infrastructure.Templates.ClipartLibrary()),
            "distribution" => new Picket302.App.UI.Forms.FormDistributionEditor(состав),
            // Отдельные reference-поверхности KonvertProNet: поиск и
            // архивирование должны сниматься как настоящие формы Picket302,
            // а не как макет/постановочный bitmap. Путь базы берётся только
            // из уже созданного изолированного состава harness.
            "search" => new Picket302.App.UI.Forms.FormSearch("гост", 3),
            "tablecolumns" => new Picket302.App.UI.Forms.FormTableSettings(
                окно.ТаблицаПолучателей,
                начальнаяВкладка: 0),
            "tableproperties" => new Picket302.App.UI.Forms.FormTableSettings(
                окно.ТаблицаПолучателей,
                начальнаяВкладка: 1),
            // Архивация открывается с путём базы из изолированного состава
            // harness и не затрагивает production-профиль.
            "backup" => new Picket302.App.UI.Forms.FormBackupRestore(состав.ПутьБазы),
            _ => null,
        };

        if (диалог is null)
        {
            Console.Out.WriteLine($"FAIL SHOW: неизвестный диалог '{имя}'");
            Console.Out.Flush();
            return;
        }

        диалог.StartPosition = FormStartPosition.Manual;
        диалог.Location = new Point(60, 60);
        ПодготовитьНеактивирующееОкно(диалог);
        диалог.Show();

        if (!НеактивирующийРежим())
        {
            диалог.BringToFront();
            диалог.Activate();
        }

        // Симуляция DPI (stage3.1): диалог, открытый при активной цели DPI,
        // перемасштабируется тем же реальным конвейером WM_DPICHANGED.
        ПрименитьЦельDpi(диалог);

        СнимкиЭкранов.ПоследнийДиалог = диалог;

        Console.Out.WriteLine($"OK SHOW {имя} {диалог.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL SHOW {имя}: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Открывает реальное вложенное меню «Вид → Фильтры» для reference-снимка.
/// Меню удерживается открытым до команды HIDE, как и рабочие формы.
/// </summary>
static void ПоказатьМенюФильтров(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        var вид = окно.MainMenuStrip?.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Вид");
        var фильтры = вид?.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Фильтры");

        if (фильтры is null)
        {
            Console.Out.WriteLine("FAIL FILTERMENU: пункт «Вид → Фильтры» не найден");
            Console.Out.Flush();
            return;
        }

        вид!.ShowDropDown();
        фильтры.ShowDropDown();
        var меню = фильтры.DropDown;

        if (меню is null || !меню.Visible)
        {
            Console.Out.WriteLine("FAIL FILTERMENU: dropdown не виден");
            Console.Out.Flush();
            return;
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK FILTERMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL FILTERMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Открывает меню быстрых фильтров с кнопки над таблицей получателей.
/// Кнопка использует тот же ToolStripDropDown, что и «Вид → Фильтры».
/// </summary>
static void ПоказатьМенюБыстрыхФильтров(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        if (!Нажать(окно, "Фильтры…"))
        {
            Console.Out.WriteLine("FAIL QUICKFILTERMENU: кнопка «Фильтры…» не найдена");
            Console.Out.Flush();
            return;
        }

        // MainShell хранит общий dropdown приватно: reflection здесь намерен
        // ограничен harness-слоем, чтобы acceptance захватывал именно тот
        // экземпляр, который открыл production-код кнопки.
        var пунктФильтров = окно.GetType()
            .GetField("_менюФильтров", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(окно) as ToolStripMenuItem;
        var меню = пунктФильтров?.DropDown;

        if (меню is null || !меню.Visible)
        {
            Console.Out.WriteLine("FAIL QUICKFILTERMENU: dropdown не виден");
            Console.Out.Flush();
            return;
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK QUICKFILTERMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL QUICKFILTERMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>Открывает реальное меню «Печать» для reference-снимка.</summary>
static void ПоказатьМенюПечати(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        var печать = окно.MainMenuStrip?.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Печать");

        if (печать is null)
        {
            Console.Out.WriteLine("FAIL PRINTMENU: пункт «Печать» не найден");
            Console.Out.Flush();
            return;
        }

        печать.ShowDropDown();
        var меню = печать.DropDown;

        if (меню is null || !меню.Visible)
        {
            Console.Out.WriteLine("FAIL PRINTMENU: dropdown не виден");
            Console.Out.Flush();
            return;
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK PRINTMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL PRINTMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Открывает настоящее контекстное меню строки адресной книги для reference-
/// снимка. Меню показывается над нижней частью таблицы, чтобы все пункты
/// оставались внутри клиентской области главного окна.
/// </summary>
static void ПоказатьМенюТаблицы(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        var таблица = окно.ТаблицаПолучателей;
        var меню = таблица.ContextMenuStrip;

        if (меню is null)
        {
            Console.Out.WriteLine("FAIL TABLEMENU: контекстное меню таблицы не создано");
            Console.Out.Flush();
            return;
        }

        var строка = таблица.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(кандидат => !кандидат.IsNewRow);

        if (строка is null)
        {
            Console.Out.WriteLine("FAIL TABLEMENU: таблица получателей пуста");
            Console.Out.Flush();
            return;
        }

        таблица.ClearSelection();
        строка.Selected = true;
        таблица.CurrentCell = строка.Cells[Math.Min(2, таблица.ColumnCount - 1)];

        var точка = new Point(
            Math.Min(180, Math.Max(20, таблица.ClientSize.Width - 20)),
            Math.Max(20, таблица.ClientSize.Height - 8));
        меню.Show(таблица, точка, ToolStripDropDownDirection.AboveRight);

        if (!меню.Visible)
        {
            Console.Out.WriteLine("FAIL TABLEMENU: меню не стало видимым");
            Console.Out.Flush();
            return;
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK TABLEMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL TABLEMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Включает native-стиль, запрещающий окну становиться foreground, только
/// для явно запрошенного неактивирующего acceptance-режима. Стиль задаётся
/// после создания HWND, но до первого Show(), поэтому окно не успевает
/// активироваться на короткое время.
/// </summary>
static void ПодготовитьНеактивирующееОкно(Form форма)
{
    if (!НеактивирующийРежим())
    {
        return;
    }

    форма.ShowInTaskbar = false;
    var hwnd = форма.Handle;
    var стиль = Платформенно.GetWindowLongPtr(hwnd, Платформенно.РасширенныйСтиль);
    var новыйСтиль = стиль.ToInt64() | Платформенно.СтильНеактивирующегоОкна;
    _ = Платформенно.SetWindowLongPtr(hwnd, Платформенно.РасширенныйСтиль, new IntPtr(новыйСтиль));
}

/// <summary>
/// Открывает настоящее выпадающее меню «Печать» для reference-снимка
/// (поверхность R3: legacy-print-menu.png). Меню держится открытым до
/// команды HIDE, как и рабочие диалоги снимков. В неактивирующем режиме
/// окну dropdown задаётся WS_EX_NOACTIVATE, чтобы меню не перехватило
/// foreground пользователя.
/// </summary>
static void ПоказатьМенюПечати(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        var печать = окно.MainMenuStrip?.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Печать");

        if (печать is null)
        {
            Console.Out.WriteLine("FAIL PRINTMENU: корневой пункт «Печать» не найден");
            Console.Out.Flush();
            return;
        }

        печать.ShowDropDown();
        var меню = печать.DropDown;

        if (меню is null || !меню.Visible)
        {
            Console.Out.WriteLine("FAIL PRINTMENU: dropdown не виден");
            Console.Out.Flush();
            return;
        }

        if (НеактивирующийРежим())
        {
            var hwndМеню = меню.Handle;
            var стильМеню = Платформенно.GetWindowLongPtr(hwndМеню, Платформенно.РасширенныйСтиль);
            _ = Платформенно.SetWindowLongPtr(
                hwndМеню,
                Платформенно.РасширенныйСтиль,
                new IntPtr(стильМеню.ToInt64() | Платформенно.СтильНеактивирующегоОкна));
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK PRINTMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL PRINTMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Открывает настоящее вложенное меню «Вид → Фильтры» для reference-снимка
/// (поверхность R5: legacy-address-filter-menu.png). Как и «Печать», меню
/// держится открытым до HIDE и в неактивирующем режиме получает
/// WS_EX_NOACTIVATE.
/// </summary>
static void ПоказатьМенюФильтров(MainShell окно)
{
    try
    {
        СнимкиЭкранов.ПоследнееМеню?.Close();
        СнимкиЭкранов.ПоследнееМеню = null;

        var вид = окно.MainMenuStrip?.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Вид");
        var фильтры = вид?.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(пункт => пункт.Text == "Фильтры");

        if (фильтры is null)
        {
            Console.Out.WriteLine("FAIL FILTERMENU: пункт «Вид → Фильтры» не найден");
            Console.Out.Flush();
            return;
        }

        вид!.ShowDropDown();
        фильтры.ShowDropDown();
        var меню = фильтры.DropDown;

        if (меню is null || !меню.Visible)
        {
            Console.Out.WriteLine("FAIL FILTERMENU: dropdown не виден");
            Console.Out.Flush();
            return;
        }

        if (НеактивирующийРежим())
        {
            var hwndМеню = меню.Handle;
            var стильМеню = Платформенно.GetWindowLongPtr(hwndМеню, Платформенно.РасширенныйСтиль);
            _ = Платформенно.SetWindowLongPtr(
                hwndМеню,
                Платформенно.РасширенныйСтиль,
                new IntPtr(стильМеню.ToInt64() | Платформенно.СтильНеактивирующегоОкна));
        }

        СнимкиЭкранов.ПоследнееМеню = меню;
        Console.Out.WriteLine($"OK FILTERMENU {меню.Handle}");
    }
    catch (Exception ошибка)
    {
        Console.Out.WriteLine($"FAIL FILTERMENU: {ошибка.Message}");
    }

    Console.Out.Flush();
}

/// <summary>
/// Считает созданные отправления: сценарий «получатель без адреса» обязан
/// доказать, что запись НЕ была частично создана.
/// </summary>
static void СчитатьОтправления(AppServices состав)
{
    var страница = состав.Письма
        .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 500))
        .GetAwaiter()
        .GetResult();

    Console.Out.WriteLine($"MAILCOUNT всего={страница.TotalCount}");
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


/// <summary>
/// Отправляет форме WM_DPICHANGED с целевым DPI: WinForms PerMonitorV2 сам
/// перемасштабирует шрифты, контролы и позицию (реальный конвейер
/// DPI-изменения, а не ручная правка числа ScreenDpi). Формы с текущим DPI,
/// равным цели, не трогаются; повторная утилизация прямоугольника
/// освобождается немедленно.
/// </summary>
static void ПрименитьЦельDpi(Form форма)
{
    var цель = СнимкиЭкранов.ЦелевойDpi;

    if (цель <= 0 || !форма.IsHandleCreated)
    {
        return;
    }

    var текущий = форма.DeviceDpi;

    if (текущий == цель)
    {
        return;
    }

    var множитель = (double)цель / текущий;
    var ширина = (int)Math.Round(форма.Width * множитель);
    var высота = (int)Math.Round(форма.Height * множитель);
    var лево = форма.Left - (ширина - форма.Width) / 2;
    var верх = форма.Top - (высота - форма.Height) / 2;

    var прямоугольник = new ПлатформенныеДанные.Прямоугольник
    {
        Лево = лево,
        Верх = верх,
        Право = лево + ширина,
        Низ = верх + высота,
    };

    var буфер = System.Runtime.InteropServices.Marshal.AllocHGlobal(
        System.Runtime.InteropServices.Marshal.SizeOf(прямоугольник));

    try
    {
        System.Runtime.InteropServices.Marshal.StructureToPtr(прямоугольник, буфер, fDeleteOld: false);
        var параметрW = (IntPtr)(цель & 0xFFFF | (цель & 0xFFFF) << 16);
        _ = Платформенно.SendMessageW(форма.Handle, Платформенно.СообщениеИзмененияDpi, параметрW, буфер);
    }
    finally
    {
        System.Runtime.InteropServices.Marshal.FreeHGlobal(буфер);
    }
}

/// <summary>
/// Критерии DPI-PASS §8.3 по каждой видимой форме: отрицательные/нулевые
/// размеры видимых кнопок и контролы, целиком вышедшие за границы
/// родительской клиентской области.
/// </summary>
static void ПроверитьКомпоновку()
{
    var формы = Application.OpenForms.OfType<Form>().Where(форма => форма.Visible).ToList();

    if (формы.Count == 0)
    {
        Console.Out.WriteLine("LAYOUTCHECK FAIL форм-нет");
        return;
    }

    var маркер = MainShell.МаркерРаскладкиПанелиОтображения;

    foreach (var форма in формы)
    {
        var нулевых = 0;
        var вне = 0;
        var примеры = new List<string>();
        var стек = new Stack<Control>();
        стек.Push(форма);

        while (стек.Count > 0)
        {
            var текущий = стек.Pop();

            foreach (Control дочерний in текущий.Controls)
            {
                стек.Push(дочерний);

                if (!дочерний.Visible)
                {
                    continue;
                }

                if (дочерний is Button && (дочерний.Width <= 0 || дочерний.Height <= 0))
                {
                    нулевых++;

                    if (примеры.Count < 5)
                    {
                        примеры.Add(
                            $"нулевая-кнопка:{дочерний.GetType().Name}:{дочерний.Bounds}");
                    }
                }

                // Layout с ещё не рассчитанной раскладкой (например, сразу после
                // рескала) даёт ложные «вне-родителя»; в пример добавляются тип
                // и границы для последующей диагностики.
                var клиент = текущий.ClientRectangle;

                if (дочерний.Right <= клиент.Left
                    || дочерний.Left >= клиент.Right
                    || дочерний.Bottom <= клиент.Top
                    || дочерний.Top >= клиент.Bottom)
                {
                    вне++;

                    if (примеры.Count < 5)
                    {
                        // Полная цепочка родителей: точное место дефекта в
                        // дереве контролов без повторных прогонов.
                        var цепочка = дочерний.GetType().Name;
                        var предок = текущий;

                        for (var уровень = 0; уровень < 6 && предок is not null; уровень++)
                        {
                            цепочка = $"{предок.GetType().Name}<{цепочка}";
                            предок = предок.Parent;
                        }

                        примеры.Add(
                            $"вне-родителя:{цепочка} ребёнок={дочерний.Bounds}"
                            + $" родитель-клиент={клиент}");
                    }
                }
            }
        }

        Console.Out.WriteLine(
            $"LAYOUTCHECK маркер={маркер} форма={форма.Name} size={форма.Width}x{форма.Height} dpi={форма.DeviceDpi} "
            + $"нулевыхКнопок={нулевых} вне={вне} "
            + $"вЭкране={ВГраницахЭкрана(форма)} "
            + $"примеры=[{string.Join(",", примеры)}]");
    }

    Console.Out.Flush();
}

/// <summary>
/// Критерий DPI-PASS §8.3 «модальные окна помещаются на экран»: границы
/// формы (в экранных координатах) целиком внутри виртуального экрана.
/// </summary>
static bool ВГраницахЭкрана(Form форма)
{
    var экран = System.Windows.Forms.Screen.FromControl(форма).Bounds;
    var границы = форма.DesktopBounds;

    return границы.Left >= экран.Left
        && границы.Top >= экран.Top
        && границы.Right <= экран.Right
        && границы.Bottom <= экран.Bottom;
}

/// <summary>
/// Ищет пункт меню по точному тексту во всех выпадающих меню полосы меню
/// главного окна (для сценариев, не имеющих кнопки на рабочей области).
/// </summary>
static System.Windows.Forms.ToolStripMenuItem? НайтиПунктМеню(Form окно, string текст)
{
    if (окно.MainMenuStrip is null)
    {
        return null;
    }

    foreach (System.Windows.Forms.ToolStripItem верхний in окно.MainMenuStrip.Items)
    {
        if (верхний is not System.Windows.Forms.ToolStripMenuItem выпадающее)
        {
            continue;
        }

        foreach (System.Windows.Forms.ToolStripItem пункт in выпадающее.DropDownItems)
        {
            if (пункт is System.Windows.Forms.ToolStripMenuItem командный && командный.Text == текст)
            {
                return командный;
            }
        }
    }

    return null;
}

/// <summary>
/// Смоук-регрессия UX3-01 (stage3.1, §7.10): в открытом диалоге клипарта
/// выделяется первый значок, проверяется активация подтверждения,
/// подтверждение нажимается (ShowDialog-путь приложения), диалог закрывается
/// и открывается повторно. Падение ленивого чтения ImageList проявляется
/// исключением и попадает в верхний catch harness.
/// </summary>
static void ВыбратьКлипарт()
{
    var диалог = СнимкиЭкранов.ПоследнийДиалог;

    if (диалог is null || диалог.IsDisposed)
    {
        Console.Out.WriteLine("FAIL CLIPSELECT: диалог не открыт");
        return;
    }

    var список = НайтиПотомка<System.Windows.Forms.ListView>(диалог);

    if (список is null)
    {
        Console.Out.WriteLine("FAIL CLIPSELECT: список значков не найден");
        return;
    }

    var элементов = список.Items.Count;
    var элементовСоЗначками = 0;

    // Пункты создаются с ImageKey (не ImageIndex): привязка значка доказывается
    // наличием ключа в ImageList, а не индексом.
    foreach (System.Windows.Forms.ListViewItem элемент in список.Items)
    {
        if (список.LargeImageList is not null
            && !string.IsNullOrEmpty(элемент.ImageKey)
            && список.LargeImageList.Images.ContainsKey(элемент.ImageKey))
        {
            элементовСоЗначками++;
        }
    }

    var выбрано = 0;

    if (список.Items.Count > 0)
    {
        список.Items[0].Selected = true;
        выбрано = список.SelectedIndices.Count;
    }

    var нажато = false;
    var активнаДоНажатия = false;

    foreach (var кнопка in НайтиПотомков<Button>(диалог))
    {
        if (кнопка.Text == "Выбрать")
        {
            активнаДоНажатия = кнопка.Enabled;
            кнопка.PerformClick();
            нажато = true;
            break;
        }
    }

    // Немодальная форма не закрывается по DialogResult (контракт ShowDialog):
    // диалог закрывается явно — Dispose прежнего экземпляра не должен ломать
    // ImageList повторного открытия.
    диалог.Close();
    var закрыт = диалог.IsDisposed;

    Console.Out.WriteLine(
        $"OK CLIPSELECT items={элементов} значки={элементовСоЗначками} "
        + $"выбрано={выбрано} активна={активнаДоНажатия} нажато={нажато} закрыт={закрыт}");
    Console.Out.Flush();
}

/// <summary>
/// Сценарий 7.8: оформление диалога вида таблиц до нажатия «Сбросить» и
/// после него. Сброс обязан вернуть цвета/кегль/высоту строки к значениям
/// по умолчанию контракта <see cref="Picket302.Domain.Contracts.GridAppearance"/>.
/// </summary>
static void СброситьОформление()
{
    var диалог = СнимкиЭкранов.ПоследнийДиалог;

    if (диалог is not Picket302.App.UI.Forms.FormGridAppearance форма)
    {
        Console.Out.WriteLine("FAIL GRIDRESET: диалог вида таблиц не открыт");
        return;
    }

    string Описать(Picket302.Domain.Contracts.GridAppearance вид) =>
        $"focus={вид.FocusColor} selected={вид.SelectedColor} hint={вид.HintColor} "
        + $"zebra={вид.ZebraEnabled} font={вид.FontSize:0.#} row={вид.RowHeight}";

    var до = форма.Оформление;

    var нажато = false;

    foreach (var кнопка in НайтиПотомков<Button>(форма))
    {
        if (кнопка.Text == "Сбросить")
        {
            кнопка.PerformClick();
            нажато = true;
            break;
        }
    }

    var после = форма.Оформление;

    Console.Out.WriteLine(
        $"OK GRIDRESET нажато={нажато} до=[{Описать(до)}] после=[{Описать(после)}]");
    Console.Out.Flush();
}

/// <summary>
/// Сеет конвертный шаблон приёмки (idempotent): сценарий «блокировка размера
/// бумаги» требует шаблон, бланк которого больше печатной области принтера
/// по запрошенному формату — PaperSizeValidator возвращает BLOCKED_SIZE до
/// передачи задания в спулер.
/// </summary>
static void ЗасеятьШаблонПриёмки(AppServices состав)
{
    var шаблоны = состав.ШаблоныПечати.GetAllAsync().GetAwaiter().GetResult();

    if (шаблоны.Any(элемент => элемент.Name == "Конверт C6 (приёмка)"))
    {
        Console.Out.WriteLine("OK SEEDTEMPLATE (уже задан)");
        return;
    }

    var идентификаторШаблона = состав.ШаблоныПечати.AddAsync(new PrintTemplate
    {
        Name = "Конверт C6 (приёмка)",
        Kind = PrintTemplateKind.Envelope,
        Format = "C6",
        WidthMm = 324m,
        HeightMm = 229m,
    }).GetAwaiter().GetResult();

    Console.Out.WriteLine($"OK SEEDTEMPLATE шаблон={идентификаторШаблона}");
    Console.Out.Flush();
}

/// <summary>Обход дерева контролов с накоплением потомков типа T.</summary>
static List<T> НайтиПотомков<T>(Control корень)
    where T : Control
{
    var результат = new List<T>();
    var стек = new Stack<Control>();
    стек.Push(корень);

    while (стек.Count > 0)
    {
        var текущий = стек.Pop();

        if (текущий is T совпадение && !ReferenceEquals(совпадение, корень))
        {
            результат.Add(совпадение);
        }

        foreach (Control дочерний in текущий.Controls)
        {
            стек.Push(дочерний);
        }
    }

    return результат;
}

/// <summary>Первый потомок типа T.</summary>
static T? НайтиПотомка<T>(Control корень)
    where T : Control
    => НайтиПотомков<T>(корень).FirstOrDefault();

/// <summary>
/// Хранилище снимков экрана: последний открытый диалог держится живым до
/// команды HIDE (иначе снимок невозможно снять).
/// </summary>
static class СнимкиЭкранов
{
    /// <summary>Последний открытый диалог снимков.</summary>
    public static Form? ПоследнийДиалог { get; set; }

    /// <summary>Последнее открытое для снимка выпадающее меню (ToolStripDropDown).</summary>
    public static ToolStripDropDown? ПоследнееМеню { get; set; }

    /// <summary>
    /// Целевой DPI симуляции (0 — выключено; 120 — 125 %; 144 — 150 %).
    /// Применяется реальной отправкой WM_DPICHANGED формам процесса.
    /// </summary>
    public static int ЦелевойDpi { get; set; }
}

/// <summary>
/// Принтерный менеджер acceptance-сценария «нет принтера» (stage3.1, §7.4):
/// возвращает null вместо принтера по умолчанию. Реальный системный принтер
/// стенда не изменяется; печать обязана блокироваться сообщением
/// <see cref="Picket302.Printing.Devices.PrinterManager.NoDefaultPrinterMessage"/>
/// в реальном коде <see cref="MainShell"/>.
/// </summary>
sealed class ПустойМенеджерПринтеров : Picket302.Printing.Devices.PrinterManager
{
    public override Picket302.Printing.Devices.PrinterDescriptor? GetDefaultPrinter() => null;
}

/// <summary>
/// Платформенные вызовы симуляции DPI: SendMessageW для WM_DPICHANGED.
/// </summary>
internal static class Платформенно
{
    private const int CbtHook = 5; // WH_CBT
    private const int CbtCreateWindow = 3; // HCBT_CREATEWND
    private const int CbtActivate = 5; // HCBT_ACTIVATE

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    // Делегат должен жить столько же, сколько native hook; static field
    // предотвращает сборку GC callback-а во время работы UI-потока.
    private static readonly HookProcedure НеактивирующийCallback = ОбработатьCbt;

    /// <summary>GWL_EXSTYLE.</summary>
    internal const int РасширенныйСтиль = -20;

    /// <summary>WS_EX_NOACTIVATE: окно не становится активным при показе.</summary>
    internal const long СтильНеактивирующегоОкна = 0x08000000L;

    internal static IntPtr УстановитьНеактивирующийПерехват()
    {
        var hook = SetWindowsHookEx(
            CbtHook,
            НеактивирующийCallback,
            IntPtr.Zero,
            GetCurrentThreadId());

        if (hook == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "SetWindowsHookEx(WH_CBT) не установил неактивирующий перехват.");
        }

        return hook;
    }

    internal static void СнятьНеактивирующийПерехват(IntPtr hook)
    {
        if (hook != IntPtr.Zero && !UnhookWindowsHookEx(hook))
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "UnhookWindowsHookEx не снял неактивирующий перехват.");
        }
    }

    private static IntPtr ОбработатьCbt(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (code == CbtCreateWindow || code == CbtActivate))
        {
            ПрименитьНеактивирующийСтиль(wParam);

            // HCBT_ACTIVATE вызывается непосредственно перед активацией.
            // Ненулевой результат запрещает активацию, но оставляет окно
            // доступным для EnumWindows/BM_CLICK/BitBlt драйвера.
            if (code == CbtActivate)
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static void ПрименитьНеактивирующийСтиль(IntPtr окно)
    {
        if (окно == IntPtr.Zero)
        {
            return;
        }

        var стиль = GetWindowLongPtr(окно, РасширенныйСтиль);
        var новыйСтиль = стиль.ToInt64() | СтильНеактивирующегоОкна;
        _ = SetWindowLongPtr(окно, РасширенныйСтиль, new IntPtr(новыйСтиль));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr окно, int индекс);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr окно, int индекс, IntPtr значение);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProcedure lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr SendMessageW(
        IntPtr окно,
        uint сообщение,
        IntPtr параметрW,
        IntPtr параметрL);

    /// <summary>WM_DPICHANGED: WinForms PerMonitorV2 перемасштабирует форму.</summary>
    internal const uint СообщениеИзмененияDpi = 0x02E0;
}



/// <summary>Платформенные структуры симуляции DPI.</summary>
internal static class ПлатформенныеДанные
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct Прямоугольник
    {
        public int Лево;
        public int Верх;
        public int Право;
        public int Низ;
    }
}
