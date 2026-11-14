using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.App.Shared;

/// <summary>
/// Локальное хранилище параметров рабочего места.
/// </summary>
/// <remarks>
/// <para>
/// В совместном режиме параметры рабочего места не хранятся в общей базе:
/// выбранный принтер, число копий и параметры печати относятся к конкретному
/// компьютеру. Хранилище лежит рядом с профилем пользователя и не покидает
/// его.
/// </para>
/// <para>
/// В одиночном режиме те же параметры хранятся в таблице <c>parameters</c>
/// общей (единственной) базы — этот путь не меняется.
/// </para>
/// </remarks>
public sealed class LocalParameterStore : IParameterRepository
{
    private const string ИмяФайла = "local-parameters.json";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _путь;
    private readonly object _блокировка = new();

    /// <summary>
    /// Создаёт локальное хранилище параметров.
    /// </summary>
    /// <param name="каталогПрофиля">Каталог профиля приложения.</param>
    public LocalParameterStore(string каталогПрофиля)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПрофиля);

        _путь = Path.Combine(каталогПрофиля, ИмяФайла);
    }

    /// <summary>Путь к файлу параметров.</summary>
    public string Путь => _путь;

    /// <inheritdoc />
    public Task<long> AddAsync(Parameter parameter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        lock (_блокировка)
        {
            var значения = Прочитать();

            parameter.Id = значения.Count == 0 ? 1 : значения.Max(з => з.Id) + 1;
            parameter.UpdatedAt = DateTime.UtcNow;

            значения.Add(parameter);

            Сохранить(значения);

            return Task.FromResult(parameter.Id);
        }
    }

    /// <inheritdoc />
    public Task<Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            var найденный = Прочитать()
                .FirstOrDefault(параметр =>
                    string.Equals(параметр.Name, name, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(найденный);
        }
    }

    /// <inheritdoc />
    public Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            var найденный = Прочитать()
                .FirstOrDefault(параметр =>
                    string.Equals(параметр.Name, name, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(найденный?.Value);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Parameter>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            IReadOnlyList<Parameter> значения = Прочитать();

            return Task.FromResult(значения);
        }
    }

    /// <inheritdoc />
    public Task<int> UpdateAsync(Parameter parameter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        lock (_блокировка)
        {
            var значения = Прочитать();

            var найденный = значения.FirstOrDefault(параметр => параметр.Id == parameter.Id);

            if (найденный is null)
            {
                return Task.FromResult(0);
            }

            найденный.Value = parameter.Value;
            найденный.UpdatedAt = DateTime.UtcNow;

            Сохранить(значения);

            return Task.FromResult(1);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            var значения = Прочитать();

            var удалено = значения.RemoveAll(параметр =>
                string.Equals(параметр.Name, name, StringComparison.OrdinalIgnoreCase));

            if (удалено > 0)
            {
                Сохранить(значения);
            }

            return Task.FromResult(удалено > 0);
        }
    }

    private List<Parameter> Прочитать()
    {
        if (!File.Exists(_путь))
        {
            return new List<Parameter>();
        }

        try
        {
            var текст = File.ReadAllText(_путь);

            return JsonSerializer.Deserialize<List<Parameter>>(текст, ПараметрыJson) ?? new List<Parameter>();
        }
        catch (JsonException)
        {
            return new List<Parameter>();
        }
        catch (IOException)
        {
            return new List<Parameter>();
        }
    }

    private void Сохранить(List<Parameter> значения)
    {
        var каталог = Path.GetDirectoryName(_путь);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        File.WriteAllText(
            _путь,
            JsonSerializer.Serialize(значения, ПараметрыJson),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

/// <summary>
/// Локальное хранилище калибровки принтеров.
/// </summary>
/// <remarks>
/// <para>
/// Калибровка относится к конкретному принтеру конкретного компьютера: в
/// общей базе она была бы чужим смещением для чужого принтера. В совместном
/// режиме хранилище лежит в профиле рабочего места.
/// </para>
/// <para>
/// При первом переходе в совместный режим калибровка переносится из таблицы
/// <c>printer_profiles</c> локальной базы в это хранилище; исходная база при
/// этом не изменяется.
/// </para>
/// </remarks>
public sealed class LocalPrinterProfileStore : IPrinterProfileRepository
{
    private const string ИмяФайла = "printer-profiles.json";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _путь;
    private readonly object _блокировка = new();

    /// <summary>
    /// Создаёт локальное хранилище калибровки.
    /// </summary>
    /// <param name="каталогПрофиля">Каталог профиля приложения.</param>
    public LocalPrinterProfileStore(string каталогПрофиля)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПрофиля);

        _путь = Path.Combine(каталогПрофиля, ИмяФайла);
    }

    /// <summary>Путь к файлу калибровки.</summary>
    public string Путь => _путь;

    /// <summary>
    /// Переносит калибровку из таблицы общей базы в локальное хранилище.
    /// </summary>
    /// <param name="профили">Профили из таблицы <c>printer_profiles</c>.</param>
    /// <returns>Число перенесённых профилей.</returns>
    public int ПеренестиИз(IEnumerable<PrinterProfile> профили)
    {
        ArgumentNullException.ThrowIfNull(профили);

        lock (_блокировка)
        {
            var существующие = Прочитать();

            var перенесено = 0;

            foreach (var профиль in профили)
            {
                if (существующие.Any(имеющийся =>
                    string.Equals(имеющийся.PrinterName, профиль.PrinterName, StringComparison.Ordinal)
                    && string.Equals(имеющийся.EnvelopeFormat, профиль.EnvelopeFormat, StringComparison.Ordinal)))
                {
                    continue;
                }

                существующие.Add(профиль);
                перенесено++;
            }

            if (перенесено > 0)
            {
                Сохранить(существующие);
            }

            return перенесено;
        }
    }

    /// <inheritdoc />
    public Task<long> AddAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_блокировка)
        {
            var профили = Прочитать();

            profile.Id = профили.Count == 0 ? 1 : профили.Max(з => з.Id) + 1;

            профили.Add(profile);

            Сохранить(профили);

            return Task.FromResult(profile.Id);
        }
    }

    /// <inheritdoc />
    public Task<PrinterProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            return Task.FromResult(Прочитать().FirstOrDefault(профиль => профиль.Id == id));
        }
    }

    /// <inheritdoc />
    public Task<PrinterProfile?> GetByPrinterAndFormatAsync(
        string printerName,
        string envelopeFormat,
        CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            var найденный = Прочитать().FirstOrDefault(профиль =>
                string.Equals(профиль.PrinterName, printerName, StringComparison.Ordinal)
                && string.Equals(профиль.EnvelopeFormat, envelopeFormat, StringComparison.Ordinal));

            return Task.FromResult(найденный);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PrinterProfile>> GetByPrinterAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            IReadOnlyList<PrinterProfile> профили = Прочитать()
                .Where(профиль =>
                    string.Equals(профиль.PrinterName, printerName, StringComparison.Ordinal))
                .ToArray();

            return Task.FromResult(профили);
        }
    }

    /// <inheritdoc />
    public Task<int> UpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_блокировка)
        {
            var профили = Прочитать();

            var найденный = профили.FirstOrDefault(запись => запись.Id == profile.Id);

            if (найденный is null)
            {
                return Task.FromResult(0);
            }

            найденный.PrinterName = profile.PrinterName;
            найденный.EnvelopeFormat = profile.EnvelopeFormat;
            найденный.OffsetXMm = profile.OffsetXMm;
            найденный.OffsetYMm = profile.OffsetYMm;
            найденный.FeedAlignment = profile.FeedAlignment;
            найденный.FixA4 = profile.FixA4;
            найденный.DuplexCapable = profile.DuplexCapable;
            найденный.IsDefault = profile.IsDefault;
            найденный.DevmodeTemplate = profile.DevmodeTemplate;

            Сохранить(профили);

            return Task.FromResult(1);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_блокировка)
        {
            var профили = Прочитать();

            var удалено = профили.RemoveAll(профиль => профиль.Id == id);

            if (удалено > 0)
            {
                Сохранить(профили);
            }

            return Task.FromResult(удалено > 0);
        }
    }

    private List<PrinterProfile> Прочитать()
    {
        if (!File.Exists(_путь))
        {
            return new List<PrinterProfile>();
        }

        try
        {
            var текст = File.ReadAllText(_путь);

            return JsonSerializer.Deserialize<List<PrinterProfile>>(текст, ПараметрыJson)
                ?? new List<PrinterProfile>();
        }
        catch (JsonException)
        {
            return new List<PrinterProfile>();
        }
        catch (IOException)
        {
            return new List<PrinterProfile>();
        }
    }

    private void Сохранить(List<PrinterProfile> профили)
    {
        var каталог = Path.GetDirectoryName(_путь);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        File.WriteAllText(
            _путь,
            JsonSerializer.Serialize(профили, ПараметрыJson),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

/// <summary>
/// Хранилище текущего отправителя в локальных параметрах рабочего места.
/// </summary>
/// <remarks>
/// Выбор отправителя относится к рабочему месту, а не к общей базе: на
/// другом компьютере он может быть другим.
/// </remarks>
public sealed class LocalCurrentSenderStore : Picket302.Domain.Services.ICurrentSenderStore
{
    /// <summary>Ключ параметра с идентификатором отправителя.</summary>
    public const string ParameterKey = "CurrentSenderId";

    private readonly IParameterRepository _параметры;

    /// <summary>
    /// Создаёт хранилище текущего отправителя.
    /// </summary>
    /// <param name="параметры">Локальное хранилище параметров.</param>
    public LocalCurrentSenderStore(IParameterRepository параметры)
    {
        ArgumentNullException.ThrowIfNull(параметры);

        _параметры = параметры;
    }

    /// <inheritdoc />
    public long? Read()
    {
        var значение = _параметры.GetValueAsync(ParameterKey).GetAwaiter().GetResult();

        return long.TryParse(значение, out var идентификатор) ? идентификатор : null;
    }

    /// <inheritdoc />
    public void Write(long? senderId)
    {
        var параметр = new Parameter
        {
            Name = ParameterKey,
            Value = senderId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UpdatedAt = DateTime.UtcNow,
        };

        var существующий = _параметры.GetByNameAsync(ParameterKey).GetAwaiter().GetResult();

        if (существующий is null)
        {
            _параметры.AddAsync(параметр).GetAwaiter().GetResult();
            return;
        }

        существующий.Value = параметр.Value;
        существующий.UpdatedAt = параметр.UpdatedAt;

        _параметры.UpdateAsync(существующий).GetAwaiter().GetResult();
    }
}
