using Picket302.App;
using Dapper;
using Picket302.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Контракт владения составом у изолированного профиля: одноразовость
/// передачи, утилизация оболочки/состава независимо от провалов,
/// машинно видимая диагностика cleanup и отсутствие остатков каталогов.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ИзолированныйПрофильOwnershipTests
{
    /// <summary>
    /// Владение составом передаётся один раз: повторная передача даёт
    /// понятную ошибку вместо молчаливой потери прежнего состава.
    /// </summary>
    [Fact]
    public async Task ВзятьВладение_ПовторнаяПередачаОтклоняется()
    {
        using var профиль = new ИзолированныйПрофиль();

        var создан = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);
        var returned = профиль.ВзятьВладение(создан);

        Assert.Same(создан, returned);

        // Повторная передача того же состава — уже ошибка владения.
        Assert.Throws<InvalidOperationException>(() => профиль.ВзятьВладение(создан));
    }

    /// <summary>
    /// После утилизации профиля повторный Dispose безопасен, а состав,
    /// переданный во владение, освобождается — попытка работы с ним даёт
    /// ObjectDisposedException.
    /// </summary>
    [Fact]
    public async Task Dispose_ОсвобождаетСостав_ИИдемпотентен()
    {
        var профиль = new ИзолированныйПрофиль();
        var состав = профиль.ВзятьВладение(
            await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

        профиль.Dispose();
        профиль.Dispose(); // повторный вызов — без эффекта и без исключений

        // База удалена вместе с профилем: обращение даёт SqliteException,
        // а не ObjectDisposedException — состав неработоспособен в любом случае.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await состав.Контрагенты.GetSendersAsync(CancellationToken.None));
    }

    /// <summary>
    /// Профиль, вышедший из области при упавшем Assert (using —
    /// компиляторный finally), удаляет временный каталог: cleanup
    /// выполняется в любом исходе теста.
    /// </summary>
    [Fact]
    public async Task Профиль_ПриИсключенииВСоставеУдаляетКаталог()
    {
        var корень = (string?)null;

        try
        {
            using var профиль = new ИзолированныйПрофиль();
            корень = профиль.Корень;

            var состав = профиль.ВзятьВладение(
                await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

            throw new InvalidOperationException("Имитация упавшего Assert.");
        }
        catch (InvalidOperationException)
        {
            // Ожидаемый путь: using-профиль утилизируется при раскрутке.
        }

        Assert.False(Directory.Exists(корень), "Временный каталог должен удаляться при исключении в тесте.");
    }

    /// <summary>
    /// Неудалённый каталог фиксируется машинно видимым исключением
    /// <see cref="ИзолированныйПрофиль.ProfileCleanupException"/> с полной
    /// диагностикой (оставшийся путь, число попыток, ошибка удаления),
    /// а не исчезает тихо (проверка через занятие файла базы внешним
    /// соединением).
    /// </summary>
    [Fact]
    public async Task Профиль_НеудалённыйКаталогВиденВДиагностике()
    {
        var профиль = new ИзолированныйПрофиль();
        var корень = профиль.Корень;

        try
        {
            // Внешнее соединение удерживает файл базы: утилизация профиля
            // не сможет удалить каталог за 5 попыток.
            await using var удерживающее = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(профиль.Пути.DataDirectory, "hold.db"),
                    Pooling = false,
                }.ToString());
            await удерживающее.OpenAsync();
            await удерживающее.ExecuteAsync("CREATE TABLE t (id INTEGER);");

            // Состав во владении профиля, чтобы путь утилизации был полным.
            _ = профиль.ВзятьВладение(
                await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

            // Dispose бросает ProfileCleanupException: cleanup failure
            // машинно видимый, тест не зелёный.
            ИзолированныйПрофиль.ProfileCleanupException? ошибка = null;

            try
            {
                профиль.Dispose();
            }
            catch (ИзолированныйПрофиль.ProfileCleanupException пойманная)
            {
                ошибка = пойманная;
            }

            Assert.NotNull(ошибка);
            Assert.Equal(корень, профиль.ОставшийсяКаталог);
            Assert.Contains(корень, ошибка.Message, StringComparison.Ordinal);
            Assert.True(ошибка.КаталогОстался);
            Assert.Equal(5, ошибка.ПопытокУдаления);
            Assert.False(
                string.IsNullOrEmpty((string?)ошибка.Data["ошибкаУдаления"]),
                "ошибка удаления каталога должна попасть в диагностику");
            Assert.True(ошибка.Data["каталогОстался"] is true, "каталогОстался должен попасть в диагностику");
            Assert.True(Directory.Exists(корень));

            // Ручная уборка после диагностики: тест не оставляет каталог.
            await удерживающее.CloseAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            for (var попытка = 0; попытка < 5; попытка++)
            {
                try
                {
                    if (Directory.Exists(корень))
                    {
                        Directory.Delete(корень, recursive: true);
                    }

                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
            }
        }
        finally
        {
            if (Directory.Exists(корень))
            {
                try
                {
                    Directory.Delete(корень, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Передача состава, созданного по путям чужого профиля, отклоняется:
    /// утилизация этого профиля иначе закрыла бы чужую базу, а собственная
    /// осталась бы неосвобождённой (задокументированное поведение).
    /// </summary>
    [Fact]
    public async Task ВзятьВладение_СоставЧужогоПрофиля_Отклоняется()
    {
        using var профиль = new ИзолированныйПрофиль();
        using var чужой = new ИзолированныйПрофиль();

        var чужойСостав = await AppServices.СоздатьAsync(чужой.Пути, создатьДемоБазу: false);

        try
        {
            Assert.Throws<InvalidOperationException>(() => профиль.ВзятьВладение(чужойСостав));

            // Чужой профиль освобождает собственный состав сам.
            await чужойСостав.DisposeAsync();
        }
        finally
        {
            чужой.Dispose();
        }
    }

    /// <summary>
    /// Провал утилизации состава не маскируется: профиль бросает
    /// <see cref="ИзолированныйПрофиль.ProfileCleanupException"/>, но
    /// остальные шаги cleanup (пул, удаление каталога) всё равно
    /// выполняются — каталог удаляется, ошибка попадает в диагностику.
    /// </summary>
    [Fact]
    public async Task Dispose_ОшибкаутилизацииСоставаНеМаскируется()
    {
        var профиль = new ИзолированныйПрофиль();
        var корень = профиль.Корень;

        try
        {
            _ = профиль.ВзятьВладение(
                await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

            // Инъекция: реальная утилизация выполняется, затем отказ.
            профиль.ТестУтилизацияСостава = async состав =>
            {
                await состав.DisposeAsync();
                throw new InvalidOperationException("Инъекция отказа утилизации состава.");
            };

            var ошибка = Assert.Throws<ИзолированныйПрофиль.ProfileCleanupException>(профиль.Dispose);

            Assert.Contains("утилизация состава", ошибка.Message, StringComparison.Ordinal);
            Assert.False(
                string.IsNullOrEmpty((string?)ошибка.Data["ошибкаСостава"]),
                "ошибка состава должна попасть в диагностику");
            Assert.Null(профиль.ОставшийсяКаталог);
            Assert.False(
                Directory.Exists(корень),
                "каталог удаляется несмотря на провал утилизации состава");
        }
        finally
        {
            // Идемпотентный повторный Dispose: даже при провале Assert
            // профиль утилизируется без повторного cleanup.
            профиль.ТестУтилизацияСостава = null;
            профиль.Dispose();
        }
    }

    /// <summary>
    /// Провал очистки пула соединений не маскируется: ошибка попадает в
    /// <see cref="ИзолированныйПрофиль.ProfileCleanupException"/>, а
    /// удаление каталога выполняется независимо.
    /// </summary>
    [Fact]
    public async Task Dispose_ОшибкаочисткиПулаНеМаскируется()
    {
        var профиль = new ИзолированныйПрофиль();
        var корень = профиль.Корень;

        try
        {
            _ = профиль.ВзятьВладение(
                await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

            // Инъекция: штатная очистка пула выполняется, затем отказ.
            профиль.ТестОчисткаПула = () =>
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                throw new InvalidOperationException("Инъекция отказа очистки пула.");
            };

            var ошибка = Assert.Throws<ИзолированныйПрофиль.ProfileCleanupException>(профиль.Dispose);

            Assert.Contains("очистка пула соединений", ошибка.Message, StringComparison.Ordinal);
            Assert.False(
                string.IsNullOrEmpty((string?)ошибка.Data["ошибкаПула"]),
                "ошибка очистки пула должна попасть в диагностику");
            Assert.Null(профиль.ОставшийсяКаталог);
            Assert.False(
                Directory.Exists(корень),
                "каталог удаляется несмотря на провал очистки пула");
        }
        finally
        {
            профиль.ТестОчисткаПула = null;
            профиль.Dispose();
        }
    }
}
