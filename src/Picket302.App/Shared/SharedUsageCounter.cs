using Picket302.Infrastructure.Licensing;

namespace Picket302.App.Shared;

/// <summary>
/// Счётчик объёма общей базы для ограничений пробного периода.
/// </summary>
/// <remarks>
/// <para>
/// Автомат лицензии спрашивает объём синхронно, а снимок объёма общей базы
/// получается сетевым запросом. Поэтому снимок обновляется заранее и
/// хранится; автомат лицензии читает последнее известное значение.
/// </para>
/// <para>
/// Недоступность службы не обнуляет снимок: при следующем успешном обновлении
/// значение уточняется.
/// </para>
/// </remarks>
public sealed class SharedUsageCounter : IDisposable
{
    /// <summary>Период обновления снимка объёма, миллисекунды.</summary>
    public const int ПериодОбновленияМс = 30000;

    private readonly IDataGateway _данные;
    private readonly System.Threading.Timer _таймер;
    private СнимокИспользования _снимок = new(0, 0);

    /// <summary>
    /// Создаёт счётчик объёма общей базы.
    /// </summary>
    /// <param name="данные">Доступ к общей базе.</param>
    public SharedUsageCounter(IDataGateway данные)
    {
        ArgumentNullException.ThrowIfNull(данные);

        _данные = данные;
        _таймер = new System.Threading.Timer(
            _ => _ = ОбновитьБезОжиданияAsync(),
            null,
            TimeSpan.FromMilliseconds(ПериодОбновленияМс),
            TimeSpan.FromMilliseconds(ПериодОбновленияМс));
    }

    /// <summary>
    /// Функция счётчика для автомата лицензии.
    /// </summary>
    public Func<СнимокИспользования> Получить => () => _снимок;

    /// <summary>
    /// Обновляет снимок объёма.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task ОбновитьAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _снимок = await _данные
                .ПолучитьСнимокОбъёмаAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SharedModeException)
        {
            // Служба недоступна: прежнее значение сохраняется.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _таймер.Dispose();

    private async Task ОбновитьБезОжиданияAsync()
    {
        try
        {
            await ОбновитьAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Фоновое обновление не должно прерывать работу программы.
        }
    }
}
