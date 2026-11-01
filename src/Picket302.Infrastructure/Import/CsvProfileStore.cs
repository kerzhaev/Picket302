using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Contracts;

namespace Picket302.Infrastructure.Import;

/// <summary>
/// Хранилище профилей сопоставления CSV в файлах с расширением *.csvprof
/// (ТЗ §8.12, функция 73: «Данные: Файлы конфигурации *.csvprof»).
/// </summary>
/// <remarks>
/// <para>
/// Формат файла — JSON с маркером формата и номером версии, поэтому файл
/// читаем без дополнительных библиотек и однозначно определяется как профиль.
/// Повреждённый файл не загружается молча: выдаётся ошибка с указанием имени
/// файла и причины (ТЗ §8.12, функция 73).
/// </para>
/// <para>
/// Класс повторяет контракт интерфейса приложения
/// <c>Picket302.Application.Import.ICsvProfileStore</c> один в один: слой
/// инфраструктуры не ссылается на слой приложения (ТЗ §2.1), поэтому
/// подключение выполняется в слое приложения через
/// <c>Picket302.Application.Import.DelegateCsvProfileStore</c>.
/// </para>
/// </remarks>
public sealed class CsvProfileStore
{
    /// <summary>Маркер формата файла профиля.</summary>
    public const string ФорматФайла = "Picket302.CsvProfile";

    /// <summary>Номер версии формата файла профиля.</summary>
    public const int ВерсияФайла = 1;

    /// <summary>Расширение файла профиля.</summary>
    public const string РасширениеФайла = CsvProfile.FileExtension;

    /// <summary>Имя кодировки UTF-8 — значение по умолчанию для профиля.</summary>
    private const string ИмяКодировкиПоУмолчанию = "utf-8";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Сохраняет профиль сопоставления в файл.
    /// </summary>
    /// <param name="profile">Сохраняемый профиль.</param>
    /// <param name="filePath">Путь к файлу профиля (*.csvprof).</param>
    /// <exception cref="ArgumentException">Путь к файлу не задан.</exception>
    public void Save(CsvProfile profile, string filePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var полныйПуть = Path.GetFullPath(filePath);

        // Профиль хранится в файле с расширением *.csvprof (ТЗ §8.12, функция 73).
        if (!string.Equals(Path.GetExtension(полныйПуть), РасширениеФайла, StringComparison.OrdinalIgnoreCase))
        {
            полныйПуть += РасширениеФайла;
        }

        var каталог = Path.GetDirectoryName(полныйПуть);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var содержимое = new ПрофильJson
        {
            Format = ФорматФайла,
            Version = ВерсияФайла,
            Name = profile.Name,
            EncodingName = profile.EncodingName,
            Delimiter = profile.Delimiter.ToString(),
            QuoteChar = profile.QuoteChar.ToString(),
            HasHeaderRow = profile.HasHeaderRow,
            FirstDataRow = profile.FirstDataRow,
            Bindings = [.. profile.Bindings.Select(привязка => new ПривязкаJson
            {
                ColumnIndex = привязка.ColumnIndex,
                Slot = привязка.Slot?.ToString(),
                ColumnName = привязка.ColumnName,
            })],
            FilePath = полныйПуть,
        };

        var текст = JsonSerializer.Serialize(содержимое, ПараметрыJson);

        File.WriteAllText(полныйПуть, текст, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// Загружает профиль сопоставления из файла.
    /// </summary>
    /// <param name="filePath">Путь к файлу профиля (*.csvprof).</param>
    /// <returns>Загруженный профиль сопоставления.</returns>
    /// <exception cref="FileNotFoundException">Файл профиля не найден.</exception>
    /// <exception cref="CsvProfileFormatException">
    /// Файл профиля повреждён: содержимое не соответствует ожидаемому формату.
    /// </exception>
    public CsvProfile Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var полныйПуть = Path.GetFullPath(filePath);

        if (!File.Exists(полныйПуть))
        {
            throw new FileNotFoundException(
                $"Файл профиля «{полныйПуть}» не найден.",
                полныйПуть);
        }

        string текст;

        try
        {
            текст = File.ReadAllText(полныйПуть);
        }
        catch (IOException ошибка)
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» не удалось прочитать: {ошибка.Message}",
                ошибка);
        }

        ПрофильJson? содержимое;

