using System.Text;
using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверки окна мастера импорта адресной книги и финального диалога
/// пост-действия мастера выгрузки
/// (ТЗ §8.12, функция 159: «Мастер импорта адресной книги (7 экранов)»;
/// ТЗ §8.12, функция 160: «Выгрузка … с последующим действием»).
/// </summary>
public sealed class ImportWizardFormTests
{
    /// <summary>
    /// Текст файла импорта: две записи, второе поле длиннее 255 символов.
    /// </summary>
    private static string ЗаписатьФайл()
    {
        var путь = Path.Combine(
            Path.GetTempPath(),
            "picket302_wizard_" + Guid.NewGuid().ToString("N") + ".csv");

        var содержимое = new StringBuilder();

        содержимое.AppendLine("Наименование;Примечание");
        содержимое.AppendLine("ООО «Первая»;Короткое примечание");
        содержимое.Append("ООО «Вторая»;").Append(new string('я', 300));

        File.WriteAllText(путь, содержимое.ToString(), new UTF8Encoding(false));

        return путь;
    }

    /// <summary>
    /// Мастер состоит из семи экранов с заголовками, зафиксированными ТЗ.
    /// </summary>
    [Fact]
    public void Мастер_СодержитСемьШагов()
    {
        Assert.Equal(7, ImportWizardSteps.TotalSteps);

        using var форма = new FormImportWizard();

        Assert.Equal(ImportWizardStep.File, форма.Шаг);

        var заголовки = new List<string>();

        for (var шаг = ImportWizardStep.File; шаг <= ImportWizardStep.Commit; шаг++)
        {
            заголовки.Add(ImportWizardSteps.Title(шаг));
        }

        Assert.Equal(
            new[]
            {
                "Файл импорта",
                "Начало импорта и категория",
                "Сопоставление столбцов",
                "Предпросмотр записей",
                "Фильтр записей",
                "Итоги и политики импорта",
                "Применение импорта",
            },
            заголовки);
    }

    /// <summary>
    /// Финальный экран предлагает ровно три политики импорта с подписями ТЗ.
    /// </summary>
    [Fact]
    public void Мастер_ПредлагаетТриПолитикиИмпорта()
    {
        using var форма = new FormImportWizard();

        Assert.Equal(
            new[]
            {
                "Добавить только проверенных",
                "Добавить только записи без ошибок",
                "Добавить все записи",
            },
            форма.ПолитикиИмпорта);
    }

    /// <summary>
    /// Мастер доходит до экрана предпросмотра и показывает записи файла.
    /// </summary>
    [Fact]
    public void Мастер_ОткрываетФайл_И_ДоходитДоПредпросмотра()
    {
        var путь = ЗаписатьФайл();

        try
        {
            using var форма = new FormImportWizard();

            Assert.True(форма.ВыбратьФайл(путь));

            var контроллер = форма.Контроллер;

            форма.УстановитьПараметрыРазбора(';', "utf-8", 1, true);

            Assert.True(форма.ПерейтиВперёд());
            Assert.Equal(ImportWizardStep.StartRowAndGroup, форма.Шаг);

            Assert.True(форма.ПерейтиВперёд());
            Assert.Equal(ImportWizardStep.ColumnMapping, форма.Шаг);

            Assert.Equal(2, контроллер.ColumnNames.Count);

            форма.СопоставитьСтолбец(0, ImportFieldSlot.Name);
            форма.СопоставитьСтолбец(1, ImportFieldSlot.ShortName);

            Assert.True(форма.ПерейтиВперёд());
            Assert.Equal(ImportWizardStep.RecordPreview, форма.Шаг);

            Assert.True(
                форма.ЧислоПоказанныхЗаписей == 2,
                $"записей: {контроллер.RecordCount}, столбцов: {string.Join("|", контроллер.ColumnNames)}, "
                + $"начало строки: {контроллер.StartRow}, заголовки: {контроллер.HasHeaderRow}, "
                + $"предупреждения файла: {string.Join("; ", контроллер.FileWarnings)}");
        }
        finally
        {
            File.Delete(путь);
        }
    }

