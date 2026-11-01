using System.Globalization;
using Microsoft.Win32;

namespace Picket302.ComAutomation;

/// <summary>
/// Снимок настроек печати конверта, прочитанных из реестра (ТЗ §9.2).
/// </summary>
/// <param name="FromName">Наименование отправителя.</param>
/// <param name="FromAddr">Адрес отправителя.</param>
/// <param name="FromIndex">Почтовый индекс отправителя.</param>
/// <param name="ToName">Наименование получателя.</param>
/// <param name="ToAddr">Адрес получателя.</param>
/// <param name="ToIndex">Почтовый индекс получателя.</param>
public sealed record PrintEnvelopeSettingsSnapshot(
    string FromName,
    string FromAddr,
    string FromIndex,
    string ToName,
    string ToAddr,
    string ToIndex);

/// <summary>
/// Отказоустойчивое чтение и запись значений реестра: отсутствующий параметр
/// читается как пустая строка и не порождает исключения (ТЗ §9.2).
/// </summary>
public sealed class RegistryValueStore
{
    private readonly string _keyPath;

    /// <summary>
    /// Создаёт хранилище значений реестра.
    /// </summary>
    /// <param name="keyPath">Путь к ключу реестра относительно HKCU.</param>
    public RegistryValueStore(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        _keyPath = keyPath;
    }

    /// <summary>Путь к ключу реестра относительно HKCU.</summary>
    public string KeyPath => _keyPath;

    /// <summary>
    /// Читает строковое значение; отсутствующее значение читается как пустая строка.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Значение параметра либо пустая строка.</returns>
    public string Read(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);

            if (key is null)
            {
                return string.Empty;
            }

            var value = key.GetValue(name);

            if (value is null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception)
        {
            // Отсутствие или запрет доступа к ветке реестра равнозначны
            // отсутствующему параметру: возвращается пустое значение.
            return string.Empty;
        }
    }

    /// <summary>
    /// Читает логическое значение; отсутствующее значение читается как
    /// значение по умолчанию.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="defaultValue">Значение по умолчанию.</param>
    /// <returns>Значение параметра.</returns>
    public bool ReadBool(string name, bool defaultValue)
    {
        var значение = Read(name);

        if (string.IsNullOrWhiteSpace(значение))
        {
            return defaultValue;
        }

        if (bool.TryParse(значение, out var разобрано))
        {
            return разобрано;
        }

        return int.TryParse(значение, NumberStyles.Integer, CultureInfo.InvariantCulture, out var число)
            && число != 0;
    }

    /// <summary>
    /// Записывает строковое значение, создавая ветку реестра при необходимости.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение параметра.</param>
    public void Write(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);

        key?.SetValue(name, value ?? string.Empty, RegistryValueKind.String);
    }

    /// <summary>
    /// Записывает логическое значение как DWORD.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение параметра.</param>
    public void WriteBool(string name, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);

        key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Настройки печати конверта в реестре: <c>HKCU\Software\Picket302\PrintEnvelope</c>
/// с параметрами <c>FromName</c>, <c>FromAddr</c>, <c>FromIndex</c>, <c>ToName</c>,
/// <c>ToAddr</c>, <c>ToIndex</c> (ТЗ §9.2).
/// </summary>
/// <remarks>
/// <para>
/// Наименование ветки продукта «Picket302» — допущение: ТЗ записывает путь как
/// <c>HKCU\Software\&lt;ключ&gt;\PrintEnvelope</c>. Путь ветки задаётся
/// конструктором, поэтому в тестах и в сценариях развёртывания его можно
/// переопределить без изменения кода.
/// </para>
/// <para>
/// Отсутствующий параметр читается как пустая строка и не порождает
/// исключения — поведение, требуемое ТЗ §9.2 для отказоустойчивого чтения
/// настроек печати.
/// </para>
/// </remarks>
public sealed class PrintEnvelopeSettings
{
    /// <summary>Путь к ветке продукта в реестре по умолчанию.</summary>
    public const string DefaultProductKeyPath = @"Software\Picket302";

    /// <summary>Имя ветки настроек печати.</summary>
    public const string PrintEnvelopeKeyName = "PrintEnvelope";

    /// <summary>Имя параметра «Наименование отправителя».</summary>
    public const string FromNameValue = "FromName";

    /// <summary>Имя параметра «Адрес отправителя».</summary>
    public const string FromAddrValue = "FromAddr";

    /// <summary>Имя параметра «Индекс отправителя».</summary>
    public const string FromIndexValue = "FromIndex";

    /// <summary>Имя параметра «Наименование получателя».</summary>
    public const string ToNameValue = "ToName";

    /// <summary>Имя параметра «Адрес получателя».</summary>
    public const string ToAddrValue = "ToAddr";

    /// <summary>Имя параметра «Индекс получателя».</summary>
    public const string ToIndexValue = "ToIndex";

    private readonly RegistryValueStore _store;

    /// <summary>
    /// Создаёт настройки печати.
    /// </summary>
    /// <param name="productKeyPath">
    /// Путь к ветке продукта относительно HKCU; по умолчанию —
    /// <c>Software\Picket302</c>.
    /// </param>
    public PrintEnvelopeSettings(string? productKeyPath = null)
    {
        var ветка = string.IsNullOrWhiteSpace(productKeyPath)
            ? DefaultProductKeyPath
            : productKeyPath;

        _store = new RegistryValueStore( ветка.TrimEnd('\\') + "\\" + PrintEnvelopeKeyName);
    }

