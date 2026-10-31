using Picket302.Application.Services;
using Picket302.Domain.Rules;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка сквозной регистрационной нумерации (ТЗ §8.1, функция 25:
/// «Сквозная регистрационная нумерация», маска вида <c>ИСХ-{YYYY}/{SEQ:5}</c>).
/// </summary>
public sealed class RegistrationNumberGeneratorTests
{
    /// <summary>
    /// Счётчик в памяти: атомарность проверяется отдельно на реальной базе,
    /// здесь проверяется формирование номера по маске.
    /// </summary>
    private sealed class Счётчик : global::Picket302.Domain.Repositories.IRegistrationNumberStore
    {
        private long _значение;

        public Task<long> NextAsync(string counterKey, CancellationToken cancellationToken = default)
        {
            _значение++;

            return Task.FromResult(_значение);
        }
    }

    [Fact]
    public async Task Номер_ФормируетсяПоМаскеПоУмолчанию()
    {
        var генератор = new RegistrationNumberGenerator(new Счётчик());
        var момент = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        var номер = await генератор.GenerateNextOutgoingNumberAsync(moment: момент);

        Assert.Equal("ИСХ-2026/00001", номер);
    }

    [Fact]
    public async Task Номер_Последовательный_СчётчикУвеличивается()
    {
        var генератор = new RegistrationNumberGenerator(new Счётчик());
        var момент = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        var первый = await генератор.GenerateNextOutgoingNumberAsync(moment: момент);
        var второй = await генератор.GenerateNextOutgoingNumberAsync(moment: момент);
        var третий = await генератор.GenerateNextOutgoingNumberAsync(moment: момент);

        Assert.Equal("ИСХ-2026/00001", первый);
        Assert.Equal("ИСХ-2026/00002", второй);
        Assert.Equal("ИСХ-2026/00003", третий);
    }

    [Fact]
    public async Task Номер_ПользовательскаяМаска_Учитывается()
    {
        var генератор = new RegistrationNumberGenerator(new Счётчик());
        var момент = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        var номер = await генератор.GenerateNextOutgoingNumberAsync("ВХ-{YYYY}/{SEQ:3}", момент);

        Assert.Equal("ВХ-2026/001", номер);
    }

    [Fact]
    public void Маска_ПоУмолчанию_СоответствуетСпецификации()
    {
        Assert.Equal("ИСХ-{YYYY}/{SEQ:5}", RegistrationNumberGenerator.DefaultTemplateMask);
    }

    [Fact]
    public void Format_МаскаИсходящего_ДаётНомерСГодомИПятизначнойПоследовательностью()
    {
        Assert.Equal("ИСХ-2026/00042", RegistrationNumberMask.Format("ИСХ-{YYYY}/{SEQ:5}", 2026, 42));
    }

    [Fact]
    public void Format_НеизвестнаяПодстановка_СохраняетсяКакТекст()
    {
        Assert.Equal("ИСХ-2026/{НЕИЗВЕСТНО}", RegistrationNumberMask.Format("ИСХ-{YYYY}/{НЕИЗВЕСТНО}", 2026, 1));
    }
}
