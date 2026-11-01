using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Picket302.Domain.Exchange;
using Picket302.Domain.ValueObjects;

using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// XML-контракты обмена (ТЗ §9.4, функция 77: «Форматы обмена (4 XML-контракта)»):
/// round-trip, имена элементов из EVIDENCE, санитайзер, извлечение индекса,
/// чтение «и починить для печати» и потоковая запись.
/// </summary>
public sealed class ExchangeXmlContractTests
{
    [Fact]
    public void EnvelopesData_WriteListToXml_СовпадаетСФорматомEvidence()
    {
        var xml = WriteEnvelopes(Envelope());

        Assert.Equal(
            "<EnvelopesData>"
            + "<EnvelopeData Number=\"Номер-1\" Note=\"Отметка\">"
            + "<From><Name>ООО Ромашка</Name><Address>101000, г. Москва</Address><ZipCode>101000</ZipCode><Phone>495-123-45-67</Phone></From>"
            + "<To><Name>ООО Романа</Name><Address>300000, г. Тула</Address><ZipCode>300000</ZipCode><Phone>487-22-33-44</Phone></To>"
            + "<Number>Номер-1</Number><Note>Отметка</Note>"
            + "</EnvelopeData>"
            + "</EnvelopesData>",
            xml);
    }

    [Fact]
    public void EnvelopesData_ИменаЭлементов_СоответствуютEvidence()
    {
        var document = XDocument.Parse(WriteEnvelopes(Envelope()));

        Assert.Equal("EnvelopesData", document.Root!.Name.LocalName);

        var envelope = document.Root.Elements("EnvelopeData").Single();

        Assert.Equal(
            new[] { "From", "To", "Number", "Note" },
            envelope.Elements().Select(element => element.Name.LocalName).ToArray());

        Assert.Equal(
            new[] { "Name", "Address", "ZipCode", "Phone" },
            envelope.Element("From")!.Elements().Select(element => element.Name.LocalName).ToArray());

        Assert.Equal(
            new[] { "Name", "Address", "ZipCode", "Phone" },
            envelope.Element("To")!.Elements().Select(element => element.Name.LocalName).ToArray());

        Assert.Equal("Номер-1", envelope.Element("Number")!.Value);
        Assert.Equal("Отметка", envelope.Element("Note")!.Value);
        Assert.Equal("Номер-1", envelope.Attribute("Number")!.Value);
        Assert.Equal("Отметка", envelope.Attribute("Note")!.Value);
    }

    [Fact]
    public void EnvelopesData_RoundTrip_ЗначенияСовпадают()
    {
        var envelopes = new[] { Envelope(), Envelope() };

        var read = EnvelopesData.ReadListFromXml(WriteEnvelopes(envelopes));

        Assert.True(read.IsSuccess);
        Assert.Equal(envelopes.Length, read.Value.Count);
        Assert.Equal(envelopes[0], read.Value[0]);
        Assert.Equal(envelopes[1], read.Value[1]);
    }

    [Fact]
    public void EnvelopesDataSerializer_XDocumentRoundTrip_ЗначенияСовпадают()
    {
        var envelopes = new[] { Envelope() };

        var document = new EnvelopesDataSerializer().WriteEnvelopes(envelopes);

        Assert.Equal("EnvelopesData", document.Root!.Name.LocalName);

        var read = EnvelopesData.ReadListFromXml(document.ToString());

        Assert.True(read.IsSuccess);
        Assert.Equal(envelopes[0], read.Value[0]);
    }

    [Fact]
    public void EnvelopesDataSerializer_ReadEnvelopes_ЧитаетПоток()
    {
        var xml = WriteEnvelopes(Envelope());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var read = new EnvelopesDataSerializer().ReadEnvelopes(stream);

        Assert.Single(read);
        Assert.Equal(Envelope(), read[0]);
    }

    [Fact]
    public void RegistersData_WriteListToXml_СовпадаетСФорматомEvidence()
    {
        var xml = WriteRegisters(Register());

        Assert.Equal(
            "<RegistersData>"
            + "<RegisterData>"
            + "<Number>Реестр-1</Number>"
            + "<From><Name>ООО Ромашка</Name><Address>101000, г. Москва</Address><ZipCode>101000</ZipCode></From>"
            + "<Packages>"
            + "<Package><Number>1</Number>"
            + "<To><Name>ООО Романа</Name><Address>300000, г. Тула</Address><ZipCode>300000</ZipCode></To>"
            + "<Note>Отметка</Note></Package>"
            + "</Packages>"
            + "</RegisterData>"
            + "</RegistersData>",
            xml);
    }

