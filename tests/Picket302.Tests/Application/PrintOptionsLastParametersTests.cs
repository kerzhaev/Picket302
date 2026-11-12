using Picket302.Application.Services;
using Picket302.Domain.Repositories;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Функция №35 (ТЗ §8.5, S22): запоминание последних введённых данных при
/// печати одного конверта — копии и принтер хранятся в профиле,
/// восстанавливаются только для этого сценария, повреждённые значения
/// безопасно откатываются, первый запуск даёт умолчания.
/// </summary>
public sealed class PrintOptionsLastParametersTests
{
    private sealed class ПамяткаПараметров : IParameterRepository
    {
        public Dictionary<string, string> Значения { get; } = new(StringComparer.OrdinalIgnoreCase);
        private long _следующийId;

        public Task<long> AddAsync(Picket302.Domain.Entities.Parameter parameter, CancellationToken cancellationToken = default)
        {
            Значения[parameter.Name] = parameter.Value ?? string.Empty;
            return Task.FromResult(++_следующийId);
        }

        public Task<Picket302.Domain.Entities.Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Picket302.Domain.Entities.Parameter?>(
                Значения.TryGetValue(name, out var значение)
                    ? new Picket302.Domain.Entities.Parameter { Name = name, Value = значение }
                    : null);

        public Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Значения.TryGetValue(name, out var значение) ? значение : null);

        public Task<IReadOnlyList<Picket302.Domain.Entities.Parameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Picket302.Domain.Entities.Parameter>>(
                Значения.Select(пара => new Picket302.Domain.Entities.Parameter
                {
                    Name = пара.Key,
                    Value = пара.Value,
                }).ToList());

        public Task<int> UpdateAsync(Picket302.Domain.Entities.Parameter parameter, CancellationToken cancellationToken = default)
        {
            Значения[parameter.Name] = parameter.Value ?? string.Empty;
            return Task.FromResult(1);
        }

        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Значения.Remove(name));
    }

    private static async Task<PrintOptions> СоздатьСоСтатусом(
        ПамяткаПараметров параметры,
        string? копии,
        string? принтер)
    {
        if (копии is not null)
        {
            параметры.Значения[PrintOptions.LastCopiesKey] = копии;
        }

        if (принтер is not null)
        {
            параметры.Значения[PrintOptions.LastPrinterKey] = принтер;
        }

        var опции = new PrintOptions(параметры);
        await опции.GetLastCopiesAsync();
        await опции.GetLastPrinterAsync();
        return опции;
    }

    [Fact]
    public async Task КруглыйЦикл_КопииИПринтерСохраняютсяИВосстанавливаются()
    {
        var параметры = new ПамяткаПараметров();
        var опции = new PrintOptions(параметры);

        await опции.SetLastCopiesAsync(3);
        await опции.SetLastPrinterAsync("Microsoft Print to PDF");

        var чтение = new PrintOptions(параметры);
        Assert.Equal(3, await чтение.GetLastCopiesAsync());
        Assert.Equal("Microsoft Print to PDF", await чтение.GetLastPrinterAsync());
    }

    [Fact]
    public async Task ПервыйЗапуск_УмолчанияБезОшибок()
    {
        var опции = await СоздатьСоСтатусом(new ПамяткаПараметров(), null, null);

        Assert.Equal(1, опции.ПоследниеКопии);
        Assert.Null(опции.ПоследнийПринтер);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task ПовреждённыеКопии_БезопасноОдна(string повреждённое)
    {
        var опции = await СоздатьСоСтатусом(new ПамяткаПараметров(), повреждённое, null);

        Assert.Equal(1, опции.ПоследниеКопии);
    }

    [Fact]
    public async Task ОтрицательныеКопииПриСохранении_НормализуютсяКОдной()
    {
        var параметры = new ПамяткаПараметров();
        var опции = new PrintOptions(параметры);

        await опции.SetLastCopiesAsync(-7);

        Assert.Equal(1, опции.ПоследниеКопии);
        Assert.Equal(1, await new PrintOptions(параметры).GetLastCopiesAsync());
    }

    [Fact]
    public async Task ПустоеИмяПринтера_СбрасываетВыбор()
    {
        var параметры = new ПамяткаПараметров();
        var опции = new PrintOptions(параметры);
        await опции.SetLastPrinterAsync("Первый принтер");
        await опции.SetLastPrinterAsync("  ");

        Assert.Null(await new PrintOptions(параметры).GetLastPrinterAsync());
    }
}
