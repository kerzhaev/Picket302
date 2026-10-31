using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;

namespace Picket302.Infrastructure.Storage;

/// <summary>
/// Хранилище настроек отображения в файле профиля пользователя
/// (ТЗ §8.1, функция 12: «Запоминание геометрии и состава колонок»).
/// </summary>
/// <remarks>
/// Файл расположен в профиле пользователя (<c>AppData/Roaming/Picket302</c>).
/// Повреждённый конфигурационный файл автоматически заменяется дефолтным.
/// </remarks>
public sealed class JsonUiSettingsStore : IUiSettingsStore
{
    private const string КаталогПриложения = "Picket302";
    private const string ИмяФайла = "ui-settings.json";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _путьФайла;
    private readonly SemaphoreSlim _блокировка = new(1, 1);

    /// <summary>Создаёт хранилище настроек в каталоге профиля пользователя.</summary>
    public JsonUiSettingsStore()
        : this(ПутьПоУмолчанию())
    {
    }

    /// <summary>Создаёт хранилище настроек по указанному пути к файлу.</summary>
    /// <param name="путьФайла">Путь к файлу настроек.</param>
    public JsonUiSettingsStore(string путьФайла)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьФайла);
        _путьФайла = путьФайла;
    }

    /// <summary>
    /// Возвращает путь к файлу настроек по умолчанию —
    /// <c>%AppData%\Picket302\ui-settings.json</c>.
    /// </summary>
    public static string ПутьПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        КаталогПриложения,
        ИмяФайла);

    /// <inheritdoc />
    public async Task<GridConfigDto?> LoadGridAsync(string gridId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);

        var конфигурации = await ПрочитатьAsync(cancellationToken).ConfigureAwait(false);

        return конфигурации.Grids.TryGetValue(gridId, out var конфигурация)
            ? конфигурация
            : null;
    }

    /// <inheritdoc />
    public async Task SaveGridAsync(GridConfigDto config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _блокировка.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var конфигурации = await ПрочитатьAsync(cancellationToken).ConfigureAwait(false);
            конфигурации.Grids[config.GridId] = config;

            await ЗаписатьAsync(конфигурации, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _блокировка.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GridAppearance> LoadAppearanceAsync(CancellationToken cancellationToken = default)
    {
        var конфигурации = await ПрочитатьAsync(cancellationToken).ConfigureAwait(false);

        return конфигурации.Appearance ?? GridAppearance.Default;
    }

    /// <inheritdoc />
    public async Task SaveAppearanceAsync(GridAppearance appearance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        await _блокировка.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var конфигурации = await ПрочитатьAsync(cancellationToken).ConfigureAwait(false);
            конфигурации.Appearance = appearance;

            await ЗаписатьAsync(конфигурации, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _блокировка.Release();
        }
    }

    /// <summary>
    /// Читает содержимое файла настроек; повреждённый файл заменяется дефолтным.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Содержимое настроек.</returns>
    private async Task<Содержимое> ПрочитатьAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_путьФайла))
        {
            return new Содержимое();
        }

        try
        {
            var текст = await File.ReadAllTextAsync(_путьФайла, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(текст))
            {
                return new Содержимое();
            }

            return JsonSerializer.Deserialize<Содержимое>(текст, ПараметрыJson) ?? new Содержимое();
        }
        catch (JsonException)
        {
            // Повреждённый конфигурационный файл автоматически заменяется дефолтным.
            return new Содержимое();
        }
        catch (IOException)
        {
            return new Содержимое();
        }
    }

    /// <summary>
    /// Записывает содержимое настроек в файл.
    /// </summary>
    /// <param name="содержимое">Сохраняемое содержимое.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ЗаписатьAsync(Содержимое содержимое, CancellationToken cancellationToken)
    {
        var каталог = Path.GetDirectoryName(_путьФайла);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var текст = JsonSerializer.Serialize(содержимое, ПараметрыJson);

        await File.WriteAllTextAsync(_путьФайла, текст, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Содержимое файла настроек: конфигурации таблиц и оформление.
    /// </summary>
    private sealed class Содержимое
    {
        /// <summary>Конфигурации таблиц по идентификаторам.</summary>
        public Dictionary<string, GridConfigDto> Grids { get; set; } = [];

        /// <summary>Оформление таблиц.</summary>
        public GridAppearance? Appearance { get; set; }
    }
}
