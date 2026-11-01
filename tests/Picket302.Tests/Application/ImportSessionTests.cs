using System.Text;
using Picket302.Application.Import;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки движка мастера импорта адресной книги (ТЗ §8.12, функция 159:
/// «Мастер импорта адресной книги»).
/// </summary>
public sealed class ImportSessionTests
{
    private static string ЗаписатьФайл(string содержимое, string расширение = ".csv")
    {
        var путь = Path.Combine(
            Path.GetTempPath(),
            "picket302_import_" + Guid.NewGuid().ToString("N") + расширение);

        File.WriteAllText(путь, содержимое, new UTF8Encoding(false));

        return путь;
    }

    private static ImportStartOptions Параметры(string путь, ICounterpartyRepository? репозиторий = null)
        => new()
        {
            FilePath = путь,
            Delimiter = ';',
            EncodingName = "utf-8",
            Repository = репозиторий,
        };

    [Fact]
    public async Task ПолитикиИмпорта_ОтбираютЗаписиИФормируютИтог()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var путь = ЗаписатьФайл(
            "Наименование;ИНН\r\n"
            + "ООО «Первая»;7707083893\r\n"
            + "ООО «Вторая»;7707083894\r\n");

        var результат = ImportSession.Start(путь, Параметры(путь, репозиторий));

        Assert.True(результат.IsSuccess, результат.Error);

        var сессия = результат.Value!;
        var сводка = сессия.ValidateAll();

        Assert.Equal(2, сводка.TotalCount);
        Assert.Equal(0, сводка.ErrorCount);
        Assert.Equal(2, сводка.UnverifiedCount);

        // Политика «Добавить только проверенных»: импортируется только проверенная запись.
        сессия.MarkVerified(сессия.Records[0]);

        var толькоПроверенных = сессия.Commit(ImportPolicy.VerifiedOnly);

        Assert.Equal(1, толькоПроверенных.Created);
        Assert.Equal(0, толькоПроверенных.Unverified);
        Assert.Equal(1, толькоПроверенных.Skipped);

        // Политика «Только без ошибок»: импортируется и непроверенная запись без ошибок.
        var безОшибок = сессия.Commit(ImportPolicy.WithoutErrors);

        Assert.Equal(1, безОшибок.Created);
        Assert.Equal(1, безОшибок.Unverified);
        Assert.Equal(1, безОшибок.Skipped);

        // Политика «Все»: обе записи уже есть в базе и не создаются повторно.
        var все = сессия.Commit(ImportPolicy.All);

        Assert.Equal(0, все.Created);
        Assert.Equal(0, все.Updated);
        Assert.Equal(2, все.Skipped);

        var выборка = await репозиторий.GetPagedAsync(new PageRequest(0, 50));

        Assert.Equal(2, выборка.TotalCount);
    }

    [Fact]
    public async Task ПолитикаВсе_ИмпортируетЗаписиСОшибкамиИСчитаетИх()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var путь = ЗаписатьФайл(
            "Наименование;ИНН\r\n"
            + "ООО «Первая»;7707083893\r\n"
            + "ООО «Вторая»;123\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь, репозиторий)).Value!;
        var сводка = сессия.ValidateAll();

        Assert.Equal(2, сводка.TotalCount);
        Assert.Equal(1, сводка.ErrorCount);

        var безОшибок = сессия.Commit(ImportPolicy.WithoutErrors);

        Assert.Equal(1, безОшибок.Created);
        Assert.Equal(0, безОшибок.Errors);

        var все = сессия.Commit(ImportPolicy.All);

        Assert.Equal(1, все.Created);
        Assert.Equal(1, все.Errors);
    }

    [Fact]
    public void ФильтрПредпросмотра_ОтбираетЗаписиСОшибками()
    {
        var путь = ЗаписатьФайл(
            "Наименование;ИНН\r\n"
            + "ООО «Первая»;7707083893\r\n"
            + "ООО «Вторая»;123\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь)).Value!;
        сессия.ValidateAll();

        Assert.Equal(2, сессия.GetRecords().Count);
        Assert.Single(сессия.GetRecords(толькоСОшибками: true));
    }

    [Fact]
    public void ПропущеннаяОператоромЗапись_НеИмпортируется()
    {
        var путь = ЗаписатьФайл(
            "Наименование;ИНН\r\n"
            + "ООО «Первая»;7707083893\r\n"
            + "ООО «Вторая»;7707083894\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь)).Value!;

        сессия.MarkSkipped(сессия.Records[1]);

        Assert.True(сессия.Records[1].IsSkipped);
        Assert.Equal(1, сессия.Records.Count(запись => !запись.IsSkipped));
    }

    [Fact]
    public void ЗначениеДлиннее255Символов_ОбрезаетсяСПредупреждением()
    {
        var путь = ЗаписатьФайл("Наименование\r\n" + new string('А', 300) + "\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь)).Value!;
        сессия.ValidateAll();

        var запись = Assert.Single(сессия.Records);

        Assert.Equal(255, запись.Draft.Name.Length);
        Assert.Contains(запись.Warnings, предупреждение => предупреждение.Contains("255"));
    }

    [Fact]
    public void ФайлСоСлитымиПолями_ОтклоняетсяДоНачалаИмпорта()
    {
        var путь = ЗаписатьФайл(
            "Наименование\r\n"
            + "Иванов Иван Иванович ООО Ромашка 350000 г. Краснодар ул. Красная д. 35\r\n");

        var результат = ImportSession.Start(путь, Параметры(путь));

        Assert.True(результат.IsFailure);
        Assert.Equal(ImportSession.СообщениеСлитыхПолей, результат.Error);
        Assert.Contains("Не удалось разделить поля", результат.Error);
    }

    [Fact]
    public void ПоврежденнаяКнигаXlsx_ПредлагаетСохранитьФайлКакCsvИлиHtml()
    {
        var путь = ЗаписатьФайл("Это не книга XLSX", ".xlsx");

        var результат = ImportSession.Start(путь, Параметры(путь));

        Assert.True(результат.IsFailure);
        Assert.Contains("CSV", результат.Error);
        Assert.Contains("HTML", результат.Error);
        Assert.Equal(ImportSession.СообщениеXlsx, результат.Error);
    }

    [Fact]
    public void СопоставлениеСтолбцов_ПредлагаетсяПоИменамСтолбцов()
    {
        var путь = ЗаписатьФайл(
            "ФИО;Название организации;Почтовый адрес;Сокращенное\r\n"
            + "Иванов Иван Иванович;ООО «Ромашка»;350000, г. Краснодар, ул. Красная;Ромашка\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь)).Value!;

        Assert.Contains(сессия.Bindings, привязка =>
            привязка.ColumnIndex == 0 && привязка.Slot == ImportFieldSlot.FullName);
        Assert.Contains(сессия.Bindings, привязка =>
            привязка.ColumnIndex == 1 && привязка.Slot == ImportFieldSlot.OrganizationName);
        Assert.Contains(сессия.Bindings, привязка =>
            привязка.ColumnIndex == 2 && привязка.Slot == ImportFieldSlot.PostalAddress);
        Assert.Contains(сессия.Bindings, привязка =>
            привязка.ColumnIndex == 3 && привязка.Slot == ImportFieldSlot.ShortName);

        var запись = Assert.Single(сессия.Records);

        Assert.Equal("ООО «Ромашка»", запись.Draft.Name);
        Assert.Equal("Ромашка", запись.Draft.ShortName);
        Assert.Equal("350000", запись.Draft.PostalZip);
    }

    [Fact]
    public void ПравкаЗаписиОператором_ПерепроверяетЗапись()
    {
        var путь = ЗаписатьФайл("Наименование;ИНН\r\nООО «Первая»;123\r\n");

        var сессия = ImportSession.Start(путь, Параметры(путь)).Value!;
        сессия.ValidateAll();

        var запись = Assert.Single(сессия.Records);

        Assert.True(запись.HasErrors);

        запись.Draft.Inn = "7707083893";
        сессия.UpdateRecord(запись);

        Assert.False(запись.HasErrors);
        Assert.Equal(ImportRecordStatus.Unverified, запись.Status);
    }

    [Fact]
    public void КатегорияАдреснойКниги_СохраняетсяВЧерновикеЗаписи()
    {
        var путь = ЗаписатьФайл("Наименование\r\nООО «Первая»\r\n");

        var параметры = Параметры(путь);
        параметры.GroupId = 42;

        var сессия = ImportSession.Start(путь, параметры).Value!;
        var запись = Assert.Single(сессия.Records);

        Assert.Equal(42, запись.Draft.GroupId);
        Assert.Equal(42, сессия.GroupId);
    }
}
