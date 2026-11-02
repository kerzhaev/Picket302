using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Ошибка чтения или записи XML-контракта обмена (ТЗ §9.4, функция 77).
/// </summary>
public sealed class ExchangeContractException : Exception
{
    /// <summary>Создаёт ошибку контракта обмена.</summary>
    /// <param name="message">Текст ошибки на русском языке.</param>
    public ExchangeContractException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт ошибку контракта обмена с указанием причины.</summary>
    /// <param name="message">Текст ошибки на русском языке.</param>
    /// <param name="inner">Причина ошибки.</param>
    public ExchangeContractException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Потоковая запись XML-контрактов обмена в три фазы — открыть элемент, записать
/// текст, закрыть элемент (ТЗ §9.4, функция 77: «запись потоковая, в три фазы
/// … без построения полного DOM для больших партий»).
/// </summary>
/// <remarks>
/// Текст записывается через <see cref="XmlWriter.WriteRaw"/> осмысленно: значение
/// предварительно пропущено через <see cref="XmlTextSanitizer.CleanXmlString"/>,
/// поэтому оно не содержит символов <c>&lt;</c> и <c>&gt;</c>, а все ссылки на
/// сущности корректны. Так значение попадает в XML ровно один раз — без двойного
/// экранирования — и не может образовать новый элемент.
/// </remarks>
public static class ExchangeXmlWriter
{
    /// <summary>Отступ для вложенных элементов при форматированной записи.</summary>
    public const string IndentChars = "  ";

    /// <summary>
    /// Создаёт писатель для текстового вывода (без XML-декларации).
    /// </summary>
    /// <param name="textWriter">Приёмник текста.</param>
    /// <param name="indent">Признак форматированной записи с отступами.</param>
    /// <returns>Писатель XML.</returns>
    public static XmlWriter CreateWriter(TextWriter textWriter, bool indent = true)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        var settings = CreateSettings(indent);
        settings.OmitXmlDeclaration = true;

        return XmlWriter.Create(textWriter, settings);
    }

    /// <summary>
    /// Создаёт писатель для файлового вывода (с XML-декларацией в UTF-8 без BOM).
    /// </summary>
    /// <param name="stream">Приёмник байтов.</param>
    /// <param name="indent">Признак форматированной записи с отступами.</param>
    /// <returns>Писатель XML.</returns>
    public static XmlWriter CreateWriter(Stream stream, bool indent = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = CreateSettings(indent);
        settings.OmitXmlDeclaration = false;
        settings.Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return XmlWriter.Create(stream, settings);
    }

    /// <summary>
    /// Формирует XML-документ потоковой записью.
    /// </summary>
    /// <remarks>
    /// Запись выполняется обычным писателем XML, а готовый разобранный текст
    /// превращается в дерево документа. Так текст записывается ровно один раз:
    /// писатель <see cref="XDocument.CreateWriter"/> сохраняет переданный
    /// текст как узел дерева и экранирует его повторно при сериализации, из-за
    /// чего значения с зарезервированными символами искажались бы
    /// («a&amp;amp;b» вместо «a&amp;b»).
    /// </remarks>
    /// <param name="write">Действие записи содержимого контракта.</param>
    /// <returns>Документ с корневым элементом контракта.</returns>
    public static XDocument CreateDocument(Action<XmlWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var буфер = new StringBuilder();

        using (var writer = CreateWriter(
                   new StringWriter(буфер, CultureInfo.InvariantCulture),
                   indent: false))
        {
            write(writer);
        }

        return XDocument.Parse(буфер.ToString());
    }

    /// <summary>
    /// Первая фаза записи: открывает элемент.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="elementName">Имя элемента.</param>
    public static void StartElement(XmlWriter writer, string elementName)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);

        writer.WriteStartElement(elementName);
    }

    /// <summary>
    /// Вторая фаза записи: записывает очищенный текст элемента.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="value">Значение элемента; может быть <see langword="null"/>.</param>
    public static void WriteText(XmlWriter writer, string? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var cleaned = XmlTextSanitizer.CleanXmlString(value);

        if (cleaned.Length == 0)
        {
            // Пустое значение записывается явным пустым элементом <Name></Name>.
            writer.WriteString(string.Empty);
            return;
        }

        // Возврат каретки сохраняется числовой ссылкой: иначе XML-парсер
        // приводит последовательность CRLF к LF, и значение не воспроизводится
        // при обратном чтении (ТЗ §8.5, функция 77: круговое преобразование
        // «печать → XML → чтение → печать»).
        writer.WriteRaw(cleaned.Replace("\r", "&#xD;", StringComparison.Ordinal));
    }

    /// <summary>
    /// Третья фаза записи: закрывает элемент.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public static void EndElement(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteEndElement();
    }

    /// <summary>
    /// Записывает элемент целиком: открывает его, записывает текст и закрывает.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="elementName">Имя элемента.</param>
    /// <param name="value">Значение элемента; может быть <see langword="null"/>.</param>
    public static void WriteElement(XmlWriter writer, string elementName, string? value)
    {
        StartElement(writer, elementName);
        WriteText(writer, value);
        EndElement(writer);
    }

    /// <summary>
    /// Записывает атрибут элемента. Значение очищается от недопустимых символов,
    /// а экранирование выполняет сам писатель.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="attributeName">Имя атрибута.</param>
    /// <param name="value">Значение атрибута; может быть <see langword="null"/>.</param>
    public static void WriteAttribute(XmlWriter writer, string attributeName, string? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);

        if (value is null)
        {
            return;
        }

        writer.WriteAttributeString(attributeName, XmlTextSanitizer.RemoveInvalidCharacters(value));
    }

    /// <summary>
    /// Записывает вложенный zip-пакет (бланк, клипарт) как элемент
    /// <see cref="EmbeddedZipPackageExtractor.ElementName"/> с содержимым в base64.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="name">Имя файла пакета.</param>
    /// <param name="archive">Содержимое zip-архива.</param>
    public static void WriteZipPackage(XmlWriter writer, string name, byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(archive);

        StartElement(writer, EmbeddedZipPackageExtractor.ElementName);
        WriteAttribute(writer, EmbeddedZipPackageExtractor.NameAttributeName, name);
        WriteText(writer, Convert.ToBase64String(archive));
        EndElement(writer);
    }

    private static XmlWriterSettings CreateSettings(bool indent) => new()
    {
        Indent = indent,
        IndentChars = IndentChars,
        NewLineChars = "\r\n",
        NewLineHandling = NewLineHandling.Replace,
        CheckCharacters = true,
        CloseOutput = false,
    };
}

