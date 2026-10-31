using System.Globalization;
using Microsoft.Win32;
using Picket302.Domain.Services;

namespace Picket302.Infrastructure.Registry;

/// <summary>
/// Хранилище текущего отправителя в реестре Windows
/// (ТЗ §8.1, функция 16: «Глобальный текущий отправитель»).
/// </summary>
/// <remarks>
/// Идентификатор активного отправителя хранится в ветке реестра
/// <c>HKCU\Software\PEPro\CurrentSender</c>.
/// </remarks>
public sealed class WindowsCurrentSenderRegistryStore : ICurrentSenderStore
{
    /// <summary>Путь ветки реестра текущего отправителя.</summary>
    public const string RegistryPath = @"Software\PEPro\CurrentSender";

    /// <summary>Имя параметра с идентификатором отправителя.</summary>
    public const string ValueName = "SenderId";

    /// <inheritdoc />
    public long? Read()
    {
        using var ключ = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);

        if (ключ?.GetValue(ValueName) is not string значение)
        {
            return null;
        }

        return long.TryParse(значение, NumberStyles.Integer, CultureInfo.InvariantCulture, out var идентификатор)
            ? идентификатор
            : null;
    }

    /// <inheritdoc />
    public void Write(long? senderId)
    {
        using var ветка = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

        if (ветка is null)
        {
            return;
        }

        if (senderId is null)
        {
            ветка.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        ветка.SetValue(ValueName, senderId.Value.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
    }
}
