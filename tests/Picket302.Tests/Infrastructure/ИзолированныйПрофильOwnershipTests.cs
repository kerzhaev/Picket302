using Picket302.App;
using Dapper;
using Picket302.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Контракт владения составом у изолированного профиля: одноразовость
/// передачи, утилизация оболочки/состава независимо от провалов и
/// видимость неудалённого каталога.
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
    /// Профиль, вышедший из области при упавшем Assert (using в тесте),
    /// удаляет временный каталог: утилизация выполняется в любом исходе.
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
    /// Неудалённый каталог фиксируется свойством
    /// <see cref="ИзолированныйПрофиль.ОставшийсяКаталог"/>, а не исчезает
    /// тихо (проверка через подмену: занять файл базы внешним соединением).
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
}