    [Fact]
    public void RegistersData_RoundTrip_ЗначенияСовпадают()
    {
        var registers = new[] { Register() };

        var read = RegistersData.ReadListFromXml(WriteRegisters(registers));

        Assert.True(read.IsSuccess);

        var register = read.Value.Single();

        Assert.Equal(registers[0].Number, register.Number);
        Assert.Equal(registers[0].From, register.From);
        Assert.Equal(registers[0].Packages.Count, register.Packages.Count);
        Assert.Equal(registers[0].Packages[0], register.Packages[0]);
    }

    [Fact]
    public void InventoriesData_WriteListToXml_СовпадаетСФорматомEvidence()
    {
        var xml = WriteInventories(Inventory());

        Assert.Equal(
            "<InventoriesData>"
            + "<InventoryData>"
            + "<From><Name>ООО Ромашка</Name></From>"
            + "<To><Name>ООО Романа</Name><Address>300000, г. Тула</Address></To>"
            + "<InventoryItems><InventoryItem><ItemName>Вложение 1</ItemName></InventoryItem></InventoryItems>"
            + "</InventoryData>"
            + "</InventoriesData>",
            xml);
    }

    [Fact]
    public void InventoriesData_RoundTrip_ЗначенияСовпадают()
    {
        var inventories = new[] { Inventory() };

        var read = InventoriesData.ReadListFromXml(WriteInventories(inventories));

        Assert.True(read.IsSuccess);

        var inventory = read.Value.Single();

        Assert.Equal(inventories[0].FromName, inventory.FromName);
        Assert.Equal(inventories[0].ToName, inventory.ToName);
        Assert.Equal(inventories[0].ToAddress, inventory.ToAddress);
        Assert.Equal(inventories[0].Items.Count, inventory.Items.Count);
        Assert.Equal(inventories[0].Items[0], inventory.Items[0]);
    }

    [Fact]
    public void EconomicMembers_WriteListToXml_СовпадаетСФорматомEvidence()
    {
        var xml = WriteMembers(LegalPerson(), NaturalPerson());

        Assert.Equal(
            "<EconomicMembers>"
            + "<LegalPerson><Name>ООО Ромашка</Name><ShortName>Ромашка</ShortName><Note>Примечания</Note><Email>info@romashka.ru</Email>"
            + "<Address><ZipCode>101000</ZipCode><Province>г. Москва</Province><City>Москва</City><AddressLine>ул. Тверская, 1</AddressLine></Address>"
            + "</LegalPerson>"
            + "<NaturalPerson><LastName>Иванов</LastName><MiddleName>Иванович</MiddleName><FirstName>Иван</FirstName><Note>Примечания</Note><Email>ivanov@mail.ru</Email>"
            + "<Address><ZipCode>300000</ZipCode><Province>Тульская обл.</Province><City>Тула</City><AddressLine>ул. Кирова, 2</AddressLine></Address>"
            + "</NaturalPerson>"
            + "</EconomicMembers>",
            xml);
    }

    [Fact]
    public void EconomicMembers_RoundTrip_ЗначенияСовпадают()
    {
        var members = new EconomicMember[] { LegalPerson(), NaturalPerson() };

        var read = EconomicMembers.ReadListFromXml(WriteMembers(members));

        Assert.True(read.IsSuccess);
        Assert.Equal(members.Length, read.Value.Count);
        Assert.Equal(members[0], read.Value[0]);
        Assert.Equal(members[1], read.Value[1]);
    }

    [Fact]
    public void CleanXmlString_УдаляетУправляющиеСимволыИНейтрализуетРазметку()
    {
        var cleaned = XmlTextSanitizer.CleanXmlString("О\u0001ОО\u0007 <b>Ромашка</b> & \"Ромашка\"");

        Assert.DoesNotContain('\u0001', cleaned);
        Assert.DoesNotContain('\u0007', cleaned);
        Assert.DoesNotContain('<', cleaned);
        Assert.DoesNotContain('>', cleaned);
        Assert.Contains("&lt;b&gt;", cleaned);
        Assert.Contains("&amp;", cleaned);
    }

    [Fact]
    public void CleanXmlString_ПустоеЗначение_ВозвращаетПустуюСтроку()
    {
        Assert.Equal(string.Empty, XmlTextSanitizer.CleanXmlString(null));
        Assert.Equal(string.Empty, XmlTextSanitizer.CleanXmlString(string.Empty));
    }

    [Fact]
    public void ExtractZip_НаходитИндексВТекстеАдреса()
    {
        Assert.Equal("123456", ZipIndexExtractor.ExtractZip("123456, г. Москва"));
        Assert.Equal(string.Empty, ZipIndexExtractor.ExtractZip("г. Москва, ул. Тверская, д. 1"));
        Assert.Equal(string.Empty, ZipIndexExtractor.ExtractZip(null));
    }

