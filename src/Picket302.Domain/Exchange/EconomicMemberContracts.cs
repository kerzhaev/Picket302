using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Адрес контрагента в формате импорта (ТЗ §9.4, функция 77:
/// <c>Address(ZipCode, Province, City, AddressLine)</c>).
/// </summary>
public sealed record EconomicMemberAddress
{
    /// <summary>Почтовый индекс.</summary>
    public string ZipCode { get; init; } = string.Empty;

    /// <summary>Регион.</summary>
    public string Province { get; init; } = string.Empty;

    /// <summary>Населённый пункт.</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>Адрес в населённом пункте.</summary>
    public string AddressLine { get; init; } = string.Empty;

    /// <summary>
    /// Записывает адрес в XML: элемент <c>Address</c> с элементами <c>ZipCode</c>,
    /// <c>Province</c>, <c>City</c> и <c>AddressLine</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, "Address");
        ExchangeXmlWriter.WriteElement(writer, "ZipCode", ZipCode);
        ExchangeXmlWriter.WriteElement(writer, "Province", Province);
        ExchangeXmlWriter.WriteElement(writer, "City", City);
        ExchangeXmlWriter.WriteElement(writer, "AddressLine", AddressLine);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает адрес из XML: элемент <c>Address</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе адреса.</param>
    /// <returns>Прочитанный адрес.</returns>
    public static EconomicMemberAddress ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var address = new EconomicMemberAddress();

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент адреса контрагента.");
        }

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return address;
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
                    case "ZipCode":
                        address = address with { ZipCode = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Province":
                        address = address with { Province = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "City":
                        address = address with { City = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "AddressLine":
                        address = address with { AddressLine = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return address;
    }

    /// <summary>
    /// Приводит адрес к печатному виду: усечение полей до 255 знаков и нормализация
    /// индекса.
    /// </summary>
    /// <param name="extractZipFromAddress">
    /// Признак извлечения индекса из текста адреса, если индекс не задан отдельно.
    /// </param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Адрес в печатном виде.</returns>
    public EconomicMemberAddress FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        var zip = PrintTextNormalizer.NormalizeZipCode(ZipCode);
        var addressLine = PrintTextNormalizer.Truncate(AddressLine);

        if (PrintTextNormalizer.IsTooLong(AddressLine))
        {
            warnings?.Add($"Поле адреса длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        if (extractZipFromAddress && string.IsNullOrEmpty(zip))
        {
            var cleaned = PrintTextNormalizer.ExtractZipFromAddress(addressLine, out var found);

            if (found.Length > 0)
            {
                zip = found;
                addressLine = PrintTextNormalizer.Truncate(cleaned);
                warnings?.Add($"Почтовый индекс {found} извлечён из текста адреса.");
            }
        }

        return this with
        {
            ZipCode = zip,
            Province = PrintTextNormalizer.Truncate(Province),
            City = PrintTextNormalizer.Truncate(City),
            AddressLine = addressLine,
        };
    }
}

/// <summary>
/// Общая часть контрагента для импорта (ТЗ §9.4, функция 77: элементы <c>Note</c>,
/// <c>Email</c> и <c>Address</c> одинаковы для юридических и физических лиц).
/// </summary>
public abstract record EconomicMember
{
    /// <summary>Примечания.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Электронный адрес контрагента.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Адрес контрагента.</summary>
    public EconomicMemberAddress Address { get; init; } = new();

    /// <summary>Имя элемента контрагента в XML.</summary>
    public abstract string ElementName { get; }

    /// <summary>
    /// Записывает контрагента в XML.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public abstract void WriteToXml(XmlWriter writer);

    /// <summary>
    /// Приводит контрагента к печатному виду.
    /// </summary>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Контрагент в печатном виде.</returns>
    public virtual EconomicMember FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        return this with
        {
            Note = PrintTextNormalizer.Truncate(Note),
            Email = PrintTextNormalizer.Truncate(Email),
            Address = Address.FixForPrint(extractZipFromAddress, warnings),
        };
    }

    /// <summary>
    /// Читает контрагента из XML: элемент <c>LegalPerson</c> или <c>NaturalPerson</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе контрагента.</param>
    /// <returns>Прочитанный контрагент.</returns>
    public static EconomicMember ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент контрагента для импорта.");
        }

        return reader.LocalName switch
        {
            "LegalPerson" => LegalPerson.ReadFromXml(reader),
            "NaturalPerson" => NaturalPerson.ReadFromXml(reader),
            _ => throw new ExchangeContractException(
                $"Неизвестный элемент контрагента «{reader.LocalName}»: ожидается LegalPerson или NaturalPerson."),
        };
    }
}

/// <summary>
/// Юридическое лицо для импорта (ТЗ §9.4, функция 77:
/// <c>LegalPerson(Name, ShortName, Note, Email, Address)</c>).
/// </summary>
public sealed record LegalPerson : EconomicMember
{
    /// <summary>Название юридического лица.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Сокращённое название юридического лица.</summary>
    public string ShortName { get; init; } = string.Empty;

    /// <inheritdoc />
    public override string ElementName => "LegalPerson";

    /// <inheritdoc />
    public override void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, ElementName);
        ExchangeXmlWriter.WriteElement(writer, "Name", Name);
        ExchangeXmlWriter.WriteElement(writer, "ShortName", ShortName);
        ExchangeXmlWriter.WriteElement(writer, "Note", Note);
        ExchangeXmlWriter.WriteElement(writer, "Email", Email);
        Address.WriteToXml(writer);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает юридическое лицо из XML: элемент <c>LegalPerson</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе контрагента.</param>
    /// <returns>Прочитанное юридическое лицо.</returns>
    public static new LegalPerson ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент юридического лица.");
        }

        var legalPerson = new LegalPerson();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return legalPerson;
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
                    case "Name":
                        legalPerson = legalPerson with { Name = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "ShortName":
                        legalPerson = legalPerson with { ShortName = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Note":
                        legalPerson = legalPerson with { Note = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Email":
                        legalPerson = legalPerson with { Email = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Address":
                        legalPerson = legalPerson with { Address = EconomicMemberAddress.ReadFromXml(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return legalPerson;
    }
}

/// <summary>
/// Физическое лицо для импорта (ТЗ §9.4, функция 77:
/// <c>NaturalPerson(LastName, MiddleName, FirstName, Note, Email, Address)</c>).
/// </summary>
public sealed record NaturalPerson : EconomicMember
{
    /// <summary>Фамилия контрагента.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Отчество контрагента.</summary>
    public string MiddleName { get; init; } = string.Empty;

    /// <summary>Имя контрагента.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <inheritdoc />
    public override string ElementName => "NaturalPerson";

    /// <inheritdoc />
    public override void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, ElementName);
        ExchangeXmlWriter.WriteElement(writer, "LastName", LastName);
        ExchangeXmlWriter.WriteElement(writer, "MiddleName", MiddleName);
        ExchangeXmlWriter.WriteElement(writer, "FirstName", FirstName);
        ExchangeXmlWriter.WriteElement(writer, "Note", Note);
        ExchangeXmlWriter.WriteElement(writer, "Email", Email);
        Address.WriteToXml(writer);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает физическое лицо из XML: элемент <c>NaturalPerson</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе контрагента.</param>
    /// <returns>Прочитанное физическое лицо.</returns>
    public static new NaturalPerson ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент физического лица.");
        }

        var naturalPerson = new NaturalPerson();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return naturalPerson;
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
                    case "LastName":
                        naturalPerson = naturalPerson with { LastName = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "MiddleName":
                        naturalPerson = naturalPerson with { MiddleName = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "FirstName":
                        naturalPerson = naturalPerson with { FirstName = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Note":
                        naturalPerson = naturalPerson with { Note = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Email":
                        naturalPerson = naturalPerson with { Email = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Address":
                        naturalPerson = naturalPerson with { Address = EconomicMemberAddress.ReadFromXml(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return naturalPerson;
    }
}

/// <summary>
/// Контейнер данных для импорта контрагентов (ТЗ §9.4, функция 77:
/// корневой элемент <c>EconomicMembers</c>).
/// </summary>
public sealed class EconomicMembers
{
    /// <summary>Имя корневого элемента контейнера.</summary>
    public const string RootElementName = "EconomicMembers";

    /// <summary>Имя элемента юридического лица.</summary>
    public const string LegalPersonElementName = "LegalPerson";

    /// <summary>Имя элемента физического лица.</summary>
    public const string NaturalPersonElementName = "NaturalPerson";

    private static readonly string[] ItemElementNames = { LegalPersonElementName, NaturalPersonElementName };

    /// <summary>Создаёт контейнер данных контрагентов.</summary>
    /// <param name="items">Контрагенты.</param>
    public EconomicMembers(IEnumerable<EconomicMember> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items.ToArray();
    }

    /// <summary>Контрагенты контейнера.</summary>
    public IReadOnlyList<EconomicMember> Items { get; }

    /// <summary>
    /// Записывает контейнер в XML.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer) => WriteListToXml(Items, writer);

    /// <summary>
    /// Потоково записывает список контрагентов в XML: корневой элемент
    /// <c>EconomicMembers</c> и элементы <c>LegalPerson</c>, <c>NaturalPerson</c>.
    /// </summary>
    /// <param name="items">Контрагенты.</param>
    /// <param name="writer">Писатель XML.</param>
    public static void WriteListToXml(IEnumerable<EconomicMember> items, XmlWriter writer)
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
    /// Читает список контрагентов из XML.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns>Успешный результат со списком контрагентов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<EconomicMember>> ReadListFromXml(XmlReader reader)
        => ExchangeXmlReader.ReadList(reader, RootElementName, ItemElementNames, EconomicMember.ReadFromXml);

    /// <summary>
    /// Читает список контрагентов из текста XML.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <returns>Успешный результат со списком контрагентов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<EconomicMember>> ReadListFromXml(string xml)
    {
        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml);

            return ReadListFromXml(reader);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<EconomicMember>>.Failure(
                "Ошибка чтения XML-данных: " + error.Message);
        }
    }

    /// <summary>
    /// Читает список контрагентов из XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Успешный результат со списком контрагентов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<EconomicMember>> ReadListFromXmlAndFixForPrint(
        XmlReader reader,
        bool extractZipFromAddress = true)
    {
        var warnings = new List<string>();

        var read = PrintTextNormalizer.ReadListFromXmlAndFixForPrint(
            reader,
            RootElementName,
            ItemElementNames,
            EconomicMember.ReadFromXml,
            member => member.FixForPrint(extractZipFromAddress, warnings));

        if (read.IsFailure)
        {
            return read;
        }

        return Result<IReadOnlyList<EconomicMember>>.Success(read.Value, warnings.ToArray());
    }
}

/// <summary>
/// Сериализатор формата данных для импорта контрагентов (ТЗ §9.4, функция 77;
/// §9.7 «Импорт из 1С»).
/// </summary>
public sealed class EconomicMembersSerializer
{
    /// <summary>
    /// Сериализует контрагентов в XML-документ.
    /// </summary>
    /// <param name="list">Контрагенты.</param>
    /// <returns>Документ с корневым элементом <c>EconomicMembers</c>.</returns>
    public XDocument WriteMembers(IEnumerable<EconomicMember> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return ExchangeXmlWriter.CreateDocument(writer => EconomicMembers.WriteListToXml(list, writer));
    }

    /// <summary>
    /// Потоково записывает контрагентов в писатель XML без построения DOM.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="list">Контрагенты.</param>
    public void WriteMembersTo(XmlWriter writer, IEnumerable<EconomicMember> list)
    {
        ArgumentNullException.ThrowIfNull(writer);

        EconomicMembers.WriteListToXml(list, writer);
    }

    /// <summary>
    /// Читает контрагентов из потока XML.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <returns>Прочитанные контрагенты.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<EconomicMember> ReadMembers(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = EconomicMembers.ReadListFromXml(reader);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }
}
