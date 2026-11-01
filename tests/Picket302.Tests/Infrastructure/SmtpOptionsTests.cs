using Picket302.Domain.Email;
using Picket302.Infrastructure.Integration;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка настроек почтового клиента (ТЗ §8.7, функция 93:
/// «настройка сервера, порта, логина и пароля»; «проверка связи с сервером —
/// по кнопке в настройках»).
/// </summary>
public sealed class SmtpOptionsTests
{
    [Fact]
    public void ValidateSmtpSettings_ПустойСервер_ВозвращаетОшибкуСерверНеУказан()
    {
        var опции = new SmtpOptions();

        var результат = опции.ValidateSmtpSettings(new SmtpSettings { Server = string.Empty, Port = 25 });

        Assert.False(результат.IsValid);
        Assert.True(результат.HasErrors);
        Assert.Contains("SMTP-сервер не указан", результат.Summary(), StringComparison.Ordinal);
        Assert.Equal(
            SmtpSettings.ServerNotSpecifiedMessage,
            результат.ErrorFor(nameof(SmtpSettings.Server)));
    }

    [Fact]
    public void ValidateSmtpSettings_КорректныеНастройки_РезультатБезОшибок()
    {
        var опции = new SmtpOptions();

        var результат = опции.ValidateSmtpSettings(new SmtpSettings
        {
            Server = "smtp.example.ru",
            Port = 587,
            Login = "user",
            SslMode = SslMode.StartTls,
        });

        Assert.True(результат.IsValid);
        Assert.False(результат.HasErrors);
        Assert.Empty(результат.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ValidateSmtpSettings_НекорректныйПорт_ВозвращаетОшибку(int порт)
    {
        var опции = new SmtpOptions();

        var результат = опции.ValidateSmtpSettings(new SmtpSettings
        {
            Server = "smtp.example.ru",
            Port = порт,
        });

        Assert.False(результат.IsValid);
        Assert.NotEqual(string.Empty, результат.ErrorFor(nameof(SmtpSettings.Port)));
    }

    [Fact]
    public void ValidateSmtpSettings_ЗаданПарольБезЛогина_ВозвращаетОшибку()
    {
        var опции = new SmtpOptions();

        var результат = опции.ValidateSmtpSettings(new SmtpSettings
        {
            Server = "smtp.example.ru",
            Port = 25,
            Login = string.Empty,
            ProtectedPassword = опции.ProtectPassword("пароль"),
        });

        Assert.False(результат.IsValid);
        Assert.NotEqual(string.Empty, результат.ErrorFor(nameof(SmtpSettings.Login)));
    }

    [Fact]
    public void Настройки_ПарольХранитсяВЗащищённомВиде_ОткрытоеЗначениеВНастройкахОтсутствует()
    {
        var опции = new SmtpOptions();
        var защищённый = опции.ProtectPassword("ОткрытыйПароль");

        Assert.NotEqual("ОткрытыйПароль", защищённый);
        Assert.DoesNotContain("ОткрытыйПароль", защищённый, StringComparison.Ordinal);
        Assert.Equal("ОткрытыйПароль", опции.UnprotectPassword(защищённый));
    }

    [Fact]
    public void Save_ПарольВЗащищённомВиде_ВФайлеНетОткрытогоЗначения()
    {
        var папка = СоздатьВременнуюПапку();

        try
        {
            var путь = Path.Combine(папка, "mail-settings.json");
            var опции = new SmtpOptions();

            var защищённый = опции.ProtectPassword("ОткрытыйПароль");

            опции.Save(new SmtpSettings
            {
                Server = "smtp.example.ru",
                Port = 587,
                Login = "user",
                ProtectedPassword = защищённый,
            }, путь);

            var содержимое = File.ReadAllText(путь);

            // Открытое значение пароля в файле отсутствует; хранится только
            // защищённый блок (в JSON символы Base64 кодируются escape-последовательностями).
            Assert.DoesNotContain("ОткрытыйПароль", содержимое, StringComparison.Ordinal);
            Assert.Contains("ProtectedPassword", содержимое, StringComparison.Ordinal);

            var прочитанные = опции.Load(путь);

            Assert.Equal("smtp.example.ru", прочитанные.Server);
            Assert.Equal(587, прочитанные.Port);
            Assert.Equal("user", прочитанные.Login);
            Assert.Equal(защищённый, прочитанные.ProtectedPassword);
            Assert.Equal("ОткрытыйПароль", опции.UnprotectPassword(прочитанные.ProtectedPassword));
        }
        finally
        {
            УдалитьПапку(папка);
        }
    }

    [Fact]
    public void Load_ФайлНастроекОтсутствует_ВозвращаетПустыеНастройки()
    {
        var папка = СоздатьВременнуюПапку();

        try
        {
            var опции = new SmtpOptions();
            var настройки = опции.Load(Path.Combine(папка, "нет-такого-файла.json"));

            Assert.False(настройки.HasServer);
            Assert.Equal(SmtpSettings.DefaultPort, настройки.Port);
        }
        finally
        {
            УдалитьПапку(папка);
        }
    }

    [Fact]
    public void ProbeConnection_СерверНеУказан_ВозвращаетОшибкуБезСоединения()
    {
        var опции = new SmtpOptions();

        var результат = опции.ProbeConnection(new SmtpSettings { Server = string.Empty }, CancellationToken.None);

        Assert.True(результат.IsFailure);
        Assert.Equal(SmtpSettings.ServerNotSpecifiedMessage, результат.Error);
    }

    [Fact]
    public void ProtectPassword_ИUnprotectPassword_ЗначениеВосстанавливается()
    {
        var опции = new SmtpOptions(new DpapiSecretStore());

        var защищённый = опции.ProtectPassword("ОткрытыйПароль");

        Assert.False(string.IsNullOrWhiteSpace(защищённый));
        Assert.Equal("ОткрытыйПароль", опции.UnprotectPassword(защищённый));
    }

    /// <summary>
    /// Создаёт временную папку для проверки файла настроек.
    /// </summary>
    /// <returns>Путь к временной папке.</returns>
    private static string СоздатьВременнуюПапку()
    {
        var путь = Path.Combine(Path.GetTempPath(), "picket302-smtp-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(путь);

        return путь;
    }

    /// <summary>
    /// Удаляет временную папку с проверочными файлами.
    /// </summary>
    /// <param name="путь">Путь к папке.</param>
    private static void УдалитьПапку(string путь)
    {
        if (Directory.Exists(путь))
        {
            Directory.Delete(путь, recursive: true);
        }
    }
}
