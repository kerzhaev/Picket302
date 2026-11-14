using System.Security.AccessControl;
using System.Security.Principal;

namespace Picket302.Service.Security;

/// <summary>
/// Ограничение прав доступа к каталогу данных службы.
/// </summary>
/// <remarks>
/// <para>
/// Каталог данных по умолчанию наследует права родительского каталога, а
/// значит доступен для чтения любому локальному пользователю. В каталоге
/// лежит административный токен службы: с ним служебные маршруты (выдача кода
/// сопряжения, отзыв устройств) доступны с локального адреса, и читать токен
/// постороннему нельзя.
/// </para>
/// <para>
/// Права задаются явно: полный доступ — системе, администраторам, учётной
/// записи службы и текущему пользователю (им запускается служба при установке).
/// Наследование прав отключается.
/// </para>
/// </remarks>
public static class SharedDataDirectoryProtection
{
    /// <summary>
    /// Устанавливает ограниченные права на каталог данных службы.
    /// </summary>
    /// <param name="каталог">Каталог данных.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <param name="учётнаяЗаписьСлужбы">
    /// Учётная запись службы; пусто — используется текущая учётная запись
    /// процесса.
    /// </param>
    public static void Применить(
        string каталог,
        Action<string> журнал,
        string? учётнаяЗаписьСлужбы = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталог);
        ArgumentNullException.ThrowIfNull(журнал);

        try
        {
            Directory.CreateDirectory(каталог);

            var правила = new DirectorySecurity();

            // Наследование отключается: иначе права на каталог вернутся к
            // прежним при пересоздании вложенных каталогов.
            правила.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var наследование = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            var распространение = PropagationFlags.None;

            правила.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                наследование,
                распространение,
                AccessControlType.Allow));

            правила.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                наследование,
                распространение,
                AccessControlType.Allow));

            using (var идентичность = WindowsIdentity.GetCurrent())
            {
                if (идентичность.User is not null)
                {
                    правила.AddAccessRule(new FileSystemAccessRule(
                        идентичность.User,
                        FileSystemRights.FullControl,
                        наследование,
                        распространение,
                        AccessControlType.Allow));
                }
            }

            if (!string.IsNullOrWhiteSpace(учётнаяЗаписьСлужбы))
            {
                try
                {
                    var учётка = new NTAccount(учётнаяЗаписьСлужбы);

                    правила.AddAccessRule(new FileSystemAccessRule(
                        учётка,
                        FileSystemRights.FullControl,
                        наследование,
                        распространение,
                        AccessControlType.Allow));
                }
                catch (IdentityNotMappedException)
                {
                    журнал(
                        $"Учётная запись службы «{учётнаяЗаписьСлужбы}» не найдена: "
                        + "доступ к каталогу данных остаётся у текущего пользователя и администраторов.");
                }
            }

            new DirectoryInfo(каталог).SetAccessControl(правила);

            журнал($"Права на каталог данных ограничены: {каталог}.");
        }
#pragma warning disable CA1031 // Недоступность защиты не должна останавливать службу: каталог всё равно закрыт от записи по умолчанию.
        catch (Exception ошибка)
        {
            журнал($"Не удалось ограничить права на каталог данных {каталог}: {ошибка}");
        }
#pragma warning restore CA1031
    }
}
