using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Picket302.Infrastructure.Export;

/// <summary>
/// Часть пакета Open Packaging Conventions: текстовая или двоичная
/// (ТЗ §8.5, функция 70 — «Полная независимость экспорта от Microsoft Office»).
/// </summary>
/// <remarks>
/// Пакет OPC (DOCX, XLSX) — это ZIP-архив, части которого могут быть как
/// XML-документами, так и двоичными вложениями: растровые подложки, вложенные
/// архивы бланков (ТЗ §9.4: «вложенные zip-пакеты (бланки, клипарт) могут
/// передаваться внутри XML»). Тип хранит содержимое части вместе с признаком
/// «текст/двоичный» и не зависит ни от Microsoft Office, ни от COM.
/// </remarks>
public sealed class OpenXmlPackagePart
{
    /// <summary>Кодировка текстовых частей пакета (UTF-8 без BOM).</summary>
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Создаёт текстовую часть пакета.</summary>
    /// <param name="name">Имя части внутри пакета (например, «word/document.xml»).</param>
    /// <param name="text">Текстовое содержимое части.</param>
    public OpenXmlPackagePart(string name, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);

        Name = name;
        Content = _utf8.GetBytes(text);
        IsText = true;
    }

    /// <summary>Создаёт двоичную часть пакета.</summary>
    /// <param name="name">Имя части внутри пакета (например, «media/image1.png»).</param>
    /// <param name="content">Двоичное содержимое части.</param>
    public OpenXmlPackagePart(string name, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);

        Name = name;
        Content = (byte[])content.Clone();
        IsText = false;
    }

    /// <summary>Имя части внутри пакета.</summary>
    public string Name { get; }

    /// <summary>Признак текстовой части; двоичные части возвращают <see langword="false"/>.</summary>
    public bool IsText { get; }

    /// <summary>Содержимое части в байтах.</summary>
    public byte[] Content { get; }

    /// <summary>Текстовое содержимое части.</summary>
    /// <exception cref="InvalidOperationException">Часть является двоичной.</exception>
    public string Text => IsText
        ? _utf8.GetString(Content)
        : throw new InvalidOperationException(
            $"Часть пакета «{Name}» является двоичной и не имеет текстового представления.");

    /// <summary>Создаёт текстовую часть пакета.</summary>
    /// <param name="name">Имя части внутри пакета.</param>
    /// <param name="text">Текстовое содержимое части.</param>
    /// <returns>Текстовая часть пакета.</returns>
    public static OpenXmlPackagePart FromText(string name, string text) => new(name, text);

    /// <summary>Создаёт двоичную часть пакета.</summary>
    /// <param name="name">Имя части внутри пакета.</param>
    /// <param name="content">Двоичное содержимое части.</param>
    /// <returns>Двоичная часть пакета.</returns>
    public static OpenXmlPackagePart FromBytes(string name, byte[] content) => new(name, content);
}

/// <summary>
/// Упаковщик пакетов Open Packaging Conventions без Microsoft Office
/// (ТЗ §8.5, функция 70 — «Полная независимость экспорта от Microsoft Office»).
/// </summary>
/// <remarks>
/// <para>
/// DOCX и XLSX — это ZIP-архивы контейнера OPC: карта типов содержимого
/// <c>[Content_Types].xml</c> и связи <c>_rels/.rels</c> описывают части пакета.
/// Класс является общим строительным блоком обоих экспортёров
/// (<see cref="WordprocessingMlExporter"/>, <see cref="SpreadsheetMlExporter"/>):
/// упаковка выполняется через <see cref="System.IO.Compression.ZipArchive"/>,
/// без OLE, COM и без Interop-библиотек Microsoft Office.
/// </para>
/// <para>
/// Порядок частей сохраняется по образцу сборки; карта типов содержимого
/// <c>[Content_Types].xml</c> записывается первой, как того требует OPC.
/// </para>
/// </remarks>
public sealed class NativeOpenXmlPackager
{
    /// <summary>Имя карты типов содержимого пакета.</summary>
    public const string ContentTypesPartName = "[Content_Types].xml";

    /// <summary>Имя части со связями пакета.</summary>
    public const string PackageRelationshipsPartName = "_rels/.rels";

    /// <summary>Первый байт сигнатуры ZIP-архива «PK».</summary>
    private const byte ZipSignatureFirst = 0x50;

    /// <summary>Второй байт сигнатуры ZIP-архива «PK».</summary>
    private const byte ZipSignatureSecond = 0x4B;

