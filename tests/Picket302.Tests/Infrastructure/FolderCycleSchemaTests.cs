using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка пресечения циклических ссылок в дереве папок на уровне схемы
/// БД (ТЗ §3.1.11: «циклические ссылки parent_id пресекаются триггером/
/// валидатором домена»).
/// </summary>
/// <remarks>
/// Триггеры схемы — последний рубеж: доменный валидатор не защищает от
/// прямого SQL-запроса, которым выполняется импорт legacy-базы (§8.1).
/// </remarks>
public sealed class FolderCycleSchemaTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест со временным каталогом.</summary>
    public FolderCycleSchemaTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Picket302Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог не удалён — на результат теста не влияет.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Папка, которой назначается родитель с идентификатором, совпадающим с
    /// автоназначаемым идентификатором самой папки, образует самоссылку.
    /// Схема обязана отклонить такую вставку.
    /// </summary>
    /// <remarks>
    /// Дефект прежней редакции триггера: он срабатывал как BEFORE INSERT,
    /// где <c>NEW.id</c> при автоназначении равен <c>-1</c>, — условие
    /// «родитель равен самой папке» не выполнялось, и цикл создавался.
    /// </remarks>
    [Fact]
    public async Task Папка_СамоссылкаПриАвтоназначенииИдентификатораОтклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        // Первая папка получает идентификатор 1, следующая — 2.
        await база.Connection.ExecuteAsync("INSERT INTO folders (name) VALUES ('Корень');");

        // Родитель указан как 2 — тот же идентификатор, который будет назначен
        // вставляемой папке.
        var ошибка = await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "INSERT INTO folders (name, parent_id) VALUES ('Самоссылка', 2);"));

        Assert.Contains("Циклическая ссылка", ошибка.Message, StringComparison.OrdinalIgnoreCase);

        // Отклонённая вставка не оставляет следов.
        var папок = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM folders;");
        Assert.Equal(1, папок);

        var циклов = await база.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM folders WHERE id = parent_id;");
        Assert.Equal(0, циклов);
    }

    /// <summary>
    /// Перемещение папки в её собственного потомка отклоняется схемой.
    /// </summary>
    [Fact]
    public async Task Папка_ПеремещениеВСобственногоПотомкаОтклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var корень = await база.Connection.QuerySingleAsync<long>(
            "INSERT INTO folders (name) VALUES ('Корень'); SELECT last_insert_rowid();");

        var потомок = await база.Connection.QuerySingleAsync<long>(
            "INSERT INTO folders (name, parent_id) VALUES ('Потомок', @ParentId); SELECT last_insert_rowid();",
            new { ParentId = корень });

        var ошибка = await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "UPDATE folders SET parent_id = @ParentId WHERE id = @Id;",
            new { ParentId = потомок, Id = корень }));

        Assert.Contains("Циклическая ссылка", ошибка.Message, StringComparison.OrdinalIgnoreCase);

        var неИзменённый = await база.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM folders WHERE id = @Id AND parent_id IS NULL;",
            new { Id = корень });

        Assert.Equal(1, неИзменённый);
    }

    /// <summary>
    /// Обновление родителя в дереве, где цикл уже есть, завершается.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Повреждённое дерево возможно: восстановление архива с некорректным
    /// деревом либо импорт legacy-базы (§8.1), где ссылки загружаются без
    /// доменной валидации. Рекурсивный обход триггера обязан быть конечен
    /// на любом графе, иначе процесс зависает, удерживая блокировку базы.
    /// </para>
    /// <para>
    /// Оператор выполняется в отдельном соединении к отдельному файлу базы:
    /// при неисправном триггере он не завершается, и освобождение ресурсов
    /// теста не должно зависеть от него.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Папка_ОбновлениеНаЦиклическомДеревеЗавершается()
    {
        var путьБазы = Path.Combine(_каталог, "cycle.db");

        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьБазы))
        {
            await СоздатьЦиклическоеДеревоAsync(база);
        }

        var строка = new SqliteConnectionStringBuilder { DataSource = путьБазы }.ToString();
        var соединение = new SqliteConnection(строка);
        await соединение.OpenAsync();

        var задача = Task.Run(() => соединение.ExecuteAsync(
            "UPDATE folders SET parent_id = 1 WHERE id = 2;"));

        var завершилась = await Task.WhenAny(задача, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.True(
            завершилась == задача,
            "Обновление parent_id на дереве с циклом не завершилось — рекурсивный обход триггера не конечен.");

        // Перемещение под корневую папку цикл разрывает — ошибки быть не должно.
        await задача;

        await using var проверка = await SqliteTestDatabase.CreateFileAsync(путьБазы, сМиграциями: false);

        var циклов = await проверка.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM folders WHERE id = parent_id;");

        Assert.Equal(0, циклов);
    }

    /// <summary>
    /// Рекурсивный обход в триггерах дерева папок отбрасывает повторно
    /// посещённые узлы: без этого обход на циклическом графе не конечен.
    /// </summary>
    [Fact]
    public async Task ТриггерыПапок_ОбходОтбрасываетПовторноПосещённыеУзлы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var определения = (await база.Connection.QueryAsync<(string Name, string? Sql)>(
            "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'trg_folders_no_cycle_%' ORDER BY name;"))
            .ToList();

        Assert.Equal(2, определения.Count);

        foreach (var определение in определения)
        {
            Assert.DoesNotContain("UNION ALL", определение.Sql!, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Создаёт дерево папок с циклом в обход триггеров схемы — так выглядит
    /// повреждённая база, восстановленная из некорректного архива.
    /// </summary>
    /// <param name="база">Тестовая база данных.</param>
    /// <returns>Задача завершения.</returns>
    private static async Task СоздатьЦиклическоеДеревоAsync(SqliteTestDatabase база)
    {
        // Текст триггеров сохраняется и восстанавливается из самой схемы —
        // тест не дублирует определения триггеров.
        var определения = (await база.Connection.QueryAsync<(string Name, string? Sql)>(
            "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'trg_folders_no_cycle_%' ORDER BY name;"))
            .ToList();

        Assert.NotEmpty(определения);

        foreach (var определение in определения)
        {
            await база.Connection.ExecuteAsync($"DROP TRIGGER {определение.Name};");
        }

        // Папка 2 ссылается сама на себя.
        await база.Connection.ExecuteAsync(
            "INSERT INTO folders (id, name, parent_id) VALUES (1, 'Корень', NULL), (2, 'Цикл', 2);");

        foreach (var определение in определения)
        {
            await база.Connection.ExecuteAsync(определение.Sql!);
        }
    }
}
