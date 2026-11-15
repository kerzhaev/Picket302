using System.Text;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;
using Picket302.App.UI.Forms;
using Picket302.Domain.Repositories;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Мастер импорта адресной книги в совместном режиме: финальный шаг
/// применяется серверной командой слияния, а не записью в локальную базу.
/// </summary>
/// <remarks>
/// Требование: в совместном режиме мастер не пишет в местную базу. Записи
/// сливает серверная служба одной командой в одной транзакции.
/// </remarks>
public sealed class ImportWizardSharedModeTests
{
    [Fact]
    public async Task Мастер_ПрименяетИмпортЧерезСлиятельСервера()
    {
        var слиятель = new СлиятельЗаглушка();
        var контроллер = new ImportWizardController(слиятель: слиятель);

        var путь = ЗаписатьФайл();

        try
        {
            Assert.True(контроллер.LoadFile(путь));

            // Сопоставление столбцов: первый столбец — наименование.
            контроллер.SetSlot(0, ImportFieldSlot.Name);
            контроллер.SetSlot(1, ImportFieldSlot.Other);

            // Оператор подтверждает записи (шаг 4): политика по умолчанию
            // применяет только подтверждённые.
            while (контроллер.CurrentRecord is not null)
            {
                контроллер.MarkCurrentVerified();

                if (!контроллер.MoveNextRecord())
                {
                    break;
                }
            }

            var итог = await контроллер.CommitAsync();

            Assert.NotNull(итог);
            Assert.Null(контроллер.ErrorMessage);

            // Слиятелю передан пакет с записями файла.
            Assert.NotNull(слиятель.Пакет);
            Assert.Equal(2, слиятель.Пакет!.Records.Count);
            Assert.Equal("CSV", слиятель.Пакет.SourceFormat);
            Assert.Equal(MergePolicy.SkipExisting, слиятель.Политика);

            // Итог мастера — из ответа службы.
            Assert.Equal(2, итог!.Created);
            Assert.Equal(0, итог.Updated);
            Assert.Equal(0, итог.Skipped);
        }
        finally
        {
            File.Delete(путь);
        }
    }

    [Fact]
    public async Task Мастер_БезСлиятеля_НеПрименяетИмпорт()
    {
        // Ни локального репозитория, ни слиятеля: применять нечего, и мастер
        // обязан сообщить об этом, а не создать локальную запись.
        var контроллер = new ImportWizardController();

        var путь = ЗаписатьФайл();

        try
        {
            Assert.True(контроллер.LoadFile(путь));

            var итог = await контроллер.CommitAsync();

            Assert.Null(итог);
            Assert.False(string.IsNullOrWhiteSpace(контроллер.ErrorMessage));
        }
        finally
        {
            File.Delete(путь);
        }
    }

    private static string ЗаписатьФайл()
    {
        var путь = Path.Combine(
            Path.GetTempPath(),
            "picket302_wizard_shared_" + Guid.NewGuid().ToString("N") + ".csv");

        var содержимое = new StringBuilder();

        содержимое.AppendLine("Наименование;Примечание");
        содержимое.AppendLine("ООО «Первая»;Короткое примечание");
        содержимое.AppendLine("ООО «Вторая»;Второе примечание");

        File.WriteAllText(путь, содержимое.ToString(), new UTF8Encoding(false));

        return путь;
    }

    /// <summary>Слиятель, запоминающий переданный пакет.</summary>
    private sealed class СлиятельЗаглушка : IImportMerger
    {
        public ImportBatch? Пакет { get; private set; }

        public MergePolicy Политика { get; private set; }

        public Task<ImportMergeOutcome> СлитьAsync(
            ImportBatch пакет,
            MergePolicy политика,
            CancellationToken токен = default)
        {
            Пакет = пакет;
            Политика = политика;

            return Task.FromResult(new ImportMergeOutcome(
                пакет.Records.Count,
                0,
                0,
                0,
                0,
                0));
        }
    }
}
