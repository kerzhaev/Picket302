using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Строка почтового реестра (ТЗ §9.4, функция 77: <c>Package(Number, To, Note)</c>).
/// </summary>
public sealed record RegisterPackage
{
    /// <summary>Номер отправления (ТЗ §9.3: API v2).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>Получатель: элемент <c>To</c>.</summary>
    public EnvelopeParty To { get; init; } = new();

    /// <summary>Особые отметки отправителя (ТЗ §9.3: API v2).</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// Записывает строку реестра в XML: элемент <c>Package</c> с элементами
    /// <c>Number</c>, <c>To</c> и <c>Note</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, RegistersData.PackageElementName);
        ExchangeXmlWriter.WriteElement(writer, "Number", Number);
        To.WriteToXml(writer, "To", includePhone: false);
        ExchangeXmlWriter.WriteElement(writer, "Note", Note);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает строку реестра из XML: элемент <c>Package</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе строки.</param>
    /// <returns>Прочитанная строка реестра.</returns>
    public static RegisterPackage ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент строки почтового реестра.");
        }

        var package = new RegisterPackage();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return package;
        }

        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.Read();
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Number":
                        package = package with { Number = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "To":
                        package = package with { To = EnvelopeParty.ReadFromXml(reader) };
                        break;
                    case "Note":
                        package = package with { Note = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return package;
    }

    /// <summary>
    /// Приводит строку реестра к печатному виду.
    /// </summary>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Строка реестра в печатном виде.</returns>
    public RegisterPackage FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        return this with
        {
            Number = PrintTextNormalizer.Truncate(Number),
            Note = PrintTextNormalizer.Truncate(Note),
            To = To.FixForPrint(extractZipFromAddress, warnings),
        };
    }
}

/// <summary>
/// Данные для печати одного почтового реестра (ТЗ §9.4, функция 77:
/// <c>RegistersData / RegisterData → Number, From, Packages → Package(Number, To, Note)</c>).
/// </summary>
public sealed record RegisterData
{
    /// <summary>Номер реестра (ТЗ §9.3: API v2).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>Отправитель: элемент <c>From</c>.</summary>
    public EnvelopeParty From { get; init; } = new();

    /// <summary>Строки реестра: элемент <c>Packages</c>.</summary>
    public IReadOnlyList<RegisterPackage> Packages { get; init; } = Array.Empty<RegisterPackage>();

    /// <summary>
    /// Записывает реестр в XML: элемент <c>RegisterData</c> с элементами
    /// <c>Number</c>, <c>From</c> и <c>Packages</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, RegistersData.ItemElementName);
        ExchangeXmlWriter.WriteElement(writer, "Number", Number);
        From.WriteToXml(writer, "From", includePhone: false);
        ExchangeXmlWriter.StartElement(writer, RegistersData.PackagesElementName);

        foreach (var package in Packages)
        {
            package.WriteToXml(writer);
        }

        ExchangeXmlWriter.EndElement(writer);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает реестр из XML: элемент <c>RegisterData</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе реестра.</param>
    /// <returns>Прочитанный реестр.</returns>
    public static RegisterData ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент почтового реестра.");
        }

        var register = new RegisterData();
        var packages = new List<RegisterPackage>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return register;
        }

        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.Read();
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Number":
                        register = register with { Number = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "From":
                        register = register with { From = EnvelopeParty.ReadFromXml(reader) };
                        break;
                    case RegistersData.PackagesElementName:
                        packages.AddRange(ReadPackages(reader));
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return register with { Packages = packages };
    }

    /// <summary>
    /// Приводит реестр к печатному виду.
    /// </summary>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Реестр в печатном виде.</returns>
    public RegisterData FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        return this with
        {
            Number = PrintTextNormalizer.Truncate(Number),
            From = From.FixForPrint(extractZipFromAddress, warnings),
            Packages = Packages
                .Select(package => package.FixForPrint(extractZipFromAddress, warnings))
                .ToArray(),
        };
    }

    private static IEnumerable<RegisterPackage> ReadPackages(XmlReader reader)
    {
        var packages = new List<RegisterPackage>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return packages;
        }

        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.Read();
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (string.Equals(reader.LocalName, RegistersData.PackageElementName, StringComparison.Ordinal))
                {
                    packages.Add(RegisterPackage.ReadFromXml(reader));
                }
                else
                {
                    ExchangeXmlReader.SkipElement(reader);
                }

                continue;
            }

            reader.Read();
        }

        return packages;
    }
}

