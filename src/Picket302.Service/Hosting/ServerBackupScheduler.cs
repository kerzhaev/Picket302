using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;

namespace Picket302.Service.Hosting;

/// <summary>
/// Ежедневное резервное копирование общей базы серверной службы.
/// </summary>
/// <remarks>
/// <para>
/// Механизм — тот же, что и в одиночном режиме: согласованный снимок
/// <c>VACUUM INTO</c> поверх существующего <see cref="BackupService"/> и
/// удержание копий по политике <see cref="BackupPolicyService"/>. Копии
/// хранятся вне каталога устанавливаемых бинарников — в каталоге
/// <c>backups</c> данных службы. Облачные копии и внешние хранилища
/// не используются.
/// </para>
/// <para>
/// Пропущенное копирование после простоя сервера: при старте службы, если
/// копий за сегодня нет, выполняется одна. Дальше копирование раз в сутки по
/// таймеру. Сбой копирования не останавливает службу — следующая попытка по
/// расписанию; ни одна старая копия не удаляется, пока новая не создана.
/// </para>
/// </remarks>
public sealed class ServerBackupScheduler : IDisposable
{
    /// <summary>Период проверки расписания, часы.</summary>
    public const int ПериодПроверкиЧасов = 1;

    /// <summary>Срок удержания копий, дней.</summary>
    public const int СрокУдержанияДней = 14;

    private readonly string _путьБазы;
    private readonly string _каталогКопий;
    private readonly Action<string> _журнал;
    private readonly System.Threading.Timer _таймер;
    private readonly object _блокировка = new();
    private DateTime _последняяКопия = DateTime.MinValue;

    /// <summary>
    /// Создаёт планировщик копирования.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных службы.</param>
    /// <param name="каталогКопий">Каталог копий.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <param name="сразу">
    /// Выполнить копирование при старте, если копий за сегодня нет
    /// (пропущенное копирование после простоя сервера).
    /// </param>
    public ServerBackupScheduler(
        string путьБазы,
        string каталогКопий,
        Action<string> журнал,
        bool сразу = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогКопий);
        ArgumentNullException.ThrowIfNull(журнал);

        _путьБазы = путьБазы;
        _каталогКопий = каталогКопий;
        _журнал = журнал;

        _таймер = new System.Threading.Timer(
            _ => ВыполнитьПоРасписаниюСинхронно(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(ПериодПроверкиЧасов));

        if (сразу)
        {
            ВыполнитьПоРасписаниюСинхронно();
        }
    }

    /// <summary>
    /// Выполняет копирование, если за сегодня его ещё не было.
    /// </summary>
    public void ВыполнитьПоРасписаниюСинхронно()
    {
        lock (_блокировка)
        {
            if (DateTime.UtcNow.Date == _последняяКопия.Date)
            {
                return;
            }

            // Метка ставится заранее: параллельные тики таймера не должны
            // создать две копии за день.
            _последняяКопия = DateTime.UtcNow;
        }

        ВыполнитьСейчас();
    }

    /// <summary>
    /// Выполняет копирование немедленно.
    /// </summary>
    /// <returns>Путь к созданной копии; <see langword="null"/> — копия не создана.</returns>
    public string? ВыполнитьСейчас()
    {
        try
        {
            Directory.CreateDirectory(_каталогКопий);

            var службаАрхива = new BackupService(_путьБазы);

            var итог = службаАрхива.BackupNow(new BackupOptions(_каталогКопий));

            if (итог.IsFailure)
            {
                _журнал($"Резервное копирование общей базы не выполнено: {итог.Error}");

                return null;
            }

            // Удержание: копии старше срока удаляются только после того, как
            // новая копия уже записана.
            var политика = new BackupPolicy(
                BackupRetentionMode.DailyHistory,
                СрокУдержанияДней,
                _каталогКопий,
                null);

            var существующие = службаАрхива.ListArchives(_каталогКопий);

            var очистка = new BackupPolicyService().Apply(политика, существующие, итог.ArchivePath);

            if (очистка.IsFailure)
            {
                _журнал($"Очистка устаревших копий не выполнена: {очистка.Error}");
            }
            else if (очистка.DeletedFiles.Count > 0)
            {
                _журнал($"Устаревших копий удалено: {очистка.DeletedFiles.Count}.");
            }

            _журнал($"Резервная копия общей базы создана: {итог.ArchivePath}");

            return итог.ArchivePath;
        }
#pragma warning disable CA1031 // Сбой копирования не должен останавливать службу: следующая попытка — по расписанию.
        catch (Exception ошибка)
        {
            _журнал($"Резервное копирование общей базы не выполнено: {ошибка}");

            return null;
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public void Dispose() => _таймер.Dispose();
}
