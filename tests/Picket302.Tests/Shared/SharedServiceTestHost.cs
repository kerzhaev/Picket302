using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Picket302.Service.Configuration;
using Picket302.Service.Data;
using Picket302.Service.Hosting;
using Picket302.Shared.Protocol;

namespace Picket302.Tests.Shared;

/// <summary>
/// Изолированный стенд серверной службы совместного режима.
/// </summary>
/// <remarks>
/// Стенд поднимает настоящую службу в процессе проверки: отдельный
/// временный каталог данных, отдельный файл базы, произвольный свободный
/// порт, собственный самоподписанный сертификат и настоящее TLS-соединение.
/// Файлы пользователя, его база и настройки не затрагиваются.
/// </remarks>
public sealed class SharedServiceTestHost : IAsyncDisposable
{
    private readonly WebApplication _приложение;
    private readonly string _каталог;

    private SharedServiceTestHost(
        WebApplication приложение,
        string каталог,
        int порт,
        X509Certificate2 сертификат,
        List<string> записи)
    {
        _приложение = приложение;
        _каталог = каталог;
        Порт = порт;
        Сертификат = сертификат;
        ЗаписиЖурнала = записи;
    }

    /// <summary>Порт службы на стенде.</summary>
    public int Порт { get; }

    /// <summary>Сертификат сервера на стенде.</summary>
    public X509Certificate2 Сертификат { get; }

    /// <summary>Записи журнала службы, собранные за время работы стенда.</summary>
    public List<string> ЗаписиЖурнала { get; }

    /// <summary>Базовый адрес службы.</summary>
    public string БазовыйАдрес => $"https://127.0.0.1:{Порт}";

    /// <summary>Каталог данных стенда.</summary>
    public string КаталогДанных => _каталог;

    /// <summary>
    /// Поднимает службу на свободном порту в изолированном каталоге.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="каталогДанных">
    /// Заранее подготовленный каталог данных; пусто — создаётся временный.
    /// Порядок подъёма как в <c>Program.Main</c>: защита каталога — до
    /// подготовки базы, до административного токена и до открытия порта.
    /// </param>
    /// <returns>Готовый стенд.</returns>
    public static async Task<SharedServiceTestHost> ПоднятьAsync(
        CancellationToken cancellationToken = default,
        string? каталогДанных = null)
    {
        var каталог = каталогДанных ?? Path.Combine(
            Path.GetTempPath(),
            "Picket302SharedTest_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(каталог);

        // Защита каталога обязательна к применению: отказ останавливает
        // подъём до подготовки базы и до открытия порта.
        if (!Picket302.Service.Security.SharedDataDirectoryProtection.Применить(каталог, _ => { }))
        {
            throw new InvalidOperationException(
                "Стенд не поднят: не удалось ограничить права на каталог данных "
                + $"({каталог}). Сеть не открывалась.");
        }

        var порт = НайтиСвободныйПорт();

        var параметры = new ServiceOptions
        {
            DataDirectory = каталог,
            Port = порт,
        };

        var база = new ServerDatabase(параметры.DatabasePath);
        await база.ПодготовитьAsync(cancellationToken).ConfigureAwait(false);

        var сертификат = SharedCertificateFactory.Создать(
            "Picket302 Shared Service",
            new[] { "localhost" },
            new[] { "127.0.0.1" });

        var записи = new List<string>();

        var приложение = SharedServiceFactory.Создать(
            параметры,
            записи.Add,
            сертификат);

        await приложение.StartAsync(cancellationToken).ConfigureAwait(false);

        return new SharedServiceTestHost(приложение, каталог, порт, сертификат, записи);
    }

    /// <summary>
    /// Создаёт клиента, сопрягает его со службой и возвращает его секрет.
    /// </summary>
    /// <param name="имя">Имя устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовый клиент.</returns>
    public async Task<SharedTestClient> СоздатьКлиентаAsync(
        string имя,
        CancellationToken cancellationToken = default)
    {
        var админТокен = new Picket302.Service.Security.SharedAdminToken(_каталог)
            .ПолучитьИлиСоздать();

        using var админский = СоздатьHttpClient();

        админский.DefaultRequestHeaders.Add("X-Picket302-Admin-Token", админТокен);

        using var ответКода = await админский.PostAsync(
            "/api/v1/admin/pairing-code",
            content: null,
            cancellationToken).ConfigureAwait(false);

        ответКода.EnsureSuccessStatusCode();

        var код = JsonSerializer.Deserialize<AdminPairingCodeResponse>(
            await ответКода.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            SharedJson.Options)!;

        var клиент = СоздатьHttpClient();

        using var ответ = await клиент.PostAsJsonAsync(
            SharedProtocol.PairRoute,
            new PairRequest(код.Code, имя),
            SharedJson.Options,
            cancellationToken).ConfigureAwait(false);

        ответ.EnsureSuccessStatusCode();

        var сопряжение = JsonSerializer.Deserialize<PairResponse>(
            await ответ.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            SharedJson.Options)!;

        return new SharedTestClient(клиент, сопряжение.DeviceId, сопряжение.Secret, имя);
    }

    /// <summary>
    /// Создаёт HTTP-клиент, доверяющий только сертификату стенда.
    /// </summary>
    /// <param name="сВерсиейПротокола">
    /// Добавить ли клиенту заголовок версии протокола по умолчанию.
    /// <see langword="false"/> — для проверок серверного требования
    /// заголовка: запрос без него обязан отклоняться кодом 426.
    /// </param>
    /// <returns>Готовый HTTP-клиент.</returns>
    public HttpClient СоздатьHttpClient(bool сВерсиейПротокола = true)
    {
        var обработчик = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, сертификат, _, _) =>
                сертификат is not null
                && string.Equals(
                    SharedFingerprint.ВычислитьОтпечаток(сертификат),
                    SharedFingerprint.ВычислитьОтпечаток(Сертификат),
                    StringComparison.OrdinalIgnoreCase),
        };

