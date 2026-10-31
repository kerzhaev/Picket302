using System.Xml;
using System.Xml.Linq;

namespace Picket302.Domain.Queries;

/// <summary>
/// Ошибка разбора определения запроса query.xml (ТЗ §4.1).
/// </summary>
public sealed class ReportQueryXmlException : Exception
{
    /// <summary>Создаёт ошибку разбора query.xml.</summary>
    /// <param name="message">Текст ошибки.</param>
    /// <param name="lineNumber">Номер строки.</param>
    public ReportQueryXmlException(string message, int lineNumber = 0)
        : base(message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>Номер строки с ошибкой.</summary>
    public int LineNumber { get; }
}

/// <summary>
/// Сериализатор файла определения запроса query.xml (ТЗ §4.1).
/// </summary>
public sealed class ReportQueryXmlSerializer
{
    /// <summary>Разбирает query.xml.</summary>
    /// <param name="xml">Текст определения.</param>
    /// <returns>Определение запроса.</returns>
    /// <exception cref="ReportQueryXmlException">Структура файла некорректна.</exception>
    public ReportQueryDefinition Deserialize(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument документ;

        try
        {
            документ = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ошибка)
        {
            throw new ReportQueryXmlException(
                "Ошибка разбора query.xml: " + ошибка.Message,
                ошибка.LineNumber);
        }

        var корень = документ.Root
            ?? throw new ReportQueryXmlException("Файл запроса пуст.", 1);

        if (корень.Name.LocalName != "ReportQueryDefinition")
        {
            throw new ReportQueryXmlException(
                $"Корневой элемент файла запроса — «{корень.Name.LocalName}», ожидается «ReportQueryDefinition».",
                Строка(корень));
        }

        var определение = new ReportQueryDefinition
        {
            Id = (string?)корень.Attribute("id") ?? string.Empty,
            Version = (string?)корень.Attribute("version") ?? "1.0",
            Name = корень.Element("Name")?.Value ?? string.Empty,
        };

        if (string.IsNullOrWhiteSpace(определение.Id))
        {
            throw new ReportQueryXmlException(
                "Определение запроса не содержит атрибут id.",
                Строка(корень));
        }

        foreach (var параметр in корень.Element("Parameters")?.Elements("Parameter") ?? Enumerable.Empty<XElement>())
        {
            определение.Parameters.Add(new QueryParameter
            {
                Name = (string?)параметр.Attribute("name") ?? string.Empty,
                Type = (string?)параметр.Attribute("type") ?? "String",
                Required = РазобратьФлаг((string?)параметр.Attribute("required")),
                Default = (string?)параметр.Attribute("default"),
            });
        }

        foreach (var правило in корень.Element("ValidationRules")?.Elements("Rule") ?? Enumerable.Empty<XElement>())
        {
            var maxDays = (string?)правило.Attribute("maxDays");

            определение.ValidationRules.Add(new QueryRule
            {
                Type = (string?)правило.Attribute("type") ?? string.Empty,
                Start = (string?)правило.Attribute("start"),
                End = (string?)правило.Attribute("end"),
                MaxDays = maxDays is null ? null : int.Parse(maxDays, System.Globalization.CultureInfo.InvariantCulture),
                Mode = (string?)правило.Attribute("mode"),
            });
        }

        foreach (var команда in корень.Elements("Command"))
        {
            определение.Commands.Add(new QueryCommand
            {
                Dialect = (string?)команда.Attribute("dialect") ?? QueryDialects.Sqlite,
                Sql = команда.Value,
            });
        }

        if (определение.Commands.Count == 0)
        {
            throw new ReportQueryXmlException(
                "Определение запроса не содержит ни одной команды Command.",
                Строка(корень));
        }

        return определение;
    }

    /// <summary>Сериализует определение запроса в query.xml.</summary>
    /// <param name="definition">Определение.</param>
    /// <returns>Текст файла.</returns>
    public string Serialize(ReportQueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var корень = new XElement(
            "ReportQueryDefinition",
            new XAttribute("id", definition.Id),
            new XAttribute("version", definition.Version),
            new XElement("Name", definition.Name),
            new XElement(
                "Parameters",
                definition.Parameters.Select(параметр => new XElement(
                    "Parameter",
                    new XAttribute("name", параметр.Name),
                    new XAttribute("type", параметр.Type),
                    new XAttribute("required", параметр.Required ? "true" : "false"),
                    параметр.Default is null ? null : new XAttribute("default", параметр.Default)))),
            new XElement(
                "ValidationRules",
                definition.ValidationRules.Select(правило =>
                {
                    var элемент = new XElement("Rule", new XAttribute("type", правило.Type));

                    if (правило.Start is not null)
                    {
                        элемент.SetAttributeValue("start", правило.Start);
                    }

                    if (правило.End is not null)
                    {
                        элемент.SetAttributeValue("end", правило.End);
                    }

                    if (правило.MaxDays is not null)
                    {
                        элемент.SetAttributeValue(
                            "maxDays",
                            правило.MaxDays.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    if (правило.Mode is not null)
                    {
                        элемент.SetAttributeValue("mode", правило.Mode);
                    }

                    return элемент;
                })),
            definition.Commands.Select(команда =>
            {
                var элемент = new XElement("Command");
                элемент.SetAttributeValue("dialect", команда.Dialect);
                элемент.Add(new XCData(NormalizeSql(команда.Sql)));
                return элемент;
            }));

        var документ = new XDocument(new XDeclaration("1.0", "utf-8", null), корень);

        return документ.Declaration + Environment.NewLine + документ.Root;
    }

    /// <summary>Нормализует переводы строк SQL-текста для CDATA.</summary>
    /// <param name="sql">Исходный текст.</param>
    /// <returns>Текст без \r.</returns>
    public static string NormalizeSql(string sql) => (sql ?? string.Empty).Replace("\r\n", "\n");

    private static int Строка(XElement элемент)
    {
        var информация = (IXmlLineInfo)элемент;

        return информация.HasLineInfo() ? информация.LineNumber : 0;
    }

    private static bool РазобратьФлаг(string? значение) => значение is "true" or "1";
}
