using Picket302.Application.Services;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// S27 (ТЗ §8.4, функция 56): менеджер пользовательских отчётов — регистрация
/// пакета «макет .lbk + query.xml», изоляция синтаксической ошибки валидации,
/// правка запроса с защитой данных (невалидный текст не сохраняется).
/// </summary>
public sealed class CustomReportManagerTests : IDisposable
{
    private readonly string _корень;
    private readonly CustomReportManager _менеджер;

    public CustomReportManagerTests()
    {
        _корень = Path.Combine(Path.GetTempPath(), "p302-user-reports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_корень);
        _менеджер = new CustomReportManager(_корень);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_корень, recursive: true);
        }
        catch (IOException)
        {
            // Каталог может быть занят антивирусом на мгновение — не ломает тест.
        }
    }

    [Fact]
    public async Task Регистрация_КопируетПакетИЗапросВКаталогПользователя()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK-фиктивный-пакет");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());

        var результат = await _менеджер.RegisterUserReport("Реестр клиентов", пакет, запрос);

        Assert.True(результат.IsValid);
        Assert.Empty(результат.Errors);
        Assert.True(_менеджер.Exists("Реестр клиентов"));

        var каталог = Path.Combine(_менеджер.UserReportsDirectory, "Реестр клиентов");
        Assert.True(File.Exists(Path.Combine(каталог, "макет.lbk")));
        Assert.True(File.Exists(Path.Combine(каталог, "query.xml")));
    }

    [Fact]
    public async Task ПовторноеИмя_Запрещено()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());

        await _менеджер.RegisterUserReport("Отчёт", пакет, запрос);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _менеджер.RegisterUserReport("Отчёт", пакет, запрос));
    }

    [Fact]
    public async Task ОтсутствующийПакет_ФайлНеНайден()
    {
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _менеджер.RegisterUserReport("Отчёт", Path.Combine(_корень, "нет.lbk"), запрос));
    }

    [Fact]
    public async Task ИмяСНедопустимымиЗнаками_Отклоняется()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _менеджер.RegisterUserReport("вышел/за/каталог", пакет, запрос));
    }

    /// <summary>
    /// ТЗ §8.4, функция 56: «Синтаксическая ошибка в пользовательском шаблоне
    /// изолируется и выводится в диалоге валидации» — регистрация с битым
    /// query.xml не бросает исключение и не повреждает данные: ошибки
    /// возвращаются списком в результате валидации.
    /// </summary>
    [Fact]
    public async Task СинтаксическаяОшибкаЗапроса_ИзолируетсяВРезультатеВалидации()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", "<не-закрытый xml");

        var результат = await _менеджер.RegisterUserReport("Битый отчёт", пакет, запрос);

        Assert.False(результат.IsValid);
        Assert.NotEmpty(результат.Errors);
        Assert.Equal("Битый отчёт", результат.ReportName);

        // Регистрация сохранена: технолог вправе исправить запрос правкой.
        Assert.True(_менеджер.Exists("Битый отчёт"));
    }

    [Fact]
    public async Task ПравкаНевалидногоЗапроса_НеСохраняетсяДанныеНеПовреждаются()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());
        await _менеджер.RegisterUserReport("Отчёт", пакет, запрос);

        var путь = Path.Combine(_менеджер.UserReportsDirectory, "Отчёт", "query.xml");
        var до = await File.ReadAllTextAsync(путь);

        var результат = await _менеджер.EditUserQueryAsync("Отчёт", "<сломанный");

        Assert.False(результат.IsValid);
        Assert.NotEmpty(результат.Errors);

        // Содержимое на диске не изменилось: правка не прошла валидацию.
        Assert.Equal(до, await File.ReadAllTextAsync(путь));
    }

    [Fact]
    public async Task ПравкаВалидногоЗапроса_Сохраняется()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());
        await _менеджер.RegisterUserReport("Отчёт", пакет, запрос);

        var новый = ВалидныйЗапрос().Replace("qrUser_Test", "qrUser_Updated");

        var результат = await _менеджер.EditUserQueryAsync("Отчёт", новый);

        Assert.True(результат.IsValid);

        var путь = Path.Combine(_менеджер.UserReportsDirectory, "Отчёт", "query.xml");
        Assert.Contains("qrUser_Updated", await File.ReadAllTextAsync(путь));
    }

    [Fact]
    public async Task Загрузка_ВозвращаетОпределениеЗапроса()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());
        await _менеджер.RegisterUserReport("Отчёт", пакет, запрос);

        var определение = await _менеджер.LoadUserQueryAsync("Отчёт");

        Assert.Equal("qrUser_Test", определение.Id);
    }

    [Fact]
    public async Task НезарегистрированноеИмя_ОтчетНеНайден()
    {
        Assert.False(_менеджер.Exists("нет такого"));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _менеджер.LoadUserQueryAsync("нет такого"));
    }

    [Fact]
    public async Task Перечень_ВозвращаетЗарегистрированныеОтчёты()
    {
        var пакет = СоздатьФайл("макет.lbk", "PK");
        var запрос = СоздатьФайл("источник-query.xml", ВалидныйЗапрос());
        await _менеджер.RegisterUserReport("Первый", пакет, запрос);
        await _менеджер.RegisterUserReport("Второй", пакет, запрос);

        var перечень = _менеджер.ListUserReports();

        Assert.Equal(2, перечень.Count);
        Assert.Contains(перечень, запись => запись.Name == "Первый");
        Assert.Contains(перечень, запись => запись.Name == "Второй");
        Assert.All(перечень, запись => Assert.EndsWith(".lbk", запись.TemplatePath, StringComparison.Ordinal));
        Assert.All(перечень, запись => Assert.EndsWith("query.xml", запись.QueryXmlPath, StringComparison.Ordinal));
    }

    /// <summary>Создаёт файл в корне проверки, возвращает путь.</summary>
    private string СоздатьФайл(string имя, string содержимое)
    {
        var путь = Path.Combine(_корень, имя);
        File.WriteAllText(путь, содержимое);

        return путь;
    }

    /// <summary>Валидный пользовательский запрос (ТЗ §4.1).</summary>
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
