using System.IO.Compression;
using System.Text.Json;
using Picket302.Domain.Serialization;

namespace Picket302.Infrastructure.IO;

/// <summary>
/// Ошибка структуры пакета бланка (ТЗ §8.3, функции 37/40: «Попытка открытия
/// поврежденного ZIP-архива — диалог „Ошибка структуры пакета“»).
/// </summary>
public sealed class LbkPackageException : Exception
{
    /// <summary>Текст диалога «Ошибка структуры пакета».</summary>
    public const string StructureErrorCaption = "Ошибка структуры пакета";

    /// <summary>Создаёт ошибку структуры пакета.</summary>
    /// <param name="message">Подробный текст.</param>
    /// <param name="inner">Вложенное исключение.</param>
    public LbkPackageException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Описание пакета бланка — manifest.json (ТЗ §5.1: «Описание пакета, версия,
/// автор, совместимость»).
/// </summary>
public sealed class LbkManifest
{
    /// <summary>Наименование пакета.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Версия пакета.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Автор пакета.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Минимальная совместимая версия формата бланка.</summary>
    public string Compatibility { get; set; } = "2.0";

    /// <summary>Момент создания пакета (UTC), ISO 8601.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Содержимое распакованного пакета .lbk (ТЗ §5.1).</summary>
public sealed class LbkPackageContent
{
    /// <summary>Описание пакета (manifest.json).</summary>
    public LbkManifest Manifest { get; init; } = new();

    /// <summary>Разобранный blank.xml.</summary>
    public CustomBlank Blank { get; init; } = new();

    /// <summary>Текст blank.xml.</summary>
    public string BlankXml { get; init; } = string.Empty;

    /// <summary>Текст query.xml; <see langword="null"/> — запроса в пакете нет.</summary>
    public string? QueryXml { get; init; }

    /// <summary>Растровая подложка background.png; <see langword="null"/> — подложки нет.</summary>
    public byte[]? Background { get; init; }

    /// <summary>Имена файлов подложки из blank.xml, присутствующие в пакете.</summary>
    public IReadOnlyList<string> AssetNames { get; init; } = Array.Empty<string>();

    /// <summary>Дополнительные статические растры assets/ по именам файлов.</summary>
    public IReadOnlyDictionary<string, byte[]> Assets { get; init; } =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Импорт/экспорт пакета бланка .lbk = ZIP-контейнер стандартной дефляции
/// (ТЗ §5.1; §8.3, функция 40: <c>Infrastructure.IO.LbkPackageArchiver</c>).
/// </summary>
/// <remarks>
/// Пакует blank.xml, query.xml и растровые подложки в единый ZIP-архив
/// и распаковывает его с проверкой целостности.
/// </remarks>
public sealed class LbkPackageArchiver
{
    private readonly BlankXmlSerializer _сериализатор;

    /// <summary>Создаёт архиватор пакетов .lbk.</summary>
    /// <param name="serializer">Сериализатор blank.xml; по умолчанию новый.</param>
    public LbkPackageArchiver(BlankXmlSerializer? serializer = null)
    {
        _сериализатор = serializer ?? new BlankXmlSerializer();
    }

    /// <summary>
    /// Экспортирует пакет бланка в ZIP-архив .lbk (ТЗ §8.3, функция 40:
    /// <c>ExportPackage</c>).
    /// </summary>
    /// <param name="blank">Бланк.</param>
    /// <param name="targetZipPath">Путь к целевому файлу .lbk.</param>
    /// <param name="manifest">Описание пакета; <see langword="null"/> — формируется автоматически.</param>
    /// <param name="queryXml">Текст query.xml; <see langword="null"/> — запрос не пакуется.</param>
    /// <param name="background">Байты растровой подложки; <see langword="null"/> — подложки нет.</param>
    /// <param name="assets">Дополнительные растры assets/ по именам файлов.</param>
    public void ExportPackage(
        CustomBlank blank,
        string targetZipPath,
        LbkManifest? manifest = null,
        string? queryXml = null,
        byte[]? background = null,
        IReadOnlyDictionary<string, byte[]>? assets = null)
    {
        ArgumentNullException.ThrowIfNull(blank);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        using var поток = File.Create(targetZipPath);
        ExportPackageToStream(blank, поток, manifest, queryXml, background, assets);
    }

    /// <summary>
    /// Экспортирует пакет бланка в поток ZIP-архива.
    /// </summary>
    /// <param name="blank">Бланк.</param>
    /// <param name="output">Целевой поток.</param>
    /// <param name="manifest">Описание пакета.</param>
    /// <param name="queryXml">Текст query.xml.</param>
    /// <param name="background">Байты подложки.</param>
    /// <param name="assets">Дополнительные растры.</param>
    public void ExportPackageToStream(
        CustomBlank blank,
        Stream output,
        LbkManifest? manifest = null,
        string? queryXml = null,
        byte[]? background = null,
        IReadOnlyDictionary<string, byte[]>? assets = null)
    {
        ArgumentNullException.ThrowIfNull(blank);
        ArgumentNullException.ThrowIfNull(output);

        var описание = manifest ?? new LbkManifest
        {
            Name = blank.Name,
            Version = "1.0",
            Compatibility = blank.Version,
            CreatedAt = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        var blankXml = _сериализатор.SerializeBlank(blank);

        using var архив = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        ЗаписатьЗапись(архив, "manifest.json", JsonSerializer.Serialize(описание, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        ЗаписатьЗапись(архив, "blank.xml", blankXml);

        if (!string.IsNullOrWhiteSpace(queryXml))
        {
            ЗаписатьЗапись(архив, "query.xml", queryXml);
        }

        if (background is not null)
        {
            ЗаписатьБайты(архив, "background.png", background);
        }

        if (assets is not null)
        {
            foreach (var (имя, байты) in assets)
            {
                ЗаписатьБайты(архив, "assets/" + имя, байты);
            }
        }
    }

    /// <summary>
    /// Импортирует пакет бланка из ZIP-архива с проверкой целостности
    /// (ТЗ §8.3, функция 40: <c>ImportPackage</c>).
    /// </summary>
    /// <param name="sourceZipPath">Путь к файлу .lbk.</param>
    /// <returns>Содержимое пакета.</returns>
    /// <exception cref="LbkPackageException">Ошибка структуры пакета.</exception>
    public LbkPackageContent ImportPackage(string sourceZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipPath);

        if (!File.Exists(sourceZipPath))
        {
            throw new LbkPackageException(
                LbkPackageException.StructureErrorCaption + ": файл пакета не найден — " + sourceZipPath);
        }

        using var поток = OpenWithRetry(sourceZipPath);

        return ImportPackageFromStream(поток);
    }

    /// <summary>
    /// Импортирует пакет бланка из потока ZIP-архива.
    /// </summary>
    /// <param name="input">Поток с ZIP-архивом.</param>
    /// <returns>Содержимое пакета.</returns>
    /// <exception cref="LbkPackageException">Ошибка структуры пакета.</exception>
    public LbkPackageContent ImportPackageFromStream(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ZipArchive архив;

        try
        {
            архив = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ошибка)
        {
            throw new LbkPackageException(
                LbkPackageException.StructureErrorCaption + ": файл не является корректным ZIP-контейнером.",
                ошибка);
        }

        using (архив)
        {
            var blankXml = ПрочитатьТекст(архив, "blank.xml");

            if (string.IsNullOrWhiteSpace(blankXml))
            {
                throw new LbkPackageException(
                    LbkPackageException.StructureErrorCaption + ": в пакете отсутствует blank.xml.");
            }

            CustomBlank бланк;

            try
            {
                бланк = _сериализатор.Deserialize(blankXml);
            }
            catch (BlankXmlException ошибка)
            {
                throw new LbkPackageException(
                    LbkPackageException.StructureErrorCaption +
                    $": blank.xml не соответствует XSD-схеме (строка {ошибка.LineNumber}).",
                    ошибка);
            }

            var manifestJson = ПрочитатьТекст(архив, "manifest.json");
            var manifest = РазобратьManifest(manifestJson, бланк.Name);
            var queryXml = ПрочитатьТекст(архив, "query.xml");
            var подложка = ПрочитатьБайты(архив, "background.png");

            var имена = new List<string>();
            var ассеты = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var запись in архив.Entries)
            {
                if (запись.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                    && запись.Length > 0)
                {
                    var имя = запись.FullName["assets/".Length..];

                    имена.Add(имя);
                    ассеты[имя] = ПрочитатьБайты(архив, запись.FullName) ?? Array.Empty<byte>();
                }
            }

            return new LbkPackageContent
            {
                Manifest = manifest,
                Blank = бланк,
                BlankXml = blankXml,
                QueryXml = string.IsNullOrWhiteSpace(queryXml) ? null : queryXml,
                Background = подложка,
                AssetNames = имена,
                Assets = ассеты,
            };
        }
    }

    private static FileStream OpenWithRetry(string путь)
    {
        // Файл .lbk может быть заблокирован другой программой — несколько
        // попыток открытия с короткой паузой.
        for (var попытка = 0; ; попытка++)
        {
            try
            {
                return File.Open(путь, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException) when (попытка < 2)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static LbkManifest РазобратьManifest(string? json, string имяПоУмолчанию)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LbkManifest { Name = имяПоУмолчанию };
        }

        try
        {
            var описание = JsonSerializer.Deserialize<LbkManifest>(json);

            if (описание is null || string.IsNullOrWhiteSpace(описание.Name))
            {
                описание = new LbkManifest { Name = имяПоУмолчанию };
            }

            return описание;
        }
        catch (JsonException ошибка)
        {
            throw new LbkPackageException(
                LbkPackageException.StructureErrorCaption + ": manifest.json поврежден.",
                ошибка);
        }
    }

    private static string? ПрочитатьТекст(ZipArchive архив, string имя)
    {
        var запись = архив.GetEntry(имя);

        if (запись is null)
        {
            return null;
        }

        using var поток = запись.Open();

        using var читатель = new StreamReader(поток);

        return читатель.ReadToEnd();
    }

    private static byte[]? ПрочитатьБайты(ZipArchive архив, string имя)
    {
        var запись = архив.GetEntry(имя);

        if (запись is null)
        {
            return null;
        }

        using var поток = запись.Open();

        using var память = new MemoryStream();
        поток.CopyTo(память);

        return память.ToArray();
    }

    private static void ЗаписатьЗапись(ZipArchive архив, string имя, string текст)
    {
        var запись = архив.CreateEntry(имя, CompressionLevel.Optimal);

        using var поток = запись.Open();

        using var писатель = new StreamWriter(поток, new System.Text.UTF8Encoding(false));
        писатель.Write(текст);
    }

    private static void ЗаписатьБайты(ZipArchive архив, string имя, byte[] байты)
    {
        var запись = архив.CreateEntry(имя, CompressionLevel.Optimal);

        using var поток = запись.Open();
        поток.Write(байты, 0, байты.Length);
    }
}
