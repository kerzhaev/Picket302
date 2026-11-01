using System.Text;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Integration;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка импорта контрагентов из отдельной внешней БД и хранения пароля
/// подключения в DPAPI (ТЗ §8.5, функция 76: «Импорт контрагентов из отдельной
/// внешней БД»).
/// </summary>
public sealed class ExternalDbImporterTests
{
    [Fact]
    public void ImportFromExternalDb_ReadsCounterparties_AppliesColumnMapping()
    {
        var directory = CreateTempDirectory();

        try
        {
            var databasePath = Path.Combine(directory, "counterparties.sqlite");
            CreateExternalDatabase(databasePath);

            var map = ExternalDbColumnMap.CreateEmpty();
            map.NameColumn = "Наименование";
            map.InnColumn = "ИНН";
            map.PhoneColumn = "Телефон";
            map.PostalAddressColumn = "Адрес";

            var importer = new ExternalDbImporter();
            var result = importer.ImportFromExternalDb(
                BuildConnectionString(databasePath),
                DbKind.Sqlite,
                "counterparty",
                map);

            Assert.True(result.IsSuccess, result.Error);

            var batch = result.Value;
            Assert.Equal("Внешняя БД", batch.SourceFormat);
            Assert.Equal("counterparty", batch.SourceFile);
            Assert.Single(batch.Records);

            var record = batch.Records[0];
            Assert.Equal(1, record.RowNumber);

            var draft = record.Draft;
            Assert.Equal("ООО «Ромашка»", draft.Name);
            Assert.Equal("7701234567", draft.Inn);
            Assert.Equal("+7 495 123-45-67", draft.Phone);
            Assert.Equal("125167", draft.PostalZip);
            Assert.Equal("г. Москва", draft.PostalLines[0]);
            Assert.Equal("ул. Пушкина", draft.PostalLines[1]);
            Assert.Equal("д. 5", draft.PostalLines[2]);
            Assert.False(draft.NeedsVerification);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ImportFromExternalDb_ChoosesTableAndMappingAutomatically()
    {
        var directory = CreateTempDirectory();

        try
        {
            var databasePath = Path.Combine(directory, "counterparties.sqlite");
            CreateExternalDatabase(databasePath);

            var importer = new ExternalDbImporter();
            var result = importer.ImportFromExternalDb(
                BuildConnectionString(databasePath),
                DbKind.Sqlite);

            Assert.Equal("counterparty", result.Batch.SourceFile);
            Assert.Single(result.Batch.Records);

            var draft = result.Batch.Records[0].Draft;
            Assert.Equal("ООО «Ромашка»", draft.Name);
            Assert.Equal("7701234567", draft.Inn);
            Assert.Equal("+7 495 123-45-67", draft.Phone);
            Assert.Equal("125167", draft.PostalZip);
            Assert.StartsWith("Импорт Внешняя БД от ", draft.Source);
            Assert.EndsWith(" из counterparty", draft.Source);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CreateAuto_MapsColumnsByTheirNames()
    {
        var columns = new[]
        {
            "Наименование",
            "ИНН",
            "КПП",
            "Телефон",
            "E-mail",
            "Адрес",
            "Юридический адрес",
            "Фактический адрес",
            "Примечание",
        };

        var map = ExternalDbColumnMap.CreateAuto(columns);

        Assert.Equal("Наименование", map.NameColumn);
        Assert.Equal("ИНН", map.InnColumn);
        Assert.Equal("КПП", map.KppColumn);
        Assert.Equal("Телефон", map.PhoneColumn);
        Assert.Equal("E-mail", map.EmailColumn);
        Assert.Equal("Адрес", map.PostalAddressColumn);
        Assert.Equal("Юридический адрес", map.LegalAddressColumn);
        Assert.Equal("Фактический адрес", map.ActualAddressColumn);
        Assert.Equal("Примечание", map.NoteColumn);
    }

    [Fact]
    public void CreateAuto_ManualOverrideWins()
    {
        var map = ExternalDbColumnMap.CreateAuto(["Наименование", "Полное наименование юридического лица"]);
        map.NameColumn = "Полное наименование юридического лица";

        Assert.Equal("Полное наименование юридического лица", map.NameColumn);
        Assert.True(ExternalDbColumnMap.TryGuessField("ИНН", out var field));
        Assert.Equal(ExternalDbField.Inn, field);
    }

    [Fact]
    public void ListTablesAsync_ReturnsTablesOfExternalDatabase()
    {
        var directory = CreateTempDirectory();

        try
        {
            var databasePath = Path.Combine(directory, "counterparties.sqlite");
            CreateExternalDatabase(databasePath);

            var importer = new ExternalDbImporter();
            var result = importer.ListTablesAsync(BuildConnectionString(databasePath), DbKind.Sqlite);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Contains("counterparty", result.Value);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TestConnectionAsync_UnavailableDatabase_ReportsConnectionFailure()
    {
        var importer = new ExternalDbImporter();
        var connectionString = BuildUnavailableConnectionString();

        var result = importer.TestConnectionAsync(connectionString, DbKind.Sqlite);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("Не удалось подключиться к внешней базе данных", result.Error);
        Assert.True(
            result.Error!.Length > ExternalDbImporter.ConnectionFailedMessage.Length + 1,
            $"Ожидалась детализация исключения подключения, получено: {result.Error}");
    }

    [Fact]
    public void ImportFromExternalDb_UnavailableDatabase_ReportsConnectionFailure()
    {
        var importer = new ExternalDbImporter();
        var connectionString = BuildUnavailableConnectionString();

        var map = ExternalDbColumnMap.CreateEmpty();
        map.NameColumn = "Наименование";

        var result = importer.ImportFromExternalDb(connectionString, DbKind.Sqlite, "counterparty", map);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.StartsWith(ExternalDbImporter.ConnectionFailedMessage, result.Error);
        Assert.True(
            result.Error!.Length > ExternalDbImporter.ConnectionFailedMessage.Length + 1,
            $"Ожидалась детализация исключения подключения, получено: {result.Error}");

        var exception = Assert.Throws<ExternalDbException>(
            () => importer.ImportFromExternalDb(connectionString, DbKind.Sqlite));

        Assert.StartsWith(ExternalDbImporter.ConnectionFailedMessage, exception.Message);
    }

    [Fact]
    public void ImportFromExternalDb_ConnectionError_DoesNotLeakPassword()
    {
        const string password = "СекретныйПароль-42";
        var connectionString =
            $"Server=127.0.0.1;Port=1;Database=picket302;User Id=operator;Password={password};Connection Timeout=1;";

        var importer = new ExternalDbImporter();
        var map = ExternalDbColumnMap.CreateEmpty();
        map.NameColumn = "Наименование";

        var result = importer.ImportFromExternalDb(connectionString, DbKind.MySql, "counterparty", map);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("Не удалось подключиться к внешней базе данных", result.Error);
        Assert.DoesNotContain(password, result.Error);

        var exception = Assert.Throws<ExternalDbException>(
            () => importer.ImportFromExternalDb(connectionString, DbKind.MySql));

        Assert.DoesNotContain(password, exception.Message);
    }

    [Fact]
    public void RedactConnectionString_MasksPassword()
    {
        var redacted = ExternalDbImporter.RedactConnectionString(
            "Server=db;User Id=operator;Password=СекретныйПароль-42;Database=picket302");

        Assert.DoesNotContain("СекретныйПароль-42", redacted);
        Assert.Contains("Password=***", redacted);
    }

    [Fact]
    public void DpapiSecretStore_ProtectAndUnprotect_RoundTripsSecret()
    {
        var store = new DpapiSecretStore();
        const string secret = "Пароль-123!";

        var protectedData = store.Protect(secret);

        Assert.NotEmpty(protectedData);
        Assert.NotEqual(Encoding.UTF8.GetBytes(secret), protectedData);
        Assert.Equal(secret, store.Unprotect(protectedData));
    }

    [Fact]
    public void DpapiSecretStore_SaveAndLoad_StoresProtectedBlob()
    {
        var directory = CreateTempDirectory();

        try
        {
            var store = new DpapiSecretStore(directory);
            const string secret = "Пароль-456!";

            var path = store.Save("mysql-подключение", secret);

            Assert.True(File.Exists(path));
            Assert.NotEqual(Encoding.UTF8.GetBytes(secret), File.ReadAllBytes(path));
            Assert.True(store.Exists("mysql-подключение"));
            Assert.Equal(secret, store.Load("mysql-подключение"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void DpapiSecretStore_DefaultDirectory_IsProfileFolder()
    {
        var store = new DpapiSecretStore();

        Assert.EndsWith(
            Path.Combine(DpapiSecretStore.ApplicationFolderName, DpapiSecretStore.SecretsFolderName),
            store.DirectoryPath);
    }

    private static string BuildConnectionString(string databasePath) =>
        $"Data Source={databasePath};Pooling=False";

    /// <summary>
    /// Строка подключения к недоступной базе: папка с файлом не существует.
    /// </summary>
    private static string BuildUnavailableConnectionString() =>
        BuildConnectionString(Path.Combine(
            Path.GetTempPath(),
            "picket302-missing-" + Guid.NewGuid().ToString("N"),
            "no-such.sqlite"));

    private static void CreateExternalDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE counterparty (
                "Наименование" TEXT NOT NULL,
                "Сокращённое наименование" TEXT NULL,
                "ИНН" TEXT NULL,
                "КПП" TEXT NULL,
                "Телефон" TEXT NULL,
                "E-mail" TEXT NULL,
                "Адрес" TEXT NULL,
                "Юридический адрес" TEXT NULL,
                "Фактический адрес" TEXT NULL,
                "Примечание" TEXT NULL
            );

            INSERT INTO counterparty (
                "Наименование", "Сокращённое наименование", "ИНН", "КПП", "Телефон",
                "E-mail", "Адрес", "Юридический адрес", "Фактический адрес", "Примечание")
            VALUES (
                'ООО «Ромашка»', 'Ромашка', '7701234567', '770101001', '+7 495 123-45-67',
                'info@romashka.ru', '125167, г. Москва, ул. Пушкина, д. 5',
                '125167, г. Москва, ул. Пушкина, д. 5', '125167, г. Москва, ул. Пушкина, д. 5',
                'Постоянный клиент');
            """;

        command.ExecuteNonQuery();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "picket302-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}
