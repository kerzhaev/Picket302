using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Licensing;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// Ворота лицензии: проверка ограничений пробного периода при создании
/// записей и печати партий (ТЗ §8.10, функция 119).
/// </summary>
public sealed class LicenseGateTests
{
    private static readonly DateTime НачалоОтсчёта = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ПробныйПериод_ОперацииРазрешены_СОстаткомДней()
    {
        using var среда = new Среда();

        среда.Время = среда.Начало.AddDays(3);

        var решение = среда.Ворота.Проверить(LicenseOperation.СозданиеЗаписей);

        Assert.True(решение.Разрешено);
        Assert.Equal(LicenseState.Trial, решение.Состояние);
        Assert.Equal(7, решение.ОсталосьДней);
        Assert.Equal(string.Empty, решение.Сообщение);
    }

    [Fact]
    public void ИстёкшийСрок_СозданиеЗаписей_ЗапрещеноСОстаткомДней()
    {
        using var среда = new Среда();

        среда.Время = среда.Начало.AddDays(11);

        var решение = среда.Ворота.Проверить(LicenseOperation.СозданиеЗаписей);

        Assert.False(решение.Разрешено);
        Assert.Equal(LicenseState.Expired, решение.Состояние);
        Assert.Equal(0, решение.ОсталосьДней);
        Assert.Contains("Пробный период закончился", решение.Сообщение, StringComparison.Ordinal);
        Assert.Contains("Осталось дней: 0", решение.Сообщение, StringComparison.Ordinal);
        Assert.Contains("Создание записей", решение.Сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void ИстёкшийСрок_ПечатьПартий_Запрещено()
    {
        using var среда = new Среда();

        среда.Время = среда.Начало.AddDays(11);

        var решение = среда.Ворота.Проверить(LicenseOperation.ПечатьПартий);

        Assert.False(решение.Разрешено);
        Assert.Contains("Печать партий", решение.Сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void ИсчерпанныйЛимитОбъёма_СообщениеСодержитДостигнутыеЗначения()
    {
        using var среда = new Среда(() => new СнимокИспользования(500, 12));

        var решение = среда.Ворота.Проверить(LicenseOperation.СозданиеЗаписей);

        Assert.False(решение.Разрешено);
        Assert.Contains("Исчерпан лимит пробного периода", решение.Сообщение, StringComparison.Ordinal);
        Assert.Contains("контрагентов 500 из 500", решение.Сообщение, StringComparison.Ordinal);
        Assert.Contains("документов 12 из 100", решение.Сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void ЛимитОбъёмаНеДостигнут_ОперацииРазрешены()
    {
        using var среда = new Среда(() => new СнимокИспользования(499, 99));

        var решение = среда.Ворота.Проверить(LicenseOperation.ПечатьПартий);

        Assert.True(решение.Разрешено);
    }

    [Fact]
    public void Лицензия_ОперацииРазрешены_БезОграничений()
    {
        using var среда = new Среда();

        // Активация лицензии снимает ограничения пробного периода.
        среда.Активировать();

        var решение = среда.Ворота.Проверить(LicenseOperation.СозданиеЗаписей);

        Assert.True(решение.Разрешено);
        Assert.Equal(LicenseState.Licensed, решение.Состояние);
    }

    /// <summary>
    /// Проверочное окружение: временный каталог, файловое хранилище лицензии
    /// и управляемые часы.
    /// </summary>
    private sealed class Среда : IDisposable
    {
        public Среда(Func<СнимокИспользования>? счётчики = null)
        {
            Каталог = Path.Combine(Path.GetTempPath(), "picket302_ворота_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Каталог);

            Реестр = LicensingRegistry.File(Каталог);
            Машина = new MachineIdentity(журнал: null, реестр: Реестр);

            Часы = () => Время;

            Хранилище = new LicenseStore(
                Path.Combine(Каталог, LicenseStore.ИмяФайлаЛицензии),
                Реестр,
                Машина);

            Автомат = new LicenseStateMachine(Хранилище, Реестр, Часы, счётчики);
            Ворота = new LicenseStateMachineGate(Автомат);
        }

        public string Каталог { get; }

        public DateTime Начало => НачалоОтсчёта;

        public DateTime Время { get; set; } = НачалоОтсчёта;

        public LicensingRegistry Реестр { get; }

        public MachineIdentity Машина { get; }

        public LicenseStore Хранилище { get; }

        public LicenseStateMachine Автомат { get; }

        public LicenseStateMachineGate Ворота { get; }

        private Func<DateTime> Часы { get; }

        /// <summary>
        /// Активирует лицензию офлайн-цепочкой: запрос — подписанный ответ.
        /// </summary>
        public void Активировать()
        {
            var пара = LicenseSignature.GenerateKeyPair();
            var активация = new ActivationService(
                Хранилище,
                Автомат,
                null,
                null,
                Каталог,
                пара.ОткрытыйКлюч);

            активация.Ключ = "ТЕСТ-КЛЮЧ-0001";

            var лицензия = new LicensePayload(
                Edition.Home,
                Начало.AddYears(1),
                "ООО Проверка",
                Хранилище.MachineId,
                [],
                Начало,
                [LicenseFeature.BasicPrinting, LicenseFeature.AllEnvelopeFormats],
                3);

            var данные = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                лицензия,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });

            var подпись = пара.ЗакрытыйКлюч is null
                ? Array.Empty<byte>()
                : new LicenseSignature().Sign(данные, пара.ЗакрытыйКлюч);
            var путьОтвета = Path.Combine(Каталог, "ответ-активации.json");
            File.WriteAllText(путьОтвета, ActivationService.BuildResponseEnvelope(данные, подпись), System.Text.Encoding.UTF8);

            var результат = активация.ApplyOfflineResponse(путьОтвета);

            Assert.True(результат.IsSuccess, результат.Error);
        }

        public void Dispose()
        {
            if (Directory.Exists(Каталог))
            {
                Directory.Delete(Каталог, recursive: true);
            }
        }
    }
}
