using System.IO.Compression;
using System.Text;
using Picket302.Infrastructure.IO;
using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Пакет .lbk — ZIP-контейнер (ТЗ §5.1; §8.3, функции 37/40: round-trip,
/// «Ошибка структуры пакета», поврежденный пакет исключается из каталога).
/// </summary>
public sealed class LbkPackageTests
{
    [Fact]
    public void ExportImport_RoundTrip_ПолныйПакет()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_lbk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            var бланк = new BuiltinTemplatesCatalog().GetBuiltinTemplate(BuiltinFormType.EnvelopeC5_2012);
            var архиватор = new LbkPackageArchiver();
            var путь = Path.Combine(каталог, "конверт.lbk");

            var подложка = Encoding.ASCII.GetBytes("PNGDATA-background");
            var ассет = Encoding.ASCII.GetBytes("PNGDATA-emblem");

            архиватор.ExportPackage(
                бланк,
                путь,
                new LbkManifest { Name = бланк.Name, Author = "Picket302", Version = "1.1" },
                queryXml: "<ReportQueryDefinition id=\"qrReport_Envelopes\" version=\"1.0\"><Name>q</Name><Command dialect=\"Sqlite\"><![CDATA[SELECT 1;]]></Command></ReportQueryDefinition>",
                background: подложка,
                assets: new Dictionary<string, byte[]> { ["emblem.png"] = ассет });

            var пакет = архиватор.ImportPackage(путь);

            Assert.Equal(бланк.Name, пакет.Manifest.Name);
            Assert.Equal("Picket302", пакет.Manifest.Author);
            Assert.Equal("1.1", пакет.Manifest.Version);
            Assert.Equal(бланк.Geometry.WidthMm, пакет.Blank.Geometry.WidthMm);
            Assert.Equal(бланк.Pages.Count, пакет.Blank.Pages.Count);
            Assert.Equal(бланк.Pages[0].Fields.Count, пакет.Blank.Pages[0].Fields.Count);
            Assert.NotNull(пакет.QueryXml);
            Assert.Contains("qrReport_Envelopes", пакет.QueryXml);
            Assert.NotNull(пакет.Background);
            Assert.Equal(подложка, пакет.Background);
            Assert.Contains("emblem.png", пакет.AssetNames);
            Assert.Equal(ассет, пакет.Assets["emblem.png"]);
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    [Fact]
    public void Import_ПоврежденныйZip_ОшибкаСтруктурыПакета()
    {
        var путь = Path.Combine(Path.GetTempPath(), "picket302_broken_" + Guid.NewGuid().ToString("N") + ".lbk");

        try
        {
            File.WriteAllBytes(путь, new byte[] { 0x50, 0x4B, 0x00, 0x07, 0x99, 0x01, 0x02, 0x03 });

            var ошибка = Assert.Throws<LbkPackageException>(() => new LbkPackageArchiver().ImportPackage(путь));

            Assert.Contains(LbkPackageException.StructureErrorCaption, ошибка.Message);
        }
        finally
        {
            File.Delete(путь);
        }
    }

    [Fact]
    public void Import_НеПомеченКакZip_Ошибка()
    {
        var путь = Path.Combine(Path.GetTempPath(), "picket302_text_" + Guid.NewGuid().ToString("N") + ".lbk");

        try
        {
            File.WriteAllText(путь, "это не ZIP-архив");

            Assert.Throws<LbkPackageException>(() => new LbkPackageArchiver().ImportPackage(путь));
        }
        finally
        {
            File.Delete(путь);
        }
    }

    [Fact]
    public void Import_НетBlankXml_Ошибка()
    {
        var путь = Path.Combine(Path.GetTempPath(), "picket302_empty_" + Guid.NewGuid().ToString("N") + ".lbk");

        using (var поток = File.Create(путь))
        {
            using var архив = new ZipArchive(поток, ZipArchiveMode.Create);
            архив.CreateEntry("manifest.json");
        }

        var ошибка = Assert.Throws<LbkPackageException>(() => new LbkPackageArchiver().ImportPackage(путь));

        Assert.Contains("blank.xml", ошибка.Message);
        File.Delete(путь);
    }

    [Fact]
    public void Import_BlankXmlНесоответствуетXSD_ОшибкаСПозиции()
    {
        var путь = Path.Combine(Path.GetTempPath(), "picket302_xsd_" + Guid.NewGuid().ToString("N") + ".lbk");

        using (var поток = File.Create(путь))
        {
            using var архив = new ZipArchive(поток, ZipArchiveMode.Create);

            using var запись = архив.CreateEntry("blank.xml").Open();

            using var писатель = new StreamWriter(запись, new UTF8Encoding(false));
            писатель.Write("<CustomBlank version=\"2.0\"><Unknown/></CustomBlank>");
        }

        var ошибка = Assert.Throws<LbkPackageException>(() => new LbkPackageArchiver().ImportPackage(путь));

        Assert.Contains("XSD", ошибка.Message);
        File.Delete(путь);
    }

    [Fact]
    public void LoadAllPackages_ПоврежденныйИсключается_БезПадения()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_pkgs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            var архиватор = new LbkPackageArchiver();

            // Валидный пакет.
            архиватор.ExportPackage(
                new BuiltinTemplatesCatalog().GetBuiltinTemplate(BuiltinFormType.Form116),
                Path.Combine(каталог, "ф.116.lbk"));

            // Поврежденные пакеты: мусор и ZIP без blank.xml.
            File.WriteAllBytes(Path.Combine(каталог, "мусор.lbk"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            using (var поток = File.Create(Path.Combine(каталог, "пустой.lbk")))
            {
                using var архив = new ZipArchive(поток, ZipArchiveMode.Create);
                архив.CreateEntry("manifest.json");
            }

            var репозиторий = new TemplatePackageRepository(архиватор);
            var записи = репозиторий.LoadAllPackages(каталог);

            Assert.Equal(3, записи.Count);
            Assert.Single(записи, запись => запись.IsValid);
            Assert.Equal(2, записи.Count(запись => !запись.IsValid));
            Assert.All(записи.Where(запись => !запись.IsValid), запись => Assert.Contains("Ошибка структуры пакета", запись.Error));
            Assert.NotNull(записи.Single(запись => запись.IsValid).Template);
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ExportBuiltinLibrary_НеМенее20Пакетов()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_lib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            var репозиторий = new TemplatePackageRepository();
            var записано = await репозиторий.ExportBuiltinLibraryAsync(каталог);

            Assert.True(записано >= 20, $"Библиотека должна содержать не менее 20 пакетов, записано {записано}.");
            Assert.Equal(записано, Directory.GetFiles(каталог, "*.lbk").Length);

            var записи = репозиторий.LoadAllPackages(каталог);

            Assert.All(записи, запись => Assert.True(запись.IsValid));
            Assert.Contains(записи, запись => запись.Template!.Name.Contains("112эп"));
            Assert.Contains(записи, запись => запись.Template!.Name.Contains("C5"));
            Assert.Contains(записи, запись => запись.Template!.Name.Contains("Доверенность"));
            Assert.Contains(записи, запись => запись.Template!.Name.Contains("ф.119"));
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    [Fact]
    public void ДобавлениеПакетаВКаталог_ДоступенБезПерезапуска()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_live_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            var архиватор = new LbkPackageArchiver();
            var репозиторий = new TemplatePackageRepository(архиватор);

            Assert.Empty(репозиторий.LoadAllPackages(каталог));

            // Новый .lbk в папке становится доступен при следующем чтении каталога.
            архиватор.ExportPackage(
                new BuiltinTemplatesCatalog().GetBuiltinTemplate(BuiltinFormType.Form112ef),
                Path.Combine(каталог, "ф.112эф.lbk"));

            var записи = репозиторий.LoadAllPackages(каталог);

            Assert.Single(записи);
            Assert.True(записи[0].IsValid);
            Assert.Contains("112эф", записи[0].Template!.Name);
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }
}
