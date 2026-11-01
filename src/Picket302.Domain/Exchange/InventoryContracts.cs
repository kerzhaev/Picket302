using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Позиция описи вложений (ТЗ §9.4, функция 77:
/// <c>InventoryItems → InventoryItem(ItemName)</c>).
/// </summary>
public sealed record InventoryItem
{
    /// <summary>Текст-описание вложения.</summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>
    /// Записывает позицию описи в XML: элемент <c>InventoryItem</c> с элементом
    /// <c>ItemName</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, InventoriesData.InventoryItemElementName);
        ExchangeXmlWriter.WriteElement(writer, "ItemName", ItemName);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает позицию описи из XML: элемент <c>InventoryItem</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе позиции.</param>
    /// <returns>Прочитанная позиция описи.</returns>
    public static InventoryItem ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент позиции описи вложений.");
        }

        var item = new InventoryItem();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return item;
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
                if (string.Equals(reader.LocalName, "ItemName", StringComparison.Ordinal))
                {
                    item = item with { ItemName = ExchangeXmlReader.ReadElementText(reader) };
                }
                else
                {
                    ExchangeXmlReader.SkipElement(reader);
                }

                continue;
            }

            reader.Read();
        }

        return item;
    }

    /// <summary>
    /// Приводит позицию описи к печатному виду.
    /// </summary>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Позиция описи в печатном виде.</returns>
    public InventoryItem FixForPrint(ICollection<string>? warnings = null)
    {
        if (PrintTextNormalizer.IsTooLong(ItemName))
        {
            warnings?.Add($"Поле «{ItemName[..20]}…» длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        return this with { ItemName = PrintTextNormalizer.Truncate(ItemName) };
    }
}

/// <summary>
/// Данные для печати одной описи вложений (ТЗ §9.4, функция 77:
/// <c>InventoriesData / InventoryData → From(Name), To(Name, Address),
/// InventoryItems → InventoryItem(ItemName)</c>).
/// </summary>
public sealed record InventoryData
{
    /// <summary>Отправитель: элемент <c>From</c> с единственным полем <c>Name</c>.</summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>Получатель: элемент <c>To</c>, поле <c>Name</c>.</summary>
    public string ToName { get; init; } = string.Empty;

    /// <summary>Получатель: элемент <c>To</c>, поле <c>Address</c>.</summary>
    public string ToAddress { get; init; } = string.Empty;

    /// <summary>Позиции описи: элемент <c>InventoryItems</c>.</summary>
    public IReadOnlyList<InventoryItem> Items { get; init; } = Array.Empty<InventoryItem>();

    /// <summary>
    /// Записывает опись в XML: элемент <c>InventoryData</c> с элементами <c>From</c>,
    /// <c>To</c> и <c>InventoryItems</c>.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ExchangeXmlWriter.StartElement(writer, InventoriesData.ItemElementName);

        ExchangeXmlWriter.StartElement(writer, "From");
        ExchangeXmlWriter.WriteElement(writer, "Name", FromName);
        ExchangeXmlWriter.EndElement(writer);

        ExchangeXmlWriter.StartElement(writer, "To");
        ExchangeXmlWriter.WriteElement(writer, "Name", ToName);
        ExchangeXmlWriter.WriteElement(writer, "Address", ToAddress);
        ExchangeXmlWriter.EndElement(writer);

        ExchangeXmlWriter.StartElement(writer, InventoriesData.ItemsElementName);

        foreach (var item in Items)
        {
            item.WriteToXml(writer);
        }

        ExchangeXmlWriter.EndElement(writer);
        ExchangeXmlWriter.EndElement(writer);
    }

    /// <summary>
    /// Читает опись из XML: элемент <c>InventoryData</c>.
    /// </summary>
    /// <param name="reader">Читатель XML, установленный на элементе описи.</param>
    /// <returns>Прочитанная опись.</returns>
    public static InventoryData ReadFromXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new ExchangeContractException("Ожидался XML-элемент описи вложений.");
        }

        var inventory = new InventoryData();
        var items = new List<InventoryItem>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return inventory;
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
                        inventory = inventory with { FromName = ReadPartyName(reader) };
                        break;
                    case "To":
                        inventory = ReadRecipient(inventory, reader);
                        break;
                    case InventoriesData.ItemsElementName:
                        items.AddRange(ReadItems(reader));
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return inventory with { Items = items };
    }

    /// <summary>
    /// Приводит опись к печатному виду: усечение полей до 255 знаков. Отдельного поля
    /// с индексом в формате описи нет, поэтому индекс остаётся в тексте адреса.
    /// </summary>
    /// <param name="warnings">Список предупреждений; может быть <see langword="null"/>.</param>
    /// <returns>Опись в печатном виде.</returns>
    public InventoryData FixForPrint(ICollection<string>? warnings = null)
    {
        if (PrintTextNormalizer.IsTooLong(FromName))
        {
            warnings?.Add($"Поле «От кого» длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        if (PrintTextNormalizer.IsTooLong(ToAddress))
        {
            warnings?.Add($"Поле «Куда» длиннее {PrintTextNormalizer.MaxFieldLength} знаков — значение усечено.");
        }

        return this with
        {
            FromName = PrintTextNormalizer.Truncate(FromName),
            ToName = PrintTextNormalizer.Truncate(ToName),
            ToAddress = PrintTextNormalizer.Truncate(ToAddress),
            Items = Items.Select(item => item.FixForPrint(warnings)).ToArray(),
        };
    }

    private static string ReadPartyName(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return string.Empty;
        }

        var name = string.Empty;

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
                if (string.Equals(reader.LocalName, "Name", StringComparison.Ordinal))
                {
                    name = ExchangeXmlReader.ReadElementText(reader);
                }
                else
                {
                    ExchangeXmlReader.SkipElement(reader);
                }

                continue;
            }

            reader.Read();
        }

        return name;
    }

    private static InventoryData ReadRecipient(InventoryData inventory, XmlReader reader)
    {
        var result = inventory;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return result;
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
                        result = result with { ToName = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    case "Address":
                        result = result with { ToAddress = ExchangeXmlReader.ReadElementText(reader) };
                        break;
                    default:
                        ExchangeXmlReader.SkipElement(reader);
                        break;
                }

                continue;
            }

            reader.Read();
        }

        return result;
    }

    private static IEnumerable<InventoryItem> ReadItems(XmlReader reader)
    {
        var items = new List<InventoryItem>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return items;
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
                if (string.Equals(reader.LocalName, InventoriesData.InventoryItemElementName, StringComparison.Ordinal))
                {
                    items.Add(InventoryItem.ReadFromXml(reader));
                }
                else
                {
                    ExchangeXmlReader.SkipElement(reader);
                }

                continue;
            }

            reader.Read();
        }

        return items;
    }
}