    [Fact]
    public void ReadListFromXmlAndFixForPrint_УсекаетПолеИНормализуетИндекс()
    {
        var longName = new string('Я', 300);

        var xml =
            "<EnvelopesData><EnvelopeData>"
            + "<From><Name>Отправитель</Name><Address>г. Москва, ул. Тверская</Address><ZipCode>300 000</ZipCode><Phone></Phone></From>"
            + "<To><Name>" + longName + "</Name><Address>101000, г. Москва</Address><ZipCode></ZipCode><Phone></Phone></To>"
            + "<Number>1</Number><Note></Note>"
            + "</EnvelopeData></EnvelopesData>";

        var read = EnvelopesData.ReadListFromXmlAndFixForPrint(xml);

        Assert.True(read.IsSuccess);

        var envelope = read.Value.Single();

        // Усечение поля длиннее 255 знаков.
        Assert.Equal(PrintTextNormalizer.MaxFieldLength, envelope.To.Name.Length);

        // Нормализация индекса: разделители убраны.
        Assert.Equal("300000", envelope.From.ZipCode);

        // Индекс выкушен из текста адреса, когда он не задан отдельно.
        Assert.Equal("101000", envelope.To.ZipCode);
        Assert.DoesNotContain("101000", envelope.To.Address);

        Assert.Contains(read.Warnings, warning => warning.Contains("255"));
    }

    [Fact]
    public void ReadListFromXmlAndFixForPrint_БезФлагаИндексИзАдресаНеИзвлекается()
    {
        var xml =
            "<EnvelopesData><EnvelopeData>"
            + "<From><Name>Отправитель</Name><Address>101000, г. Москва</Address><ZipCode></ZipCode><Phone></Phone></From>"
            + "<To><Name>Получатель</Name><Address>300000, г. Тула</Address><ZipCode></ZipCode><Phone></Phone></To>"
            + "<Number>1</Number><Note></Note>"
            + "</EnvelopeData></EnvelopesData>";

        var read = EnvelopesData.ReadListFromXmlAndFixForPrint(xml, extractZipFromAddress: false);

        Assert.True(read.IsSuccess);

        var envelope = read.Value.Single();

        Assert.Equal(string.Empty, envelope.From.ZipCode);
        Assert.Contains("101000", envelope.From.Address);
    }

    [Fact]
    public void PrintTextNormalizer_ПредельнаяДлина_СовпадаетСОграничениемБазы()
    {
        Assert.Equal(Address.MaxLineLength, PrintTextNormalizer.MaxFieldLength);
        Assert.Equal(255, PrintTextNormalizer.MaxFieldLength);
    }

    [Fact]
    public void ExchangeXmlWriter_ТриФазы_ПишутЭлемент()
    {
        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            ExchangeXmlWriter.StartElement(writer, "Note");
            ExchangeXmlWriter.WriteText(writer, "текст");
            ExchangeXmlWriter.EndElement(writer);
        }