    /// <summary>Путь к ветке настроек печати относительно HKCU.</summary>
    public string KeyPath => _store.KeyPath;

    /// <summary>
    /// Читает все настройки печати; отсутствующие параметры читаются как пустые строки.
    /// </summary>
    /// <returns>Снимок настроек печати.</returns>
    public PrintEnvelopeSettingsSnapshot Read() => new(
        _store.Read(FromNameValue),
        _store.Read(FromAddrValue),
        _store.Read(FromIndexValue),
        _store.Read(ToNameValue),
        _store.Read(ToAddrValue),
        _store.Read(ToIndexValue));

    /// <summary>
    /// Читает отдельный параметр настроек печати.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Значение параметра либо пустая строка.</returns>
    public string ReadValue(string name) => _store.Read(name);

    /// <summary>
    /// Записывает отдельный параметр настроек печати.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение параметра.</param>
    public void WriteValue(string name, string value) => _store.Write(name, value);

    /// <summary>
    /// Записывает снимок настроек печати.
    /// </summary>
    /// <param name="settings">Снимок настроек печати.</param>
    public void Write(PrintEnvelopeSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _store.Write(FromNameValue, settings.FromName);
        _store.Write(FromAddrValue, settings.FromAddr);
        _store.Write(FromIndexValue, settings.FromIndex);
        _store.Write(ToNameValue, settings.ToName);
        _store.Write(ToAddrValue, settings.ToAddr);
        _store.Write(ToIndexValue, settings.ToIndex);
    }
}

/// <summary>
/// Флаги автоматизации, сохраняемые между запусками сервера (ТЗ §9.1,
/// методы 14–17).
/// </summary>
/// <remarks>
/// <para>
/// Флаги хранятся в ветке <c>HKCU\Software\Picket302\Automation</c> — рядом с
/// настройками печати, которые ТЗ также предписывает читать из реестра.
/// Хранение в реестре выбрано потому, что флаги читаются при «слепом запуске»
/// (ТЗ §9.2), когда база данных не загружена, и должны переживать перезапуск
/// процесса сервера.
/// </para>
/// <para>
/// Допущение об отклонении: абстракция <c>IUiSettingsStore</c> (доменный слой)
/// хранит только геометрию таблиц и оформление и не имеет общего канала
/// «ключ — значение», поэтому флаги автоматизации сохраняются в реестре, а не
/// в файле настроек интерфейса.
/// </para>
/// </remarks>
public sealed class AutomationFlagSettings
{
    /// <summary>Имя ветки флагов автоматизации.</summary>
    public const string AutomationKeyName = "Automation";

    /// <summary>Имя параметра «Модальный показ окон».</summary>
    public const string ShowFormsAsModalValue = "ShowFormsAsModal";

    /// <summary>Имя параметра «Выкусывать индекс из строки адреса».</summary>
    public const string ParseZipIndexFromAddressValue = "ParseZipIndexFromAddress";

    private readonly RegistryValueStore _store;

    /// <summary>
    /// Создаёт хранилище флагов автоматизации.
    /// </summary>
    /// <param name="productKeyPath">
    /// Путь к ветке продукта относительно HKCU; по умолчанию —
    /// <c>Software\Picket302</c>.
    /// </param>
    public AutomationFlagSettings(string? productKeyPath = null)
    {
        var ветка = string.IsNullOrWhiteSpace(productKeyPath)
            ? PrintEnvelopeSettings.DefaultProductKeyPath
            : productKeyPath;

        _store = new RegistryValueStore(ветка.TrimEnd('\\') + "\\" + AutomationKeyName);
    }

    /// <summary>Путь к ветке флагов относительно HKCU.</summary>
    public string KeyPath => _store.KeyPath;

    /// <summary>
    /// Читает флаг модального показа окон; по умолчанию — <see langword="false"/>
    /// (слепой запуск, ТЗ §9.2).
    /// </summary>
    /// <returns>Значение флага.</returns>
    public bool GetShowFormsAsModal() => _store.ReadBool(ShowFormsAsModalValue, false);

    /// <summary>
    /// Устанавливает флаг модального показа окон.
    /// </summary>
    /// <param name="value">Новое значение флага.</param>
    public void SetShowFormsAsModal(bool value) => _store.WriteBool(ShowFormsAsModalValue, value);

    /// <summary>
    /// Читает флаг «выкусывать индекс из строки адреса»; по умолчанию —
    /// <see langword="false"/>.
    /// </summary>
    /// <returns>Значение флага.</returns>
    public bool GetParseZipIndexFromAddress() => _store.ReadBool(ParseZipIndexFromAddressValue, false);

    /// <summary>
    /// Устанавливает флаг «выкусывать индекс из строки адреса».
    /// </summary>
    /// <param name="value">Новое значение флага.</param>
    public void SetParseZipIndexFromAddress(bool value) => _store.WriteBool(ParseZipIndexFromAddressValue, value);
}
