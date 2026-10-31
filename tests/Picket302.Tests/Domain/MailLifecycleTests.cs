using Picket302.Domain.Entities;
using Picket302.Domain.Rules;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка жизненного цикла отправления (ТЗ §8.1, функция 24 «Жизненный цикл
/// отправления (4 даты)»; §3.1.3).
/// </summary>
public sealed class MailLifecycleTests
{
    private static Mail НовоеОтправление() => new()
    {
        CounterpartyId = 1,
        SenderId = 2,
        CopyCount = 1,
        Status = MailStatus.Draft,
        DateCreated = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void TransitionStatus_ЧерновикВНапечатано_ПроставляетДатуПечати()
    {
        var отправление = НовоеОтправление();
        var момент = new DateTime(2026, 10, 30, 10, 15, 0, DateTimeKind.Utc);

        отправление.TransitionStatus(MailStatus.Printed, момент);

        Assert.Equal(MailStatus.Printed, отправление.Status);
        Assert.Equal(момент, отправление.DatePrinted);
        Assert.Null(отправление.DateDispatched);
    }

    [Fact]
    public void TransitionStatus_ПолнаяЦепочка_ПроставляетВсеЧетыреДаты()
    {
        var отправление = НовоеОтправление();

        отправление.TransitionStatus(MailStatus.Printed, new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc));
        отправление.TransitionStatus(MailStatus.Dispatched, new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc));
        отправление.TransitionStatus(MailStatus.Confirmed, new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(MailStatus.Confirmed, отправление.Status);
        Assert.NotNull(отправление.DatePrinted);
        Assert.NotNull(отправление.DateDispatched);
        Assert.NotNull(отправление.DateConfirmation);
        Assert.Null(отправление.DateCanceled);
    }

    /// <summary>
    /// Переход в «Подтверждено» без промежуточного «Отправлено» недопустим
    /// (ТЗ §8.1, функция 24: «Нельзя перевести в «Подтверждено», минуя
    /// «Отправлено»»).
    /// </summary>
    [Fact]
    public void TransitionStatus_ПодтвержденоМинуяОтправлено_Отклоняется()
    {
        var отправление = НовоеОтправление();
        отправление.TransitionStatus(MailStatus.Printed, new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc));

        Assert.False(отправление.CanTransitionTo(MailStatus.Confirmed));
        Assert.Throws<InvalidOperationException>(
            () => отправление.TransitionStatus(MailStatus.Confirmed, DateTime.UtcNow));
        Assert.Equal(MailStatus.Printed, отправление.Status);
    }

    [Fact]
    public void TransitionStatus_ОтправленоМинуяНапечатано_Отклоняется()
    {
        var отправление = НовоеОтправление();

        Assert.False(отправление.CanTransitionTo(MailStatus.Dispatched));
        Assert.Throws<InvalidOperationException>(
            () => отправление.TransitionStatus(MailStatus.Dispatched, DateTime.UtcNow));
    }

    [Fact]
    public void TransitionStatus_ОтменаПослеПечати_ПроставляетДатуОтмены()
    {
        var отправление = НовоеОтправление();
        отправление.TransitionStatus(MailStatus.Printed, new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc));

        var момент = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        отправление.TransitionStatus(MailStatus.Canceled, момент);

        Assert.Equal(MailStatus.Canceled, отправление.Status);
        Assert.Equal(момент, отправление.DateCanceled);
    }

    /// <summary>
    /// Подтверждённое отправление — конечное состояние.
    /// </summary>
    [Fact]
    public void TransitionStatus_ПослеПодтвержденияПереходыЗапрещены()
    {
        var отправление = НовоеОтправление();
        отправление.TransitionStatus(MailStatus.Printed, DateTime.UtcNow);
        отправление.TransitionStatus(MailStatus.Dispatched, DateTime.UtcNow);
        отправление.TransitionStatus(MailStatus.Confirmed, DateTime.UtcNow);

        Assert.False(отправление.CanTransitionTo(MailStatus.Canceled));
        Assert.False(отправление.CanTransitionTo(MailStatus.Dispatched));
        Assert.Throws<InvalidOperationException>(
            () => отправление.TransitionStatus(MailStatus.Canceled, DateTime.UtcNow));
    }
}

/// <summary>
/// Проверка правила «ровно одна группа по умолчанию» (ТЗ §3.1.2).
/// </summary>
public sealed class DefaultGroupPolicyTests
{
    [Fact]
    public void Validate_ОднаГруппаПоУмолчанию_Принимается()
    {
        var группы = new[]
        {
            new Group { Id = 1, Name = "Неразобранные", IsDefault = true },
            new Group { Id = 2, Name = "Поставщики" },
        };

        var результат = DefaultGroupPolicy.Validate(группы);

        Assert.True(результат.IsSuccess);
    }

