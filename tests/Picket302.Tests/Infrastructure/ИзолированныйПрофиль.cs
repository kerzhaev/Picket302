using Picket302.App;
using Picket302.Infrastructure;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Изолированный тестовый профиль приложения: все файлы состава — база
/// данных, реквизиты подключения, журналы, лицензия, настройки интерфейса —
/// создаются под уникальным временным корнем и удаляются после теста.
/// Реальные файлы пользователя в <c>%AppData%\Picket302</c> не затрагиваются.
/// </summary>
public sealed class ИзолированныйПрофиль : IDisposable
{
    /// <summary>Временный корень профиля теста.</summary>
    public string Корень { get; }

    /// <summary>Пути профиля приложения внутри временного корня.</summary>
    public Picket302ProfilePaths Пути { get; }

    private MainShell? _оболочка;
    private AppServices? _состав;

    /// <summary>
    /// Создаёт уникальный временный корень профиля.
    /// </summary>
    public ИзолированныйПрофиль()
    {
        Корень = Path.Combine(Path.GetTempPath(), "Picket302Tests", Guid.NewGuid().ToString("N"));
        Пути = Picket302ProfilePaths.ДляКорня(Корень);

        Directory.CreateDirectory(Пути.DataDirectory);
    }

    /// <summary>
    /// Создаёт состав приложения в изолированном профиле и главное окно
    /// поверх него. Состав освобождается вместе с профилем; владение окном
    /// остаётся у вызывающего.
    /// </summary>
    /// <param name="создатьДемоБазу">Наполнять ли базу демо-данными.</param>
    /// <returns>Главное окно с готовым составом.</returns>
    public async Task<MainShell> СоздатьОболочкуAsync(bool создатьДемоБазу = true)
    {
        _состав = await AppServices.СоздатьAsync(Пути, создатьДемоБазу).ConfigureAwait(false);

        _оболочка = new MainShell(_состав);

        return _оболочка;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _оболочка?.Dispose();
        _состав?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Пул соединений может удерживать файл базы в момент удаления.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        for (var попытка = 0; попытка < 5; попытка++)
        {
            try
            {
                if (Directory.Exists(Корень))
                {
                    Directory.Delete(Корень, recursive: true);
                }

                break;
            }
            catch (IOException)
            {
                // Каталог ещё занят завершающимися соединениями — повторяем.
                Thread.Sleep(200);
            }
        }

        // Родительский каталог «Picket302Tests» удаляется, когда опустел:
        // параллельные тесты создают в нём свои уникальные корни.
        try
        {
            var родитель = Path.GetDirectoryName(Корень);

            if (родитель is not null
                && Directory.Exists(родитель)
                && !Directory.EnumerateFileSystemEntries(родитель).Any())
            {
                Directory.Delete(родитель);
            }
        }
        catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
        {
            // Занятый параллельным тестом каталог удалит последний тест.
        }
    }
}