/// <summary>
/// Контейнер данных для печати описей вложений (ТЗ §9.4, функция 77:
/// корневой элемент <c>InventoriesData</c>).
/// </summary>
public sealed class InventoriesData
{
    /// <summary>Имя корневого элемента контейнера.</summary>
    public const string RootElementName = "InventoriesData";

    /// <summary>Имя элемента одной описи.</summary>
    public const string ItemElementName = "InventoryData";

    /// <summary>Имя элемента со списком позиций описи.</summary>
    public const string ItemsElementName = "InventoryItems";

    /// <summary>Имя элемента одной позиции описи.</summary>
    public const string InventoryItemElementName = "InventoryItem";

    private static readonly string[] ItemElementNames = { ItemElementName };

    /// <summary>Создаёт контейнер данных описей.</summary>
    /// <param name="items">Описи.</param>
    public InventoriesData(IEnumerable<InventoryData> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items.ToArray();
    }

    /// <summary>Описи контейнера.</summary>
    public IReadOnlyList<InventoryData> Items { get; }

    /// <summary>
    /// Записывает контейнер в XML.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    public void WriteToXml(XmlWriter writer) => WriteListToXml(Items, writer);

    /// <summary>
    /// Потоково записывает список описей в XML: корневой элемент
    /// <c>InventoriesData</c> и элементы <c>InventoryData</c>.
    /// </summary>
    /// <param name="items">Описи.</param>
    /// <param name="writer">Писатель XML.</param>
    public static void WriteListToXml(IEnumerable<InventoryData> items, XmlWriter writer)
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
    /// Читает список описей из XML.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns>Успешный результат со списком описей либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<InventoryData>> ReadListFromXml(XmlReader reader)
        => ExchangeXmlReader.ReadList(reader, RootElementName, ItemElementNames, InventoryData.ReadFromXml);

    /// <summary>
    /// Читает список описей из текста XML.
    /// </summary>
    /// <param name="xml">Текст XML.</param>
    /// <returns>Успешный результат со списком описей либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<InventoryData>> ReadListFromXml(string xml)
    {
        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml);

            return ReadListFromXml(reader);
        }
        catch (XmlException error)
        {
            return Result<IReadOnlyList<InventoryData>>.Failure(
                "Ошибка чтения XML-данных: " + error.Message);
        }
    }

    /// <summary>
    /// Читает список описей из XML и приводит данные к печатному виду.
    /// </summary>
    /// <param name="reader">Читатель XML.</param>
    /// <returns>Успешный результат со списком описей либо текст ошибки разбора XML.</returns>
    public static Result<IReadOnlyList<InventoryData>> ReadListFromXmlAndFixForPrint(XmlReader reader)
    {
        var warnings = new List<string>();

        var read = PrintTextNormalizer.ReadListFromXmlAndFixForPrint(
            reader,
            RootElementName,
            ItemElementNames,
            InventoryData.ReadFromXml,
            inventory => inventory.FixForPrint(warnings));

        if (read.IsFailure)
        {
            return read;
        }

        return Result<IReadOnlyList<InventoryData>>.Success(read.Value, warnings.ToArray());
    }
}

/// <summary>
/// Сериализатор формата данных для печати описей вложений (ТЗ §9.4, функция 77).
/// </summary>
public sealed class InventoriesDataSerializer
{
    /// <summary>
    /// Сериализует описи в XML-документ.
    /// </summary>
    /// <param name="list">Описи.</param>
    /// <returns>Документ с корневым элементом <c>InventoriesData</c>.</returns>
    public XDocument WriteInventories(IEnumerable<InventoryData> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return ExchangeXmlWriter.CreateDocument(writer => InventoriesData.WriteListToXml(list, writer));
    }

    /// <summary>
    /// Потоково записывает описи в писатель XML без построения DOM.
    /// </summary>
    /// <param name="writer">Писатель XML.</param>
    /// <param name="list">Описи.</param>
    public void WriteInventoriesTo(XmlWriter writer, IEnumerable<InventoryData> list)
    {
        ArgumentNullException.ThrowIfNull(writer);

        InventoriesData.WriteListToXml(list, writer);
    }

    /// <summary>
    /// Читает описи из потока XML.
    /// </summary>
    /// <param name="xml">Поток с данными XML.</param>
    /// <returns>Прочитанные описи.</returns>
    /// <exception cref="ExchangeContractException">XML не является корректным контрактом.</exception>
    public List<InventoryData> ReadInventories(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = ExchangeXmlReader.CreateReader(xml);

        var read = InventoriesData.ReadListFromXml(reader);

        if (read.IsFailure)
        {
            throw new ExchangeContractException(read.Error!);
        }

        return read.Value.ToList();
    }
}
