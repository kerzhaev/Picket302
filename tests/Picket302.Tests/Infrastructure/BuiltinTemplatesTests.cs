using Picket302.Domain.Serialization;
using Picket302.Infrastructure.Templates;
using Picket302.Printing.Editor;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Восемь встроенных эталонных бланков (ТЗ §8.3, функция 36; числа текстовых
/// полей и размеры — по заданию этапа, состав полей — по словарю форм).
/// </summary>
public sealed class BuiltinTemplatesTests
{
    private readonly BuiltinTemplatesCatalog _каталог = new();
    private readonly BlankLayoutValidator _валидатор = new();

    public static TheoryData<BuiltinFormType, decimal, decimal, int, int> Ожидания() => new()
    {
        // Форма, ширина, высота, число страниц, число текстовых полей.
        { BuiltinFormType.Form112ep, 148m, 104m, 1, 39 },
        { BuiltinFormType.Form112e, 220m, 110m, 1, 39 },
        { BuiltinFormType.Form112ef, 195m, 185m, 2, 81 },
        { BuiltinFormType.Form112eyu, 195m, 185m, 2, 81 },
        { BuiltinFormType.Form117and113, 297m, 210m, 2, 95 },
        { BuiltinFormType.Form116, 210m, 297m, 2, 83 },
        { BuiltinFormType.Form119_2011, 148m, 105m, 2, 11 },
        { BuiltinFormType.EnvelopeC5_2012, 229m, 162m, 1, 46 },
    };

    [Theory]
    [MemberData(nameof(Ожидания))]
    public void ВстроенныйБланк_ГабаритыСтраницыЧислаПолей(
        BuiltinFormType форма,
        decimal ширина,
        decimal высота,
        int страницы,
        int числоПолей)
    {
        var бланк = _каталог.GetBuiltinTemplate(форма);

        Assert.Equal(ширина, бланк.Geometry.WidthMm);
        Assert.Equal(высота, бланк.Geometry.HeightMm);
        Assert.Equal(страницы, бланк.Pages.Count);
        Assert.True(бланк.StandardLayout);

        var текстовые = бланк.AllFields().Count(поле => поле.Kind != BlankFieldKinds.PostIndex);

        Assert.Equal(числоПолей, текстовые);
    }

    [Fact]
    public void Ф112эп_39Полей_ДваШтампа()
    {
        var бланк = _каталог.GetBuiltinTemplate(BuiltinFormType.Form112ep);

        var штампы = бланк.AllFields().Where(поле => поле.Kind == BlankFieldKinds.PostIndex).ToList();

        Assert.Equal(2, штампы.Count);
        Assert.All(штампы, штамп => Assert.NotNull(штамп.PostIndex));
        Assert.Contains(бланк.AllFields(), поле => поле.Name == "Строки «Кому»");
        Assert.Contains(бланк.AllFields(), поле => поле.Name == "ИНН получателя");
    }

    [Fact]
    public void Ф119_ДвеСтраницы_ШтампыНаОбеееСторонах()
    {
        var бланк = _каталог.GetBuiltinTemplate(BuiltinFormType.Form119_2011);

        Assert.Equal(2, бланк.Pages.Count);
        Assert.Single(бланк.Pages[0].Fields.Where(поле => поле.Kind == BlankFieldKinds.PostIndex));
        Assert.Single(бланк.Pages[1].Fields.Where(поле => поле.Kind == BlankFieldKinds.PostIndex));
        Assert.Equal(11, бланк.AllFields().Count(поле => поле.Kind != BlankFieldKinds.PostIndex));
    }

    [Fact]
    public void КонвертC5_ШтампПечатается_ФонЗадан()
    {
        var бланк = _каталог.GetBuiltinTemplate(BuiltinFormType.EnvelopeC5_2012);

        Assert.Equal("C5.jpg", бланк.Pages[0].BackgroundFile);

        var штамп = бланк.AllFields().Single(поле => поле.Kind == BlankFieldKinds.PostIndex);

        Assert.True(штамп.Printable);
        Assert.True(бланк.AllFields().Any(поле => поле.Name == "Республика/край/область места назначения"));
    }

    [Fact]
    public void ВстроенныеБланки_БезВыходаЗаГраницы_БезЗапретаСохранения()
    {
        foreach (BuiltinFormType форма in Enum.GetValues<BuiltinFormType>())
        {
            var бланк = _каталог.GetBuiltinTemplate(форма);
            var отчёт = _валидатор.Validate(бланк);

            Assert.True(
                !отчёт.SaveBlocked,
                $"Бланк «{бланк.Name}»: элемент за границами печатной области — {string.Join("; ", отчёт.Messages)}");
        }
    }

    [Fact]
    public void ВстроенныеБланки_БезПересеченийAabb()
    {
        foreach (BuiltinFormType форма in Enum.GetValues<BuiltinFormType>())
        {
            var бланк = _каталог.GetBuiltinTemplate(форма);
            var отчёт = _валидатор.Validate(бланк);

            Assert.True(
                отчёт.Overlaps.Count == 0,
                $"Бланк «{бланк.Name}»: пересечения AABB — {string.Join("; ", отчёт.Overlaps.Select(п => п.First + "+" + п.Second))}");
        }
    }

    [Fact]
    public void ПолеШтампа_НормативыГост()
    {
        foreach (BuiltinFormType форма in Enum.GetValues<BuiltinFormType>())
        {
            var бланк = _каталог.GetBuiltinTemplate(форма);

            foreach (var штамп in бланк.AllFields().Where(поле => поле.Kind == BlankFieldKinds.PostIndex))
            {
                Assert.Equal(3.7m, штамп.PostIndex!.DigitWidthMm);
                Assert.Equal(5.5m, штамп.PostIndex.DigitHeightMm);
                Assert.Equal(0.45m, штамп.PostIndex.LineWidthMm);
                Assert.Equal(5.4m, штамп.PostIndex.SpacingMm);
                Assert.Equal(6, штамп.PostIndex.DigitCount);
            }
        }
    }

    [Fact]
    public void Библиотека_НеМенее20Шаблонов()
    {
        var библиотека = _каталог.BuildLibrary();

        Assert.True(библиотека.Count >= 20);
        Assert.All(библиотека, запись => Assert.False(string.IsNullOrWhiteSpace(запись.PackageName)));
        Assert.All(библиотека, запись => Assert.True(запись.Blank.Pages.Count > 0));
    }

    [Fact]
    public void ПопыткаПерезаписиВстроенного_СохраняетсяКакКопия()
    {
        // Поведение экспорта библиотеки: существующий файл не перезаписывается.
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_overwrite_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            var репозиторий = new TemplatePackageRepository();

            репозиторий.ExportBuiltinLibraryAsync(каталог).GetAwaiter().GetResult();
            var первая = Directory.GetFiles(каталог, "*.lbk").Select(Path.GetFileName).OrderBy(имя => имя).ToList();

            репозиторий.ExportBuiltinLibraryAsync(каталог).GetAwaiter().GetResult();
            var вторая = Directory.GetFiles(каталог, "*.lbk").Select(Path.GetFileName).OrderBy(имя => имя).ToList();

            Assert.True(вторая.Count > первая.Count, "Повторный экспорт добавляет пользовательские копии.");
            Assert.Subset(вторая.ToHashSet(), первая.ToHashSet());
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }
}
