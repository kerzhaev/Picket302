using Picket302.Service;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Слияние настроек службы: отпечаток сертификата и порт приходят из
/// <c>service.json</c> после защиты каталога; явный аргумент командной
/// строки сильнее файла даже при значении по умолчанию.
/// </summary>
/// <remarks>
/// Регрессия P0-1: после перестройки запуска служба перестала читать
/// отпечаток сертификата из файла и всегда завершалась с кодом 2. Путь
/// Main не тестируется напрямую — проверяются его внутренние шаги.
/// </remarks>
public sealed class ServiceSettingsMergeTests
{
    [Fact]
    public void Настройки_Из_Файла_Сливаются_После_Защиты()
    {
        var каталог = Каталог();

        try
        {
            File.WriteAllText(
                Path.Combine(каталог, "service.json"),
                """{"Port":30243,"CertificateThumbprint":"ABCDEF123456"}""");

            var (параметры, портИзАргументов) = Program.ПрочитатьАргументы(new[] { "--data", каталог });

            Assert.Null(портИзАргументов);
            Assert.Equal(30242, параметры.Port);
            Assert.Null(параметры.CertificateThumbprint);

            Program.СлитьНастройки(параметры, портИзАргументов);

            // Отпечаток из файла обязателен: без него служба не запускается.
            Assert.Equal("ABCDEF123456", параметры.CertificateThumbprint);
            Assert.Equal(30243, параметры.Port);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Явный_Порт_Аргумента_Побеждает_Файл()
    {
        var каталог = Каталог();

        try
        {
            File.WriteAllText(
                Path.Combine(каталог, "service.json"),
                """{"Port":30243,"CertificateThumbprint":"ABCDEF123456"}""");

            // Явный аргумент, равный порту по умолчанию: файл не должен
            // его переопределить.
            var (параметры, портИзАргументов) = Program.ПрочитатьАргументы(
                new[] { "--data", каталог, "--port", "30242" });

            Assert.Equal(30242, портИзАргументов);

            Program.СлитьНастройки(параметры, портИзАргументов);

            Assert.Equal(30242, параметры.Port);
            Assert.Equal("ABCDEF123456", параметры.CertificateThumbprint);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Недопустимый_Порт_В_Файле_Игнорируется()
    {
        var каталог = Каталог();

        try
        {
            File.WriteAllText(
                Path.Combine(каталог, "service.json"),
                """{"Port":70000,"CertificateThumbprint":"ABCDEF123456"}""");

            var (параметры, портИзАргументов) = Program.ПрочитатьАргументы(new[] { "--data", каталог });

            Program.СлитьНастройки(параметры, портИзАргументов);

            Assert.Equal(30242, параметры.Port);
            Assert.Equal("ABCDEF123456", параметры.CertificateThumbprint);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    /// <summary>Временный каталог проверки.</summary>
    /// <returns>Путь каталога.</returns>
    private static string Каталог()
    {
        var каталог = Path.Combine(
            Path.GetTempPath(),
            "Picket302Settings_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(каталог);

        return каталог;
    }

    /// <summary>Удаляет временный каталог, терпя занятые файлы.</summary>
    /// <param name="каталог">Каталог.</param>
    private static void Удалить(string каталог)
    {
        try
        {
            Directory.Delete(каталог, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог не блокирует работу проверок.
        }
    }
}
