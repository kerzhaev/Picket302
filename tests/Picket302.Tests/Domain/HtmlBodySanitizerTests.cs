using Picket302.Domain.Email;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка очистки разметки письма (ТЗ §8.7, функция 96: «тело письма в HTML:
/// очистка разметки от исполняемых вставок»).
/// </summary>
public sealed class HtmlBodySanitizerTests
{
    [Fact]
    public void Sanitize_ЭлементScript_УдаляетЭлементВместеССодержимым()
    {
        var разметка = "<p>Текст</p><script>alert('x')</script><p>Ещё текст</p>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("<script", результат, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", результат, StringComparison.Ordinal);
        Assert.Contains("Текст", результат, StringComparison.Ordinal);
        Assert.Contains("Ещё текст", результат, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_ОбработчикСобытия_УдаляетОбработчикOnclick()
    {
        var разметка = "<a href=\"https://example.ru\" onclick=\"alert('x')\">Ссылка</a>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("onclick", результат, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", результат, StringComparison.Ordinal);
        Assert.Contains("Ссылка", результат, StringComparison.Ordinal);
        Assert.Contains("https://example.ru", результат, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_СсылкаНаJavaScript_ОчищаетОпаснуюСхему()
    {
        var разметка = "<a href=\"javascript:alert('x')\">Переход</a>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("javascript:", результат, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Переход", результат, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_ОбычнаяРазметка_НеПортитСодержимое()
    {
        var разметка = "<p>Обычный <b>жирный</b> текст</p><ul><li>Пункт</li></ul>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.Equal(разметка, результат);
    }

    /// <summary>Регрессия F-D6: значение схемы без кавычек вырезается.</summary>
    [Fact]
    public void Sanitize_СхемаБезКавычек_Очищается()
    {
        var разметка = "<a href=javascript:alert(1)>Переход</a>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("javascript:", результат, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", результат, StringComparison.Ordinal);
    }

    /// <summary>Регрессия F-D6: сущности внутри имени схемы декодируются
    /// перед проверкой.</summary>
    [Fact]
    public void Sanitize_СущностиВнутриСхемы_Очищаются()
    {
        var разметка = "<a href=\"java&#115;cript:alert(1)\">Переход</a>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("alert(1)", результат, StringComparison.Ordinal);
        Assert.DoesNotContain("&#115;", результат, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Регрессия F-D6: пробельные символы внутри имени схемы
    /// удаляются перед проверкой.</summary>
    [Fact]
    public void Sanitize_ПробелыВнутриСхемы_Очищаются()
    {
        var разметка = "<a href=\"java\tscript:alert(1)\">Переход</a>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("alert(1)", результат, StringComparison.Ordinal);
    }

    /// <summary>Регрессия F-D6: обработчик после «/» без пробела вырезается.</summary>
    [Fact]
    public void Sanitize_ОбработчикПослеСлеша_Вырезается()
    {
        var разметка = "<img/src=x/onerror=alert(1)>";

        var результат = HtmlBodySanitizer.Sanitize(разметка);

        Assert.DoesNotContain("onerror", результат, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", результат, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_ПустаяРазметка_ВозвращаетПустуюСтроку()
    {
        Assert.Equal(string.Empty, HtmlBodySanitizer.Sanitize(null));
        Assert.Equal(string.Empty, HtmlBodySanitizer.Sanitize(string.Empty));
    }
}