/// <summary>
/// Общие помощники чтения XML-контрактов обмена (ТЗ §9.4, функция 77).
/// </summary>
public static class ExchangeXmlReader
{
    /// <summary>
    /// Создаёт читатель для текста XML. Недоверенный вход пропускается через
    /// санитайзер, запрещаются DTD и внешние сущности.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <returns>Читатель XML.</returns>
    public static XmlReader CreateReader(string xml)
    {
        var cleaned = XmlTextSanitizer.RemoveInvalidCharacters(xml ?? string.Empty);

        return XmlReader.Create(new StringReader(cleaned), CreateSettings());
    }

    /// <summary>
    /// Создаёт читатель для текстового источника XML.
    /// </summary>
    /// <param name="textReader">Источник текста.</param>
    /// <returns>Читатель XML.</returns>
    public static XmlReader CreateReader(TextReader textReader)
    {
        ArgumentNullException.ThrowIfNull(textReader);

        return XmlReader.Create(textReader, CreateSettings());
    }

    /// <summary>
    /// Создаёт читатель для файлового источника XML.
    /// </summary>
    /// <param name="stream">Источник байтов.</param>
    /// <returns>Читатель XML.</returns>
    public static XmlReader CreateReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return XmlReader.Create(stream, CreateSettings());
    }

    /// <summary>
    /// Переводит читатель на первый элемент содержимого документа.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns><see langword="true"/>, если в документе есть элемент.</returns>
    public static bool MoveToContent(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.MoveToContent() == XmlNodeType.Element;
    }

    /// <summary>
    /// Читает текст текущего элемента и переводит читатель на следующий узел.
    /// Вложенная разметка внутри значения игнорируется.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе.</param>
    /// <returns>Текст элемента.</returns>
    /// <exception cref="ExchangeContractException">Читатель не positioned на элементе.</exception>
    public static string ReadElementText(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент с текстовым значением.");
        }

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return string.Empty;
        }

        var text = new StringBuilder();
        var depth = 1;

        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                depth--;
                reader.Read();

                if (depth == 0)
                {
                    break;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                // Разметка внутри значения не является данными и пропускается.
                SkipElement(reader);
                continue;
            }

            if (reader.NodeType is XmlNodeType.Text
                or XmlNodeType.CDATA
                or XmlNodeType.SignificantWhitespace
                or XmlNodeType.Whitespace)
            {
                text.Append(reader.Value);
            }

            reader.Read();
        }

        return text.ToString();
    }

    /// <summary>
    /// Пропускает текущий элемент вместе с его содержимым.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    public static void SkipElement(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType == XmlNodeType.Element && reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.Skip();
    }

    /// <summary>
    /// Читает список элементов контейнера. Корневой элемент может быть как контейнером
    /// (<c>EnvelopesData</c>), так и самим элементом списка (<c>EnvelopeData</c>).
    /// </summary>
    /// <typeparam name="T">Тип элемента списка.</typeparam>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="rootElementName">Имя корневого элемента контейнера.</param>
    /// <param name="itemElementNames">Имена элементов списка.</param>
    /// <param name="readItem">Чтение одного элемента списка.</param>
    /// <returns>
    /// Успешный результат со списком элементов либо текст ошибки разбора XML.
    /// </returns>
    public static Result<IReadOnlyList<T>> ReadList<T>(
        XmlReader reader,
        string rootElementName,
        IReadOnlyList<string> itemElementNames,
        Func<XmlReader, T> readItem)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(itemElementNames);
        ArgumentNullException.ThrowIfNull(readItem);

        try
        {
            var items = new List<T>();

            if (!MoveToContent(reader))
            {
                return Result<IReadOnlyList<T>>.Failure(
                    "Ошибка чтения XML-данных: документ не содержит элементов данных.");
            }

            if (Contains(itemElementNames, reader.LocalName))
            {
                items.Add(readItem(reader));
                return Result<IReadOnlyList<T>>.Success(items);
            }

            if (!string.Equals(reader.LocalName, rootElementName, StringComparison.Ordinal))
            {
                return Result<IReadOnlyList<T>>.Failure(
                    $"Ошибка чтения XML-данных: ожидался корневой элемент «{rootElementName}», получен «{reader.LocalName}».");
            }

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return Result<IReadOnlyList<T>>.Success(items);
            }

            reader.Read();

            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.EndElement
                    && string.Equals(reader.LocalName, rootElementName, StringComparison.Ordinal))
                {
                    reader.Read();
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (Contains(itemElementNames, reader.LocalName))
                    {
                        items.Add(readItem(reader));
                    }
                    else
                    {
                        SkipElement(reader);
                    }

                    continue;
                }

                reader.Read();
            }

            return Result<IReadOnlyList<T>>.Success(items);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<T>>.Failure("Ошибка чтения XML-данных: " + error.Message);
        }
        catch (ExchangeContractException error)
        {
            return Result<IReadOnlyList<T>>.Failure("Ошибка чтения XML-данных: " + error.Message);
        }
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
        => names.Contains(name, StringComparer.Ordinal);

    private static XmlReaderSettings CreateSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
        CheckCharacters = true,
        CloseInput = true,
    };
}

