using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing.Elements;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия F-A8: кэш QR-кода учитывает границы поля и DPI растра. Прежде
/// кэшированный растр, построенный под другие границы, выводился в размере
/// кэша; теперь код перестраивается под новые границы, кэш обновляется,
/// а пересечение с логотипом проверяется по фактическому прямоугольнику
/// вывода.
/// </summary>
public sealed class QrCacheBoundsTests
{
    [Fact]
    public async Task RenderQrAsync_ДругиеГраницы_КэшПерестраиваетсяИВыводПоПолю()
    {
        var кэш = new КэшВПамяти();
        var отрисовщик = new QrCodeRenderer(кэш);

        var первыйКонтекст = new КонтекстВывода();

        await отрисовщик.RenderQrAsync(первыйКонтекст, "101000", new RectangleF(0f, 0f, 30f, 30f));

        // Тот же payload, но другое поле: прежний растр кэша не годится.
        var второйКонтекст = new КонтекстВывода();

        var использованКэш = await отрисовщик.RenderQrAsync(
            второйКонтекст,
            "101000",
            new RectangleF(0f, 0f, 18f, 18f));

        Assert.False(использованКэш);

        var вывод = Assert.Single(второйКонтекст.Выводы);

        // Растровый вывод выполняется по границам поля, а не по размеру кэша.
        Assert.Equal(18f, вывод.Ширина, 2);
        Assert.Equal(18f, вывод.Высота, 2);

        // Кэш обновлён под новые границы: повторный вызов берёт из кэша.
        var третийКонтекст = new КонтекстВывода();

        var повтор = await отрисовщик.RenderQrAsync(
            третийКонтекст,
            "101000",
            new RectangleF(0f, 0f, 18f, 18f));

        Assert.True(повтор);
        Assert.Single(третийКонтекст.Выводы);
    }

    /// <summary>Хранилище кэша QR-кодов в памяти.</summary>
    private sealed class КэшВПамяти : IQrCacheRepository
    {
        private readonly Dictionary<string, QrCache> _записи = new(StringComparer.Ordinal);

        public IReadOnlyCollection<QrCache> Записи() => _записи.Values;

        public Task<long> AddAsync(QrCache cache, CancellationToken cancellationToken = default)
        {
            _записи[cache.PayloadHash] = cache;

            return Task.FromResult((long)_записи.Count);
        }

        public Task<QrCache?> GetByPayloadHashAsync(
            string payloadHash,
            CancellationToken cancellationToken = default)
        {
            _записи.TryGetValue(payloadHash, out var запись);

            return Task.FromResult(запись);
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            foreach (var ключ in _записи.Keys)
            {
                if (_записи[ключ].Id == id)
                {
                    _записи.Remove(ключ);

                    break;
                }
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>Контекст с фиксацией прямоугольников растрового вывода.</summary>
    private sealed class КонтекстВывода : IGraphicsContext
    {
        public List<(float X, float Y, float Ширина, float Высота)> Выводы { get; } = [];

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

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm) =>
            Выводы.Add((xMm, yMm, widthMm, heightMm));

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }
    }
}
