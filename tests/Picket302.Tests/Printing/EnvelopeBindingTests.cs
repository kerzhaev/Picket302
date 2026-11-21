using System.Drawing;
using Picket302.Domain.Dictionaries;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Fonts;
using Picket302.Printing;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Rendering;
using Picket302.Tests.Packaging;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия пустого конверта (21.11.2026): рендерер ищет значение по имени
/// поля шаблона, а данные печати ключевались только тегом привязки. Ни
/// встроенные шаблоны («Строки «Кому»»), ни оригинальные имена (str_to1) не
/// совпадали с ключами-тегами, и конверт печатался без адреса, отправителя и
/// индекса. ExpandBindings/NormalizePrintValues обязаны закрывать все имена.
/// </summary>
public sealed class EnvelopeBindingTests
{
    private static Dictionary<string, string> Колонки() => new(StringComparer.OrdinalIgnoreCase)
    {
        [FieldDictionary.ColumnToZip] = "125009",
        [FieldDictionary.ColumnToStr1] = "ООО «Ромашка»",
        [FieldDictionary.ColumnToStr2] = "ул. Тверская, 1",
        [FieldDictionary.ColumnToStr5] = "г. Москва",
        [FieldDictionary.ColumnFromZip] = "101000",
        [FieldDictionary.ColumnFromStr1] = "ИП Отправитель",
        [FieldDictionary.ColumnFromStr5] = "г. Москва",
    };

    [Fact]
    public void ExpandBindings_Закрывает_Теги_Алиасы_И_ЧеловеческиеИмена()
    {
        var значения = FieldDictionary.ExpandBindings(Колонки());

        // тег привязки
        Assert.Equal("ООО «Ромашка»", значения["[edt_str_to1]"]);
        // псевдоним оригинала (KonvertProNet .fr3)
        Assert.Equal("ООО «Ромашка»", значения["str_to1"]);
        Assert.Equal("125009", значения["ind_to"]);
        // человеческие имена встроенных шаблонов
        Assert.Contains("ООО «Ромашка»", значения["Строки «Кому»"]);
        Assert.Contains("ИП Отправитель", значения["Строки «От кого»"]);
        Assert.Equal("125009", значения["Индекс назначения для машинной обработки"]);
        Assert.Equal("101000", значения["Индекс отправителя для машинной обработки"]);
    }

    [Fact]
    public void NormalizePrintValues_ИзТегов_ПолучаетЧеловеческиеИмена()
    {
        // старый сервер шлёт только теги
        var теги = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["[edt_str_to1]"] = "ООО «Ромашка»",
            ["[edt_str_from1]"] = "ИП Отправитель",
            ["[edtIndeks_to]"] = "125009",
        };

        var значения = FieldDictionary.NormalizePrintValues(теги);

        Assert.Contains("ООО «Ромашка»", значения["Строки «Кому»"]);
        Assert.Contains("ИП Отправитель", значения["Строки «От кого»"]);
        Assert.Equal("125009", значения["Индекс назначения для машинной обработки"]);
        // исходные теги сохранены
        Assert.Equal("ООО «Ромашка»", значения["[edt_str_to1]"]);
    }

    [Fact]
    public void Рендер_ВстроенногоКонверта_ВыводитАдресОтправителяИИндекс()
    {
        var контекст = new ЗаписывающийКонтекст();
        var гард = new ZipcodeFontGuard(Path.Combine(RepositoryRoot.Resolve(), @"src\Picket302.App\Fonts"));
        var рендерер = new EnvelopeRenderer(new GdiTextMeasurer());
        var индекс = new PostIndexRenderer(гард);

        var поля = new List<BlankField>
        {
            new() { Name = "Строки «Кому»", Kind = BlankFieldKind.Text, XMm = 5, YMm = 5, LengthMm = 60, HeightMm = 18, IsPrintable = true, IsVisible = true },
            new() { Name = "Строки «От кого»", Kind = BlankFieldKind.Text, XMm = 5, YMm = 30, LengthMm = 60, HeightMm = 18, IsPrintable = true, IsVisible = true },
            new() { Name = "Индекс назначения для машинной обработки", Kind = BlankFieldKind.PostIndex, XMm = 5, YMm = 60, LengthMm = 30, HeightMm = 6, IsPrintable = true, IsVisible = true },
        };

        var job = new PrintJobContext
        {
            Fields = поля,
            Items = new[]
            {
                new PrintableItem { Id = 1, Values = FieldDictionary.ExpandBindings(Колонки()) },
            },
        };

        var предупреждения = рендерер.Render(контекст, job, job.Items[0], индекс);

        // адрес получателя и отправителя напечатаны непустыми строками
        Assert.Contains(контекст.Строки, т => т.Contains("ООО «Ромашка»"));
        Assert.Contains(контекст.Строки, т => т.Contains("ИП Отправитель"));
        // индекс отрисован кодовым штампом (6 цифр), а не предупреждением
        Assert.Equal(6, контекст.ЗнакиИндекса);
        Assert.DoesNotContain(предупреждения, п => п.Contains("не выведен"));
    }

    /// <summary>Контекст, фиксирующий напечатанные строки и знаки индекса.</summary>
    private sealed class ЗаписывающийКонтекст : IGraphicsContext
    {
        public List<string> Строки { get; } = new();
        public int ЗнакиИндекса { get; private set; }

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(string text, string fontName, float widthMm, float heightMm, float xMm, float yMm)
        {
            Строки.Add(text);
        }

        public void DrawStringMm(string text, FontFamily family, float widthMm, float heightMm, float xMm, float yMm)
        {
            // кодовый штамп рисует по одной цифре за вызов
            ЗнакиИндекса++;
            Строки.Add(text);
        }

        public void DrawStringMm(string text, string fontName, float sizePt, bool bold, float xMm, float yMm)
        {
            Строки.Add(text);
        }
    }
}
