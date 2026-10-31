using Picket302.Domain.Postal;
using Picket302.Domain.Repositories;

namespace Picket302.Domain.Entities;

/// <summary>
/// Индивидуальные калибровочные смещения для пары «принтер + формат конверта»
/// (ТЗ §8.4, функция 45: <c>Domain.Entities.PrinterOffsetProfile</c>).
/// </summary>
/// <remarks>
/// Юстировка печатающей головки задаётся независимо для каждой связки
/// «Принтер + Формат конверта» с шагом 0,1 мм и хранится в таблице
/// <c>printer_profiles</c>.
/// </remarks>
public sealed class PrinterOffsetProfile
{
    /// <summary>Шаг калибровочного смещения, мм.</summary>
    public const decimal StepMm = 0.1m;

    private readonly IPrinterProfileRepository _репозиторий;

    /// <summary>
    /// Создаёт профиль калибровочных смещений.
    /// </summary>
    /// <param name="repository">Хранилище профилей принтеров.</param>
    public PrinterOffsetProfile(IPrinterProfileRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _репозиторий = repository;
    }

    /// <summary>
    /// Сохраняет калибровочное смещение для пары «принтер + формат».
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="format">Формат конверта.</param>
    /// <param name="offsetXmm">Смещение по горизонтали, мм.</param>
    /// <param name="offsetYmm">Смещение по вертикали, мм.</param>
    public void SaveOffset(string printerName, EnvelopeFormat format, float offsetXmm, float offsetYmm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var задание = СохранитьAsync(printerName, format, offsetXmm, offsetYmm);

        Task.Run(() => задание).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Сохраняет калибровочное смещение для пары «принтер + формат».
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="format">Формат конверта.</param>
    /// <param name="offsetXmm">Смещение по горизонтали, мм.</param>
    /// <param name="offsetYmm">Смещение по вертикали, мм.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SaveOffsetAsync(
        string printerName,
        EnvelopeFormat format,
        float offsetXmm,
        float offsetYmm,
        CancellationToken cancellationToken = default)
    {
        await СохранитьAsync(printerName, format, offsetXmm, offsetYmm).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает калибровочное смещение для пары «принтер + формат».
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="envelopeFormat">Обозначение формата конверта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Смещения по горизонтали и вертикали, мм.</returns>
    public async Task<(decimal OffsetXMm, decimal OffsetYMm)> GetOffsetAsync(
        string printerName,
        string envelopeFormat,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeFormat);

        var профиль = await _репозиторий
            .GetByPrinterAndFormatAsync(printerName, envelopeFormat, cancellationToken)
            .ConfigureAwait(false);

        if (профиль is null)
        {
            return (0m, 0m);
        }

        return (профиль.OffsetXMm, профиль.OffsetYMm);
    }

    /// <summary>
    /// Приводит значение смещения к шагу 0,1 мм.
    /// </summary>
    /// <param name="value">Исходное значение, мм.</param>
    /// <returns>Значение, округлённое до шага 0,1 мм.</returns>
    public static decimal Quantize(decimal value) =>
        Math.Round(value / StepMm, MidpointRounding.AwayFromZero) * StepMm;

    private async Task СохранитьAsync(
        string printerName,
        EnvelopeFormat format,
        float offsetXmm,
        float offsetYmm)
    {
        var обозначение = format.ToString();

        var профиль = await _репозиторий
            .GetByPrinterAndFormatAsync(printerName, обозначение)
            .ConfigureAwait(false);

        if (профиль is null)
        {
            профиль = new PrinterProfile
            {
                PrinterName = printerName,
                EnvelopeFormat = обозначение,
                OffsetXMm = Quantize((decimal)offsetXmm),
                OffsetYMm = Quantize((decimal)offsetYmm),
            };

            await _репозиторий.AddAsync(профиль).ConfigureAwait(false);
            return;
        }

        профиль.OffsetXMm = Quantize((decimal)offsetXmm);
        профиль.OffsetYMm = Quantize((decimal)offsetYmm);

        await _репозиторий.UpdateAsync(профиль).ConfigureAwait(false);
    }
}
