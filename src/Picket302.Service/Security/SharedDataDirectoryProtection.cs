using System.Security.AccessControl;
using System.Security.Principal;

namespace Picket302.Service.Security;

/// <summary>
/// Ограничение прав доступа к каталогу данных службы.
/// </summary>
/// <remarks>
/// <para>
/// Каталог данных по умолчанию наследует права родительского каталога
/// (<c>ProgramData</c>), а значит доступен для чтения любому локальному
/// пользователю. В каталоге лежат база общей данных, копии, шаблоны,
/// <c>service.json</c> и административный токен службы: читать их и менять
/// их посторонним учётным записям нельзя.
/// </para>
/// <para>
/// Права задаются явно и заменяют прежние: полный доступ — системе,
/// администраторам и фактической учётной записи службы (для установленной
/// службы это <c>NETWORK SERVICE</c>, для консольного запуска — текущий
/// пользователь). Наследование прав отключается, явно выданные ранее права
/// группе <c>Users</c> (например, от прежней версии установщика) удаляются.
/// Защита применяется к каталогу и ко всем вложенным каталогам и файлам.
/// </para>
/// <para>
/// Защита обязательна к применению: метод возвращает признак успеха, и
/// служба не продолжает запуск, если ограничить или проверить права не
/// удалось (см. <c>Program.Main</c>).
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
    /// <returns>
    /// <see langword="true"/>, если права ограничены и проверка после
    /// установки прошла; <see langword="false"/> — служба должна прекратить
    /// запуск.
    /// </returns>
    public static bool Применить(
        string каталог,
        Action<string> журнал,
        string? учётнаяЗаписьСлужбы = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталог);
        ArgumentNullException.ThrowIfNull(журнал);

        try
        {
            Directory.CreateDirectory(каталог);

            var допустимые = Допустимые(журнал, учётнаяЗаписьСлужбы);

            new DirectoryInfo(каталог).SetAccessControl(ПравилаКаталога(допустимые));

            ЗащититьВложенные(каталог, допустимые);

            if (!ПраваОграничены(каталог, допустимые, журнал))
            {
                return false;
            }

            журнал($"Права на каталог данных ограничены: {каталог}.");

            return true;
        }
#pragma warning disable CA1031 // Любой отказ защиты (ACL, доступ, состояние каталога) обязан останавливать запуск: служба не должна работать с незащищённым каталогом.
        catch (Exception ошибка)
        {
            журнал($"Не удалось ограничить права на каталог данных {каталог}: {ошибка}");

            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Определяет допустимые учётные записи: система, администраторы,
    /// текущий пользователь и учётная запись службы.
    /// </summary>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <param name="учётнаяЗаписьСлужбы">Учётная запись службы.</param>
    /// <returns>Список допустимых SID.</returns>
    private static List<SecurityIdentifier> Допустимые(
        Action<string> журнал,
        string? учётнаяЗаписьСлужбы)
    {
        var допустимые = new List<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };

        using (var идентичность = WindowsIdentity.GetCurrent())
        {
            if (идентичность.User is not null && !допустимые.Contains(идентичность.User))
            {
                допустимые.Add(идентичность.User);
            }
        }

        if (!string.IsNullOrWhiteSpace(учётнаяЗаписьСлужбы))
        {
            try
            {
                var служба = (SecurityIdentifier)new NTAccount(учётнаяЗаписьСлужбы)
                    .Translate(typeof(SecurityIdentifier));

                if (!допустимые.Contains(служба))
                {
                    допустимые.Add(служба);
                }
            }
            catch (IdentityNotMappedException)
            {
                журнал($"Учётная запись службы «{учётнаяЗаписьСлужбы}» не найдена: "
                    + "доступ к каталогу данных остаётся у текущего пользователя и администраторов.");
            }
        }

        return допустимые;
    }

    /// <summary>
    /// Строит правила каталога для допустимых учётных записей с отключённым
    /// наследованием.
    /// </summary>
    /// <param name="допустимые">Допустимые SID.</param>
    /// <returns>Свежий дескриптор безопасности каталога.</returns>
    private static DirectorySecurity ПравилаКаталога(List<SecurityIdentifier> допустимые)
    {
        var правила = new DirectorySecurity();

        // Наследование отключается: иначе права на каталог вернутся к
        // прежним при пересоздании вложенных каталогов.
        правила.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var наследование = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        foreach (var идентификатор in допустимые)
        {
            правила.AddAccessRule(new FileSystemAccessRule(
                идентификатор,
                FileSystemRights.FullControl,
                наследование,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return правила;
    }

    /// <summary>
    /// Строит правила файла для допустимых учётных записей.
    /// </summary>
    /// <param name="допустимые">Допустимые SID.</param>
    /// <returns>Свежий дескриптор безопасности файла.</returns>
    private static FileSecurity ПравилаФайла(List<SecurityIdentifier> допустимые)
    {
        var правила = new FileSecurity();

        правила.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var идентификатор in допустимые)
        {
            правила.AddAccessRule(new FileSystemAccessRule(
                идентификатор,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return правила;
    }

    /// <summary>
    /// Распространяет те же правила на вложенные каталоги и файлы.
    /// </summary>
    /// <remarks>
    /// Уже существующие вложенные объекты не получают новые правила через
    /// наследование (защита корня отключает наследование), поэтому для
    /// каждого объекта строится свежий дескриптор безопасности. Ошибка на
    /// любом объекте считается неудачей защиты.
    /// </remarks>
    /// <param name="каталог">Каталог данных.</param>
    /// <param name="допустимые">Допустимые SID.</param>
    private static void ЗащититьВложенные(string каталог, List<SecurityIdentifier> допустимые)
    {
        foreach (var вложенный in Directory.EnumerateDirectories(каталог, "*", SearchOption.AllDirectories))
        {
            new DirectoryInfo(вложенный).SetAccessControl(ПравилаКаталога(допустимые));
        }

        foreach (var файл in Directory.EnumerateFiles(каталог, "*", SearchOption.AllDirectories))
        {
            new FileInfo(файл).SetAccessControl(ПравилаФайла(допустимые));
        }
    }

    /// <summary>
    /// Проверяет, что права каталога действительно ограничены: никто, кроме
    /// допустимых учётных записей (система, администраторы, служба), не
    /// имеет разрешающих правил.
    /// </summary>
    /// <param name="каталог">Каталог данных.</param>
    /// <param name="допустимые">Допустимые SID.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <returns>
    /// <see langword="true"/>, если посторонних разрешающих правил нет.
    /// </returns>
    private static bool ПраваОграничены(
        string каталог,
        List<SecurityIdentifier> допустимые,
        Action<string> журнал)
    {
        var защищаемый = new DirectoryInfo(каталог).GetAccessControl();

        if (!защищаемый.AreAccessRulesProtected)
        {
            журнал($"Каталог данных {каталог}: наследование прав не отключено.");

            return false;
        }

        foreach (FileSystemAccessRule правило in защищаемый.GetAccessRules(
            true,
            true,
            typeof(SecurityIdentifier)))
        {
            if (правило.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var идентификатор = (SecurityIdentifier)правило.IdentityReference;

            if (!допустимые.Contains(идентификатор))
            {
                журнал($"Каталог данных {каталог}: остаётся разрешающее правило для {идентификатор.Value}.");

                return false;
            }
        }

        return true;
    }
}
