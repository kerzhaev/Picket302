using Picket302.Domain.Common;
using Picket302.Infrastructure.Text;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка склонения ФИО и адресов (ТЗ §8.1, функция 6: «Нормализация и
/// склонение русских ФИО и адресов»).
/// </summary>
public sealed class RussianDeclensionEngineTests
{
    private readonly RussianDeclensionEngine _движок = new();

    [Fact]
    public void Declinename_ДательныйПадеж_СклоняетВсеЧастиИмени()
    {
        var результат = _движок.Declinename("Иванов Иван Иванович", GrammaticalCase.Dative);

        Assert.Equal("Иванову Ивану Ивановичу", результат);
    }

    [Fact]
    public void Declinename_РодительныйПадеж_СклоняетВсеЧастиИмени()
    {
        var результат = _движок.Declinename("Иванов Иван Иванович", GrammaticalCase.Genitive);

        Assert.Equal("Иванова Ивана Ивановича", результат);
    }

    [Fact]
    public void Declinename_ТворительныйПадеж_СклоняетВсеЧастиИмени()
    {
        var результат = _движок.Declinename("Иванов Иван Иванович", GrammaticalCase.Instrumental);

        Assert.Equal("Ивановым Иваном Ивановичем", результат);
    }

    [Fact]
    public void Declinename_ЖенскоеФИО_СклоняетсяПоЖенскимОкончаниям()
    {
        var результат = _движок.Declinename("Петрова Анна Петровна", GrammaticalCase.Dative);

        Assert.Equal("Петровой Анне Петровне", результат);
    }

    [Fact]
    public void Declinename_ЖенскоеФИО_РодительныйПадеж()
    {
        var результат = _движок.Declinename("Петрова Анна Петровна", GrammaticalCase.Genitive);

        Assert.Equal("Петровой Анны Петровны", результат);
    }

    [Fact]
    public void Declinename_ИменительныйПадеж_ИмяНеИзменяется()
    {
        var результат = _движок.Declinename("Иванов Иван Иванович", GrammaticalCase.Nominative);

        Assert.Equal("Иванов Иван Иванович", результат);
    }

    [Fact]
    public void Declinename_ИностранноеИмя_ОстаетсяВИменительномПадеже()
    {
        var результат = _движок.Declinename("Джон Смит", GrammaticalCase.Dative);

        Assert.Equal("Джон Смит", результат);
    }

    [Fact]
    public void Declinename_ОдноСлово_Склоняется()
    {
        var результат = _движок.Declinename("Иванов", GrammaticalCase.Dative);

        Assert.Equal("Иванову", результат);
    }

    [Fact]
    public void Declinename_ПустоеИмя_ВозвращаетсяПустаяСтрока()
    {
        Assert.Equal(string.Empty, _движок.Declinename(string.Empty, GrammaticalCase.Dative));
    }

    [Fact]
    public void DeclineAddress_ГородВДательномПадеже_СклоняетсяТопоним()
    {
        var результат = _движок.DeclineAddress(
            "г. Москва, ул. Тверская, д. 12",
            GrammaticalCase.Dative);

        Assert.Equal("г. Москве, ул. Тверская, д. 12", результат);
    }

    [Fact]
    public void DeclineAddress_ГородВРодительномПадеже_СклоняетсяТопоним()
    {
        var результат = _движок.DeclineAddress(
            "г. Москва, ул. Тверская, д. 12",
            GrammaticalCase.Genitive);

        Assert.Equal("г. Москвы, ул. Тверская, д. 12", результат);
    }

    [Fact]
    public void DeclineAddress_ГородБезСокращения_ТожеСклоняется()
    {
        var результат = _движок.DeclineAddress("Москва", GrammaticalCase.Dative);

        Assert.Equal("Москве", результат);
    }

    [Fact]
    public void DeclineAddress_Тверь_СклоняетсяПоМягкомуЗнаку()
    {
        var результат = _движок.DeclineAddress("г. Тверь", GrammaticalCase.Dative);

        Assert.Equal("г. Твери", результат);
    }

    [Fact]
    public void DeclineAddress_Улица_НеСклоняется()
    {
        var результат = _движок.DeclineAddress(
            "ул. Тверская, д. 12",
            GrammaticalCase.Dative);

        Assert.Equal("ул. Тверская, д. 12", результат);
    }

    [Fact]
    public void DeclineAddress_ИменительныйПадеж_АдресНеИзменяется()
    {
        var результат = _движок.DeclineAddress(
            "г. Москва, ул. Тверская, д. 12",
            GrammaticalCase.Nominative);

        Assert.Equal("г. Москва, ул. Тверская, д. 12", результат);
    }

    /// <summary>
    /// Регрессия F-C2: слова на «-ия» проверяются до общей ветви «-я».
    /// Прежде «Мария» склонялась как «Марие».
    /// </summary>
    [Fact]
    public void Declinename_ИмяНаИя_СклоняетсяПоПравиламИя()
    {
        Assert.Equal("Марии", _движок.Declinename("Мария", GrammaticalCase.Genitive));
        Assert.Equal("Марии", _движок.Declinename("Мария", GrammaticalCase.Dative));
        Assert.Equal("Марией", _движок.Declinename("Мария", GrammaticalCase.Instrumental));
        Assert.Equal("Марии", _движок.Declinename("Мария", GrammaticalCase.Prepositional));
    }

    /// <summary>
    /// Регрессия F-C3: отчества «-ична» склоняются единым путём замены «на».
    /// Прежнее особое усечение «-ична» на 4 символа давало «Никитны».
    /// </summary>
    [Fact]
    public void Declinename_ОтчествоНаИчна_СклоняетсяБезУсечкиОсновы()
    {
        Assert.Equal("Никитичны", _движок.Declinename("Никитична", GrammaticalCase.Genitive));
        Assert.Equal("Никитичне", _движок.Declinename("Никитична", GrammaticalCase.Dative));
        Assert.Equal("Никитичной", _движок.Declinename("Никитична", GrammaticalCase.Instrumental));
        Assert.Equal("Ильиничны", _движок.Declinename("Ильинична", GrammaticalCase.Genitive));
    }

    /// <summary>
    /// Регрессия F-C3: отчество «-овна» и фамилии/имена на «-а»/-ый не
    /// повреждены единым путём.
    /// </summary>
    [Fact]
    public void Declinename_ЖенскиеИменаИФамилии_НеЛомаются()
    {
        Assert.Equal("Петровны", _движок.Declinename("Петровна", GrammaticalCase.Genitive));
        Assert.Equal("Натальи", _движок.Declinename("Наталья", GrammaticalCase.Genitive));
        Assert.Equal("Иванову", _движок.Declinename("Иванов", GrammaticalCase.Dative));
    }
}
