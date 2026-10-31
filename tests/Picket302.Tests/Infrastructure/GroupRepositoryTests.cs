using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки хранения группы по умолчанию: правило «ровно одна группа по
/// умолчанию» (ТЗ §3.1.2) не должно нарушаться через операции хранилища.
/// </summary>
public sealed class GroupRepositoryTests
{
    /// <summary>
    /// Группу по умолчанию удалить нельзя: справочник обязан содержать ровно
    /// одну группу по умолчанию (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public async Task ГруппуПоУмолчаниюУдалитьНельзя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var поУмолчанию = await группы.GetDefaultAsync();
        Assert.NotNull(поУмолчанию);

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => группы.DeleteAsync(поУмолчанию!.Id));

        Assert.Contains("по умолчанию", ошибка.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await группы.GetByIdAsync(поУмолчанию.Id));
        Assert.Single(await группы.GetAllAsync(), группа => группа.IsDefault);
    }

    /// <summary>
    /// Признак группы по умолчанию нельзя снять обычным изменением карточки:
    /// смена группы по умолчанию выполняется только назначением (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public async Task ПризнакГруппыПоУмолчаниюНельзяСнятьОбычнымИзменением()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var поУмолчанию = await группы.GetDefaultAsync();
        Assert.NotNull(поУмолчанию);

        поУмолчанию!.IsDefault = false;

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => группы.UpdateAsync(поУмолчанию));

        Assert.Contains("Нельзя снять признак группы по умолчанию", ошибка.Message, StringComparison.Ordinal);
        Assert.True((await группы.GetByIdAsync(поУмолчанию.Id))!.IsDefault);
        Assert.Single(await группы.GetAllAsync(), группа => группа.IsDefault);
    }

    /// <summary>
    /// После назначения другой группы по умолчанию прежнюю группу удалить
    /// можно — правило «ровно одна» сохраняется.
    /// </summary>
    [Fact]
    public async Task ПрежнююГруппуПоУмолчаниюМожноУдалитьПослеНазначенияНовой()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var прежняя = await группы.GetDefaultAsync();
        Assert.NotNull(прежняя);

        var новая = new Group { Name = "Поставщики" };
        await группы.AddAsync(новая);

        Assert.True(await группы.SetDefaultAsync(новая.Id));
        Assert.True(await группы.DeleteAsync(прежняя!.Id));

        var все = await группы.GetAllAsync();
        Assert.Single(все);
        Assert.Equal(новая.Id, все[0].Id);
        Assert.True(все[0].IsDefault);
    }

    /// <summary>
    /// Обычную группу удаление не блокирует.
    /// </summary>
    [Fact]
    public async Task ОбычнуюГруппуУдалитьМожно()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var группа = new Group { Name = "Поставщики" };
        await группы.AddAsync(группа);

        Assert.True(await группы.DeleteAsync(группа.Id));
        Assert.Null(await группы.GetByIdAsync(группа.Id));
    }
}
