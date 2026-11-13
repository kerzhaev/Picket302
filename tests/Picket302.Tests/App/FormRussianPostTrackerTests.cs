using Picket302.App.UI.Forms;
using Picket302.Infrastructure.Integration;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S34 (ТЗ §8.8, функция 110): диалог трекера Почты России — регистрация
/// партии (ШПИ/партия/дата/пункт) и запрос статусов на ТЕСТОВОМ источнике
/// (внедрённый адаптер), состояния «нет данных / ошибка / найдено». Сеть не
/// используется; COM, реестр, принтер не задействованы.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormRussianPostTrackerTests
{
    /// <summary>Тестовый источник: разбирает команду из тексте запроса.</summary>
    private static Func<string, string, CancellationToken, Task<string>> ТестовыйИсточник(
        IReadOnlyDictionary<string, string> ответы)
        => (адрес, запрос, _) =>
        {
            foreach (var (команда, ответ) in ответы)
            {
                if (запрос.Contains(команда, StringComparison.Ordinal))
                {
                    return Task.FromResult(ответ);
                }
            }

            return Task.FromResult(string.Empty);
        };

    private static RussianPostCredentials Учётка() => new("acceptance", null, "тест-токен");

    [Fact]
    public void РегистрацияПартии_ПоляЗаполнены_ТребованияШПИ()
    {
        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(new Dictionary<string, string>()),
            null);

        using var форма = new FormRussianPostTracker(трекер);

        var партия = форма.ЗарегистрироватьДляПроверокAsync(
            "ПАР-2026-01",
            new DateTime(2026, 11, 13),
            "Москва, УЦ",
            new[] { "12345678901234", "12345678901235" });

        Assert.Equal("ПАР-2026-01", партия.BatchNumber);
        Assert.Equal(2, партия.TrackingIds.Count);
        Assert.Equal(партия.BatchNumber, форма.Партия!.BatchNumber);
        Assert.Equal(TrackerQueryState.NotQueried, форма.Состояние);
    }

    [Fact]
    public async Task Найдено_СтатусыВТаблицеСВручением()
    {
        var ответы = new Dictionary<string, string>
        {
            ["PAR-FOUND-01"] = """
                {"items":[
                  {"trackingId":"12345678901234","status":"Отправлено","delivered":false},
                  {"trackingId":"12345678901235","status":"Подтверждено","delivered":true,"deliveredAt":"2026-11-12T10:00:00Z"}
                ]}
                """,
        };

        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(ответы),
            null);

        using var форма = new FormRussianPostTracker(трекер);
        форма.ЗарегистрироватьДляПроверокAsync(
            "PAR-FOUND-01",
            new DateTime(2026, 11, 10),
            "Москва",
            new[] { "12345678901234", "12345678901235" });

        var результат = await форма.ЗапроситьДляПроверокAsync();

        Assert.Equal(TrackerQueryState.Found, форма.Состояние);
        Assert.Equal(2, форма.ЧислоСтрокСтатусов);
        Assert.Equal(1, результат.DeliveredCount);
        Assert.Contains("Найдено 2", форма.ТекстСостояния);
    }

    [Fact]
    public async Task НетДанных_ПустойОтветОтСервиса()
    {
        var ответы = new Dictionary<string, string>
        {
            ["PAR-EMPTY-01"] = "{\"items\":[]}",
        };

        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(ответы),
            null);

        using var форма = new FormRussianPostTracker(трекер);
        форма.ЗарегистрироватьДляПроверокAsync(
            "PAR-EMPTY-01",
            new DateTime(2026, 11, 10),
            "Москва",
            new[] { "12345678901234" });

        var результат = await форма.ЗапроситьДляПроверокAsync();

        Assert.Equal(TrackerQueryState.NoData, форма.Состояние);
        Assert.Equal(0, форма.ЧислоСтрокСтатусов);
        Assert.Empty(результат.Statuses);
        Assert.Contains("Нет данных", форма.ТекстСостояния);
    }

    [Fact]
    public async Task ОшибкаОпроса_СостояниеОшибкиИУведомление()
    {
        var ответы = new Dictionary<string, string>
        {
            ["PAR-ERROR-01"] = "{\"error\":\"Сервис недоступен\"}",
        };

        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(ответы),
            null);

        using var форма = new FormRussianPostTracker(трекер);

        var уведомления = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => уведомления.Add(текст);

        форма.ЗарегистрироватьДляПроверокAsync(
            "PAR-ERROR-01",
            new DateTime(2026, 11, 10),
            "Москва",
            new[] { "12345678901234" });

        var результат = await форма.ЗапроситьДляПроверокAsync();

        Assert.Equal(TrackerQueryState.Error, форма.Состояние);
        Assert.True(результат.IsFailure);
        Assert.Equal(0, форма.ЧислоСтрокСтатусов);
        Assert.Contains("Ошибка опроса", Assert.Single(уведомления));
    }

    [Fact]
    public async Task ЗапросБезПартии_Отклоняется()
    {
        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(new Dictionary<string, string>()),
            null);

        using var форма = new FormRussianPostTracker(трекер);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => форма.ЗапроситьДляПроверокAsync());
    }

    [Fact]
    public void НекорректныйШПИ_ОтклоняетсяТрекером()
    {
        var трекер = new RussianPostTracker(
            "https://tracker.test/",
            Учётка(),
            ТестовыйИсточник(new Dictionary<string, string>()),
            null);

        using var форма = new FormRussianPostTracker(трекер);

        var уведомления = new List<(string, Exception?)>();
        форма.УведомитьДляПроверок = (текст, _) => уведомления.Add((текст, null));

        Assert.ThrowsAny<Exception>(() => форма.ЗарегистрироватьДляПроверокAsync(
            "PAR-BAD-ID",
            new DateTime(2026, 11, 10),
            "Москва",
            new[] { "короткий-шпи" }));
    }
}
