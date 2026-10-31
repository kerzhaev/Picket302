using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка опции «Показывать список шаблонов перед печатью» и хранения
/// последнего использованного шаблона в профиле пользователя (ТЗ §8.6,
/// функция 82).
/// </summary>
public sealed class PrintOptionsTests
{
    /// <summary>Хранилище параметров в памяти.</summary>
    private sealed class ПараметрыВПамяти : IParameterRepository
    {
        private readonly Dictionary<string, Parameter> _записи = new(StringComparer.Ordinal);

        public Task<long> AddAsync(Parameter parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = _записи.Count + 1;
            _записи[parameter.Name] = parameter;

            return Task.FromResult(parameter.Id);
        }

        public Task<Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.TryGetValue(name, out var запись) ? запись : null);

        public Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.TryGetValue(name, out var запись) ? запись.Value : null);

        public Task<IReadOnlyList<Parameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parameter>>(_записи.Values.ToList());

        public Task<int> UpdateAsync(Parameter parameter, CancellationToken cancellationToken = default)
        {
            _записи[parameter.Name] = parameter;

            return Task.FromResult(1);
        }

        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.Remove(name));
    }

    [Fact]
    public void ПоказыватьСписокШаблонов_ПоУмолчанию_Включено()
    {
        var опции = new PrintOptions(new ПараметрыВПамяти());

        Assert.True(опции.ПоказыватьСписокШаблонов);
    }

    [Fact]
    public async Task GetShowTemplateListAsync_БезПараметра_ВозвращаетИстину()
    {
        var опции = new PrintOptions(new ПараметрыВПамяти());

        Assert.True(await опции.GetShowTemplateListAsync());
        Assert.True(опции.ПоказыватьСписокШаблонов);
    }

    [Fact]
    public async Task SetShowTemplateListAsync_СохраняетЗначениеВПрофиле()
    {
        var хранилище = new ПараметрыВПамяти();
        var опции = new PrintOptions(хранилище);

        await опции.SetShowTemplateListAsync(false);

        Assert.False(опции.ПоказыватьСписокШаблонов);
        Assert.Equal(
            "0",
            await хранилище.GetValueAsync(PrintOptions.ShowTemplateListKey));

        // Значение читается новым экземпляром службы из того же профиля.
        var повторное = new PrintOptions(хранилище);

        Assert.False(await повторное.GetShowTemplateListAsync());
        Assert.False(повторное.ПоказыватьСписокШаблонов);
    }

    [Fact]
    public async Task SetShowTemplateListAsync_ВключаетОпциюОбратно()
    {
        var хранилище = new ПараметрыВПамяти();
        var опции = new PrintOptions(хранилище);

        await опции.SetShowTemplateListAsync(false);
        await опции.SetShowTemplateListAsync(true);

        Assert.True(опции.ПоказыватьСписокШаблонов);
        Assert.Equal(
            "1",
            await хранилище.GetValueAsync(PrintOptions.ShowTemplateListKey));
    }

    [Fact]
    public async Task GetShowTemplateListAsync_ЧитаетНольИзПрофиля()
    {
        var хранилище = new ПараметрыВПамяти();

        await хранилище.AddAsync(
            new Parameter { Name = PrintOptions.ShowTemplateListKey, Value = "0" });

        var опции = new PrintOptions(хранилище);

        Assert.False(await опции.GetShowTemplateListAsync());
    }

    [Fact]
    public async Task SetLastTemplateIdAsync_СохраняетПоследнийШаблон()
    {
        var хранилище = new ПараметрыВПамяти();
        var опции = new PrintOptions(хранилище);

        await опции.SetLastTemplateIdAsync(42);

        Assert.Equal(42, опции.ПоследнийШаблонId);

        var повторное = new PrintOptions(хранилище);

        Assert.Equal(42, await повторное.GetLastTemplateIdAsync());
    }

    [Fact]
    public async Task GetLastTemplateIdAsync_БезШаблона_ВозвращаетNull()
    {
        var опции = new PrintOptions(new ПараметрыВПамяти());

        Assert.Null(await опции.GetLastTemplateIdAsync());
        Assert.Null(опции.ПоследнийШаблонId);
    }
}
