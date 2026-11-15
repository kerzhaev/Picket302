using System.Security.AccessControl;
using System.Security.Principal;

namespace Picket302.Service.Security;

/// <summary>
/// Подготовка каталога данных службы к защите, которую сама служба
/// применяет при каждом запуске.
/// </summary>
/// <remarks>
/// <para>
/// Служба работает под учётной записью с минимальными правами
/// (<c>NETWORK SERVICE</c>). Защита каталога данных обязательна: служба
/// ограничивает права сама и отказывается стартовать, если не смогла этого
/// сделать (см. <see cref="SharedDataDirectoryProtection"/>).
/// </para>
/// <para>
/// Изменить права каталога может только тот, кому выдано право
/// <c>ChangePermissions</c> (или владелец). Каталог создаёт установщик от
/// имени администратора, поэтому учётной записи службы заранее выдаётся
/// полный доступ на каталог и на уже существующие вложенные объекты. Без
/// этого служба не смогла бы ограничить права и остановилась бы с кодом
/// выхода 4 — установка была бы внешне успешной, а служба нерабочей.
/// </para>
/// <para>
/// Владельцем каталога служба не назначается: Windows не допускает
/// назначения владельцем служебной учётной записи. Право изменять права
/// выдаётся явно, а окончательные права (система, администраторы, служба;
/// посторонних разрешающих правил нет; наследование отключено) устанавливает
/// сама служба при первом запуске.
/// </para>
/// </remarks>
public static class SharedDataDirectoryPreparation
{
    /// <summary>
    /// Готовит каталог данных: создаёт его и выдаёт учётной записи службы
    /// право изменять права на каталог и на вложенные объекты.
    /// </summary>
    /// <param name="каталог">Каталог данных службы.</param>
    /// <param name="учётнаяЗаписьСлужбы">Учётная запись службы.</param>
    /// <returns>Пояснение результата; <see langword="null"/> — подготовлено.</returns>
    public static string? Подготовить(string каталог, string учётнаяЗаписьСлужбы)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталог);
        ArgumentException.ThrowIfNullOrWhiteSpace(учётнаяЗаписьСлужбы);

        try
        {
            Directory.CreateDirectory(каталог);

            var служебная = (SecurityIdentifier)new NTAccount(учётнаяЗаписьСлужбы)
                .Translate(typeof(SecurityIdentifier));

            ПодготовитьКаталог(каталог, служебная);
            ПодготовитьВложенные(каталог, служебная);

            return null;
        }
        catch (Exception ошибка)
        {
            return ошибка.Message;
        }
    }

    /// <summary>
    /// Выдаёт учётной записи службы полный доступ на каталог.
    /// </summary>
    /// <param name="каталог">Каталог.</param>
    /// <param name="служебная">Учётная запись службы.</param>
    private static void ПодготовитьКаталог(string каталог, SecurityIdentifier служебная)
    {
        var правила = new DirectorySecurity();

        // Наследование сохраняется: посторонние права, унаследованные от
        // ProgramData, снимает сама служба при первом запуске (она уже имеет
        // на это право).
        правила.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);

        var наследование = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        правила.AddAccessRule(new FileSystemAccessRule(
            служебная,
            FileSystemRights.FullControl,
            наследование,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(каталог).SetAccessControl(правила);
    }

    /// <summary>
    /// Выдаёт учётной записи службы полный доступ на вложенные каталоги и
    /// файлы: без этого служба не сможет переписать их права при собственной
    /// защите.
    /// </summary>
    /// <param name="каталог">Корневой каталог данных.</param>
    /// <param name="служебная">Учётная запись службы.</param>
    private static void ПодготовитьВложенные(string каталог, SecurityIdentifier служебная)
    {
        foreach (var вложенный in Directory.EnumerateDirectories(каталог, "*", SearchOption.AllDirectories))
        {
            ПодготовитьКаталог(вложенный, служебная);
        }

        foreach (var файл in Directory.EnumerateFiles(каталог, "*", SearchOption.AllDirectories))
        {
            var правила = new FileSecurity();

            правила.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);

            правила.AddAccessRule(new FileSystemAccessRule(
                служебная,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));

            new FileInfo(файл).SetAccessControl(правила);
        }
    }
}
