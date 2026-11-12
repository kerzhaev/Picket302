using System.Windows.Forms;
using Picket302.Application.Services;
using Picket302.App.UI.Forms;
using Picket302.Infrastructure.Queries;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S27 (ТЗ §8.4, функция 56): диалог пользовательских отчётов — перечень,
/// регистрация с изоляцией ошибок валидации, предпросмотр данных поверх
/// текущей базы. COM, реестр, принтер и физическая печать не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormUserReportsTests : IAsyncLifetime
{
    private readonly string _корень;
    private SqliteTestDatabase _база = null!;

    public FormUserReportsTests()
    {
        _корень = Path.Combine(Path.GetTempPath(), "p302-user-reports-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_корень);
    }

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
        await ReportDataSeeder.SeedAsync(_база.Connection);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();

        try
        {
            Directory.Delete(_корень, recursive: true);
        }
        catch (IOException)
        {
            // Каталог может быть занят антивирусом на мгновение.
        }
    }

    [Fact]
    public void Список_ОтображаетЗарегистрированныеОтчёты()
    {
        var менеджер = СоздатьМенеджер();
        ЗарегистрироватьНапрямую(менеджер, "Реестр клиентов");

        using var форма = new FormUserReports(менеджер, Поставщик());

        Assert.Equal(1, форма.ОбновитьСписок());
    }

    [Fact]
    public async Task РегистрацияБитогоЗапроса_ОшибкаИзолируетсяИПоказывается()
    {
        var менеджер = СоздатьМенеджер();
        var пакет = Path.Combine(_корень, "макет.lbk");
        var запрос = Path.Combine(_корень, "источник.xml");
        File.WriteAllText(пакет, "PK");
        File.WriteAllText(запрос, "<не-закрытый xml");

        using var форма = new FormUserReports(менеджер, Поставщик());

        var сообщения = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => сообщения.Add(текст);

        var результат = await форма.ЗарегистрироватьAsync("Битый", пакет, запрос);

        Assert.False(результат.IsValid);
        Assert.NotEmpty(результат.Errors);

        // ТЗ §8.4, ф.56: ошибка выведена в диалоге валидации (текст сообщения
        // содержит перечень ошибок), приложение не прервано.
        var сообщение = Assert.Single(сообщения);
        Assert.Contains("Битый", сообщение);
        Assert.Equal(1, форма.ЧислоОтчётов);
    }

    [Fact]
    public async Task РегистрацияБезИмени_УведомляетИНеРегистрирует()
    {
        var менеджер = СоздатьМенеджер();

        using var форма = new FormUserReports(менеджер, Поставщик());

        var сообщения = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => сообщения.Add(текст);

        var результат = await форма.ЗарегистрироватьAsync(string.Empty, "пакет", "запрос");

        // Пустое имя отклоняется аргументной проверкой менеджера; ошибка
        // изолируется в результате валидации, ничего не регистрируется.
        Assert.False(результат.IsValid);
        Assert.Single(результат.Errors);
        Assert.Contains("ошибок 1", Assert.Single(сообщения));
        Assert.Equal(0, форма.ЧислоОтчётов);
    }

    [Fact]
    public async Task РегистрацияПовторногоИмени_ИзолируетсяВРезультате()
    {
        var менеджер = СоздатьМенеджер();
        var пакет = Path.Combine(_корень, "макет.lbk");
        var запрос = Path.Combine(_корень, "источник.xml");
        File.WriteAllText(пакет, "PK");
        File.WriteAllText(запрос, ВалидныйЗапрос());
        await менеджер.RegisterUserReport("Дубль", пакет, запрос);

        using var форма = new FormUserReports(менеджер, Поставщик());

        var сообщения = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => сообщения.Add(текст);

        var результат = await форма.ЗарегистрироватьAsync("Дубль", пакет, запрос);

        Assert.False(результат.IsValid);
        Assert.Contains("уже зарегистрирован", Assert.Single(сообщения));
        Assert.Equal(1, форма.ЧислоОтчётов);
    }

    [Fact]
    public async Task ПредпросмотрДанных_ПоказываетСтрокиТекущейБазы()
    {
        var менеджер = СоздатьМенеджер();
        ЗарегистрироватьНапрямую(менеджер, "Реестр групп");

        using var форма = new FormUserReports(менеджер, Поставщик());

        var текст = await форма.ПросмотрДанныхТекстомAsync("Реестр групп", 10);

        Assert.Contains("Реестр групп", текст);
        Assert.Contains("Клиенты", текст);
    }

    [Fact]
    public async Task ПредпросмотрБитогоОтчёта_ОшибкаИзолируетсяТекстом()
    {
        var менеджер = СоздатьМенеджер();
        var каталог = Path.Combine(менеджер.UserReportsDirectory, "Сломан");
        Directory.CreateDirectory(каталог);
        File.WriteAllText(Path.Combine(каталог, "query.xml"), "<обрыв");

        using var форма = new FormUserReports(менеджер, Поставщик());

        var текст = await форма.ПросмотрДанныхТекстомAsync("Сломан", 10);

        Assert.Contains("ошибка", текст);
    }

    [Fact]
    public async Task ПроверкаЗапроса_ВозвращаетДиагностику()
    {
        var менеджер = СоздатьМенеджер();
        ЗарегистрироватьНапрямую(менеджер, "Корректный");

        using var форма = new FormUserReports(менеджер, Поставщик());

        var текст = await форма.ПроверитьТекстомAsync("Корректный");

        Assert.Contains("запрос корректен", текст);
    }

    [Fact]
    public void ТекстВалидации_РазличаетУспехИОшибки()
    {
        var успех = new UserReportValidationResult(true, "Хороший", Array.Empty<string>());
        var ошибки = new UserReportValidationResult(false, "Плохой", new[] { "строка 2, позиция 4" });

        var текстУспеха = FormUserReports.ТекстВалидации(успех);
        var текстОшибки = FormUserReports.ТекстВалидации(ошибки);

        Assert.Contains("ошибок нет", текстУспеха);
        Assert.Contains("ошибок 1", текстОшибки);
        Assert.Contains("строка 2, позиция 4", текстОшибки);
    }

    [Fact]
    public void КнопкиДействий_НедоступныБезВыделения()
    {
        var менеджер = СоздатьМенеджер();

        using var форма = new FormUserReports(менеджер, Поставщик());

        Assert.Equal(0, форма.ЧислоОтчётов);
        Assert.Null(форма.ВыбранноеИмя);
    }

    /// <summary>Создаёт менеджер в каталоге проверки.</summary>
    private CustomReportManager СоздатьМенеджер() => new(_корень);

    /// <summary>Поставщик данных поверх тестовой базы.</summary>
    private SqliteReportDataProvider Поставщик() => new(_база.Connection);

    /// <summary>Регистрирует валидный отчёт напрямую через менеджер.</summary>
    private static void ЗарегистрироватьНапрямую(CustomReportManager менеджер, string имя)
    {
        var каталог = Path.Combine(менеджер.UserReportsDirectory, имя);
        Directory.CreateDirectory(каталог);
        File.WriteAllText(Path.Combine(каталог, "макет.lbk"), "PK");
        File.WriteAllText(Path.Combine(каталог, "query.xml"), ВалидныйЗапрос());
    }

    /// <summary>Валидный пользовательский запрос.</summary>
    private static string ВалидныйЗапрос() => """
        <?xml version="1.0" encoding="utf-8"?>
        <ReportQueryDefinition id="qrUser_Test" version="1.0">
          <Name>Тестовый отчёт</Name>
          <Parameters />
          <ValidationRules>
            <Rule type="SqlInjectionGuard" mode="StrictParametric" />
          </ValidationRules>
          <Command dialect="Sqlite">
            <![CDATA[SELECT name FROM groups ORDER BY name;]]>
          </Command>
        </ReportQueryDefinition>
        """;
}
