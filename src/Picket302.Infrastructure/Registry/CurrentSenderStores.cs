using System.Data;
using System.Globalization;
using Picket302.Domain.Services;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Registry;

/// <summary>
/// Хранилище текущего отправителя в таблице параметров
/// (ТЗ §8.1, функция 16: «Реестр HKCU\Software\PEPro\CurrentSender / таблица
/// parameters»).
/// </summary>
public sealed class ParameterCurrentSenderStore : ICurrentSenderStore
{
    /// <summary>Ключ параметра с идентификатором отправителя.</summary>
    public const string ParameterKey = "CurrentSenderId";

    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт хранилище текущего отправителя в таблице параметров.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public ParameterCurrentSenderStore(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public long? Read()
    {
        using var команда = _сессия.Connection.CreateCommand();
        команда.CommandText = "SELECT value FROM parameters WHERE name = @Name;";
        команда.Transaction = _сессия.Transaction;

        var параметр = команда.CreateParameter();
        параметр.ParameterName = "@Name";
        параметр.Value = ParameterKey;
        команда.Parameters.Add(параметр);

        var значение = команда.ExecuteScalar();

        if (значение is not string текст
            || !long.TryParse(текст, NumberStyles.Integer, CultureInfo.InvariantCulture, out var идентификатор))
        {
            return null;
        }

        return идентификатор;
    }

    /// <inheritdoc />
    public void Write(long? senderId)
    {
        using var команда = _сессия.Connection.CreateCommand();
        команда.Transaction = _сессия.Transaction;

        if (senderId is null)
        {
            команда.CommandText = "DELETE FROM parameters WHERE name = @Name;";
        }
        else
        {
            команда.CommandText = """
                INSERT INTO parameters (name, value, updated_at)
                VALUES (@Name, @Value, strftime ('%Y-%m-%d %H:%M:%f', 'now'))
                ON CONFLICT(name) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at;
                """;

            var значение = команда.CreateParameter();
            значение.ParameterName = "@Value";
            значение.Value = senderId.Value.ToString(CultureInfo.InvariantCulture);
            команда.Parameters.Add(значение);
        }

        var параметр = команда.CreateParameter();
        параметр.ParameterName = "@Name";
        параметр.Value = ParameterKey;
        команда.Parameters.Add(параметр);

        команда.ExecuteNonQuery();
    }
}

/// <summary>
/// Составное хранилище текущего отправителя: реестр Windows и таблица
/// параметров (ТЗ §8.1, функция 16).
/// </summary>
/// <remarks>
/// Значение записывается в оба хранилища, поэтому выбор отправителя сохраняется
/// и между перезапусками приложения, и при переносе профиля. При чтении
/// приоритет у реестра; если реестр недоступен, используется таблица параметров.
/// </remarks>
public sealed class CompositeCurrentSenderStore : ICurrentSenderStore
{
    private readonly ICurrentSenderStore _реестр;
    private readonly ICurrentSenderStore _параметры;

    /// <summary>Создаёт составное хранилище текущего отправителя.</summary>
    /// <param name="реестр">Хранилище в реестре Windows.</param>
    /// <param name="параметры">Хранилище в таблице параметров.</param>
    public CompositeCurrentSenderStore(ICurrentSenderStore реестр, ICurrentSenderStore параметры)
    {
        ArgumentNullException.ThrowIfNull(реестр);
        ArgumentNullException.ThrowIfNull(параметры);

        _реестр = реестр;
        _параметры = параметры;
    }

    /// <inheritdoc />
    public long? Read() => _реестр.Read() ?? _параметры.Read();

    /// <inheritdoc />
    public void Write(long? senderId)
    {
        _реестр.Write(senderId);
        _параметры.Write(senderId);
    }
}
