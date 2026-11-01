using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Сторона конверта — отправитель или получатель (ТЗ §9.4, функция 77:
/// <c>EnvelopesData / EnvelopeData → From(Name, Address, ZipCode, Phone) /
/// To(Name, Address, ZipCode, Phone)</c>).
/// </summary>
public sealed record EnvelopeParty
{
    /// <summary>Имя контрагента: поле «Кому» или «От кого».</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Адрес контрагента единым текстом: поле «Куда» или «Откуда».</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Почтовый индекс адресата.</summary>
    public string ZipCode { get; init; } = string.Empty;

    /// <summary>Контактный телефон контрагента (ТЗ §9.3: API v2).</summary>
    public string Phone { get; init; } = string.Empty;

    /// <summary>
    /// Строки адресного блока бланка: единый текст адреса разложен по шести
    /// строкам (ТЗ §8.5, функция 77: «выравнивание 6 адресных строк»).
    /// </summary>
    public string[] AddressLines => PrintTextNormalizer.SplitAddressLines(Address);

    /// <summary>
    /// Записывает сторону в XML: элемент <c>From</c> или <c>To</c> с дочерними
    /// элементами <c>Name</c>, <c>Address</c>, <c>ZipCode</c> и <c>Phone</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="elementName">Имя элемента стороны: <c>From</c> или <c>To</c>.</param>
    /// <param name="includePhone">
    /// Признак записи элемента <c>Phone</c>. В реестрах почтовых отправлений
    /// телефон в формате не предусмотрен.
    /// </param>
    public void WriteToXml(XmlWriter writer, string elementName, bool includePhone = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);

        ExchangeXmlWriter.StartElement(writer, elementName);
        ExchangeXmlWriter.WriteElement(writer, "Name", Name);
        ExchangeXmlWriter.WriteElement(writer, "Address", Address);
        ExchangeXmlWriter.WriteElement(writer, "ZipCode", ZipCode);

        if (includePhone)
        {
            ExchangeXmlWriter.WriteElement(writer, "Phone", Phone);
        }

        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает сторону из XML: элемент <c>From</c> или <c>To</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе стороны.</param>
    /// <returns>Прочитанная сторона.</returns>
    public static EnvelopeParty ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var party = new EnvelopeParty();

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент стороны конверта.");
        }

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return party;
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
                        party = party with { Name = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Address":
                        party = party with { Address = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "ZipCode":
                        party = party with { ZipCode = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Phone":
                        party = party with { Phone = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return party;
    }

    /// <summary>
    /// Приводит сторону к печатному виду: усекает поля до 255 знаков, нормализует
    /// индекс и, при установленном флаге, выкусывает индекс из текста адреса.
    /// </summary>
    /// <param name="extractZipFromAddress">
    /// Признак извлечения индекса из текста адреса, если индекс не задан отдельно.
    /// </param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Сторона в печатном виде.</returns>
    public EnvelopeParty FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        var name = PrintTextNormalizer.Truncate(Name);
        var address = PrintTextNormalizer.Truncate(Address);
        var zip = PrintTextNormalizer.NormalizeZipCode(ZipCode);
        var phone = PrintTextNormalizer.Truncate(Phone);

        if (PrintTextNormalizer.IsTooLong(Name))
        {
            warnings?.Add($"Поле «{Name[..20]}…» длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        if (PrintTextNormalizer.IsTooLong(Address))
        {
            warnings?.Add($"Поле адреса длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        if (extractZipFromAddress && string.IsNullOrEmpty(zip))
        {
            var cleaned = PrintTextNormalizer.ExtractZipFromAddress(address, out var found);

            if (found.Length > 0)
            {
                zip = found;
                address = PrintTextNormalizer.Truncate(cleaned);
                warnings?.Add($"Почтовый индекс {found} извлечён из текста адреса.");
            }
        }

        return this with
        {
            Name = name,
            Address = address,
            ZipCode = zip,
            Phone = phone,
        };
    }
}

/// <summary>
/// Данные для печати одного конверта или уведомления (ТЗ §9.4, функция 77:
/// <c>EnvelopesData / EnvelopeData</c>).
/// </summary>
public sealed record PrintableEnvelope
{
    /// <summary>Отправитель: элемент <c>From</c>.</summary>
    public EnvelopeParty From { get; init; } = new();

    /// <summary>Получатель: элемент <c>To</c>.</summary>
    public EnvelopeParty To { get; init; } = new();

    /// <summary>Номер отправления (ТЗ §9.3: API v2).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>Особые отметки отправителя (ТЗ §9.3: API v2).</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// Записывает конверт в XML: элемент <c>EnvelopeData</c> с элементами <c>From</c>,
    /// <c>To</c>, <c>Number</c> и <c>Note</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, EnvelopesData.ItemElementName);
        ExchangeXmlWriter.WriteAttribute(writer, "Number", Number);
        ExchangeXmlWriter.WriteAttribute(writer, "Note", Note);
        From.WriteToXml(writer, "From");
        To.WriteToXml(writer, "To");
        ExchangeXmlWriter.WriteElement(writer, "Number", Number);
        ExchangeXmlWriter.WriteElement(writer, "Note", Note);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает конверт из XML: элемент <c>EnvelopeData</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе конверта.</param>
    /// <returns>Прочитанный конверт.</returns>
    public static PrintableEnvelope ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент конверта.");
        }

        var number = reader.GetAttribute("Number") ?? string.Empty;
        var note = reader.GetAttribute("Note") ?? string.Empty;
        var envelope = new PrintableEnvelope();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return envelope with { Number = number, Note = note };
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
                    case "From":
                        envelope = envelope with { From = EnvelopeParty.ReadFromXml(reader) };
                        break;
                    case "To":
                        envelope = envelope with { To = EnvelopeParty.ReadFromXml(reader) };
                        break;
                    case "Number":
                        var elementNumber = ExchangeXmlReader.ReadElementText(reader);

                        if (elementNumber.Length > 0)
                        {
                            number = elementNumber;
                        }

                        break;
                    case "Note":
                        var elementNote = ExchangeXmlReader.ReadElementText(reader);

                        if (elementNote.Length > 0)
                        {
                            note = elementNote;
                        }

                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return envelope with { Number = number, Note = note };
    }

    /// <summary>
    /// Приводит конверт к печатному виду.
    /// </summary>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Конверт в печатном виде.</returns>
    public PrintableEnvelope FixForPrint(bool extractZipFromAddress, ICollection<string>? warnings = null)
    {
        return this with
        {
            From = From.FixForPrint(extractZipFromAddress, warnings),
            To = To.FixForPrint(extractZipFromAddress, warnings),
            Number = PrintTextNormalizer.Truncate(Number),
            Note = PrintTextNormalizer.Truncate(Note),
        };
    }
}

/// <summary>
/// Контейнер данных для печати конвертов и уведомлений (ТЗ §9.4, функция 77:
/// корневой элемент <c>EnvelopesData</c>).
/// </summary>
public sealed class EnvelopesData
{
    /// <summary>Имя корневого элемента контейнера.</summary>
    public const string RootElementName = "EnvelopesData";

    /// <summary>Имя элемента одного конверта.</summary>
    public const string ItemElementName = "EnvelopeData";

    private static readonly string[] ItemElementNames = { ItemElementName };

    /// <summary>Создаёт контейнер данных конвертов.</summary>
    /// <param name="items">Конверты.</param>
    public EnvelopesData(IEnumerable<PrintableEnvelope> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items.ToArray();
    }

    /// <summary>Конверты контейнера.</summary>
    public IReadOnlyList<PrintableEnvelope> Items { get; }

    /// <summary>
    /// Записывает контейнер в XML.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer) => WriteListToXml(Items, writer);

    /// <summary>
    /// Потоково записывает список конвертов в XML: корневой элемент
    /// <c>EnvelopesData</c> и элементы <c>EnvelopeData</c>.
    /// </summary>
    /// <param name="items">Конверты.</param>
    /// <param name="writer">Писатель XML.</param>
    public static void WriteListToXml(IEnumerable<PrintableEnvelope> items, XmlWriter writer)
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
    /// Читает список конвертов из XML.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns>Успешный результат со списком конвертов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<PrintableEnvelope>> ReadListFromXml(XmlReader reader)
        => ExchangeXmlReader.ReadList(reader, RootElementName, ItemElementNames, PrintableEnvelope.ReadFromXml);

    /// <summary>
    /// Читает список конвертов из текста XML. Недоверенный вход пропускается через
    /// санитайзер.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <returns>Успешный результат со списком конвертов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<PrintableEnvelope>> ReadListFromXml(string xml)
    {
        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml);

            return ReadListFromXml(reader);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<PrintableEnvelope>>.Failure(
                "Ошибка чтения XML-данных: " + error.Message);
        }
    }

    /// <summary>
    /// Читает список конвертов из XML и приводит данные к печатному виду: усечение
    /// полей до 255 знаков, нормализация индексов и, при установленном флаге,
    /// извлечение индекса из текста адреса.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Успешный результат со списком конвертов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<PrintableEnvelope>> ReadListFromXmlAndFixForPrint(
        XmlReader reader,
        bool extractZipFromAddress = true)
    {
        var warnings = new List<string>();

        var read = PrintTextNormalizer.ReadListFromXmlAndFixForPrint(
            reader,
            RootElementName,
            ItemElementNames,
            PrintableEnvelope.ReadFromXml,
            envelope => envelope.FixForPrint(extractZipFromAddress, warnings));

        if (read.IsFailure)
        {
            return read;
        }

        return Result<IReadOnlyList<PrintableEnvelope>>.Success(read.Value, warnings.ToArray());
    }

    /// <summary>
    /// Читает список конвертов из текста XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Успешный результат со списком конвертов либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<PrintableEnvelope>> ReadListFromXmlAndFixForPrint(
        string xml,
        bool extractZipFromAddress = true)
    {
        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml);

            return ReadListFromXmlAndFixForPrint(reader, extractZipFromAddress);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<PrintableEnvelope>>.Failure(
                "Ошибка чтения XML-данных: " + error.Message);
        }
    }
}

