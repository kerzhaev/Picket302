using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Picket302.App.UI.Editors;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S39 (ТЗ §8.7, функция 97): визуальный редактор письма — команды
/// форматирования (чистый слой), очистка опасного HTML, шаблон по умолчанию
/// и совпадение «UI и MIME» (HTML редактора попадает в письмо без искажений).
/// COM, реестр, сеть (кроме локального приёмника) и печать не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class HtmlMailEditorTests
{
    /// <summary>Команды форматирования оборачивают выделение.</summary>
    [Theory]
    [InlineData("bold", "<b>", "</b>")]
    [InlineData("italic", "<i>", "</i>")]
    [InlineData("underline", "<u>", "</u>")]
    public void Форматирование_ОборачиваетВыделение(string команда, string открывающий, string закрывающий)
    {
        var ожидаемое = "текст " + открывающий + "выделение" + закрывающий + " хвост";

        var результат = КомандыРазметки.Применить("текст выделение хвост", команда, null, 6, 9);

        Assert.Equal(ожидаемое, результат.Html);
        Assert.Equal(6, результат.SelectionStart);
    }

    [Fact]
    public void Цвет_НормализуетсяВHex()
    {
        var результат = КомандыРазметки.Применить("текст", "foreColor", "FF0000", 0, 5);

        Assert.Contains("color:#FF0000", результат.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Списки_ОборачиваютФрагмент()
    {
        var маркированный = КомандыРазметки.Применить("пункт", "insertUnorderedList", null, 0, 5);

        Assert.Equal("<ul><li>пункт</li></ul>", маркированный.Html);

        var нумерованный = КомандыРазметки.Применить("пункт", "insertOrderedList", null, 0, 5);

        Assert.Equal("<ol><li>пункт</li></ol>", нумерованный.Html);
    }

    [Fact]
    public void Ссылка_ЗначениеАтрибутаЭкранируется()
    {
        var результат = КомандыРазметки.Применить(
            "сайт",
            "insertLink",
            "https://x.test/a\" onmouseover=\"alert(1)",
            0,
            4);

        // Кавычки в значении href экранированы — атрибут не разрывается.
        Assert.Contains("&quot;", результат.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("\" onmouseover=", результат.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ОчиститьФорматирование_СнимаетТеги()
    {
        var результат = КомандыРазметки.Применить(
            "<b>жирный</b> и <i>курсив</i>",
            "removeFormat",
            null,
            0,
            27);

        Assert.DoesNotContain("<b>", результат.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>", результат.Html, StringComparison.Ordinal);
        Assert.Contains("жирный", результат.Html, StringComparison.Ordinal);
        Assert.Contains("курсив", результат.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void НеизвестнаяКоманда_РазметкаБезИзменений()
    {
        var результат = КомандыРазметки.Применить("<p>текст</p>", "неизвестнаяКоманда", null, 0, 4);

        Assert.Equal("<p>текст</p>", результат.Html);
    }

    [Fact]
    public void ВыделениеЗаГраницами_Нормализуется()
    {
        var результат = КомандыРазметки.Применить("текст", "bold", null, 100, 50);

        // Начало сжато к длине текста; выделение пустое — команда оборачивает
        // пустой фрагмент (точка вставки готова к вводу жирным).
        Assert.Equal("текст<b></b>", результат.Html);
        Assert.Equal(5, результат.SelectionStart);
    }

    /// <summary>
    /// Критерий приёмки №97 «очистка опасного HTML»: script-блок удаляется
    /// целиком, обработчики событий и javascript:-схемы нейтрализуются,
    /// безопасное содержимое сохраняется.
    /// </summary>
    [Fact]
    public void ОчисткаОпасногоHtml_СкриптыИОбработчикиУдаляются()
    {
        var опасный = "<p>Привет <script>alert(1)</script>мир</p>"
            + "<iframe src=\"https://evil.test\"></iframe>"
            + "<img src=\"x\" onerror=\"alert(2)\">"
            + "<a href=\"javascript:alert(3)\">ссылка</a>";

        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(опасный);

        Assert.DoesNotContain("<script", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", очищенный, StringComparison.Ordinal);
        Assert.DoesNotContain("<iframe", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", очищенный, StringComparison.OrdinalIgnoreCase);

        // Безопасное содержимое сохранено.
        Assert.Contains("Привет мир", очищенный, StringComparison.Ordinal);
        Assert.Contains("src=\"x\"", очищенный, StringComparison.Ordinal);
    }

    /// <summary>
    /// F-S39R2-02 (регрессионные пины обходов санитизатора):
    /// вложенная пересборка «scr<script>ipt» снимается итерациями.
    /// </summary>
    [Fact]
    public void Санитизатор_ВложеннаяПересборкаСкрипта_Удаляется()
    {
        var опасный = "&lt;scr&lt;script&gt;alert(1)&lt;/script&gt;ipt&gt;alert(2)&lt;/script&gt;"
            .Replace("&lt;", "<").Replace("&gt;", ">");

        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(опасный);

        // Вся разметка снята: вывод — инертный текст без «<».
        Assert.DoesNotContain("<", очищенный, StringComparison.Ordinal);
        Assert.Contains("alert(1)", очищенный, StringComparison.Ordinal);
    }

    /// <summary>F-S39R2-02: незакрытый script-блок удаляется до EOF.</summary>
    [Fact]
    public void Санитизатор_НезакрытыйСкрипт_Удаляется()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml("<p>текст</p><script>alert(1)");

        Assert.DoesNotContain("script", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", очищенный, StringComparison.Ordinal);
        Assert.Contains("текст", очищенный, StringComparison.Ordinal);
    }

    /// <summary>F-S39R2-02: «script/src=…» (слэш в теге) удаляется.</summary>
    [Fact]
    public void Санитизатор_СкриптСоСлэшем_Удаляется()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(
            "<script/src=\"https://evil.test/x.js\">alert(1)</script>");

        Assert.DoesNotContain("alert", очищенный, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.test", очищенный, StringComparison.Ordinal);
    }

    /// <summary>F-S39R2-02: обработчик слитно с кавычкой (href=x"onclick=).</summary>
    [Fact]
    public void Санитизатор_ОбработчикСлитноСКавычкой_Удаляется()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(
            "<a href=\"x\"onclick=\"alert(1)\">ссылка</a>");

        Assert.DoesNotContain("onclick", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"x\"", очищенный, StringComparison.Ordinal);
    }

    /// <summary>
    /// Корректирующий проход: подтверждённый обход
    /// <c>&lt;img src=x ONERROR=alert(1)&gt;</c> (верхний регистр, значение
    /// без кавычек) — обработчик удаляется, img остаётся с src.
    /// </summary>
    [Fact]
    public void Санитизатор_ВерхнийРегистрОбработчикаБезКавычек_Удаляется()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml("<img src=x ONERROR=alert(1)>");

        Assert.DoesNotContain("onerror", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", очищенный, StringComparison.Ordinal);
        Assert.Contains("src=\"x\"", очищенный, StringComparison.Ordinal);
    }

    /// <summary>Смешанный регистр и одинарные кавычки в обработчике.</summary>
    [Fact]
    public void Санитизатор_СмешанныйРегистрОдинарныеКавычки_Удаляются()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(
            "<p OnError='alert(1)'>текст</p><a HREF=\'javascript:alert(2)\'>x</a>");

        Assert.DoesNotContain("onerror", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("текст", очищенный, StringComparison.Ordinal);
    }

    /// <summary>
    /// Слитный атрибут после кавычки (href="x"onclick=) — обработчик
    /// удаляется, безопасный href остаётся.
    /// </summary>
    [Fact]
    public void Санитизатор_СлитныйОбработчик_УдаляетсяБезопасныйHrefОстаётся()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(
            "<a href=\"https://ok.test/\"onclick=\"alert(1)\">ссылка</a>");

        Assert.DoesNotContain("onclick", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://ok.test/\"", очищенный, StringComparison.Ordinal);
    }

    /// <summary>Опасные URL-схемы (смешанный регистр, сущности) удаляются.</summary>
    [Fact]
    public void Санитизатор_ОпасныеСхемы_Удаляются()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(
            "<a href=\"JaVaScRiPt:alert(1)\">x</a>"
            + "<a href=\"java&#115;cript:alert(2)\">y</a>"
            + "<a href=\"data:text/html;base64,x\">z</a>");

        Assert.DoesNotContain("javascript", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", очищенный, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Недопустимый элемент: тег удаляется, содержимое остаётся.</summary>
    [Fact]
    public void Санитизатор_НедопустимыйЭлемент_ТегУдаляетсяСодержимоОстаётся()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml("до <foobar>текст внутри</foobar> после");

        Assert.DoesNotContain("foobar", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("текст внутри", очищенный, StringComparison.Ordinal);
        Assert.Contains("до ", очищенный, StringComparison.Ordinal);
        Assert.Contains(" после", очищенный, StringComparison.Ordinal);
    }

    /// <summary>Комментарий удаляется целиком.</summary>
    [Fact]
    public void Санитизатор_Комментарий_Удаляется()
    {
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml("а<!-- скрытый скрипт -->б");

        Assert.DoesNotContain("скрытый", очищенный, StringComparison.Ordinal);
        Assert.Contains("а", очищенный, StringComparison.Ordinal);
        Assert.Contains("б", очищенный, StringComparison.Ordinal);
    }

    /// <summary>
    /// Идемпотентность: повторная очистка уже очищенного не меняет разметку.
    /// </summary>
    [Fact]
    public void Санитизатор_Идемпотентен()
    {
        var опасный = "<p>Привет <script>alert(1)</script><img src=x onerror=alert(2)>мир</p>";

        var первый = КомандыРазметки.ОчиститьОпасныйHtml(опасный);
        var второй = КомандыРазметки.ОчиститьОпасныйHtml(первый);

        Assert.Equal(первый, второй);
    }

    /// <summary>
    /// Загрузка в редактор: и ТекущийHtml, и поле кода получают очищенную
    /// разметку (все пути формы — Готово/код/preview — безопасны).
    /// </summary>
    /// <summary>WebBrowser (ActiveX) требует STA: форма создаётся и
    /// управляется на выделенном STA-потоке.</summary>
    private static T НаSta<T>(Func<T> действие)
    {
        T результат = default!;

        var поток = new Thread(() => результат = действие());
        поток.SetApartmentState(ApartmentState.STA);
        поток.Start();
        поток.Join();

        return результат;
    }

    [Fact]
    public void Загрузить_КодПолеТожеОчищено()
    {
        var форма = НаSta(() => new HtmlMailEditor());

        НаSta<object?>(() =>
        {
            форма.Загрузить("<p>ок</p><script>alert(1)</script><img src=x onerror=alert(2)>");
            return null;
        });

        var текст = форма.ТекущийHtml;

        Assert.DoesNotContain("script", текст, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", текст, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ок</p>", текст, StringComparison.Ordinal);
    }

    /// <summary>Пустая разметка — без исключений.</summary>
    [Fact]
    public void ОчисткаОпасногоHtml_ПустаяСтрока()
    {
        Assert.Equal(string.Empty, КомандыРазметки.ОчиститьОпасныйHtml(string.Empty));
    }

    /// <summary>Шаблон по умолчанию не пуст и безопасен после очистки.</summary>
    [Fact]
    public void ШаблонПоУмолчанию_НеПустойИБезопасный()
    {
        var шаблон = HtmlMailEditor.ЗагрузитьШаблон();

        Assert.False(string.IsNullOrWhiteSpace(шаблон));

        // Allowlist-очистка: шаблон остаётся безопасным и содержимым,
        // head/meta (вне белого списка) удаляются.
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(шаблон);

        Assert.False(string.IsNullOrWhiteSpace(очищенный));
        Assert.DoesNotContain("<meta", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", очищенный, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            КомандыРазметки.ОчиститьОпасныйHtml(очищенный),
            очищенный,
            ignoreCase: false,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreAllWhiteSpace: false);
    }

    /// <summary>
    /// Критерий «UI и MIME совпадают»: HTML, собранный командами редактора,
    /// уходит в письмо (base64, BodyFormat.Html) без искажений — приёмник
    /// захватывает полное DATA (сервер S38).
    /// </summary>
    [Fact]
    public async Task РедакторHtml_СовпадаетСMimeПисьмом()
    {
        var шаг1 = КомандыРазметки.Применить("Уважаемый клиент!", "bold", null, 0, 17);
        var разметка = "<html><body><p>" + шаг1.Html + "</p></body></html>";

        using var сервер = new MimeStand();
        var порт = сервер.Запустить();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = порт,
            SslMode = SslMode.None,
            TimeoutSeconds = 15,
        });

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "UI и MIME",
            Body = разметка,
            BodyFormat = BodyFormat.Html,
        };

        var результат = клиент.SendOne(письмо);

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        // MIME-письмо содержит ИМЕННО HTML редактора (base64 → исходная строка).
        Assert.NotNull(сервер.ТелоПисьма);
        Assert.Contains("Content-Type: text/html; charset=\"utf-8\"", сервер.ТелоПисьма, StringComparison.Ordinal);

        var часть = сервер.ТелоПисьма!.Substring(
            сервер.ТелоПисьма.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
        var base64 = string.Concat(
            часть.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).TakeWhile(строка => строка != "."));

        Assert.Equal(разметка, Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
    }

    /// <summary>
    /// Локальный приёмник с захватом полного DATA — копия минимального
    /// диалога стенда (внутренние серверы S36/S38 приватны).
    /// </summary>
    public sealed class MimeStand : IDisposable
    {
        private readonly TcpListener _слушатель;
        private readonly Task _диалог;
        private readonly TaskCompletionSource _готов = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MimeStand()
        {
            _слушатель = new TcpListener(IPAddress.Loopback, 0);
            _слушатель.Start();
            _диалог = Task.Run(async () =>
            {
                using var клиент = await _слушатель.AcceptTcpClientAsync();
                await ОбслужитьAsync(клиент.GetStream());
            });
        }

        /// <summary>Полное тело DATA (MIME-текст).</summary>
        public string? ТелоПисьма { get; private set; }

        /// <summary>Запускает приёмник и возвращает порт.</summary>
        public int Запустить()
        {
            var порт = ((IPEndPoint)_слушатель.LocalEndpoint).Port;
            _ = _готов.Task.Wait(TimeSpan.FromSeconds(5));

            return порт;
        }

        private async Task ОбслужитьAsync(NetworkStream поток)
        {
            using var читатель = new StreamReader(поток, Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var писатель = new StreamWriter(поток, new ASCIIEncoding(), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };

            await писатель.WriteLineAsync("220 mime-stand ESMTP ready").ConfigureAwait(false);
            _готов.TrySetResult();

            var состояние = "старт";
            var тело = new StringBuilder();

            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
            {
                switch (состояние)
                {
                    case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal)
                        || команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.Equals("DATA", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("354 End data").ConfigureAwait(false);

                        while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                        {
                            тело.AppendLine(строка);
                        }

                        ТелоПисьма = тело.ToString();
                        await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH LOGIN", StringComparison.Ordinal):
                        состояние = "ждётЛогин";
                        await писатель.WriteLineAsync("334 Username:").ConfigureAwait(false);
                        continue;

                    case "ждётЛогин":
                        состояние = "ждётПароль";
                        await писатель.WriteLineAsync("334 Password:").ConfigureAwait(false);
                        continue;

                    case "ждётПароль":
                        состояние = "авторизован";
                        await писатель.WriteLineAsync("235 Authentication successful").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal)
                        || команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.Equals("DATA", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("354 End data").ConfigureAwait(false);

                        while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                        {
                            тело.AppendLine(строка);
                        }

                        ТелоПисьма = тело.ToString();
                        await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.Equals("QUIT", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                        return;

                    default:
                        await писатель.WriteLineAsync("500 Unknown").ConfigureAwait(false);
                        return;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                _слушатель.Stop();
            }
            catch (SocketException)
            {
                // Повторная остановка безопасна.
            }
        }
    }
}
