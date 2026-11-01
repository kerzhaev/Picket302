using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Storage;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки сохранённых пользовательских фильтров (ТЗ §8.1, функция 9:
/// «сохранённые пользовательские фильтры»).
/// </summary>
public sealed class SavedFilterServiceTests : IDisposable
{
    private readonly string _путьФайла = Path.Combine(
        Path.GetTempPath(),
        $"picket302_filters_{Guid.NewGuid():N}.json");

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_путьФайла))
        {
            File.Delete(_путьФайла);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт критерии отбора для сохранения.
    /// </summary>
    /// <returns>Критерии с текстовым условием и сортировкой.</returns>
    private static FilterCriteria Критерии() => new()
    {
        Conditions =
        [
            new FilterCriteria.Condition
            {
                Field = "name",
                Operator = FilterOperator.Contains,
                Value = "Аптека",
            },
        ],
        SortColumn = "name",
        SortDescending = true,
    };

    /// <summary>
    /// Сохранённый фильтр переживает перезапуск программы: он записывается в
    /// файл настроек и читается новым экземпляром хранилища.
    /// </summary>
    [Fact]
    public async Task СохранённыйФильтр_ПереживаетПерезапуск()
    {
        var первый = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));
        await первый.СохранитьAsync("Мои аптеки", Критерии());

        // Новый экземпляр хранилища — как после перезапуска программы.
        var второй = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));
        var фильтры = await второй.ПолучитьСписокAsync();

        var сохранённый = Assert.Single(фильтры);
        Assert.Equal("Мои аптеки", сохранённый.Name);
        Assert.Equal("name", сохранённый.Criteria.SortColumn);
        Assert.True(сохранённый.Criteria.SortDescending);
        Assert.Equal("Аптека", Assert.Single(сохранённый.Criteria.Conditions).Value);

        // Фильтр применяется: условия возвращаются без изменений.
        var применённые = await второй.ПолучитьКритерииAsync("Мои аптеки");

        Assert.NotNull(применённые);
        Assert.Equal("name", применённые!.SortColumn);
        Assert.True(применённые.SortDescending);
        Assert.Equal("Аптека", Assert.Single(применённые.Conditions).Value);
    }

    /// <summary>
    /// Сохранение под занятым именем обновляет фильтр — имена уникальны в
    /// пределах набора.
    /// </summary>
    [Fact]
    public async Task СохранениеПодЗанятымИменем_ОбновляетФильтр()
    {
        var служба = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));

        await служба.СохранитьAsync("Аптеки", Критерии());
        await служба.СохранитьAsync("Аптеки", new FilterCriteria { SortColumn = "inn" });

        var фильтры = await служба.ПолучитьСписокAsync();

        var сохранённый = Assert.Single(фильтры);
        Assert.Equal("Аптеки", сохранённый.Name);
        Assert.Equal("inn", сохранённый.Criteria.SortColumn);
    }

    /// <summary>
    /// Переименование фильтра сохраняет его условия.
    /// </summary>
    [Fact]
    public async Task ПереименованиеФильтра_СохраняетУсловия()
    {
        var служба = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));
        await служба.СохранитьAsync("Аптеки", Критерии());

        Assert.True(await служба.ПереименоватьAsync("Аптеки", "Избранные аптеки"));

        var фильтры = await служба.ПолучитьСписокAsync();

        var сохранённый = Assert.Single(фильтры);
        Assert.Equal("Избранные аптеки", сохранённый.Name);
        Assert.Equal("name", сохранённый.Criteria.SortColumn);
        Assert.Equal("Аптека", Assert.Single(сохранённый.Criteria.Conditions).Value);
    }

    /// <summary>
    /// Переименование не выполняется, если новое имя уже занято или прежнего
    /// имени нет в наборе.
    /// </summary>
    [Fact]
    public async Task ПереименованиеНаЗанятоеИмя_НеВыполняется()
    {
        var служба = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));
        await служба.СохранитьAsync("Аптеки", Критерии());
        await служба.СохранитьAsync("Салон", Критерии());

        Assert.False(await служба.ПереименоватьAsync("Аптеки", "Салон"));
        Assert.False(await служба.ПереименоватьAsync("Нет такого", "Аптеки"));

        var фильтры = await служба.ПолучитьСписокAsync();

        Assert.Equal(2, фильтры.Count);
        Assert.Contains(фильтры, фильтр => фильтр.Name == "Аптеки");
    }

    /// <summary>
    /// Удаление фильтра убирает его из набора.
    /// </summary>
    [Fact]
    public async Task УдалениеФильтра_УбираетЕгоИзНабора()
    {
        var служба = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));
        await служба.СохранитьAsync("Аптеки", Критерии());
        await служба.СохранитьAsync("Салон", Критерии());

        Assert.True(await служба.УдалитьAsync("Аптеки"));
        Assert.False(await служба.УдалитьAsync("Аптеки"));

        var фильтры = await служба.ПолучитьСписокAsync();

        var сохранённый = Assert.Single(фильтры);
        Assert.Equal("Салон", сохранённый.Name);
    }

    /// <summary>
    /// Файл настроек, сохранённый до появления пользовательских фильтров,
    /// читается без ошибок: отсутствующие поля дают значения по умолчанию.
    /// </summary>
    [Fact]
    public async Task СтарыйФайлНастроек_ЧитаетсяБезОшибок()
    {
        await File.WriteAllTextAsync(
            _путьФайла,
            """{"grids":{},"appearance":{"focusColor":4227327,"selectedColor":6205183,"hintColor":3723516}}""");

        var служба = new SavedFilterService(new JsonUiSettingsStore(_путьФайла));

        Assert.Empty(await служба.ПолучитьСписокAsync());

        await служба.СохранитьAsync("Аптеки", Критерии());

        Assert.Single(await служба.ПолучитьСписокAsync());
    }
}
