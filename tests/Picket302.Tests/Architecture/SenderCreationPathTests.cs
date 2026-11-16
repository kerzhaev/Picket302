using Xunit;

namespace Picket302.Tests.Architecture;

/// <summary>
/// Архитектурный контроль пути создания отправителя (дефект D1 приёмки
/// 16.11.2026).
/// </summary>
/// <remarks>
/// <para>
/// Проверка идёт по исходному тексту <c>MainShell</c>: карточка отправителя
/// обязана сохраняться полным путём контрагента. Усечённый путь через
/// <c>AgentDto</c> терял сокращённое наименование, телефон, почту, примечание
/// и адресные срезы — именно это и было найдено на живом пилоте.
/// </para>
/// <para>
/// Тест намеренно смотрит на код, а не на результат: ошибка была не в
/// хранилище, а в том, какие поля UI-слой передавал на сохранение.
/// </para>
/// </remarks>
public sealed class SenderCreationPathTests
{
    private const string SolutionFileName = "Picket302.sln";

    [Fact]
    public void Создание_отправителя_идёт_полной_карточкой()
    {
        var корень = НайтиКореньРепозитория();
        var путь = Path.Combine(корень, "src", "Picket302.App", "MainShell.cs");

        Assert.True(File.Exists(путь), $"Не найден файл: {путь}");

        var текст = File.ReadAllText(путь);
        var метод = ИзвлечьМетод(текст, "СоздатьОтправителя");

        Assert.NotNull(метод);

        Assert.DoesNotContain("new AgentDto", метод, StringComparison.Ordinal);
        Assert.Contains("СохранитьКонтрагентаAsync", метод, StringComparison.Ordinal);
        Assert.Contains("IsSender = true", метод, StringComparison.Ordinal);
        Assert.Contains("ПользовательскиеПоля", метод, StringComparison.Ordinal);

        // Текущий отправитель назначается только после сохранения: иначе
        // неудачное сохранение меняло бы состояние окна.
        var индексСохранения = метод!.IndexOf("СохранитьКонтрагентаAsync", StringComparison.Ordinal);
        var индексНазначения = метод.IndexOf("SetGlobalSender", StringComparison.Ordinal);

        Assert.True(индексСохранения >= 0, "В методе нет сохранения контрагента.");
        Assert.True(
            индексНазначения > индексСохранения,
            "Текущий отправитель назначается до сохранения карточки.");
    }

    private static string НайтиКореньРепозитория()
    {
        var каталог = new DirectoryInfo(AppContext.BaseDirectory);

        while (каталог is not null)
        {
            if (File.Exists(Path.Combine(каталог.FullName, SolutionFileName)))
            {
                return каталог.FullName;
            }

            каталог = каталог.Parent;
        }

        throw new InvalidOperationException(
            $"Файл решения {SolutionFileName} не найден выше каталога {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Вырезает тело метода по имени: от объявления до парной закрывающей скобки.
    /// </summary>
    private static string? ИзвлечьМетод(string текст, string имя)
    {
        // Ищем именно объявление метода: вызовы вида «СоздатьОтправителя();»
        // встречаются раньше по тексту и не содержат тела.
        var объявление = текст.IndexOf("void " + имя + "(", StringComparison.Ordinal);

        if (объявление < 0)
        {
            return null;
        }

        var открывающая = текст.IndexOf('{', объявление);

        if (открывающая < 0)
        {
            return null;
        }

        var глубина = 0;

        for (var индекс = открывающая; индекс < текст.Length; индекс++)
        {
            if (текст[индекс] == '{')
            {
                глубина++;
            }
            else if (текст[индекс] == '}')
            {
                глубина--;

                if (глубина == 0)
                {
                    return текст[открывающая..(индекс + 1)];
                }
            }
        }

        return null;
    }
}
