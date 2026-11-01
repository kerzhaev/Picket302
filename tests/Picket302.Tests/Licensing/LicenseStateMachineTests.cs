using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Licensing;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// Проверка автомата состояний лицензии (ТЗ §8.10, функция 119: «Перевод
/// системной даты за границу срока переводит приложение в режим истечения без
/// потери данных; уменьшение лимита не удаляет существующие записи»).
/// </summary>
public sealed class LicenseStateMachineTests
{
    private static readonly DateTime НачалоОтсчёта = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ПробныйПериод_ЗавершённыйСрок_ПереводитВСостояниеИстечения()
    {
        using var среда = new Среда();

        Assert.Equal(LicenseState.Trial, среда.Автомат.Status.State);

        среда.Время = среда.Начало.AddDays(11);

        var состояние = среда.Автомат.EvaluateState();

        Assert.Equal(LicenseState.Expired, состояние);
        Assert.Equal(0, среда.Автомат.DaysRemaining);
        Assert.False(среда.Автомат.CanCreate);
        Assert.False(среда.Автомат.CanPrintBatches);
    }

    [Fact]
    public void NagСообщение_СодержитЧислоОставшихсяДней()
    {
        using var среда = new Среда();

        среда.Время = среда.Начало.AddDays(3);
        среда.Автомат.EvaluateState();

        Assert.Equal(7, среда.Автомат.DaysRemaining);

        var сообщение = среда.Автомат.Status.BuildNagMessage();

        Assert.Contains("Осталось дней: 7", сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void NagСообщение_ИстёкшийСрок_СодержитТекстОЗакончился()
    {
        using var среда = new Среда();

        среда.Время = среда.Начало.AddDays(11);
        среда.Автомат.EvaluateState();

        var сообщение = среда.Автомат.Status.BuildNagMessage();

        Assert.Contains("Пробный период закончился", сообщение, StringComparison.Ordinal);
        Assert.Contains("Осталось дней: 0", сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void СнятиеКлюча_ВозвращаетПробныйРежим_ДанныеСохраняются()
    {
        using var среда = new Среда();

        Активировать(среда);

        Assert.Equal(LicenseState.Licensed, среда.Автомат.Status.State);

        var файлДанных = Path.Combine(среда.Каталог, "picket302.db");
        File.WriteAllText(файлДанных, "записи адресной книги", Encoding.UTF8);

        среда.Менеджер.RemoveKey();

        Assert.Equal(LicenseState.Trial, среда.Автомат.Status.State);
        Assert.True(File.Exists(файлДанных));
        Assert.Equal("записи адресной книги", File.ReadAllText(файлДанных, Encoding.UTF8));
        Assert.False(File.Exists(Path.Combine(среда.Каталог, LicenseStore.ИмяФайлаЛицензии)));
    }

    [Fact]
    public void ОфлайнАктивация_ПодписанныйОтвет_ПереводитВЛицензию()
    {
        using var среда = new Среда();

        var пара = LicenseSignature.GenerateKeyPair();
        var активация = СоздатьАктивацию(среда, пара.ОткрытыйКлюч);

        активация.Ключ = "ТЕСТ-КЛЮЧ-0001";

        var путьЗапроса = активация.BuildOfflineRequest();

        Assert.StartsWith(
            ActivationService.ПрефиксИмениФайлаЗапроса,
            Path.GetFileName(путьЗапроса),
            StringComparison.Ordinal);

        Assert.True(File.Exists(путьЗапроса));

        using (var документ = JsonDocument.Parse(File.ReadAllText(путьЗапроса, Encoding.UTF8)))
        {
            var корень = документ.RootElement;

            Assert.Equal("ТЕСТ-КЛЮЧ-0001", корень.GetProperty("key").GetString());
            Assert.False(string.IsNullOrWhiteSpace(корень.GetProperty("machineId").GetString()));
            Assert.Equal("Composite", корень.GetProperty("machineIdVariant").GetString());
            Assert.True(корень.TryGetProperty("createdAtUtc", out _));
        }

        var путьОтвета = Path.Combine(среда.Каталог, "ответ-активации.json");
        File.WriteAllText(путьОтвета, СформироватьОтвет(среда, пара), Encoding.UTF8);

        var результат = активация.ApplyOfflineResponse(путьОтвета);

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Equal(LicenseState.Licensed, среда.Автомат.Status.State);
        Assert.Equal(Edition.Home, среда.Автомат.Status.Edition);
        Assert.Equal("ООО Проверка", среда.Автомат.Status.Owner);
        Assert.True(среда.Автомат.CanCreate);
        Assert.True(среда.Автомат.CanPrintBatches);
    }

    [Fact]
    public void ОфлайнАктивация_ИспорченнаяПодпись_Отклоняется()
    {
        using var среда = new Среда();

        var пара = LicenseSignature.GenerateKeyPair();
        var активация = СоздатьАктивацию(среда, пара.ОткрытыйКлюч);

        var лицензия = СформироватьЛицензию(среда);
        var данные = Сериализовать(лицензия);

        var чужаяПодпись = new LicenseSignature().Sign(данные, пара.ЗакрытыйКлюч);
        чужаяПодпись[0] ^= 0xFF;

        var путьОтвета = Path.Combine(среда.Каталог, "ответ-активации.json");
        File.WriteAllText(
            путьОтвета,
            ActivationService.BuildResponseEnvelope(данные, чужаяПодпись),
            Encoding.UTF8);

        var результат = активация.ApplyOfflineResponse(путьОтвета);

        Assert.False(результат.IsSuccess);
        Assert.Contains("подпись", результат.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LicenseState.Trial, среда.Автомат.Status.State);
    }

    [Fact]
    public void ОнлайнАктивация_ОтветСПодписью_ПереводитВЛицензию()
    {
        using var среда = new Среда();

        var пара = LicenseSignature.GenerateKeyPair();
        var ответ = СформироватьОтвет(среда, пара);

        var активация = new ActivationService(
            среда.Хранилище,
            среда.Автомат,
            отправитьЗапрос: (_, _, _) => Task.FromResult(ответ),
            адресСервера: "https://activation.invalid/activate",
            каталогЗапросов: среда.Каталог,
            открытыйКлючПоставщика: пара.ОткрытыйКлюч);

        var результат = активация.ActivateOnline("КЛЮЧ-ОНЛАЙН");

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Equal(LicenseState.Licensed, среда.Автомат.Status.State);
    }

    [Fact]
    public void ОнлайнАктивация_СерверНедоступен_ПредлагаетОфлайнПуть()
    {
        using var среда = new Среда();

        var пара = LicenseSignature.GenerateKeyPair();

        var активация = new ActivationService(
            среда.Хранилище,
            среда.Автомат,
            отправитьЗапрос: (_, _, _) => throw new HttpRequestException("соединение отклонено"),
            адресСервера: "https://activation.invalid/activate",
            каталогЗапросов: среда.Каталог,
            открытыйКлючПоставщика: пара.ОткрытыйКлюч);

        var результат = активация.ActivateOnline("КЛЮЧ-ОНЛАЙН");

        Assert.False(результат.IsSuccess);
        Assert.Contains("офлайн", результат.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ПовреждённыеДанныеЛицензии_СчитаютсяОтсутствующими()
    {
        using var среда = new Среда();

        Активировать(среда);

        Assert.Equal(LicenseState.Licensed, среда.Автомат.Status.State);

        File.WriteAllBytes(Path.Combine(среда.Каталог, LicenseStore.ИмяФайлаЛицензии), new byte[] { 1, 2, 3, 4 });

        Assert.Equal(LicenseState.Trial, среда.Автомат.EvaluateState());
    }

    [Fact]
    public void УменьшениеЛимита_НеУдаляетСуществующиеЗаписи()
    {
        var счётчик = new Func<СнимокИспользования>(() => new СнимокИспользования(600, 150));

        using var среда = new Среда(счётчик);

        var файлДанных = Path.Combine(среда.Каталог, "picket302.db");
        File.WriteAllText(файлДанных, "записи контрагентов", Encoding.UTF8);

        var состояние = среда.Автомат.EvaluateState();

        Assert.Equal(LicenseState.Expired, состояние);
        Assert.False(среда.Автомат.CanCreate);
        Assert.False(среда.Автомат.CanPrintBatches);
        Assert.True(File.Exists(файлДанных));
        Assert.Equal("записи контрагентов", File.ReadAllText(файлДанных, Encoding.UTF8));
    }

    [Fact]
    public void IsVolumeLimitReached_СравниваетОбъёмСЛимитамиПробногоПериода()
    {
        using var среда = new Среда();

        Assert.False(среда.Автомат.IsVolumeLimitReached(10, 10));
        Assert.True(среда.Автомат.IsVolumeLimitReached(500, 0));
        Assert.True(среда.Автомат.IsVolumeLimitReached(0, 100));
    }

    [Fact]
    public void AddBinding_НесколькоУправляющихМашин_РегистрируетПривязки()
    {
        using var среда = new Среда();

        Активировать(среда);

        Assert.True(среда.Менеджер.AddBinding("машина-Б"));
        Assert.True(среда.Менеджер.AddBinding("машина-В"));
        Assert.False(среда.Менеджер.AddBinding("машина-Г"));

        var привязки = среда.Хранилище.Bindings;

        Assert.Equal(3, привязки.Count);
    }

    [Fact]
    public void BuildOfflineRequestFileName_СтрогийФорматИмени()
    {
        var uuid = Guid.NewGuid();

        var имя = ActivationService.BuildOfflineRequestFileName(uuid, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), 2, 5);

        Assert.Equal("На_регистрацию_ПК!" + uuid.ToString() + "_20260301-2-5", имя);
    }

    /// <summary>
    /// Создаёт службу активации с открытым ключом поставщика и файловым
    /// каталогом запросов.
    /// </summary>
    /// <param name="среда">Проверочное окружение.</param>
    /// <param name="открытыйКлюч">Открытый ключ поставщика.</param>
    /// <returns>Служба активации.</returns>
    private static ActivationService СоздатьАктивацию(Среда среда, byte[] открытыйКлюч)
        => new(
            среда.Хранилище,
            среда.Автомат,
            отправитьЗапрос: null,
            адресСервера: null,
            каталогЗапросов: среда.Каталог,
            открытыйКлючПоставщика: открытыйКлюч);

    /// <summary>
    /// Формирует подписанный ответ активации.
    /// </summary>
    /// <param name="среда">Проверочное окружение.</param>
    /// <param name="пара">Пара ключей поставщика.</param>
    /// <returns>Содержимое ответного файла.</returns>
    private static string СформироватьОтвет(Среда среда, LicenseKeyPair пара)
    {
        var данные = Сериализовать(СформироватьЛицензию(среда));
        var подпись = new LicenseSignature().Sign(данные, пара.ЗакрытыйКлюч);

        return ActivationService.BuildResponseEnvelope(данные, подпись);
    }

    /// <summary>
    /// Формирует сведения о лицензии для проверки.
    /// </summary>
    /// <param name="среда">Проверочное окружение.</param>
    /// <returns>Сведения о лицензии.</returns>
    private static LicensePayload СформироватьЛицензию(Среда среда) => new(
        Edition.Home,
        среда.Начало.AddYears(1),
        "ООО Проверка",
        среда.Хранилище.MachineId,
        [],
        среда.Начало,
        [LicenseFeature.BasicPrinting, LicenseFeature.AllEnvelopeFormats],
        3);

    /// <summary>
    /// Сериализует сведения о лицензии в формате ответа активации.
    /// </summary>
    /// <param name="лицензия">Сведения о лицензии.</param>
    /// <returns>Подписанные данные ответа.</returns>
    private static byte[] Сериализовать(LicensePayload лицензия) => JsonSerializer.SerializeToUtf8Bytes(
        лицензия,
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        });

    /// <summary>
    /// Активирует лицензию офлайн-цепочкой: запрос — подписанный ответ.
    /// </summary>
    /// <param name="среда">Проверочное окружение.</param>
    private static void Активировать(Среда среда)
    {
        var пара = LicenseSignature.GenerateKeyPair();
        var активация = СоздатьАктивацию(среда, пара.ОткрытыйКлюч);

        активация.Ключ = "ТЕСТ-КЛЮЧ-0001";

        var путьОтвета = Path.Combine(среда.Каталог, "ответ-активации.json");
        File.WriteAllText(путьОтвета, СформироватьОтвет(среда, пара), Encoding.UTF8);

        var результат = активация.ApplyOfflineResponse(путьОтвета);

        Assert.True(результат.IsSuccess, результат.Error);
    }

    /// <summary>
    /// Проверочное окружение: временный каталог, файловое хранилище значений
    /// и управляемые часы.
    /// </summary>
    private sealed class Среда : IDisposable
    {
        public Среда(Func<СнимокИспользования>? счётчики = null)
        {
            Каталог = Path.Combine(Path.GetTempPath(), "picket302_лицензия_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Каталог);

            Реестр = LicensingRegistry.File(Каталог);
            Машина = new MachineIdentity(журнал: null, реестр: Реестр);

            Часы = () => Время;

            Хранилище = new LicenseStore(
                Path.Combine(Каталог, LicenseStore.ИмяФайлаЛицензии),
                Реестр,
                Машина);

            Автомат = new LicenseStateMachine(Хранилище, Реестр, Часы, счётчики);
            Менеджер = new LicenseManager(Хранилище, Автомат);
        }

        public string Каталог { get; }

        public DateTime Начало => НачалоОтсчёта;

        public DateTime Время { get; set; } = НачалоОтсчёта;

        public LicensingRegistry Реестр { get; }

        public MachineIdentity Машина { get; }

        public LicenseStore Хранилище { get; }

        public LicenseStateMachine Автомат { get; }

        public LicenseManager Менеджер { get; }

        private Func<DateTime> Часы { get; }

        public void Dispose()
        {
            if (Directory.Exists(Каталог))
            {
                Directory.Delete(Каталог, recursive: true);
            }
        }
    }
}