        var клиент = new HttpClient(обработчик)
        {
            BaseAddress = new Uri(БазовыйАдрес),
        };

        if (сВерсиейПротокола)
        {
            клиент.DefaultRequestHeaders.Add(
                SharedProtocol.ProtocolVersionHeader,
                SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return клиент;
    }

    /// <summary>
    /// Находит свободный порт на локальном адресе.
    /// </summary>
    /// <returns>Номер свободного порта.</returns>
    private static int НайтиСвободныйПорт()
    {
        var слушатель = new TcpListener(IPAddress.Loopback, 0);

        слушатель.Start();

        try
        {
            return ((IPEndPoint)слушатель.LocalEndpoint).Port;
        }
        finally
        {
            слушатель.Stop();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _приложение.StopAsync().ConfigureAwait(false);
        await _приложение.DisposeAsync().ConfigureAwait(false);

        Сертификат.Dispose();

        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Каталог остаётся: временные файлы проверки не критичны.
        }
    }
}

/// <summary>
/// Клиент совместного режима для проверок.
/// </summary>
public sealed class SharedTestClient : IDisposable
{
    private readonly HttpClient _клиент;

    /// <summary>
    /// Создаёт клиента поверх указанного HTTP-клиента.
    /// </summary>
    /// <param name="клиент">HTTP-клиент.</param>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="секрет">Секрет устройства.</param>
    /// <param name="имя">Имя устройства.</param>
    public SharedTestClient(HttpClient клиент, string deviceId, string секрет, string имя)
    {
        _клиент = клиент;
        Устройство = deviceId;
        Секрет = секрет;
        Имя = имя;

        _клиент.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", секрет);

        // Заголовок версии мог быть уже задан: повторное добавление дало бы
        // несколько значений, и служба не смогла бы разобрать заголовок.
        _клиент.DefaultRequestHeaders.Remove(SharedProtocol.ProtocolVersionHeader);
        _клиент.DefaultRequestHeaders.Add(
            SharedProtocol.ProtocolVersionHeader,
            SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Идентификатор устройства.</summary>
    public string Устройство { get; }

    /// <summary>Секрет устройства.</summary>
    public string Секрет { get; }

    /// <summary>Имя устройства.</summary>
    public string Имя { get; }

    /// <summary>HTTP-клиент.</summary>
    public HttpClient Клиент => _клиент;

    /// <summary>
    /// Выполняет команду службы.
    /// </summary>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="запрос">Тело запроса.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ службы.</returns>
    public async Task<HttpResponseMessage> ВыполнитьAsync<T>(
        string имяКоманды,
        T запрос,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/{имяКоманды}");

        сообщение.Headers.Add(
            SharedProtocol.OperationIdHeader,
            operationId ?? Guid.NewGuid().ToString("D"));

        сообщение.Content = new StringContent(
            JsonSerializer.Serialize(запрос, SharedJson.Options),
            System.Text.Encoding.UTF8,
            "application/json");

        return await _клиент.SendAsync(сообщение, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Выполняет команду службы и разбирает её результат.
    /// </summary>
    /// <typeparam name="TОтвет">Тип результата команды.</typeparam>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="запрос">Тело запроса.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    public async Task<TОтвет> ВыполнитьИРазобратьAsync<TЗапрос, TОтвет>(
        string имяКоманды,
        TЗапрос запрос,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        using var ответ = await ВыполнитьAsync(имяКоманды, запрос, operationId, cancellationToken)
            .ConfigureAwait(false);

        var текст = await ответ.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!ответ.IsSuccessStatusCode)
        {
            var ошибка = JsonSerializer.Deserialize<SharedError>(текст, SharedJson.Options);

            throw new SharedTestCommandException(ошибка?.Code ?? "unknown", текст, ошибка?.Message);
        }

        return JsonSerializer.Deserialize<TОтвет>(текст, SharedJson.Options)!;
    }

    /// <inheritdoc />
    public void Dispose() => _клиент.Dispose();
}

/// <summary>
/// Отказ команды совместного режима в проверке.
/// </summary>
public sealed class SharedTestCommandException : Exception
{
    /// <summary>
    /// Создаёт исключение.
    /// </summary>
    /// <param name="код">Код ошибки протокола.</param>
    /// <param name="тело">Тело ответа службы.</param>
    /// <param name="сообщение">Сообщение службы.</param>
    public SharedTestCommandException(string код, string тело, string? сообщение)
        : base($"{код}: {сообщение ?? тело}")
    {
        Код = код;
        Тело = тело;
    }

    /// <summary>Код ошибки протокола.</summary>
    public string Код { get; }

    /// <summary>Тело ответа службы.</summary>
    public string Тело { get; }
}