/// <summary>
/// Сериализатор формата данных для печати конвертов (ТЗ §9.4, функция 77;
/// §8.5, функция 77: <c>Domain.Exchange.EnvelopesDataSerializer</c>).
/// </summary>
public sealed class EnvelopesDataSerializer
{
    /// <summary>
    /// Сериализует конверты в XML-документ. Это удобная обёртка: для больших партий
    /// следует использовать <see cref="WriteEnvelopesTo"/>, который пишет потоково.
    /// </summary>
    /// <param name="list">Конверты.</param>
    /// <returns>Документ с корневым элементом <c>EnvelopesData</c>.</returns>
    public XDocument WriteEnvelopes(IEnumerable<PrintableEnvelope> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return ExchangeXmlWriter.CreateDocument(writer => EnvelopesData.WriteListToXml(list, writer));
    }

    /// <summary>
    /// Потоково записывает конверты в писатель XML без построения DOM.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="list">Конверты.</param>
    public void WriteEnvelopesTo(XmlWriter writer, IEnumerable<PrintableEnvelope> list)
    {
        ArgumentNullException.ThrowIfNull(writer);

        EnvelopesData.WriteListToXml(list, writer);
    }

    /// <summary>
    /// Читает конверты из потока XML.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <returns>Прочитанные конверты.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<PrintableEnvelope> ReadEnvelopes(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = EnvelopesData.ReadListFromXml(reader);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }

    /// <summary>
    /// Читает конверты из потока XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <param name="extractZipFromAddress">Признак извлечения индекса из текста адреса.</param>
    /// <returns>Прочитанные конверты в печатном виде.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<PrintableEnvelope> ReadEnvelopesAndFixForPrint(Stream xml, bool extractZipFromAddress = true)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = EnvelopesData.ReadListFromXmlAndFixForPrint(reader, extractZipFromAddress);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }
}
