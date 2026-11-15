using System.Text.Json;
using Picket302.App.Shared;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Fail-closed поведение хранилища незавершённых операций: нечитаемый
/// существующий файл блокирует новые мутации (история — единственная копия
/// сведений о неизвестном исходе), исходный файл не удаляется и не
/// перезаписывается, восстановление — повторным чтением или явным сбросом
/// администратора; отказ записи не теряет сведения.
/// </summary>
public sealed class PendingOperationsStoreFailClosedTests
{
    [Fact]
    public void Отсутствующий_Файл_Не_Блокирует_Мутации()
    {
        var каталог = Каталог();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");

            // Файла никогда не было: легальное пустое хранилище.
            var хранилище = new PendingOperationsStore(файл);

            Assert.False(хранилище.Заблокировано);
            Assert.Null(хранилище.ПричинаБлокировки);
            Assert.Empty(хранилище.Незавершённые);

            var идентификатор = хранилище.Начать("counterparties.save", "{}");

            Assert.False(хранилище.Заблокировано);
            Assert.True(File.Exists(файл));
            Assert.Single(хранилище.Незавершённые);
            Assert.Equal(идентификатор, хранилище.Незавершённые[0].Идентификатор);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Повреждённый_JSON_Блокирует_Мутации_И_Сохраняет_Файл()
    {
        var каталог = Каталог();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");
            var повреждённоеСодержимое = "{\"запись операции без конца";

            File.WriteAllText(файл, повреждённоеСодержимое);

            var хранилище = new PendingOperationsStore(файл);

            // Файл существует и нечитаем: блокировка, исход неизвестен.
            Assert.True(хранилище.Заблокировано);
            Assert.True(хранилище.ТребуетсяЯвныйСброс);
            Assert.Contains("повреждена", хранилище.ПричинаБлокировки);
            Assert.Empty(хранилище.Незавершённые);

            // Новая мутация отклоняется до отправки: она получила бы новый
            // идентификатор и могла бы повторить уже выполненное действие.
            var ошибка = Assert.Throws<SharedModeException>(
                () => хранилище.Начать("counterparties.save", "{}"));

            Assert.Equal(SharedErrorCodes.PendingHistoryUnavailable, ошибка.Код);

            // Исходный файл не переименован, содержимое не изменено.
            Assert.Equal(повреждённоеСодержимое, File.ReadAllText(файл));

            // Восстановление чтением невозможно.
            Assert.False(хранилище.ПопытатьсяВосстановить());
            Assert.True(хранилище.Заблокировано);

            // Явный сброс администратора: файл сохранён рядом для
            // диагностики, блокировка снята.
            Assert.True(хранилище.ЯвныйСбросПовреждённогоФайла());
            Assert.False(хранилище.Заблокировано);
            Assert.False(File.Exists(файл));
            Assert.Single(Directory.GetFiles(каталог, "pending-operations.json.повреждён-*"));

            var идентификатор = хранилище.Начать("counterparties.save", "{}");

            Assert.False(string.IsNullOrWhiteSpace(идентификатор));
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Временный_Отказ_Чтения_Восстанавливается_Повторным_Чтением()
    {
        var каталог = Каталог();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");
            var записи = new List<PendingOperationsStore.НезавершённаяОперация>
            {
                new("оп-1", "counterparties.save", "{}", DateTime.UtcNow),
            };

            File.WriteAllText(файл, JsonSerializer.Serialize(записи, SharedJson.Options));

            // Файл занят другим процессом: чтение не состоялось.
            using (var держатель = File.Open(файл, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var хранилище = new PendingOperationsStore(файл);

                Assert.True(хранилище.Заблокировано);
                Assert.False(хранилище.ТребуетсяЯвныйСброс);
                Assert.Contains("недоступна", хранилище.ПричинаБлокировки);

                var ошибка = Assert.Throws<SharedModeException>(
                    () => хранилище.Начать("counterparties.save", "{}"));

                Assert.Equal(SharedErrorCodes.PendingHistoryUnavailable, ошибка.Код);

                // Пока файл занят, повторное чтение не помогает.
                Assert.False(хранилище.ПопытатьсяВосстановить());

                var опознано = хранилище;
                Assert.True(опознано.Заблокировано);
            }

            // Причина устранена: повторное чтение восстанавливает сведения,
            // включая операцию с неизвестным исходом.
            var восстановленное = new PendingOperationsStore(файл);

            Assert.False(восстановленное.Заблокировано);

            var единственная = Assert.Single(восстановленное.Незавершённые);

            Assert.Equal("оп-1", единственная.Идентификатор);
            Assert.Equal("counterparties.save", единственная.Команда);
            Assert.Equal("{}", единственная.Тело);
        }
        finally
        {
            Удалить(каталог);
        }
    }

    [Fact]
    public void Отказ_Записи_При_Очистке_TTL_Сохраняет_Сведения()
    {
        var каталог = Каталог();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");
            var момент = DateTime.UtcNow;

            // Запись устаревает ПОСЛЕ загрузки: конструктор ещё видит её
            // свежей (6 дней), явная очистка выполняется при сдвинутых часах.
            var записи = new List<PendingOperationsStore.НезавершённаяОперация>
            {
                new("оп-старая", "counterparties.save", "{}", момент - TimeSpan.FromDays(6)),
            };

            var исходноеСодержимое = JsonSerializer.Serialize(записи, SharedJson.Options);

            File.WriteAllText(файл, исходноеСодержимое);
            File.SetAttributes(файл, FileAttributes.ReadOnly);

            var хранилище = new PendingOperationsStore(файл, часы: () => момент);

            Assert.False(хранилище.Заблокировано);
            Assert.Single(хранилище.Незавершённые);

            // Неделя прошла: запись устарела.
            момент = момент.AddDays(2);

            var (удалено, отказ) = хранилище.ОчиститьУстаревшие();

            // Запись на диск не состоялась: сведения сохранены и в памяти,
            // и на диске; файл не потерян.
            Assert.Equal(0, удалено);
            Assert.True(отказ);
            Assert.Single(хранилище.Незавершённые);
            Assert.Equal(исходноеСодержимое, File.ReadAllText(файл));
            Assert.False(File.Exists(файл + ".tmp"));

            // Причина устранена: очистка выполняется и подтверждается записью.
            File.SetAttributes(файл, FileAttributes.Normal);

            (удалено, отказ) = хранилище.ОчиститьУстаревшие();

            Assert.Equal(1, удалено);
            Assert.False(отказ);
            Assert.Empty(хранилище.Незавершённые);
            Assert.Equal("[]", File.ReadAllText(файл));
        }
        finally
        {
            var файл = Path.Combine(каталог, "pending-operations.json");

            if (File.Exists(файл))
            {
                File.SetAttributes(файл, FileAttributes.Normal);
            }

            Удалить(каталог);
        }
    }

    [Fact]
    public void Сбой_Замены_Не_Уничтожает_Прежний_Файл()
    {
        var каталог = Каталог();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");
            var записи = new List<PendingOperationsStore.НезавершённаяОперация>
            {
                new("оп-1", "counterparties.save", "{}", DateTime.UtcNow),
            };

            var исходноеСодержимое = JsonSerializer.Serialize(записи, SharedJson.Options);

            File.WriteAllText(файл, исходноеСодержимое);
            File.SetAttributes(файл, FileAttributes.ReadOnly);

            var хранилище = new PendingOperationsStore(файл);

            Assert.False(хранилище.Заблокировано);

            // Сбой атомарной замены: мутация не отправляется, прежний
            // корректный файл не уничтожен, временный файл убран.
            var ошибка = Assert.Throws<SharedModeException>(
                () => хранилище.Начать("counterparties.save", "{}"));

            Assert.Equal(SharedErrorCodes.PendingHistoryUnavailable, ошибка.Код);

            Assert.Equal(исходноеСодержимое, File.ReadAllText(файл));
            Assert.False(File.Exists(файл + ".tmp"));
            Assert.Single(хранилище.Незавершённые);

            // Причина устранена: мутация выполняется и подтверждается записью.
            File.SetAttributes(файл, FileAttributes.Normal);

            var новый = хранилище.Начать("counterparties.save", "{}");

            Assert.Equal(2, хранилище.Незавершённые.Count);
            Assert.Contains(новый, хранилище.Незавершённые.Select(о => о.Идентификатор));
        }
        finally
        {
            var файл = Path.Combine(каталог, "pending-operations.json");

            if (File.Exists(файл))
            {
                File.SetAttributes(файл, FileAttributes.Normal);
            }

            Удалить(каталог);
        }
    }

    /// <summary>Временный каталог проверки.</summary>
    /// <returns>Путь каталога.</returns>
    private static string Каталог()
    {
        var каталог = Path.Combine(
            Path.GetTempPath(),
            "Picket302Pending_" + Guid.NewGuid().ToString("N"));

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
    }
}