        try
        {
            содержимое = JsonSerializer.Deserialize<ПрофильJson>(текст, ПараметрыJson);
        }
        catch (JsonException ошибка)
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: содержимое не является корректным JSON ({ошибка.Message}).",
                ошибка);
        }

        if (содержимое is null)
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: содержимое файла пустое.");
        }

        if (!string.Equals(содержимое.Format, ФорматФайла, StringComparison.Ordinal))
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: ожидается формат «{ФорматФайла}».");
        }

        if (содержимое.Version != ВерсияФайла)
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: не поддерживается версия профиля {содержимое.Version}.");
        }

        if (string.IsNullOrWhiteSpace(содержимое.Name))
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: не задано имя профиля.");
        }

        if (!ПроверитьСимвол(содержимое.Delimiter, out var разделитель))
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: не задан символ разделителя столбцов.");
        }

        if (!ПроверитьСимвол(содержимое.QuoteChar, out var кавычка))
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: не задан символ обрамления значений.");
        }

        if (содержимое.FirstDataRow < 1)
        {
            throw new CsvProfileFormatException(
                $"Файл профиля «{полныйПуть}» повреждён: неверно задана строка начала данных.");
        }

        var привязки = new List<ImportColumnBinding>();

        foreach (var привязка in содержимое.Bindings ?? [])
        {
            if (привязка.ColumnIndex < 0)
            {
                throw new CsvProfileFormatException(
                    $"Файл профиля «{полныйПуть}» повреждён: неверно задан номер столбца в сопоставлении.");
            }

            ImportFieldSlot? слот = null;

            if (!string.IsNullOrWhiteSpace(привязка.Slot))
            {
                if (!Enum.TryParse<ImportFieldSlot>(привязка.Slot, ignoreCase: true, out var разобранный))
                {
                    throw new CsvProfileFormatException(
                        $"Файл профиля «{полныйПуть}» повреждён: неизвестное поле «{привязка.Slot}» в сопоставлении столбцов.");
                }

                слот = разобранный;
            }

            привязки.Add(new ImportColumnBinding(привязка.ColumnIndex, слот, привязка.ColumnName));
        }

        return new CsvProfile
        {
            Name = содержимое.Name,
            EncodingName = string.IsNullOrWhiteSpace(содержимое.EncodingName)
                ? ИмяКодировкиПоУмолчанию
                : содержимое.EncodingName,
            Delimiter = разделитель,
            QuoteChar = кавычка,
            HasHeaderRow = содержимое.HasHeaderRow,
            FirstDataRow = содержимое.FirstDataRow,
            Bindings = привязки,
            FilePath = полныйПуть,
        };
    }

    /// <summary>
    /// Перечисляет файлы профилей в каталоге.
    /// </summary>
    /// <param name="directory">Каталог с файлами профилей.</param>
    /// <returns>Полные пути к файлам профилей в порядке сортировки по имени.</returns>
    /// <exception cref="DirectoryNotFoundException">Каталог не найден.</exception>
    public IReadOnlyList<string> ListProfiles(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var полныйПуть = Path.GetFullPath(directory);

        if (!Directory.Exists(полныйПуть))
        {
            throw new DirectoryNotFoundException($"Каталог профилей «{полныйПуть}» не найден.");
        }

        return [.. Directory
            .EnumerateFiles(полныйПуть, $"*{РасширениеФайла}", SearchOption.TopDirectoryOnly)
            .OrderBy(путь => путь, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Собирает путь файла профиля по имени профиля и каталогу.
    /// </summary>
    /// <param name="directory">Каталог хранения профилей.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Путь файла профиля.</returns>
    public static string BuildFilePath(string directory, string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var безопасноеИмя = new string(profileName.Select(символ =>
            Path.GetInvalidFileNameChars().Contains(символ) ? '_' : символ).ToArray());

        return Path.Combine(Path.GetFullPath(directory), безопасноеИмя + РасширениеФайла);
    }

    /// <summary>
    /// Проверяет, что в поле файла задан ровно один символ.
    /// </summary>
    /// <param name="значение">Значение поля файла.</param>
    /// <param name="символ">Распознанный символ.</param>
    /// <returns><see langword="true"/>, если задано ровно один символ.</returns>
    private static bool ПроверитьСимвол(string? значение, out char символ)
    {
        символ = '\0';

        if (string.IsNullOrEmpty(значение) || значение.Length != 1)
        {
            return false;
        }

        символ = значение[0];

        return символ != '\0';
    }

    /// <summary>Содержимое файла профиля.</summary>
    private sealed class ПрофильJson
    {
        /// <summary>Маркер формата файла.</summary>
        public string Format { get; set; } = ФорматФайла;

        /// <summary>Номер версии формата файла.</summary>
        public int Version { get; set; } = ВерсияФайла;

        /// <summary>Имя профиля.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Имя кодировки файла-источника.</summary>
        public string EncodingName { get; set; } = ИмяКодировкиПоУмолчанию;

        /// <summary>Разделитель столбцов.</summary>
        public string Delimiter { get; set; } = ",";

        /// <summary>Символ обрамления значений.</summary>
        public string QuoteChar { get; set; } = "\"";

        /// <summary>Признак наличия строки заголовков.</summary>
        public bool HasHeaderRow { get; set; } = true;

        /// <summary>Номер первой строки данных.</summary>
        public int FirstDataRow { get; set; } = 1;

        /// <summary>Сопоставление столбцов файла со слотами полей.</summary>
        public List<ПривязкаJson> Bindings { get; set; } = [];

        /// <summary>Путь к файлу профиля.</summary>
        public string? FilePath { get; set; }
    }

    /// <summary>Сопоставление столбца файла со слотом поля.</summary>
    private sealed class ПривязкаJson
    {
        /// <summary>Номер столбца файла (нумерация с нуля).</summary>
        public int ColumnIndex { get; set; }

        /// <summary>Имя слота поля.</summary>
        public string? Slot { get; set; }

        /// <summary>Название столбца из словаря полей.</summary>
        public string? ColumnName { get; set; }
    }
}

/// <summary>
/// Ошибка формата файла профиля сопоставления (ТЗ §8.12, функция 73).
/// </summary>
public sealed class CsvProfileFormatException : Exception
{
    /// <summary>Создаёт ошибку формата файла профиля.</summary>
    /// <param name="message">Описание ошибки на русском языке.</param>
    public CsvProfileFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт ошибку формата файла профиля с указанием причины.</summary>
    /// <param name="message">Описание ошибки на русском языке.</param>
    /// <param name="внутренняяОшибка">Исходная ошибка чтения или разбора файла.</param>
    public CsvProfileFormatException(string message, Exception внутренняяОшибка)
        : base(message, внутренняяОшибка)
    {
    }
}