/// <summary>
/// Извлечение вложенных zip-пакетов (бланки, клипарт), которые передаются внутри XML
/// (ТЗ §9.4, функция 77: «вложенные zip-пакеты … могут передаваться внутри XML
/// и извлекаются штатным средством»).
/// </summary>
public static class EmbeddedZipPackageExtractor
{
    /// <summary>Имя элемента с вложенным zip-пакетом.</summary>
    public const string ElementName = "ZipPackage";

    /// <summary>Имя атрибута с именем файла пакета.</summary>
    public const string NameAttributeName = "name";

    /// <summary>
    /// Извлекает вложенные zip-пакеты из XML-документа в указанный каталог.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="targetDirectory">Каталог для распаковки пакетов.</param>
    /// <returns>Список путей сохранённых архивов.</returns>
    public static IReadOnlyList<string> Extract(XmlReader reader, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Directory.CreateDirectory(targetDirectory);

        var saved = new List<string>();
        var number = 0;

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element
                && string.Equals(reader.LocalName, ElementName, StringComparison.Ordinal))
            {
                var name = reader.GetAttribute(NameAttributeName);
                var payload = ExchangeXmlReader.ReadElementText(reader);

                var archiveName = SaveArchive(targetDirectory, name, payload, ++number);

                if (archiveName is not null)
                {
                    saved.Add(archiveName);
                }

                continue;
            }

            reader.Read();
        }

        return saved;
    }

    /// <summary>
    /// Извлекает вложенные zip-пакеты из текста XML в указанный каталог.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <param name="targetDirectory">Каталог для распаковки пакетов.</param>
    /// <returns>Список путей сохранённых архивов.</returns>
    public static IReadOnlyList<string> ExtractFromXml(string xml, string targetDirectory)
    {
        using var reader = ExchangeXmlReader.CreateReader(xml);

        return Extract(reader, targetDirectory);
    }

    private static string? SaveArchive(string targetDirectory, string? name, string payload, int number)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(payload.Trim());
        }
        catch (FormatException)
        {
            return null;
        }

        var fileName = SafeFileName(name, number);
        var path = Path.Combine(targetDirectory, fileName);

        File.WriteAllBytes(path, bytes);
        Unpack(path, targetDirectory);

        return path;
    }

    private static void Unpack(string archivePath, string targetDirectory)
    {
        try
        {
            // Границы корня с завершающим разделителем: сравнение по голому
            // префиксу пропускало запись «..\out2\evil.exe» при цели «...\out»
            // — соседний каталог «out2» начинался тем же префиксом (F-D4).
            var корень = Path.GetFullPath(targetDirectory);

            if (!корень.EndsWith(Path.DirectorySeparatorChar))
            {
                корень += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var entryPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));

                if (!entryPath.StartsWith(корень, StringComparison.OrdinalIgnoreCase))
                {
                    // Запись за пределами каталога распаковки не допускается.
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
                entry.ExtractToFile(entryPath, overwrite: true);
            }
        }
        catch (InvalidDataException)
        {
            // Содержимое не является zip-архивом: сохраняется как есть.
        }
        catch (IOException)
        {
            // Архив недоступен для распаковки: сохраняется как есть.
        }
    }

    private static string SafeFileName(string? name, int number)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"package-{number}.zip";
        }

        var builder = new StringBuilder(name.Length);

        foreach (var symbol in name)
        {
            builder.Append(char.IsLetterOrDigit(symbol) || symbol is '.' or '_' or '-' ? symbol : '_');
        }

        var result = builder.ToString().Trim('.');

        return result.Length == 0 ? $"package-{number}.zip" : result;
    }
}
