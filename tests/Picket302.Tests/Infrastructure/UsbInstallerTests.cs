using System.Globalization;

using Microsoft.Data.Sqlite;

using Picket302.Infrastructure.Deployment;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка установки на USB-флешку (ТЗ §10.6, функции 105 «Установка на
/// USB-флешку» и 130 «Установка на USB-флешку (два варианта)»).
/// </summary>
/// <remarks>
/// Установка проверяется на временных папках: доступный объём памяти носителя
/// и копирование файлов подменяются, поэтому тест не зависит от наличия
/// флешки и не затрагивает файлы программы.
/// </remarks>
public sealed class UsbInstallerTests
{
    /// <summary>
    /// Режим «Только база данных» копирует файл базы и не копирует программу.
    /// </summary>
    [Fact]
    public void РежимТолькоБаза_КопируетФайлБазыИНеКопируетПрограмму()
    {
        using var флешка = new ВременнаяПапка();
        using var программа = new ВременнаяПапка();

        var путьБазы = СоздатьБазу(Path.Combine(программа.Путь, "picket302.db"));
        var исполняемый = Path.Combine(программа.Путь, "Picket302.App.exe");
        File.WriteAllText(исполняемый, "оболочка");

        var установщик = new UsbInstaller(путьБазы, программа.Путь, _ => 1_000_000_000);

        var результат = установщик.InstallToUsb(флешка.Путь, UsbInstallMode.DatabaseOnly);

        Assert.True(результат.IsSuccess, результат.Error);

        var целеваяБаза = Path.Combine(флешка.Путь, UsbInstaller.ИмяПапкиУстановки, UsbInstaller.ИмяФайлаБазы);
        Assert.True(File.Exists(целеваяБаза));

        Assert.False(File.Exists(Path.Combine(флешка.Путь, UsbInstaller.ИмяПапкиУстановки, "Picket302.App.exe")));

        // Копия базы — рабочая: данные перенесены механизмом VACUUM INTO.
        Assert.Equal("проверка", ПрочитатьЗначение(целеваяБаза));
    }

    /// <summary>
    /// Режим «Всё целиком» копирует файлы программы и файл базы.
    /// </summary>
    [Fact]
    public void РежимВсёЦеликом_КопируетФайлыПрограммыИБазу()
    {
        using var флешка = new ВременнаяПапка();
        using var программа = new ВременнаяПапка();

        var путьБазы = СоздатьБазу(Path.Combine(программа.Путь, "picket302.db"));
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.exe"), "оболочка");
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.dll"), "сборка");
        File.WriteAllText(Path.Combine(программа.Путь, "appsettings.json"), "{}");

        var установщик = new UsbInstaller(путьБазы, программа.Путь, _ => 1_000_000_000);

        var результат = установщик.InstallToUsb(флешка.Путь, UsbInstallMode.Full);

        Assert.True(результат.IsSuccess, результат.Error);

        var целеваяПапка = Path.Combine(флешка.Путь, UsbInstaller.ИмяПапкиУстановки);

        Assert.True(File.Exists(Path.Combine(целеваяПапка, "Picket302.App.exe")));
        Assert.True(File.Exists(Path.Combine(целеваяПапка, "Picket302.App.dll")));
        Assert.True(File.Exists(Path.Combine(целеваяПапка, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(целеваяПапка, UsbInstaller.ИмяФайлаБазы)));
    }

    /// <summary>
    /// Недостаток места на флешке блокирует установку: копирование не выполняется.
    /// </summary>
    [Fact]
    public void НедостаточноМеста_ВозвращаетОшибкуИНеКопирует()
    {
        using var флешка = new ВременнаяПапка();
        using var программа = new ВременнаяПапка();

        var путьБазы = СоздатьБазу(Path.Combine(программа.Путь, "picket302.db"));
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.exe"), "оболочка");

        var установщик = new UsbInstaller(путьБазы, программа.Путь, _ => 1);

        var результат = установщик.InstallToUsb(флешка.Путь, UsbInstallMode.Full);

        Assert.False(результат.IsSuccess);
        Assert.NotNull(результат.Error);
        Assert.Contains("Недостаточно места", результат.Error!, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(флешка.Путь, UsbInstaller.ИмяПапкиУстановки, UsbInstaller.ИмяФайлаБазы)));
    }

    /// <summary>
    /// Сбой копирования (извлечение флешки) приводит к откату: скопированные
    /// в рамках операции файлы удаляются.
    /// </summary>
    [Fact]
    public void СбойКопирования_ОткатываетЧастичноСкопированное()
    {
        using var флешка = new ВременнаяПапка();
        using var программа = new ВременнаяПапка();

        var путьБазы = СоздатьБазу(Path.Combine(программа.Путь, "picket302.db"));
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.exe"), "оболочка");
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.dll"), "сборка");

        var попыток = 0;

        var установщик = new UsbInstaller(
            путьБазы,
            программа.Путь,
            _ => 1_000_000_000,
            (источник, цель) =>
            {
                попыток++;

                if (попыток == 2)
                {
                    throw new IOException("Носитель извлечён");
                }

                File.Copy(источник, цель);
            });

        var результат = установщик.InstallToUsb(флешка.Путь, UsbInstallMode.Full);

        Assert.False(результат.IsSuccess);
        Assert.NotNull(результат.Error);
        Assert.Contains("прервана", результат.Error!, StringComparison.Ordinal);

        var целеваяПапка = Path.Combine(флешка.Путь, UsbInstaller.ИмяПапкиУстановки);

        Assert.False(File.Exists(Path.Combine(целеваяПапка, "Picket302.App.exe")));
        Assert.False(File.Exists(Path.Combine(целеваяПапка, "Picket302.App.dll")));
        Assert.False(File.Exists(Path.Combine(целеваяПапка, UsbInstaller.ИмяФайлаБазы)));
    }

    /// <summary>
    /// План установки показывает требуемый и доступный объём памяти носителя.
    /// </summary>
    [Fact]
    public void ПланУстановки_СодержитТребуемыйИДоступныйОбъём()
    {
        using var флешка = new ВременнаяПапка();
        using var программа = new ВременнаяПапка();

        var путьБазы = СоздатьБазу(Path.Combine(программа.Путь, "picket302.db"));
        File.WriteAllText(Path.Combine(программа.Путь, "Picket302.App.exe"), "оболочка");

        var установщик = new UsbInstaller(путьБазы, программа.Путь, _ => 777);

        var план = установщик.PlanInstall(флешка.Путь, UsbInstallMode.Full);

        Assert.Equal(UsbInstallMode.Full, план.Mode);
        Assert.Equal(777, план.AvailableBytes);
        Assert.True(план.RequiredBytes > 0);
        Assert.False(план.IsEnoughSpace);
        Assert.Contains(UsbInstaller.ИмяФайлаБазы, план.Files);
        Assert.Contains("Picket302.App.exe", план.Files);
    }

    /// <summary>
    /// Создаёт временную папку, удаляемую по завершении теста.
    /// </summary>
    private sealed class ВременнаяПапка : IDisposable
    {
        /// <summary>Путь к временной папке.</summary>
        public string Путь { get; }

        /// <summary>Создаёт временную папку.</summary>
        public ВременнаяПапка()
        {
            Путь = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "picket302_usb_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Путь);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Путь))
                {
                    Directory.Delete(Путь, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временные файлы остаются: тест не должен падать из-заcleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Временные файлы остаются: тест не должен падать из-за cleanup.
            }
        }
    }

    /// <summary>
    /// Создаёт файл базы данных SQLite с проверочной записью.
    /// </summary>
    /// <param name="путь">Путь к файлу базы.</param>
    /// <returns>Путь к созданному файлу базы.</returns>
    private static string СоздатьБазу(string путь)
    {
        var строка = new SqliteConnectionStringBuilder
        {
            DataSource = путь,
        }.ToString();

        using var соединение = new SqliteConnection(строка);
        соединение.Open();

        using var команда = соединение.CreateCommand();
        команда.CommandText = "CREATE TABLE demo (id INTEGER PRIMARY KEY, value TEXT);"
            + "INSERT INTO demo (value) VALUES ('проверка');";
        команда.ExecuteNonQuery();

        return путь;
    }

    /// <summary>
    /// Читает проверочное значение из копии базы.
    /// </summary>
    /// <param name="путь">Путь к файлу базы.</param>
    /// <returns>Значение записи.</returns>
    private static string? ПрочитатьЗначение(string путь)
    {
        var строка = new SqliteConnectionStringBuilder
        {
            DataSource = путь,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        using var соединение = new SqliteConnection(строка);
        соединение.Open();

        using var команда = соединение.CreateCommand();
        команда.CommandText = "SELECT value FROM demo WHERE id = 1;";

        var значение = команда.ExecuteScalar();

        return значение is null or DBNull ? null : Convert.ToString(значение, CultureInfo.InvariantCulture);
    }
}