        Assert.Equal("<Note>текст</Note>", buffer.ToString());
    }

    [Fact]
    public void ExchangeXmlWriter_БольшаяПартия_ПишетсяПотоково()
    {
        const int count = 5000;

        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            EnvelopesData.WriteListToXml(GenerateEnvelopes(count), writer);
        }

        var xml = buffer.ToString();

        Assert.Equal(count, Occurrences(xml, "<EnvelopeData"));
        Assert.Equal(1, Occurrences(xml, "<EnvelopesData>"));
    }

    [Fact]
    public void EmbeddedZipPackageExtractor_ИзвлекаетВложенныйZip()
    {
        var archive = CreateArchive("blank.txt", "содержимое бланка");

        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            ExchangeXmlWriter.StartElement(writer, "EnvelopesData");
            ExchangeXmlWriter.WriteZipPackage(writer, "blank.zip", archive);
            ExchangeXmlWriter.EndElement(writer);
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "picket302-s6-" + Guid.NewGuid().ToString("N"));

        try
        {
            var saved = EmbeddedZipPackageExtractor.ExtractFromXml(buffer.ToString(), directory);

            Assert.Single(saved);
            Assert.True(File.Exists(Path.Combine(directory, "blank.zip")));
            Assert.True(File.Exists(Path.Combine(directory, "blank.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadListFromXml_НекорректныйXml_ВозвращаетОшибкуБезИсключения()
    {
        var read = EnvelopesData.ReadListFromXml("<EnvelopesData><EnvelopeData></EnvelopesData>");

        Assert.True(read.IsFailure);
        Assert.Contains("Ошибка чтения XML-данных", read.Error);
    }

    [Fact]
    public void ReadListFromXml_НеожиданныйКорневойЭлемент_ВозвращаетОшибку()
    {
        var read = EnvelopesData.ReadListFromXml("<Something><EnvelopeData /></Something>");

        Assert.True(read.IsFailure);
        Assert.Contains("EnvelopesData", read.Error);
    }

    [Fact]
    public void WriteToXml_РазметкаВЗначении_НеСоздаётЭлементов()
    {
        var envelope = Envelope() with
        {
            Note = "<script>alert(1)</script>",
        };

        var xml = WriteEnvelopes(envelope);

        Assert.DoesNotContain("<script>", xml);
        Assert.Contains("&lt;script&gt;", xml);

        var read = EnvelopesData.ReadListFromXml(xml);

        Assert.True(read.IsSuccess);
        Assert.Equal("<script>alert(1)</script>", read.Value[0].Note);
    }

    private static PrintableEnvelope Envelope() => new()
    {
        From = new EnvelopeParty
        {
            Name = "ООО Ромашка",
            Address = "101000, г. Москва",
            ZipCode = "101000",
            Phone = "495-123-45-67",
        },
        To = new EnvelopeParty
        {
            Name = "ООО Романа",
            Address = "300000, г. Тула",
            ZipCode = "300000",
            Phone = "487-22-33-44",
        },
        Number = "Номер-1",
        Note = "Отметка",
    };

    private static RegisterData Register() => new()
    {
        Number = "Реестр-1",
        From = new EnvelopeParty
        {
            Name = "ООО Ромашка",
            Address = "101000, г. Москва",
            ZipCode = "101000",
        },
        Packages = new[]
        {
            new RegisterPackage
            {
                Number = "1",
                To = new EnvelopeParty
                {
                    Name = "ООО Романа",
                    Address = "300000, г. Тула",
                    ZipCode = "300000",
                },
                Note = "Отметка",
            },
        },
    };

    private static InventoryData Inventory() => new()
    {
        FromName = "ООО Ромашка",
        ToName = "ООО Романа",
        ToAddress = "300000, г. Тула",
        Items = new[] { new InventoryItem { ItemName = "Вложение 1" } },
    };

    private static LegalPerson LegalPerson() => new()
    {
        Name = "ООО Ромашка",
        ShortName = "Ромашка",
        Note = "Примечания",
        Email = "info@romashka.ru",
        Address = new EconomicMemberAddress
        {
            ZipCode = "101000",
            Province = "г. Москва",
            City = "Москва",
            AddressLine = "ул. Тверская, 1",
        },
    };

    private static NaturalPerson NaturalPerson() => new()
    {
        LastName = "Иванов",
        MiddleName = "Иванович",
        FirstName = "Иван",
        Note = "Примечания",
        Email = "ivanov@mail.ru",
        Address = new EconomicMemberAddress
        {
            ZipCode = "300000",
            Province = "Тульская обл.",
            City = "Тула",
            AddressLine = "ул. Кирова, 2",
        },
    };

    private static IEnumerable<PrintableEnvelope> GenerateEnvelopes(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return Envelope() with { Number = index.ToString(CultureInfo.InvariantCulture) };
        }
    }

    private static string WriteEnvelopes(PrintableEnvelope envelope)
        => WriteEnvelopes(new[] { envelope });

    private static string WriteEnvelopes(IEnumerable<PrintableEnvelope> envelopes)
    {
        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            EnvelopesData.WriteListToXml(envelopes, writer);
        }

        return buffer.ToString();
    }

    private static string WriteRegisters(RegisterData register)
        => WriteRegisters(new[] { register });

    private static string WriteRegisters(IEnumerable<RegisterData> registers)
    {
        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            RegistersData.WriteListToXml(registers, writer);
        }

        return buffer.ToString();
    }

    private static string WriteInventories(InventoryData inventory)
        => WriteInventories(new[] { inventory });

    private static string WriteInventories(IEnumerable<InventoryData> inventories)
    {
        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            InventoriesData.WriteListToXml(inventories, writer);
        }

        return buffer.ToString();
    }

    private static string WriteMembers(EconomicMember first, EconomicMember second)
        => WriteMembers(new EconomicMember[] { first, second });

    private static string WriteMembers(IEnumerable<EconomicMember> members)
    {
        var buffer = new StringWriter();

        using (var writer = ExchangeXmlWriter.CreateWriter(buffer, indent: false))
        {
            EconomicMembers.WriteListToXml(members, writer);
        }

        return buffer.ToString();
    }

    private static byte[] CreateArchive(string entryName, string content)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);

            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        return buffer.ToArray();
    }

    private static int Occurrences(string text, string fragment)
    {
        var count = 0;
        var index = text.IndexOf(fragment, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