    /// <summary>Записывает пакет OPC в файл.</summary>
    /// <param name="packagePath">Путь к целевому файлу пакета.</param>
    /// <param name="parts">Части пакета в порядке записи.</param>
    public void WritePackage(string packagePath, IEnumerable<OpenXmlPackagePart> parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(parts);

        var fullPath = Path.GetFullPath(packagePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(fullPath);

        WritePackageToStream(stream, parts);
    }

    /// <summary>Записывает пакет OPC в поток.</summary>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    /// <param name="parts">Части пакета в порядке записи.</param>
    public void WritePackageToStream(Stream output, IEnumerable<OpenXmlPackagePart> parts)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(parts);

        var ordered = OrderParts(parts);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var part in ordered)
        {
            var entry = archive.CreateEntry(part.Name, CompressionLevel.Optimal);

            using var entryStream = entry.Open();

            entryStream.Write(part.Content, 0, part.Content.Length);
        }
    }

    /// <summary>Читает пакет OPC из файла.</summary>
    /// <param name="packagePath">Путь к файлу пакета.</param>
    /// <returns>Части пакета в порядке записи в архиве.</returns>
    public IReadOnlyList<OpenXmlPackagePart> ReadPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        using var stream = File.OpenRead(packagePath);

        return ReadPackageFromStream(stream);
    }

    /// <summary>Читает пакет OPC из потока.</summary>
    /// <param name="input">Поток с ZIP-архивом пакета.</param>
    /// <returns>Части пакета в порядке записи в архиве.</returns>
    public IReadOnlyList<OpenXmlPackagePart> ReadPackageFromStream(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);

        var parts = new List<OpenXmlPackagePart>();

        foreach (var entry in archive.Entries)
        {
            // Запись каталога в ZIP не является частью пакета OPC.
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();

            entryStream.CopyTo(buffer);

            parts.Add(new OpenXmlPackagePart(entry.FullName, buffer.ToArray()));
        }

        return parts;
    }

    /// <summary>Ищет часть пакета по имени.</summary>
    /// <param name="packagePath">Путь к файлу пакета.</param>
    /// <param name="partName">Имя части.</param>
    /// <param name="part">Найденная часть; <see langword="null"/> — часть отсутствует.</param>
    /// <returns><see langword="true"/>, если часть найдена.</returns>
    public bool TryGetPart(string packagePath, string partName, out OpenXmlPackagePart? part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);

        foreach (var candidate in ReadPackage(packagePath))
        {
            if (string.Equals(candidate.Name, partName, StringComparison.OrdinalIgnoreCase))
            {
                part = candidate;
                return true;
            }
        }

        part = null;
        return false;
    }

    /// <summary>Читает текстовую часть пакета.</summary>
    /// <param name="packagePath">Путь к файлу пакета.</param>
    /// <param name="partName">Имя части.</param>
    /// <returns>Текст части; <see langword="null"/> — часть отсутствует.</returns>
    public string? ReadTextPart(string packagePath, string partName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);

        if (!TryGetPart(packagePath, partName, out var part) || part is null)
        {
            return null;
        }

        return Encoding.UTF8.GetString(part.Content);
    }

    /// <summary>
    /// Извлекает вложенный ZIP-архив, переданный внутри XML-части пакета
    /// (ТЗ §9.4: вложенные zip-пакеты бланков и клипарта переносятся внутри XML).
    /// </summary>
    /// <param name="packagePath">Путь к файлу пакета.</param>
    /// <param name="partName">Имя XML-части с вложением.</param>
    /// <returns>Байты вложенного ZIP-архива.</returns>
    /// <exception cref="InvalidOperationException">
    /// Часть отсутствует либо вложенный архив в ней не найден.
    /// </exception>
    public byte[] ExtractNestedZipBytes(string packagePath, string partName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);

        if (!TryGetPart(packagePath, partName, out var part) || part is null)
        {
            throw new InvalidOperationException($"Часть пакета «{partName}» не найдена.");
        }

        return ExtractNestedZipBytes(part);
    }

    /// <summary>
    /// Извлекает вложенный ZIP-архив, переданный внутри XML-части пакета.
    /// </summary>
    /// <param name="part">XML-часть с вложением.</param>
    /// <returns>Байты вложенного ZIP-архива.</returns>
    /// <exception cref="InvalidOperationException">Вложенный архив в части не найден.</exception>
    public byte[] ExtractNestedZipBytes(OpenXmlPackagePart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var text = Encoding.UTF8.GetString(part.Content);

        XDocument document;

        try
        {
            document = XDocument.Parse(text, LoadOptions.None);
        }
        catch (XmlException error)
        {
            throw new InvalidOperationException(
                $"Часть пакета «{part.Name}» не является корректным XML: {error.Message}",
                error);
        }

        foreach (var candidate in CollectBase64Candidates(document))
        {
            if (!TryDecodeBase64(candidate, out var bytes))
            {
                continue;
            }

            if (IsZipArchive(bytes))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException(
            $"Вложенный ZIP-пакет в части «{part.Name}» не найден.");
    }

    /// <summary>
    /// Извлекает вложенный ZIP-архив из XML-части пакета в отдельный файл.
    /// </summary>
    /// <param name="packagePath">Путь к файлу пакета.</param>
    /// <param name="partName">Имя XML-части с вложением.</param>
    /// <param name="targetZipPath">Путь к извлекаемому файлу архива.</param>
    public void ExtractNestedZip(string packagePath, string partName, string targetZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        var bytes = ExtractNestedZipBytes(packagePath, partName);

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetZipPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(targetZipPath, bytes);
    }

    /// <summary>
    /// Проверяет архитектурную независимость экспорта от Microsoft Office
    /// (ТЗ §8.5, функция 70).
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, если ни сборка упаковщика, ни загруженные сборки
    /// процесса, ни сборки каталога поставки не ссылаются на Interop-библиотеки
    /// Microsoft Office (сборки пространства имён «Microsoft.Office.*») и на
    /// библиотеку «office».
    /// </returns>
    public bool ValidateOfficeAbsenceCompatibility()
    {
        var referenced = typeof(NativeOpenXmlPackager).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty);

        if (referenced.Concat(loaded).Any(IsOfficeAssembly))
        {
            return false;
        }

        // Проверяются и сборки каталога поставки: зависимость от Office может
        // появиться в прикладном или инфраструктурном слое, а не в самом
        // упаковщике.
        var каталог = AppContext.BaseDirectory;

        if (!Directory.Exists(каталог))
        {
            return true;
        }

        var файлы = Directory.GetFiles(каталог, "*.dll")
            .Concat(Directory.GetFiles(каталог, "*.exe"));

        foreach (var файл in файлы)
        {
            if (ПрочитатьСсылкиСборки(файл).Any(IsOfficeAssembly))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Читает имена сборок, на которые ссылается файл сборки, без её загрузки.
    /// </summary>
    /// <param name="путь">Путь к файлу сборки.</param>
    /// <returns>Имена сборок, на которые ссылается сборка.</returns>
    private static IReadOnlyList<string> ПрочитатьСсылкиСборки(string путь)
    {
        try
        {
            using var поток = File.OpenRead(путь);
            using var peReader = new PEReader(поток);

            if (!peReader.HasMetadata)
            {
                return [];
            }

            var reader = peReader.GetMetadataReader();
            var ссылки = new List<string>();

            foreach (var handle in reader.AssemblyReferences)
            {
                var ссылка = reader.GetAssemblyReference(handle);

                ссылки.Add(reader.GetString(ссылка.Name));
            }

            return ссылки;
        }
        catch (BadImageFormatException)
        {
            // Файл не является сборкой .NET — проверять нечего.
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>Признак сборки Microsoft Office (Interop, Core, «office»).</summary>
    /// <param name="name">Имя сборки.</param>
    /// <returns><see langword="true"/>, если сборка относится к Microsoft Office.</returns>
    private static bool IsOfficeAssembly(string name) =>
        name.StartsWith("Microsoft.Office", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Microsoft.Office.Core", StringComparison.OrdinalIgnoreCase)
        || name.Equals("office", StringComparison.OrdinalIgnoreCase);

    /// <summary>Упорядочивает части пакета: карта типов содержимого идёт первой.</summary>
    /// <param name="parts">Исходные части пакета.</param>
    /// <returns>Части в порядке записи.</returns>
    private static IReadOnlyList<OpenXmlPackagePart> OrderParts(IEnumerable<OpenXmlPackagePart> parts)
    {
        var list = parts.ToList();

        var duplicates = list
            .GroupBy(part => part.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"В пакете повторяются имена частей: {string.Join(", ", duplicates)}.");
        }

        var contentTypes = list
            .Where(part => string.Equals(part.Name, ContentTypesPartName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var others = list
            .Where(part => !string.Equals(part.Name, ContentTypesPartName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return contentTypes.Concat(others).ToList();
    }

    /// <summary>Собирает строки-кандидаты на base64 из текста XML-части.</summary>
    /// <param name="document">Разобранный XML-документ части.</param>
    /// <returns>Кандидаты в порядке обхода документа.</returns>
    private static IEnumerable<string> CollectBase64Candidates(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var node in element.Nodes().OfType<XText>())
            {
                yield return node.Value;
            }

            foreach (var attribute in element.Attributes())
            {
                yield return attribute.Value;
            }
        }
    }

    /// <summary>Пытается декодировать строку как base64.</summary>
    /// <param name="value">Исходная строка.</param>
    /// <param name="bytes">Декодированные байты.</param>
    /// <returns><see langword="true"/>, если строка декодирована.</returns>
    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        var compact = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

        if (compact.Length < 8)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(compact);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Признак ZIP-архива по сигнатуре.</summary>
    /// <param name="bytes">Проверяемые байты.</param>
    /// <returns><see langword="true"/>, если содержимое является ZIP-архивом.</returns>
    private static bool IsZipArchive(byte[] bytes)
    {
        if (bytes.Length < 4
            || bytes[0] != ZipSignatureFirst
            || bytes[1] != ZipSignatureSecond)
        {
            return false;
        }

        // 0x03 0x04 — обычный архив, 0x05 0x06 — пустой, 0x07 0x08 — многотомный.
        var third = bytes[2];
        var fourth = bytes[3];

        return (third == 0x03 && fourth == 0x04)
            || (third == 0x05 && fourth == 0x06)
            || (third == 0x07 && fourth == 0x08);
    }
}
