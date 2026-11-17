using Xunit;

namespace Picket302.Tests.Architecture;

/// <summary>
/// Архитектурный контроль пути создания отправителя (дефект D1 приёмки
/// 16.11.2026).
/// </summary>
/// <remarks>
/// <para>
/// Проверка идёт по исходному тексту: карточка отправителя обязана сохраняться
/// полным путём контрагента. Усечённый путь через <c>AgentDto</c> терял
/// сокращённое наименование, телефон, почту, примечание и адресные срезы —
/// именно это и было найдено на живом пилоте.
/// </para>
/// <para>
/// Логика вынесена в <c>SenderRegistrationService</c>, поэтому проверяются три
/// места: оболочка не собирает <c>AgentDto</c>, служба регистрации ставит
/// признак отправителя и вызывает сохранение карточки, адаптер ведёт в
/// <c>СохранитьКонтрагентаAsync</c>.
/// </para>
/// </remarks>
public sealed class SenderCreationPathTests
{
    private const string SolutionFileName = "Picket302.sln";

    [Fact]
    public void Создание_отправителя_идёт_полной_карточкой()
    {
        var корень = НайтиКореньРепозитория();

        var оболочка = Path.Combine(корень, "src", "Picket302.App", "MainShell.cs");
        var служба = Path.Combine(корень, "src", "Picket302.App", "Shared", "SenderRegistrationService.cs");

        Assert.True(File.Exists(оболочка), $"Не найден файл: {оболочка}");
        Assert.True(File.Exists(служба), $"Не найден файл: {служба}");

        var метод = ИзвлечьМетод(File.ReadAllText(оболочка), "СоздатьОтправителя");

        Assert.NotNull(метод);
        Assert.DoesNotContain("new AgentDto", метод, StringComparison.Ordinal);
        Assert.Contains("SenderRegistrationService", метод, StringComparison.Ordinal);
        Assert.Contains("ПользовательскиеПоля", метод, StringComparison.Ordinal);

        var текстСлужбы = File.ReadAllText(служба);

        Assert.Contains("карточка.IsSender = true", текстСлужбы, StringComparison.Ordinal);
        Assert.Contains("СохранитьКарточкуAsync", текстСлужбы, StringComparison.Ordinal);

        // Текущий отправитель назначается только после сохранения и только при
        // полном успехе: строка SetGlobalSender идёт после вызова сохранения.
        var индексСохранения = текстСлужбы.IndexOf("СохранитьКарточкуAsync", StringComparison.Ordinal);
        var индексНазначения = текстСлужбы.IndexOf("SetGlobalSender", StringComparison.Ordinal);

        Assert.True(индексСохранения >= 0, "В службе нет сохранения карточки.");
        Assert.True(
            индексНазначения > индексСохранения,
            "Текущий отправитель назначается до сохранения карточки.");

        // Адаптер обязан вести в полное сохранение контрагента, а не в
        // упрощённую команду создания агента.
        Assert.Contains("СохранитьКонтрагентаAsync", текстСлужбы, StringComparison.Ordinal);
        Assert.DoesNotContain("СоздатьОтправителяAsync", текстСлужбы, StringComparison.Ordinal);
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