/// <summary>
/// Контейнер данных для печати почтовых реестров (ТЗ §9.4, функция 77:
/// корневой элемент <c>RegistersData</c>).
/// </summary>
public sealed class RegistersData
{
    /// <summary>Имя корневого элемента контейнера.</summary>
    public const string RootElementName = "RegistersData";

    /// <summary>Имя элемента одного реестра.</summary>
    public const string ItemElementName = "RegisterData";

    /// <summary>Имя элемента со строками реестра.</summary>
    public const string PackagesElementName = "Packages";

    /// <summary>Имя элемента одной строки реестра.</summary>
    public const string PackageElementName = "Package";

    private static readonly string[] ItemElementNames = { ItemElementName };

    /// <summary>Создаёт контейнер данных реестров.</summary>
    /// <param name="items">Реестры.</param>
    public RegistersData(IEnumerable<RegisterData> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items.ToArray();
    }

    /// <summary>Реестры контейнера.</summary>
    public IReadOnlyList<RegisterData> Items { get; }

    /// <summary>
    /// Записывает контейнер в XML.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer) => WriteListToXml(Items, writer);

    /// <summary>
    /// Потоково записывает список реестров в XML: корневой элемент
    /// <c>RegistersData</c> и элементы <c>RegisterData</c>.
    /// </summary>
    /// <param name="items">Реестры.</param>
    /// <param name="writer">Писатель XML.</param>
    public static void WriteListToXml(IEnumerable<RegisterData> items, XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, RootElementName);

        foreach (var item in items)
        {
            item.WriteToXml(writer);
        }

        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает список реестров из XML.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns>Успешный результат со списком реестров либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<RegisterData>> ReadListFromXml(XmlReader reader)
        => ExchangeXmlReader.ReadList(reader, RootElementName, ItemElementNames, RegisterData.ReadFromXml);

    /// <summary>
    /// Читает список реестров из текста XML.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <returns>Успешный результат со списком реестров либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<RegisterData>> ReadListFromXml(string xml)
    {
        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml);

            return ReadListFromXml(reader);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<RegisterData>>.Failure(
                "Ошибка чтения XML-данных: " + error.Message);
        }
    }

    /// <summary>
    /// Читает список реестров из XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Успешный результат со списком реестров либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<RegisterData>> ReadListFromXmlAndFixForPrint(
        XmlReader reader,
        bool extractZipFromAddress = true)
    {
        var warnings = new List<string>();

        var read = PrintTextNormalizer.ReadListFromXmlAndFixForPrint(
            reader,
            RootElementName,
            ItemElementNames,
            RegisterData.ReadFromXml,
            register => register.FixForPrint(extractZipFromAddress, warnings));

        if (read.IsFailure)
        {
            return read;
        }

        return Result<IReadOnlyList<RegisterData>>.Success(read.Value, warnings.ToArray());
    }
}

/// <summary>
/// Сериализатор формата данных для печати почтовых реестров (ТЗ §9.4, функция 77).
/// </summary>
public sealed class RegistersDataSerializer
{
    /// <summary>
    /// Сериализует реестры в XML-документ.
    /// </summary>
    /// <param name="list">Реестры.</param>
    /// <returns>Документ с корневым элементом <c>RegistersData</c>.</returns>
    public XDocument WriteRegisters(IEnumerable<RegisterData> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return ExchangeXmlWriter.CreateDocument(writer => RegistersData.WriteListToXml(list, writer));
    }

    /// <summary>
    /// Потоково записывает реестры в писатель XML без построения DOM.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="list">Реестры.</param>
    public void WriteRegistersTo(XmlWriter writer, IEnumerable<RegisterData> list)
    {
        ArgumentNullException.ThrowIfNull(writer);

        RegistersData.WriteListToXml(list, writer);
    }

    /// <summary>
    /// Читает реестры из потока XML.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <returns>Прочитанные реестры.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<RegisterData> ReadRegisters(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = RegistersData.ReadListFromXml(reader);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }

    /// <summary>
    /// Читает реестры из потока XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Прочитанные реестры в печатном виде.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<RegisterData> ReadRegistersAndFixForPrint(Stream xml, bool extractZipFromAddress = true)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = RegistersData.ReadListFromXmlAndFixForPrint(reader, extractZipFromAddress);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }
}