    /// <summary>
    /// На экране предпросмотра показывается предупреждение об усечении
    /// значения длиннее 255 символов.
    /// </summary>
    [Fact]
    public void Мастер_ПоказываетПредупреждениеОбУсечении()
    {
        var путь = ЗаписатьФайл();

        try
        {
            using var форма = new FormImportWizard();

            Assert.True(форма.ВыбратьФайл(путь));

            var контроллер = форма.Контроллер;

            форма.УстановитьПараметрыРазбора(';', "utf-8", 1, true);

            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();

            форма.СопоставитьСтолбец(0, ImportFieldSlot.Name);
            форма.СопоставитьСтолбец(1, ImportFieldSlot.ShortName);

            форма.ПерейтиВперёд();

            // Переход к записи с длинным значением.
            Assert.True(форма.ПерейтиКСледующейЗаписи());

            Assert.Contains("Предупреждения", форма.ПредупрежденияПредпросмотра, StringComparison.Ordinal);
            Assert.Contains("255", форма.ПредупрежденияПредпросмотра, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(путь);
        }
    }

    /// <summary>
    /// Итоговый экран показывает числа непроверенных и ошибочных записей.
    /// </summary>
    [Fact]
    public void Мастер_ПоказываетЧислаНепроверенныхИОшибочныхЗаписей()
    {
        var путь = ЗаписатьФайл();

        try
        {
            using var форма = new FormImportWizard();

            Assert.True(форма.ВыбратьФайл(путь));

            var контроллер = форма.Контроллер;

            форма.УстановитьПараметрыРазбора(';', "utf-8", 1, true);

            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();

            форма.СопоставитьСтолбец(0, ImportFieldSlot.Name);
            форма.СопоставитьСтолбец(1, ImportFieldSlot.ShortName);

            форма.ПерейтиВперёд();

            // Шаг 4 → 5 → 6: фильтр и итоги.
            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();

            Assert.Equal(ImportWizardStep.Summary, форма.Шаг);

            Assert.Contains("Непроверенных записей: 2", форма.ИтогиПроверки, StringComparison.Ordinal);
            Assert.Contains("Записей с ошибками: 0", форма.ИтогиПроверки, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(путь);
        }
    }

    /// <summary>
    /// Четвёртый выход финального экрана возвращает мастер к редактированию
    /// записей, сохраняя сессию импорта.
    /// </summary>
    [Fact]
    public void Мастер_ВозвращаетсяКРедактированию()
    {
        var путь = ЗаписатьФайл();

        try
        {
            using var форма = new FormImportWizard();

            Assert.True(форма.ВыбратьФайл(путь));

            var контроллер = форма.Контроллер;

            форма.УстановитьПараметрыРазбора(';', "utf-8", 1, true);

            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();

            форма.СопоставитьСтолбец(0, ImportFieldSlot.Name);
            форма.СопоставитьСтолбец(1, ImportFieldSlot.ShortName);

            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();
            форма.ПерейтиВперёд();

            Assert.Equal(ImportWizardStep.Summary, форма.Шаг);

            Assert.True(форма.ВернутьсяКРедактированию());

            Assert.Equal(ImportWizardStep.RecordPreview, форма.Шаг);
            Assert.Equal(2, форма.ЧислоПоказанныхЗаписей);
        }
        finally
        {
            File.Delete(путь);
        }
    }

    /// <summary>
    /// Финальный диалог пост-действия выгрузки содержит ровно три кнопки
    /// с подписями ТЗ §8.12, функция 160.
    /// </summary>
    [Fact]
    public void ДиалогПостДействия_СодержитТриКнопки()
    {
        using var диалог = new ДиалогПостДействия();

        var кнопки = диалог.Controls.OfType<Button>().ToList();

        Assert.Equal(3, кнопки.Count);

        Assert.Equal(
            new[] { "Открыть файл", "Открыть папку", "Закрыть" },
            кнопки.Select(кнопка => кнопка.Text).ToArray());

        Assert.Contains(
            диалог.Controls.OfType<Label>(),
            надпись => надпись.Text.Contains(
                "Сохранение файлов завершено, что вы хотите сделать?",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Мастер создаёт новую категорию адресной книги и назначает её
    /// категорией импорта (ТЗ §8.12, функция 159, шаг 2: «выбор категории
    /// назначения или создание новой»).
    /// </summary>
    [Fact]
    public async Task Мастер_СоздаётНовуюКатегорию()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new GroupRepository(база.Session);

        using var форма = new FormImportWizard(new ImportWizardController(null, репозиторий));

        var категория = await форма.СоздатьКатегориюAsync("Поставщики");

        Assert.NotNull(категория);
        Assert.Equal("Поставщики", категория!.Name);
        Assert.True(категория.Id > 0);

        Assert.Equal(категория.Id, форма.Контроллер.GroupId);

        var сохранённые = await репозиторий.GetAllAsync();

        Assert.Contains(сохранённые, группа => группа.Name == "Поставщики");
    }

    /// <summary>
    /// Регрессия F-E4: переход на шаг «Начало импорта и категория» загружает
    /// категории в список. Прежде ОбновитьКатегории вызывался под флагом
    /// _обновление и молча пропускался.
    /// </summary>
    [Fact]
    public async Task Мастер_ШагНачала_КатегорииЗагружаютсяВСписок()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new GroupRepository(база.Session);

        await репозиторий.AddAsync(new Picket302.Domain.Entities.Group { Name = "Клиенты" });

        var контроллер = new ImportWizardController(null, репозиторий);

        using var форма = new FormImportWizard(контроллер);

        var путь = ЗаписатьФайл();

        try
        {
            Assert.True(форма.ВыбратьФайл(путь));
            Assert.True(форма.ПерейтиВперёд());

            Assert.Equal(ImportWizardStep.StartRowAndGroup, форма.Шаг);

            // Загрузка категорий асинхронная: ожидаем появления пунктов.
            var крайнийСрок = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < крайнийСрок && форма.ПунктыКатегорий.Count == 0)
            {
                await Task.Delay(50);
            }

            // «(без категории)» и «Создать категорию…» плюс загруженные группы.
            Assert.True(
                форма.ПунктыКатегорий.Count >= 3,
                $"Категории должны быть загружены в список; фактически: [{string.Join("; ", форма.ПунктыКатегорий)}].");

            Assert.Contains(
                форма.ПунктыКатегорий,
                элемент => элемент is Picket302.Domain.Entities.Group группа && группа.Name == "Клиенты");
        }
        finally
        {
            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }
}
