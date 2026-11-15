using System.Security.AccessControl;
using System.Security.Principal;
using Picket302.Service.Security;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Защита каталога данных службы: права ограничиваются и проверяются,
/// ранее выданные посторонние права убираются, отказ защиты останавливает
/// запуск до подготовки базы и до создания токена.
/// </summary>
public sealed class SharedDirectoryProtectionTests
{
    [Fact]
    public void Права_Ограничиваются_И_Проверяются()
    {
        var каталог = Каталог();
        var записи = new List<string>();

        try
        {
            Directory.CreateDirectory(Path.Combine(каталог, "logs"));
            File.WriteAllText(Path.Combine(каталог, "service.json"), "{}");

            Assert.True(
                SharedDataDirectoryProtection.Применить(каталог, записи.Add),
                "защита не применилась: " + string.Join(" | ", записи));

            var правила = new DirectoryInfo(каталог).GetAccessControl();

            // Наследование отключено: права не вернутся от родителя.
            Assert.True(правила.AreAccessRulesProtected);

            foreach (FileSystemAccessRule правило in правила.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier)))
            {
                if (правило.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var sid = (SecurityIdentifier)правило.IdentityReference;

                var допустимый = sid == new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)
                    || sid == new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                    || WindowsIdentity.GetCurrent().User == sid;

                Assert.True(допустимый, $"постороннее разрешающее правило: {sid.Value}");
            }

            // Вложенные объекты тоже защищены: созданный до защиты каталог
            // logs получил те же правила.
            var вложенный = new DirectoryInfo(Path.Combine(каталог, "logs")).GetAccessControl();

            Assert.True(вложенный.AreAccessRulesProtected);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Ранее_Выданные_Права_Пользователей_Убираются()
    {
        var каталог = Каталог();

        try
        {
            Directory.CreateDirectory(каталог);

            // Имитация прежней версии установщика: правка
            // «users-modify» — разрешающие правила для аутентифицированных.
            var прежние = new DirectoryInfo(каталог).GetAccessControl();

            прежние.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            new DirectoryInfo(каталог).SetAccessControl(прежние);

            Assert.True(SharedDataDirectoryProtection.Применить(каталог, _ => { }));

            var после = new DirectoryInfo(каталог).GetAccessControl();

            foreach (FileSystemAccessRule правило in после.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier)))
            {
                Assert.NotEqual(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    (SecurityIdentifier)правило.IdentityReference);
            }
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public async Task Отказ_Защиты_Останавливает_Запуск_Стенда()
    {
        var каталог = Каталог();

        try
        {
            Directory.CreateDirectory(каталог);

            var вложенный = Path.Combine(каталог, "blocked");
            Directory.CreateDirectory(вложенный);

            // Владелец запрещает себе просмотр вложенного каталога: обход
            // вложенных объектов при установке правил невозможен, и защита
            // каталога данных в целом не может быть применена.
            var запрет = new DirectoryInfo(вложенный).GetAccessControl();

            запрет.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny));

            new DirectoryInfo(вложенный).SetAccessControl(запрет);

            var исключение = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SharedServiceTestHost.ПоднятьAsync(каталогДанных: каталог));

            Assert.Contains("не удалось ограничить права", исключение.Message);

            // Защита отклонила запуск до подготовки базы и до создания
            // административного токена.
            Assert.False(File.Exists(Path.Combine(каталог, "picket302.db")));
            Assert.False(File.Exists(Path.Combine(каталог, "admin.token")));
        }
        finally
        {
            Удалить(каталог);
        }
    }

    /// <summary>Временный каталог проверки.</summary>
    /// <returns>Путь каталога.</returns>
    private static string Каталог()
    {
        var каталог = Path.Combine(
            Path.GetTempPath(),
            "Picket302Protection_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(каталог);

        return каталог;
    }

    /// <summary>Удаляет временный каталог, терпя занятые файлы.</summary>
    /// <param name="каталог">Каталог.</param>
    private static void Удалить(string каталог)
    {
        try
        {
            Directory.Delete(каталог, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог не блокирует работу проверок.
        }
        catch (UnauthorizedAccessException)
        {
            // Запрещённый DACL мешает удалению: каталог останется во
            // временном хранилище и не влияет на другие проверки.
        }
    }
}
