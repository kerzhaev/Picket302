using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// S33 (ТЗ §8.2, функция 30): движение входящего документа — валидные
/// переходы (приём → передача → архив), история не переписывается (append-only),
/// текущее местонахождение видно.
/// </summary>
public sealed class IncomingMovementTrackerTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;
    private IncomingMailService _служба = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
        var профиль = Path.Combine(Path.GetTempPath(), "p302-s33-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(профиль);
        _служба = new IncomingMailService(
            new IncomingMailRepository(_база.UnitOfWork),
            профиль);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();
    }

    private IncomingMovementTracker СоздатьТрекер() => new(
        new IncomingMovementRepository(_база.UnitOfWork),
        new IncomingMailRepository(_база.UnitOfWork));

    private async Task<IncomingMail> ЗарегистрироватьДокументAsync(string номер)
        => await _служба.ЗарегистрироватьAsync(
            номер, DateTime.UtcNow, "Отправитель", "Тема", IncomingMailType.Letter);

    [Fact]
    public async Task ПолныйЦикл_ПриёмПередачаПередачаАрхив()
    {
        var документ = await ЗарегистрироватьДокументAsync("ВХ-Ц/1");
        var трекер = СоздатьТрекер();

        await трекер.ПриёмAsync(документ.Id, "Канцелярия");
        await трекер.ПередатьAsync(документ.Id, "Юридический отдел", "Смирнов");
        await трекер.ПередатьAsync(документ.Id, "Бухгалтерия");
        await трекер.АрхивироватьAsync(документ.Id);

        var история = await трекер.ИсторияAsync(документ.Id);

        Assert.Equal(4, история.Count);
        Assert.Equal(DocumentMovementAction.Reception, история[0].Action);
        Assert.Equal(DocumentMovementAction.Execution, история[1].Action);
        Assert.Equal("Юридический отдел", история[1].Department);
        Assert.Equal("Смирнов", история[1].Executor);
        Assert.Equal(DocumentMovementAction.Execution, история[2].Action);
        Assert.Equal(DocumentMovementAction.Archived, история[3].Action);

        // Текущее местонахождение — архив.
        var текущее = await трекер.ТекущееAsync(документ.Id);
        Assert.True(текущее.Архивен);
        Assert.Equal("Архив", текущее.Местонахождение);
    }

    [Fact]
    public async Task ТекущееМестонахождение_ПоМереЫ痣еВистории()
    {
        var документ = await ЗарегистрироватьДокументAsync("ВХ-М/1");
        var трекер = СоздатьТрекер();

        var нетПриёма = await трекер.ТекущееAsync(документ.Id);
        Assert.False(нетПриёма.Архивен);
        Assert.Equal("Не принят", нетПриёма.Местонахождение);
        Assert.Null(нетПриёма.Действие);

        await трекер.ПриёмAsync(документ.Id, "Канцелярия");

        var принят = await трекер.ТекущееAsync(документ.Id);
        Assert.False(принят.Архивен);
        Assert.Contains("Канцелярия", принят.Местонахождение);
        Assert.Equal(DocumentMovementAction.Reception, принят.Действие!.Action);

        await трекер.ПередатьAsync(документ.Id, "Отдел кадров");

        var передан = await трекер.ТекущееAsync(документ.Id);
        Assert.Contains("Отдел кадров", передан.Местонахождение);
        Assert.Equal(DocumentMovementAction.Execution, передан.Действие!.Action);
    }

    [Fact]
    public async Task ПовторныйПриём_Запрещён()
    {
        var документ = await ЗарегистрироватьДокументAsync("ВХ-П/1");
        var трекер = СоздатьТрекер();

        await трекер.ПриёмAsync(документ.Id, "Канцелярия");

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.ПриёмAsync(документ.Id, "Другая канцелярия"));

        Assert.Contains("уже зарегистрирован", ошибка.Message);

        // История не изменилась: по-прежнему одно движение.
        Assert.Single(await трекер.ИсторияAsync(документ.Id));
    }

    [Fact]
    public async Task ПередачаБезПриёма_Запрещена()
    {
        var документ = await ЗарегистрироватьДокументAsync("ВХ-БП/1");
        var трекер = СоздатьТрекер();

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.ПередатьAsync(документ.Id, "Отдел"));

        Assert.Contains("не принят", ошибка.Message);
        Assert.Empty(await трекер.ИсторияAsync(документ.Id));
    }

    [Fact]
    public async Task ДвижениеПослеАрхива_Запрещено()
    {
        var документ = await ЗарегистрироватьДокументAsync("ВХ-А/1");
        var трекер = СоздатьТрекер();

        await трекер.ПриёмAsync(документ.Id, "Канцелярия");
        await трекер.АрхивироватьAsync(документ.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.ПередатьAsync(документ.Id, "Отдел"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.АрхивироватьAsync(документ.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.ПриёмAsync(документ.Id, "Канцелярия"));

        // Ровно два движения: приём и архив.
        Assert.Equal(2, (await трекер.ИсторияAsync(документ.Id)).Count);
    }

    [Fact]
    public async Task НесуществующийДокумент_Отклоняется()
    {
        var трекер = СоздатьТрекер();

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => трекер.ПриёмAsync(424242, "Канцелярия"));

        Assert.Contains("не найден", ошибка.Message);
    }

    /// <summary>
    /// Критерий «история не переписывается»: поверх репозитория нет операций
    /// изменения/удаления — интерфейс append-only (закрепление контракта).
    /// </summary>
    [Fact]
    public void ПоверхностьРепозитория_ТолькоДополнениеИЧтение()
    {
        var методы = typeof(Picket302.Domain.Repositories.IIncomingMovementRepository)
            .GetMethods()
            .Select(метод => метод.Name)
            .ToList();

        Assert.Contains("AddAsync", методы);
        Assert.Contains("GetByIncomingAsync", методы);
        Assert.DoesNotContain("UpdateAsync", методы);
        Assert.DoesNotContain("DeleteAsync", методы);
        Assert.Equal(2, методы.Count);
    }
}
