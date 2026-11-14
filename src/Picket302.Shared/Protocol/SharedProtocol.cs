namespace Picket302.Shared.Protocol;

/// <summary>
/// Параметры сетевого протокола совместного режима: версия, адреса и пределы.
/// </summary>
/// <remarks>
/// <para>
/// Протокол версионируется целым числом. Клиент объявляет версию в заголовке
/// <c>X-Picket302-Protocol-Version</c>; служба отвечает той же версией. Если
/// версия клиента вне поддерживаемого диапазона, запрос отклоняется с кодом
/// <see cref="SharedErrorCodes.VersionIncompatible"/> — данные при этом не
/// изменяются.
/// </para>
/// <para>
/// Пределы приняты с запасом к реальным размерам данных программы и
/// ограничивают то, что клиент может прислать службе.
/// </para>
/// </remarks>
public static class SharedProtocol
{
    /// <summary>Текущая версия протокола.</summary>
    public const int Version = 1;

    /// <summary>Минимальная поддерживаемая версия протокола.</summary>
    public const int MinimumSupportedVersion = 1;

    /// <summary>Заголовок с объявленной версией протокола.</summary>
    public const string ProtocolVersionHeader = "X-Picket302-Protocol-Version";

    /// <summary>Заголовок с идентификатором операции (для идемпотентности).</summary>
    public const string OperationIdHeader = "X-Picket302-Operation-Id";

    /// <summary>Заголовок с идентификатором устройства-клиента.</summary>
    public const string DeviceIdHeader = "X-Picket302-Device-Id";

    /// <summary>Префикс маршрутов API совместного режима.</summary>
    public const string RoutePrefix = "/api/v1";

    /// <summary>Маршрут проверки состояния службы.</summary>
    public const string HealthRoute = RoutePrefix + "/health";

    /// <summary>Маршрут сведений о сервере (идентичность и отпечаток).</summary>
    public const string ServerInfoRoute = RoutePrefix + "/server-info";

    /// <summary>Маршрут сопряжения клиентского устройства.</summary>
    public const string PairRoute = RoutePrefix + "/pair";

    /// <summary>Маршрут выполнения команды.</summary>
    public const string CommandRoute = RoutePrefix + "/commands/{command}";

    /// <summary>Маршрут перечисления пакетов шаблонов.</summary>
    public const string TemplatePackagesRoute = RoutePrefix + "/template-packages";

    /// <summary>Маршрут загрузки содержимого пакета шаблона.</summary>
    public const string TemplatePackageContentRoute = RoutePrefix + "/template-packages/{packageId}/content";

    /// <summary>Порт службы по умолчанию (не совпадает с портом PostgreSQL).</summary>
    public const int DefaultPort = 30242;

    /// <summary>
    /// Предельный размер тела обычного запроса (команда или чтение) в байтах.
    /// </summary>
    public const int MaxRequestBodyBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Предельный размер пакета шаблона в байтах.
    /// </summary>
    /// <remarks>
    /// Загрузка пакета шаблона идёт отдельным маршрутом и имеет собственный
    /// предел: тело пакета не является командой протокола, поэтому общий
    /// предел тела запроса на него не распространяется.
    /// </remarks>
    public const int MaxTemplatePackageBytes = 32 * 1024 * 1024;

    /// <summary>Предельное число распакованных байт в пакете шаблона.</summary>
    public const long MaxTemplatePackageExpandedBytes = 128L * 1024 * 1024;

    /// <summary>Предельное число записей в пакете шаблона.</summary>
    public const int MaxTemplatePackageEntries = 512;

    /// <summary>Предельное число элементов в одной странице выборки.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Предельное число идентификаторов в одном запросе.</summary>
    public const int MaxIdListLength = 5000;

    /// <summary>
    /// Проверяет, поддерживается ли версия протокола клиента.
    /// </summary>
    /// <param name="version">Объявленная клиентом версия.</param>
    /// <returns><see langword="true"/>, если версия в поддерживаемом диапазоне.</returns>
    public static bool IsSupported(int version)
        => version >= MinimumSupportedVersion && version <= Version;
}
