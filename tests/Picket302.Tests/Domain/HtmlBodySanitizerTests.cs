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

    [Fact]
    public void Sanitize_ПустаяРазметка_ВозвращаетПустуюСтроку()
    {
        Assert.Equal(string.Empty, HtmlBodySanitizer.Sanitize(null));
        Assert.Equal(string.Empty, HtmlBodySanitizer.Sanitize(string.Empty));
    }
}
