using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing.Codes;
using Picket302.Printing.Elements;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка генерации QR-кода по ГОСТ Р 56042-2014 (ТЗ §6.4; §8.4, функция 48:
/// <c>Printing.Elements.QrCodeRenderer</c>).
/// </summary>
public sealed class QrCodeRendererTests
{
    /// <summary>Хранилище кэша QR-кодов в памяти.</summary>
    private sealed class КэшВПамяти : IQrCacheRepository
    {
        private readonly Dictionary<string, QrCache> _записи = new(StringComparer.Ordinal);

        public int Сохранений { get; private set; }

        public int Чтений { get; private set; }

        public IReadOnlyCollection<QrCache> Записи() => _записи.Values;

        public Task<long> AddAsync(QrCache cache, CancellationToken cancellationToken = default)
        {
            Сохранений++;
            _записи[cache.PayloadHash] = cache;
            return Task.FromResult((long)_записи.Count);
        }

        public Task<QrCache?> GetByPayloadHashAsync(
            string payloadHash,
            CancellationToken cancellationToken = default)
        {
            Чтений++;
            _записи.TryGetValue(payloadHash, out var запись);
            return Task.FromResult(запись);
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    /// <summary>Графический контекст, подменяющий растровые вызовы.</summary>
    private sealed class Контекст : IGraphicsContext
    {
        public List<Image> Изображения { get; } = new();

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

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
            Изображения.Add(image);
        }
    }

    [Fact]
    public void Encode_КороткаяСтрока_Версия1()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);

        Assert.Equal(1, код.Version);
        Assert.Equal(21, код.Size);
    }

    [Fact]
    public void Encode_ДлиннаяСтрока_ВыбираетБольшуюВерсию()
    {
        var код = QrEncoder.Encode(new string('A', 200), QrErrorCorrectionLevel.Medium);

        Assert.True(код.Version > 1);
        Assert.Equal((код.Version * 4) + 17, код.Size);
    }

    [Fact]
    public void Encode_СлишкомДлиннаяСтрока_ОшибкаГенерации()
    {
        Assert.Throws<QrPayloadTooLongException>(
            () => QrEncoder.Encode(new string('A', 5000), QrErrorCorrectionLevel.High));
    }

    [Fact]
    public void Encode_ПоисковыеШаблоны_НаСвоихМестах()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);

        foreach (var (началоX, началоY) in new[] { (0, 0), (14, 0), (0, 14) })
        {
            Assert.True(код.GetModule(началоX, началоY), "Внешняя рамка поискового шаблона должна быть тёмной.");
            Assert.False(код.GetModule(началоX + 1, началоY + 1), "Светлая рамка поискового шаблона.");
            Assert.True(код.GetModule(началоX + 3, началоY + 3), "Центр поискового шаблона должен быть тёмным.");
        }
    }

    [Fact]
    public void Encode_ВременнойШаблон_Чередуется()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);

        for (var i = 8; i <= 12; i++)
        {
            Assert.Equal(i % 2 == 0, код.GetModule(i, 6));
            Assert.Equal(i % 2 == 0, код.GetModule(6, i));
        }
    }

    [Fact]
    public void Encode_КодовыеСлова_СоответствуютНормативу()
    {
        // «01234567» в байтовом режиме, версия 1, уровень M:
        // 0100 (режим) + 00001000 (счётчик) + данные + терминатор,
        // затем заполнение кодами 0xEC и 0x11.
        var слова = QrEncoder.ПостроитьКодовыеСлова("01234567", QrErrorCorrectionLevel.Medium);

        Assert.Equal(26, слова.Length);
        Assert.Equal(0x40, слова[0]);
        Assert.Equal(0x83, слова[1]);
        Assert.Equal(0x03, слова[2]);
        Assert.Equal(0x13, слова[3]);
        Assert.Equal(0x23, слова[4]);
        Assert.Equal(0x33, слова[5]);
        Assert.Equal(0x43, слова[6]);
        Assert.Equal(0x53, слова[7]);
        Assert.Equal(0x63, слова[8]);
        Assert.Equal(0x70, слова[9]);
        Assert.Equal(0xEC, слова[10]);
        Assert.Equal(0x11, слова[11]);
        Assert.Equal(0xEC, слова[12]);
        Assert.Equal(0x11, слова[13]);
    }

    [Fact]
    public void Encode_КодовыеСлова_ЧитаютсяОбратноИзМатрицы()
    {
        const string полезнаяНагрузка = "Платёж 1234,56; адрес 101000, Москва";

        var код = QrEncoder.Encode(полезнаяНагрузка, QrErrorCorrectionLevel.Medium);
        var ожидаемые = QrEncoder.ПостроитьБлокиДанных(полезнаяНагрузка, QrErrorCorrectionLevel.Medium);
        var прочитанные = Декодер.ПрочитатьКодовыеСлова(код);

        Assert.Equal(ожидаемые.Length, прочитанные.Length);
        Assert.Equal(
            Convert.ToHexString(ожидаемые),
            Convert.ToHexString(прочитанные));
    }

    [Fact]
    public void Encode_ДанныеВосстанавливаютсяИзМатрицы()
    {
        const string полезнаяНагрузка = "Платёж 1234,56; адрес 101000, Москва";

        var код = QrEncoder.Encode(полезнаяНагрузка, QrErrorCorrectionLevel.Medium);
        var восстановленная = Декодер.ПрочитатьПолезнуюНагрузку(код);

        Assert.Equal(полезнаяНагрузка, восстановленная);
    }

    [Fact]
    public void Encode_ФорматнаяИнформация_СоответствуетМаске()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);

        Assert.Equal(QrEncoder.ФорматнаяИнформация(код.ErrorCorrection, код.MaskPattern), Декодер.ПрочитатьФорматнуюИнформацию(код));
    }

    [Fact]
    public void ПостроитьРастр_МодульВыровненПоЦелочисленнойСетке()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);
        var параметры = new QrRenderOptions { Dpi = 300 };

        using var растр = QrCodeRenderer.ПостроитьРастр(код, new RectangleF(0, 0, 30, 30), параметры);

        var модулей = код.Size + (2 * параметры.QuietZoneModules);
        Assert.Equal(0, растр.Width % модулей);
        Assert.Equal(0, растр.Height % модулей);
        Assert.Equal(300f, растр.HorizontalResolution, 0);
    }

    [Fact]
    public void ПостроитьРастр_МонохромныйБезСглаживания()
    {
        var код = QrEncoder.Encode("101000", QrErrorCorrectionLevel.Medium);

        using var растр = QrCodeRenderer.ПостроитьРастр(
            код,
            new RectangleF(0, 0, 30, 30),
            new QrRenderOptions { Dpi = 600 });

        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format1bppIndexed, растр.PixelFormat);
    }

    [Fact]
    public async Task RenderQrAsync_ПервыйВызов_СохраняетРастрВКэшПоSha256()
    {
        var кэш = new КэшВПамяти();
        var отрисовщик = new QrCodeRenderer(кэш);
        var контекст = new Контекст();

        var использованКэш = await отрисовщик.RenderQrAsync(контекст, "101000", new RectangleF(0, 0, 30, 30));

        Assert.False(использованКэш);
        Assert.Equal(1, кэш.Сохранений);
        Assert.Equal(
            QrCache.ComputePayloadHash("101000"),
            Assert.Single(кэш.Записи()).PayloadHash);
        Assert.Single(контекст.Изображения);
    }

    [Fact]
    public async Task RenderQrAsync_ПовторныйВызов_ИспользуетКэш()
    {
        var кэш = new КэшВПамяти();
        var отрисовщик = new QrCodeRenderer(кэш);

        await отрисовщик.RenderQrAsync(new Контекст(), "101000", new RectangleF(0, 0, 30, 30));
        var использованКэш = await отрисовщик.RenderQrAsync(
            new Контекст(),
            "101000",
            new RectangleF(0, 0, 30, 30));

        Assert.True(использованКэш);
        Assert.Equal(1, кэш.Сохранений);
        Assert.Equal(2, кэш.Чтений);
    }

    [Fact]
    public async Task RenderQrAsync_РазныеДанные_РазныеКлючиКэша()
    {
        var кэш = new КэшВПамяти();
        var отрисовщик = new QrCodeRenderer(кэш);

        await отрисовщик.RenderQrAsync(new Контекст(), "101000", new RectangleF(0, 0, 30, 30));
        await отрисовщик.RenderQrAsync(new Контекст(), "101001", new RectangleF(0, 0, 30, 30));

        Assert.Equal(2, кэш.Сохранений);
        Assert.Equal(2, кэш.Записи().Count);
    }

    [Fact]
    public void RenderQr_ПересечениеСЛоготипом_Ошибка()
    {
        var контекст = new Контекст();

        Assert.Throws<QrRenderException>(
            () => new QrCodeRenderer().RenderQr(
                контекст,
                "101000",
                new RectangleF(0, 0, 30, 30),
                new RectangleF(10, 10, 10, 10)));
    }

    [Fact]
    public void RenderQr_БезПересеченияСЛоготипом_КодВыводится()
    {
        var контекст = new Контекст();

        new QrCodeRenderer().RenderQr(
            контекст,
            "101000",
            new RectangleF(0, 0, 30, 30),
            new RectangleF(100, 100, 10, 10));

        Assert.Single(контекст.Изображения);
    }
}
