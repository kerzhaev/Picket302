using Picket302.Domain.Contracts;

namespace Picket302.Application.Import;

/// <summary>
/// Хранилище профилей сопоставления CSV (ТЗ §8.12, функция 73: «Данные:
/// Файлы конфигурации *.csvprof»).
/// </summary>
/// <remarks>
/// <para>
/// Файловый ввод-вывод выполняется слоем инфраструктуры
/// (<c>Picket302.Infrastructure.Import.CsvProfileStore</c>), а слой приложения
/// обращается к хранилищу через этот интерфейс: слой инфраструктуры не
/// зависит от слоя приложения (ТЗ §2.1), поэтому зависимость объявляется
/// здесь, а реализация подставляется при сборке приложения.
/// </para>
/// <para>
/// Для подключения реализации инфраструктуры используется
/// <see cref="DelegateCsvProfileStore"/>: он реализует интерфейс поверх пары
/// делегатов и не требует объявления ссылки инфраструктуры на приложение.
/// </para>
/// </remarks>
public interface ICsvProfileStore
{
    /// <summary>Сохраняет профиль сопоставления в файл.</summary>
    /// <param name="profile">Сохраняемый профиль.</param>
    /// <param name="filePath">Путь к файлу профиля (*.csvprof).</param>
    void Save(CsvProfile profile, string filePath);

    /// <summary>Загружает профиль сопоставления из файла.</summary>
    /// <param name="filePath">Путь к файлу профиля (*.csvprof).</param>
    /// <returns>Загруженный профиль сопоставления.</returns>
    CsvProfile Load(string filePath);

    /// <summary>Перечисляет файлы профилей в каталоге.</summary>
    /// <param name="directory">Каталог с файлами профилей.</param>
    /// <returns>Полные пути к файлам профилей в порядке сортировки по имени.</returns>
    IReadOnlyList<string> ListProfiles(string directory);
}

/// <summary>
/// Реализация хранилища профилей поверх делегатов (ТЗ §8.12, функция 73).
/// </summary>
/// <remarks>
/// Позволяет использовать реализацию файлового ввода-вывода из слоя
/// инфраструктуры, не объявляя зависимости слоя приложения от инфраструктуры
/// (ТЗ §2.1).
/// </remarks>
public sealed class DelegateCsvProfileStore : ICsvProfileStore
{
    private readonly Action<CsvProfile, string> _сохранить;
    private readonly Func<string, CsvProfile> _загрузить;
    private readonly Func<string, IReadOnlyList<string>> _перечислить;

    /// <summary>Создаёт хранилище поверх делегатов.</summary>
    /// <param name="сохранить">Делегат сохранения профиля в файл.</param>
    /// <param name="загрузить">Делегат загрузки профиля из файла.</param>
    /// <param name="перечислить">Делегат перечисления файлов профилей в каталоге.</param>
    public DelegateCsvProfileStore(
        Action<CsvProfile, string> сохранить,
        Func<string, CsvProfile> загрузить,
        Func<string, IReadOnlyList<string>> перечислить)
    {
        ArgumentNullException.ThrowIfNull(сохранить);
        ArgumentNullException.ThrowIfNull(загрузить);
        ArgumentNullException.ThrowIfNull(перечислить);

        _сохранить = сохранить;
        _загрузить = загрузить;
        _перечислить = перечислить;
    }

    /// <inheritdoc />
    public void Save(CsvProfile profile, string filePath) => _сохранить(profile, filePath);

    /// <inheritdoc />
    public CsvProfile Load(string filePath) => _загрузить(filePath);

    /// <inheritdoc />
    public IReadOnlyList<string> ListProfiles(string directory) => _перечислить(directory);
}
