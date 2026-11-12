using Picket302.Domain.Entities;
using Picket302.Domain.Postal;
using Picket302.Domain.Repositories;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Функция №45 (ТЗ §8.4): индивидуальные калибровочные смещения для пары
/// «принтер + формат конверта» с шагом 0,1 мм; пары не смешиваются,
/// значения применяются в предпросмотре и печати (S20).
/// </summary>
public sealed class PrinterOffsetProfileTests
{
    private sealed class ПамяткаХранилища : IPrinterProfileRepository
    {
        public List<PrinterProfile> Записи { get; } = new();

        public Task<long> AddAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
        {
            Записи.Add(profile);
            profile.Id = Записи.Count;
            return Task.FromResult((long)profile.Id);
        }

        public Task<PrinterProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Записи.FirstOrDefault(запись => запись.Id == id));

        public Task<PrinterProfile?> GetByPrinterAndFormatAsync(
            string printerName,
            string envelopeFormat,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Записи.FirstOrDefault(запись =>
                запись.PrinterName == printerName && запись.EnvelopeFormat == envelopeFormat));

        public Task<IReadOnlyList<PrinterProfile>> GetByPrinterAsync(
            string printerName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrinterProfile>>(
                Записи.Where(запись => запись.PrinterName == printerName).ToList());

        public Task<int> UpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Записи.RemoveAll(запись => запись.Id == id) > 0);
    }

    [Fact]
    public async Task СохранениеИВосстановление_КвантизацияШага01()
    {
        var хранилище = new ПамяткаХранилища();
        var профиль = new PrinterOffsetProfile(хранилище);

        await профиль.SaveOffsetAsync("Принтер А", EnvelopeFormat.C6, 1.25f, -2.34f);

        var значение = await профиль.GetOffsetAsync("Принтер А", nameof(EnvelopeFormat.C6));

        // Шаг 0,1 мм: 1,25 → 1,3 (в большую сторону по banker's? нет —
        // Quantize округляет к ближайшему шагу); главное — кратность 0,1.
        Assert.Equal(0m, значение.OffsetXMm % PrinterOffsetProfile.StepMm);
        Assert.Equal(0m, значение.OffsetYMm % PrinterOffsetProfile.StepMm);
        Assert.InRange(значение.OffsetXMm, 1.2m, 1.3m);
        Assert.InRange(значение.OffsetYMm, -2.4m, -2.3m);
    }

    [Fact]
    public async Task ПарыНеСмешиваются_ПринтерФормат()
    {
        var хранилище = new ПамяткаХранилища();
        var профиль = new PrinterOffsetProfile(хранилище);

        await профиль.SaveOffsetAsync("Принтер А", EnvelopeFormat.C6, 1.0f, 1.0f);
        await профиль.SaveOffsetAsync("Принтер А", EnvelopeFormat.DL, 2.0f, 2.0f);
        await профиль.SaveOffsetAsync("Принтер Б", EnvelopeFormat.C6, 3.0f, 3.0f);

        var аC6 = await профиль.GetOffsetAsync("Принтер А", nameof(EnvelopeFormat.C6));
        var аDL = await профиль.GetOffsetAsync("Принтер А", nameof(EnvelopeFormat.DL));
        var бC6 = await профиль.GetOffsetAsync("Принтер Б", nameof(EnvelopeFormat.C6));

        Assert.Equal(1.0m, аC6.OffsetXMm);
        Assert.Equal(2.0m, аDL.OffsetXMm);
        Assert.Equal(3.0m, бC6.OffsetXMm);
        Assert.Equal(3, хранилище.Записи.Count);
    }

    [Fact]
    public async Task ПовторноеСохранение_ОбновляетТуЖеЗапись()
    {
        var хранилище = new ПамяткаХранилища();
        var профиль = new PrinterOffsetProfile(хранилище);

        await профиль.SaveOffsetAsync("Принтер А", EnvelopeFormat.C6, 1.0f, 1.0f);
        await профиль.SaveOffsetAsync("Принтер А", EnvelopeFormat.C6, 4.0f, -4.0f);

        Assert.Single(хранилище.Записи);

        var значение = await профиль.GetOffsetAsync("Принтер А", nameof(EnvelopeFormat.C6));
        Assert.Equal(4.0m, значение.OffsetXMm);
        Assert.Equal(-4.0m, значение.OffsetYMm);
    }

    [Fact]
    public async Task ОтсутствующийПрофиль_НулевоеСмещение()
    {
        var профиль = new PrinterOffsetProfile(new ПамяткаХранилища());

        var значение = await профиль.GetOffsetAsync("Нет такого", nameof(EnvelopeFormat.C6));

        Assert.Equal(0m, значение.OffsetXMm);
        Assert.Equal(0m, значение.OffsetYMm);
    }
}
