using Picket302.Domain.Entities;
using Picket302.Domain.Rules;

using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверки правил целостности доменного слоя (ТЗ §3.1.19):
/// единственная группа по умолчанию и пресечение циклов в дереве папок.
/// </summary>
public sealed class IntegrityRulesTests
{
    /// <summary>
    /// Группу по умолчанию удалить нельзя — иначе в справочнике не останется
    /// ни одной группы по умолчанию (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public void Правило_ГруппуПоУмолчаниюУдалитьНельзя()
    {
        var группа = new Group { Id = 1, Name = Group.DefaultGroupName, IsDefault = true };

        var проверка = DefaultGroupPolicy.ValidateRemoval(группа);

        Assert.True(проверка.IsFailure);
        Assert.Contains("по умолчанию", проверка.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Обычную группу удалять можно.
    /// </summary>
    [Fact]
    public void Правило_ОбычнуюГруппуУдалятьМожно()
    {
        var группа = new Group { Id = 2, Name = "Поставщики", IsDefault = false };

        var проверка = DefaultGroupPolicy.ValidateRemoval(группа);

        Assert.True(проверка.IsSuccess);
        Assert.Equal(группа, проверка.Value);
    }

    /// <summary>
    /// Признак группы по умолчанию нельзя снять обычным изменением карточки:
    /// смена группы по умолчанию выполняется только назначением (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public void Правило_ПризнакГруппыПоУмолчаниюНельзяСнятьОбычнымИзменением()
    {
        var сохранённая = new Group { Id = 1, Name = Group.DefaultGroupName, IsDefault = true };
        var изменённая = new Group { Id = 1, Name = Group.DefaultGroupName, IsDefault = false };

        var проверка = DefaultGroupPolicy.ValidateDefaultFlag(сохранённая, изменённая);

        Assert.True(проверка.IsFailure);
        Assert.Contains("Нельзя снять признак группы по умолчанию", проверка.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Переименование группы по умолчанию допустимо: признак не меняется.
    /// </summary>
    [Fact]
    public void Правило_ПереименованиеГруппыПоУмолчаниюДопустимо()
    {
        var сохранённая = new Group { Id = 1, Name = Group.DefaultGroupName, IsDefault = true };
        var изменённая = new Group { Id = 1, Name = "Неразобранные (архив)", IsDefault = true };

        var проверка = DefaultGroupPolicy.ValidateDefaultFlag(сохранённая, изменённая);

        Assert.True(проверка.IsSuccess);
    }

    /// <summary>
    /// Проверка цикла не зацикливается на дереве, в котором цикл уже есть:
    /// обход цепочки родителей прерывается (ТЗ §3.1.11).
    /// </summary>
    [Fact]
    public void Правило_ПроверкаЦиклаЗавершаетсяНаДеревеСЦиклом()
    {
        var дерево = new[]
        {
            new Folder { Id = 1, Name = "А", ParentId = 2 },
            new Folder { Id = 2, Name = "Б", ParentId = 1 },
            new Folder { Id = 3, Name = "В", ParentId = null },
        };

        Assert.True(FolderHierarchy.ContainsCycle(дерево));
        Assert.True(FolderHierarchy.WouldCreateCycle(3, 1, дерево));
    }

    /// <summary>
    /// Дерево без циклов проверку цикла проходит.
    /// </summary>
    [Fact]
    public void Правило_ДеревоБезЦиклаНеСчитаетсяЦиклическим()
    {
        var дерево = new[]
        {
            new Folder { Id = 1, Name = "А", ParentId = null },
            new Folder { Id = 2, Name = "Б", ParentId = 1 },
            new Folder { Id = 3, Name = "В", ParentId = 2 },
        };

        Assert.False(FolderHierarchy.ContainsCycle(дерево));
        Assert.False(FolderHierarchy.WouldCreateCycle(3, 1, дерево));
        Assert.True(FolderHierarchy.WouldCreateCycle(1, 3, дерево));
    }

    /// <summary>
    /// Перемещение папки отклоняется, если в дереве уже есть циклическая
    /// ссылка: правило циклов пресекается (ТЗ §3.1.11).
    /// </summary>
    [Fact]
    public void Правило_ПеремещениеОтклоняетсяПриЦиклеВДереве()
    {
        var папка = new Folder { Id = 3, Name = "В", ParentId = null };

        var дерево = new[]
        {
            new Folder { Id = 1, Name = "А", ParentId = 2 },
            new Folder { Id = 2, Name = "Б", ParentId = 1 },
        };

        var назначение = FolderHierarchy.AssignParent(папка, 1, дерево);

        Assert.True(назначение.IsFailure);
        Assert.Contains("циклическую ссылку", назначение.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(папка.ParentId);
    }
}
