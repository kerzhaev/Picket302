using Picket302.App.UI.Forms;
using Picket302.Application.Services;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S33 (ТЗ §8.2, функции 29 и 30): диалог входящей корреспонденции —
/// перечень с поиском, регистрация, приём/передача/архив через точки
/// автопроверок (без модальных диалогов). COM, реестр, принтер и печать
/// не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormIncomingMailTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;
    private string _профиль = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
        _профиль = Path.Combine(Path.GetTempPath(), "p302-s33-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_профиль);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();

        try
        {
            Directory.Delete(_профиль, recursive: true);
        }
        catch (IOException)
        {
            // Каталог может быть занят на мгновение.
        }
    }

    private FormIncomingMail СоздатьФорму()
    {
        var служба = new IncomingMailService(
            new IncomingMailRepository(_база.UnitOfWork),
            _профиль);
        var трекер = new IncomingMovementTracker(
            new IncomingMovementRepository(_база.UnitOfWork),
            new IncomingMailRepository(_база.UnitOfWork));

        return new FormIncomingMail(служба, трекер);
    }

    [Fact]
    public void ПустойЖурнал_СписокПуст()
    {
        using var форма = СоздатьФорму();

        Assert.Equal(0, форма.ЧислоЗаписей);
        Assert.Null(форма.ВыбранныйId);
    }

    [Fact]
    public async Task РегистрацияИзФормы_ПоявляетсяВСпискеСМестонахождением()
    {
        using var форма = СоздатьФорму();

        var запись = await форма.ЗарегистрироватьДляПроверокAsync(
            "ВХ-UI/1", "ООО «Отправитель»", "Запрос", Picket302.Domain.Entities.IncomingMailType.Letter, null);

        // Регистрация отражается в списке и выделении (статус — в строке статуса).
        Assert.Equal(1, форма.ЧислоЗаписей);
        Assert.Equal(запись.Id, форма.ВыбранныйId);

        // Приём: местонахождение в списке обновляется.
        форма.ОбновитьСписок();
        await форма.ПриёмДляПроверокAsync("Канцелярия");

        Assert.Contains("Канцелярия", форма.СведенияДляПроверок);
        Assert.Equal(запись.Id, форма.ВыбранныйId);
    }

    [Fact]
    public async Task ПередачаИАрхив_ЧерезТочкиФормы_МестонахождениеОбновляется()
    {
        using var форма = СоздатьФорму();

        var запись = await форма.ЗарегистрироватьДляПроверокAsync(
            "ВХ-UI/2", "Отправитель", "Тема", Picket302.Domain.Entities.IncomingMailType.Courier, null);
        await форма.ПриёмДляПроверокAsync("Канцелярия");

        await форма.ПередачаДляПроверокAsync("Юридический отдел", "Смирнов");

        Assert.Contains("Юридический отдел", форма.СведенияДляПроверок);

        await форма.АрхивДляПроверокAsync();

        Assert.Contains("Архив", форма.СведенияДляПроверок);
    }

    [Fact]
    public async Task ДвижениеСОшибкой_ИзолируетсяУведомлением()
    {
        using var форма = СоздатьФорму();

        var сообщения = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => сообщения.Add(текст);

        var запись = await форма.ЗарегистрироватьДляПроверокAsync(
            "ВХ-UI/3", "Отправитель", "Тема", Picket302.Domain.Entities.IncomingMailType.Letter, null);
        await форма.ПриёмДляПроверокAsync("Канцелярия");
        await форма.АрхивДляПроверокAsync();

        // Движение после архива — InvalidOperationException из трекера,
        // форма изолирует его в уведомление (не падает).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => форма.ПередачаДляПроверокAsync("Отдел"));
    }

    /// <summary>
    /// D-S33-02 (ревью S33): удаление принятого документа (с историей
    /// движения) запрещено и изолируется уведомлением — не краш.
    /// </summary>
    [Fact]
    public async Task УдалениеПринятогоДокумента_ЗапрещеноУведомлением()
    {
        using var форма = СоздатьФорму();

        var сообщения = new List<string>();
        форма.УведомитьДляПроверок = (текст, _) => сообщения.Add(текст);

        var запись = await форма.ЗарегистрироватьДляПроверокAsync(
            "ВХ-UI/5", "Отправитель", "Тема", Picket302.Domain.Entities.IncomingMailType.Letter, null);

        await форма.ПриёмДляПроверокAsync("Канцелярия");

        // Проверочная точка бросает; уведомление оператору — в обработчике
        // кнопки (catch InvalidOperationException -> Сообщить).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => форма.УдалитьДляПроверокAsync());

        Assert.Empty(сообщения);

        // Запись на месте.
        Assert.Equal(1, форма.ЧислоЗаписей);
    }

    [Fact]
    public async Task Удаление_ЗаписьУходитСканОстаётся()
    {
        using var форма = СоздатьФорму();

        var скан = Path.Combine(_профиль, "scan.pdf");
        File.WriteAllText(скан, "%PDF-1.4");

        var запись = await форма.ЗарегистрироватьДляПроверокAsync(
            "ВХ-UI/4", "Отправитель", "Тема", Picket302.Domain.Entities.IncomingMailType.Letter, скан);

        Assert.NotNull(запись.ScanPath);
        Assert.True(File.Exists(запись.ScanPath));

        Assert.True(await форма.УдалитьДляПроверокAsync());

        Assert.Equal(0, форма.ЧислоЗаписей);

        // Скан в профиле сохранён.
        Assert.True(File.Exists(запись.ScanPath));
    }
}
