using System.Drawing;
using Picket302.App;
using Picket302.App.UI.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Templates;
using Picket302.Printing;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Rendering;
using Picket302.Tests.Infrastructure;
using Xunit;
using static Picket302.Application.Services.MailFactory;

namespace Picket302.Tests.App;

/// <summary>
/// Интеграция клипарта как логотипа получателя (ТЗ §8.10, функция 158):
/// выбор элемента каталога, сохранение PNG контрагенту настоящим
/// репозиторием во временную базу, повторное чтение, подстановка в
/// PrintDataSource и удаление логотипа. Внешние файлы и сеть не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ClipartLogoIntegrationTests
{
    private static Counterparty Контрагент(string имя) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsSender = false,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    [Fact]
    public async Task Клипарт_НазначаетсяЛоготипомПолучателяИДоходитДоИсточникаПечати()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var отправитель = await состав.СоставДанных!.Контрагенты.AddAsync(Контрагент("ООО «Отправитель»"));
        var получатель = await состав.СоставДанных!.Контрагенты.AddAsync(Контрагент("ООО «Получатель»"));

        // 1–2. Выбор элемента каталога через диалог.
        var каталог = new ClipartLibrary();

        Assert.True(каталог.TryGet("конверт", out var png));

        using var диалог = new FormClipartPicker(каталог);

        Assert.True(диалог.ВыбратьЭлемент("конверт"));
        Assert.NotNull(диалог.ВыбранныйPng);

        // 3. Назначение PNG выбранному контрагенту и сохранение настоящим
        // репозиторием во временную базу.
        var карточка = await состав.СоставДанных!.Контрагенты.GetByIdAsync(получатель);

        Assert.NotNull(карточка);
        Assert.Null(карточка!.Logo);

        карточка.Logo = диалог.ВыбранныйPng;

        await состав.СоставДанных!.Контрагенты.UpdateAsync(карточка);

        // 4–5. Повторное чтение: байты PNG совпадают с выбранным элементом.
        var перечитанный = await состав.СоставДанных!.Контрагенты.GetByIdAsync(получатель);

        Assert.NotNull(перечитанный);
        Assert.NotNull(перечитанный!.Logo);
        Assert.Equal(png, перечитанный.Logo);
        Assert.True(
            перечитанный.Logo.Length >= 4
                && перечитанный.Logo[0] == 0x89
                && перечитанный.Logo[1] == 0x50
                && перечитанный.Logo[2] == 0x4E
                && перечитанный.Logo[3] == 0x47,
            "Сохранённый логотип обязан быть PNG.");

        // 6–7. Печатный конвейер: PrintDataSource передаёт сохранённый PNG
        // в LogoBytes элемента печати.
        var письмо = CreateOutcoming(
            получатель,
            отправитель,
            "C6",
            перечитанный.GetAddress(перечитанный.AddressMainType)!);

        письмо.DistributionId = null;
        var идентификаторПисьма = await состав.СоставДанных!.Письма.AddAsync(письмо);

        var источник = new PrintDataSource(состав.СоставДанных!.Письма, состав.СоставДанных!.Контрагенты);
        var элементы = await источник.GetItemsAsync([идентификаторПисьма]);

        var элемент = Assert.Single(элементы);

        Assert.NotNull(элемент.LogoBytes);
        Assert.Equal(png, элемент.LogoBytes);

        // 8–9. Удаление логотипа: сохраняется и повторно читается пустым.
        перечитанный.Logo = null;

        await состав.СоставДанных!.Контрагенты.UpdateAsync(перечитанный);

        var безЛоготипа = await состав.СоставДанных!.Контрагенты.GetByIdAsync(получатель);

        Assert.Null(безЛоготипа!.Logo);

        var послеУдаления = await источник.GetItemsAsync([идентификаторПисьма]);

        Assert.Null(Assert.Single(послеУдаления).LogoBytes);
    }

    /// <summary>
    /// Элемент каталога клипартов доходит до носителя: PNG из ClipartLibrary
    /// выводится конвейером EnvelopeRenderer/LogoRenderer в границы поля
    /// логотипа без предупреждений; в режиме «без логотипа» не выводится.
    /// </summary>
    [Fact]
    public void Клипарт_РендеритсяНаКонвертеЧерезLogoRenderer()
    {
        var каталог = new ClipartLibrary();

        foreach (var элемент in каталог.Items)
        {
            Assert.True(каталог.TryGet(элемент.Id, out var png));

            var фиксатор = new ФиксаторГрафики();
            var рендерер = new EnvelopeRenderer(new Измеритель());

            var предупреждения = рендерер.Render(
                фиксатор,
                КонтекстПечати(сЛоготипом: true, png),
                ПечатноеИзделие(),
                logo: new LogoRenderer());

            Assert.True(
                фиксатор.Изображения.Count == 1,
                $"Элемент «{элемент.Id}» обязан выводиться ровно один раз, выведено {фиксатор.Изображения.Count}.");

            var изображение = фиксатор.Изображения[0];

            // Квадратный элемент вписывается в поле 30×15 мм с сохранением
            // пропорций: сторона 15 мм, центрирование по ширине поля.
            Assert.Equal(47.5f, изображение.X, 2);
            Assert.Equal(80f, изображение.Y, 2);
            Assert.Equal(15f, изображение.Width, 2);
            Assert.Equal(15f, изображение.Height, 2);

            Assert.False(
                предупреждения.Any(текст => текст.Contains("Логотип", StringComparison.Ordinal)),
                $"Элемент «{элемент.Id}»: предупреждений о логотипе быть не должно: "
                + string.Join("; ", предупреждения));

            // Режим «без логотипа» исключает вывод элемента.
            var безЛоготипа = new ФиксаторГрафики();

            рендерер.Render(
                безЛоготипа,
                КонтекстПечати(сЛоготипом: false, png),
                ПечатноеИзделие(),
                logo: new LogoRenderer());

            Assert.Empty(безЛоготипа.Изображения);
        }
    }

    private static PrintableItem ПечатноеИзделие() =>
        new()
        {
            Id = 1,
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["[edt_str_to1]"] = "ООО Ромашка",
            },
        };

    private static PrintJobContext КонтекстПечати(bool сЛоготипом, byte[]? логотип) =>
        new()
        {
            TemplateName = "Конверт C6.lbk",
            Blank = new BlankMetrics
            {
                TemplateName = "Конверт C6.lbk",
                WidthMm = 114m,
                HeightMm = 162m,
                Orientation = TemplateOrientation.Portrait,
                PaperSizeName = "C6",
            },
            Printer = new PrinterDeviceInfo
            {
                PrinterName = "Picket302 Мок-Принтер",
                PaperSizeName = "C6",
                PaperWidthMm = 114m,
                PaperHeightMm = 162m,
                PrintableWidthMm = 110m,
                PrintableHeightMm = 158m,
            },
            Orientation = TemplateOrientation.Portrait,
            PaperSizeName = "C6",
            PaperWidthMm = 114m,
            PaperHeightMm = 162m,
            WithLogo = сЛоготипом,
            LogoBytes = логотип,
            Items = new[] { ПечатноеИзделие() },
            Fields = new[]
            {
                new BlankField
                {
                    Name = "[edt_str_to1]",
                    Kind = BlankFieldKind.Text,
                    XMm = 8m,
                    YMm = 20m,
                    LengthMm = 60m,
                    HeightMm = 12m,
                    FontHeightPt = 10m,
                    FontHeightMinPt = 6m,
                    FontHeightMaxPt = 14m,
                },
                new BlankField
                {
                    Name = "logo",
                    Kind = BlankFieldKind.Picture,
                    XMm = 40m,
                    YMm = 80m,
                    LengthMm = 30m,
                    HeightMm = 15m,
                },
            },
        };

    /// <summary>Детерминированный измеритель текста (§11.2).</summary>
    private sealed class Измеритель : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) =>
            (double)fontSizePt * 1.2;
    }

    /// <summary>
    /// Фиксатор графического контекста: запоминает выведенные изображения
    /// (без реального принтера).
    /// </summary>
    private sealed class ФиксаторГрафики : IGraphicsContext
    {
        public List<(float X, float Y, float Width, float Height)> Изображения { get; } = new();

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(
            string text,
            string fontName,
            float widthMm,
            float heightMm,
            float xMm,
            float yMm)
        {
        }

        public void DrawStringMm(
            string text,
            string fontName,
            float sizePt,
            bool bold,
            float xMm,
            float yMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }


            /// <summary>
            /// Отрисовка заданным семейством: фикстура записывает обращение так же,
            /// как и вариант с именем гарнитуры.
            /// </summary>
            public void DrawStringMm(
                string text,
                FontFamily family,
                float widthMm,
                float heightMm,
                float xMm,
                float yMm)
            {
                DrawStringMm(text, family.Name, widthMm, heightMm, xMm, yMm);
            }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm) =>
            Изображения.Add((xMm, yMm, widthMm, heightMm));
    }
}