    [Fact]
    public void Validate_НетГруппыПоУмолчанию_Отклоняется()
    {
        var группы = new[] { new Group { Id = 1, Name = "Поставщики" } };

        var результат = DefaultGroupPolicy.Validate(группы);

        Assert.True(результат.IsFailure);
        Assert.Contains("нет", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ДвеГруппыПоУмолчанию_Отклоняется()
    {
        var группы = new[]
        {
            new Group { Id = 1, Name = "Неразобранные", IsDefault = true },
            new Group { Id = 2, Name = "Поставщики", IsDefault = true },
        };

        var результат = DefaultGroupPolicy.Validate(группы);

        Assert.True(результат.IsFailure);
        Assert.Contains("2", результат.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Назначение новой группы по умолчанию снимает признак с прежней:
    /// групп по умолчанию остаётся ровно одна.
    /// </summary>
    [Fact]
    public void ApplyDefault_СнимаетПризнакСОстальныхГрупп()
    {
        var группы = new List<Group>
        {
            new() { Id = 1, Name = "Неразобранные", IsDefault = true },
            new() { Id = 2, Name = "Поставщики" },
        };

        var результат = DefaultGroupPolicy.ApplyDefault(группы[1], группы);

        Assert.True(результат.IsSuccess);
        Assert.False(группы[0].IsDefault);
        Assert.True(группы[1].IsDefault);
        Assert.True(DefaultGroupPolicy.Validate(группы).IsSuccess);
    }

    [Fact]
    public void ApplyDefault_ГруппыНетВСправочнике_Отклоняется()
    {
        var группы = new List<Group> { new() { Id = 1, Name = "Неразобранные", IsDefault = true } };
        var посторонняя = new Group { Id = 9, Name = "Поставщики" };

        var результат = DefaultGroupPolicy.ApplyDefault(посторонняя, группы);

        Assert.True(результат.IsFailure);
    }
}

/// <summary>
/// Проверка пресечения циклических ссылок в иерархии папок (ТЗ §3.1.11).
/// </summary>
public sealed class FolderHierarchyTests
{
    private static readonly Folder Корень = new() { Id = 1, Name = "Корень", ParentId = null };
    private static readonly Folder Внук = new() { Id = 3, Name = "Внук", ParentId = 2 };
    private static readonly Folder Ребёнок = new() { Id = 2, Name = "Ребёнок", ParentId = 1 };

    [Fact]
    public void WouldCreateCycle_ПапкаВСвоемПоддереве_ОпределяетсяЦикл()
    {
        var папки = new[] { Корень, Ребёнок, Внук };

        Assert.True(FolderHierarchy.WouldCreateCycle(1, 3, папки));
        Assert.True(FolderHierarchy.WouldCreateCycle(2, 2, папки));
    }

    [Fact]
    public void WouldCreateCycle_КорректныйРодитель_ЦиклаНет()
    {
        var папки = new[] { Корень, Ребёнок, Внук };

        Assert.False(FolderHierarchy.WouldCreateCycle(3, 1, папки));
        Assert.False(FolderHierarchy.WouldCreateCycle(2, null, папки));
    }

    [Fact]
    public void AssignParent_ПопыткаСоздатьЦикл_Отклоняется()
    {
        var папки = new[] { Корень, Ребёнок, Внук };

        var результат = FolderHierarchy.AssignParent(Корень, Внук.Id, папки);

        Assert.True(результат.IsFailure);
        Assert.Null(Корень.ParentId);
    }

    [Fact]
    public void AssignParent_КорректныйРодитель_Назначается()
    {
        var папки = new[] { Корень, Ребёнок, Внук };

        var результат = FolderHierarchy.AssignParent(Внук, Корень.Id, папки);

        Assert.True(результат.IsSuccess);
        Assert.Equal(Корень.Id, Внук.ParentId);
    }
}

/// <summary>
/// Проверка ключа кэша кодов (ТЗ §3.1.8: SHA-256 хэш строки данных).
/// </summary>
public sealed class QrCacheTests
{
    [Fact]
    public void ComputePayloadHash_ОднаИТаЖеСтрока_ДаетОдинаковыйХэш()
    {
        var первый = QrCache.ComputePayloadHash("101000");
        var второй = QrCache.ComputePayloadHash("101000");

        Assert.Equal(первый, второй);
        Assert.Equal(QrCache.PayloadHashLength, первый.Length);
        Assert.Equal(64, первый.Length);
    }

    [Fact]
    public void ComputePayloadHash_РазныеСтроки_ДаютРазныеХэши()
    {
        Assert.NotEqual(
            QrCache.ComputePayloadHash("101000"),
            QrCache.ComputePayloadHash("101001"));
    }

    /// <summary>
    /// Хэш вычисляется по алгоритму SHA-256: эталонное значение для пустой
    /// строки и для «abc».
    /// </summary>
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void ComputePayloadHash_СоответствуетSha256(string данные, string ожидаемыйХэш)
    {
        Assert.Equal(ожидаемыйХэш, QrCache.ComputePayloadHash(данные));
    }

    [Fact]
    public void Create_ЗаполняетКлючИДанные()
    {
        var запись = QrCache.Create("101000", new byte[] { 1, 2, 3 }, DateTime.UtcNow);

        Assert.Equal(QrCache.ComputePayloadHash("101000"), запись.PayloadHash);
        Assert.Equal(3, запись.ImageBlob.Length);
    }
}
