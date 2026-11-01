using Picket302.Domain.Entities;
using Picket302.Domain.Serialization;
using Picket302.Infrastructure.IO;

namespace Picket302.Infrastructure.Templates;

/// <summary>Запись каталога пакетов бланков.</summary>
/// <param name="FileName">Имя файла пакета.</param>
/// <param name="Template">Шаблон из пакета; <see langword="null"/> для невалидных пакетов.</param>
/// <param name="IsValid">Признак валидности пакета.</param>
/// <param name="Error">Текст ошибки для невалидных пакетов.</param>
public sealed record TemplatePackageRecord(
    string FileName,
    PrintTemplate? Template,
    bool IsValid,
    string? Error = null);

/// <summary>
/// Библиотека пакетов бланков (ТЗ §8.3, функция 37:
/// <c>Infrastructure.Templates.TemplatePackageRepository.LoadAllPackages</c>).
/// </summary>
/// <remarks>
/// Сканирует каталог шаблонов, валидирует пакеты и загружает реестр доступных
/// конвертов (C4, C5, C6, DL, B4), доверенностей, описей и заявлений.
/// Поврежденный пакет помечается как невалидный и исключается из списка без
/// падения программы; добавление файла .lbk в папку делает бланк доступным
/// без перезапуска приложения (каталог читается при каждом вызове).
/// </remarks>
public sealed class TemplatePackageRepository
{
    private readonly LbkPackageArchiver _архиватор;
    private readonly BlankXmlSerializer _сериализатор = new();

    /// <summary>Создаёт репозиторий пакетов бланков.</summary>
    /// <param name="archiver">Архиватор пакетов; по умолчанию новый.</param>
    public TemplatePackageRepository(LbkPackageArchiver? archiver = null)
    {
        _архиватор = archiver ?? new LbkPackageArchiver();
    }

    /// <summary>
    /// Сканирует каталог шаблонов и загружает реестр доступных пакетов
    /// (ТЗ §8.3, функция 37).
    /// </summary>
    /// <param name="directoryPath">Каталог Templates/Packages.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Записи каталога; поврежденные пакеты — с IsValid=false.</returns>
    public Task<IReadOnlyList<TemplatePackageRecord>> LoadAllPackagesAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        return Task.FromResult(СканироватьКаталог(directoryPath, cancellationToken));
    }

    /// <summary>
    /// Сканирует каталог пакетов и формирует записи реестра.
    /// </summary>
    /// <param name="directoryPath">Каталог Templates/Packages.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Записи каталога; повреждённые пакеты — с IsValid=false.</returns>
    private IReadOnlyList<TemplatePackageRecord> СканироватьКаталог(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var записи = new List<TemplatePackageRecord>();

        if (!Directory.Exists(directoryPath))
        {
            return записи;
        }

        foreach (var файл in Directory.GetFiles(directoryPath, "*.lbk"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var пакет = _архиватор.ImportPackage(файл);
                var шаблон = СоздатьШаблон(Path.GetFileName(файл), пакет.Blank);

                записи.Add(new TemplatePackageRecord(файл, шаблон, IsValid: true));
            }
            catch (LbkPackageException ошибка)
            {
                // Поврежденный пакет помечается как невалидный и исключается
                // из списка без падения программы (ТЗ §8.3, ф.37).
                записи.Add(new TemplatePackageRecord(файл, null, IsValid: false, ошибка.Message));
            }
        }

        return записи;
    }

    /// <summary>
    /// Синхронный вариант сканирования каталога (ТЗ §8.3, функция 37).
    /// </summary>
    /// <param name="directoryPath">Каталог Templates/Packages.</param>
    /// <returns>Записи каталога.</returns>
    public IReadOnlyList<TemplatePackageRecord> LoadAllPackages(string directoryPath) =>
        LoadAllPackagesAsync(directoryPath).GetAwaiter().GetResult();

    /// <summary>
    /// Экспортирует полную библиотеку встроенных шаблонов в каталог пакетов
    /// (не менее 20 файлов .lbk, ТЗ §8.3, ф.36/ф.37).
    /// </summary>
    /// <param name="directoryPath">Целевой каталог Templates/Packages.</param>
    /// <param name="catalog">Каталог встроенных бланков.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записанных пакетов.</returns>
    public async Task<int> ExportBuiltinLibraryAsync(
        string directoryPath,
        BuiltinTemplatesCatalog? catalog = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        Directory.CreateDirectory(directoryPath);

        var каталог = catalog ?? new BuiltinTemplatesCatalog();
        var записано = 0;

        foreach (var (имяПакета, бланк) in каталог.BuildLibrary())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Имя пакета санитизируется до сборки пути: знаки «/» и прочие
            // недопустимые знаки имени файла заменяются, чтобы «C5/О» не
            // превратилось в подкаталог.
            var имяФайла = СанитизироватьИмя(имяПакета) + ".lbk";
            var файл = Path.Combine(directoryPath, имяФайла);

            if (File.Exists(файл))
            {
                // Попытка перезаписи встроенного бланка сохраняется как
                // пользовательская копия (ТЗ §8.3, ф.36).
                файл = Path.Combine(
                    directoryPath,
                    СанитизироватьИмя(имяПакета) + " (копия " + DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ").lbk");
            }

            await Task.Run(() => _архиватор.ExportPackage(бланк, файл), cancellationToken).ConfigureAwait(false);
            записано++;
        }

        return записано;
    }

    /// <summary>Заменяет недопустимые знаки имени файла на подчёркивания.</summary>
    /// <param name="name">Исходное имя пакета.</param>
    /// <returns>Безопасное имя файла без расширения.</returns>
    private static string СанитизироватьИмя(string name)
    {
        var имя = name.Trim();

        foreach (var недопустимый in Path.GetInvalidFileNameChars())
        {
            имя = имя.Replace(недопустимый, '_');
        }

        return имя;
    }

    private static PrintTemplate СоздатьШаблон(string имяФайла, CustomBlank бланк) => new()
    {
        Name = бланк.Name,
        Kind = PrintTemplateKind.Blank,
        Format = "CUSTOM",
        WidthMm = бланк.Geometry.WidthMm,
        HeightMm = бланк.Geometry.HeightMm,
        Orientation = бланк.Geometry.Orientation == BlankOrientation.Landscape
            ? TemplateOrientation.Landscape
            : TemplateOrientation.Portrait,
        FilePath = имяФайла,
        IsBuiltin = false,
        Country = бланк.Country,
    };
}
