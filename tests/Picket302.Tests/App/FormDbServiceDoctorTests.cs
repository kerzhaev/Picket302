using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Infrastructure.Persistence;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S35 (ТЗ §8.11, функция 136): диалог «Диагностика сервера базы данных» —
/// отчёт диагностики с ошибками показывается человеку; команды выполняются
/// только через подменяемый исполнитель (никаких непроверенных команд и
/// реальных sc.exe в проверках).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormDbServiceDoctorTests
{
    /// <summary>Создаёт диагноста с подменённым sc.exe.</summary>
    private static DbServiceDoctor СоздатьДиагноста(
        Dictionary<string, string> ответы,
        List<string>? вызовы = null)
        => new(
            аргументы =>
            {
                вызовы?.Add(аргументы);

                foreach (var (ключ, значение) in ответы)
                {
                    if (аргументы.StartsWith(ключ, StringComparison.Ordinal))
                    {
                        return значение;
                    }
                }

                return string.Empty;
            },
            null,
            () => true);

    [Fact]
    public void Диагностика_ОтчётСОшибкамиПоказываетсяЧеловеку()
    {
        var вызовы = new List<string>();

        var ответы = new Dictionary<string, string>
        {
            ["query"] = "SERVICE_NAME: picket302-db\n"
                + "DISPLAY_NAME: База Picket302\n"
                + "СОСТОЯНИЕ : 1  STOPPED\n"
                + "\n",
            ["qc"] = "BINARY_PATH_NAME : C:\\Program Files\\Picket302\\db.exe",
        };

        var диагност = СоздатьДиагноста(ответы, вызовы);

        using var форма = new FormDbServiceDoctor(диагност);

        // Ошибки/состояния показаны человеку в поле отчёта.
        Assert.Contains("picket302-db", форма.ТекстОтчётаДляПроверок, StringComparison.Ordinal);

        // Никаких непроверенных команд: только query/qc подменяемого
        // исполнителя.
        Assert.All(вызовы, аргументы => Assert.True(
            аргументы.StartsWith("query", StringComparison.Ordinal)
            || аргументы.StartsWith("qc", StringComparison.Ordinal)));
    }

    [Fact]
    public void УдалитьСлужбу_БезПодтверждения_ОтказБезКоманды()
    {
        var вызовы = new List<string>();
        var ответы = new Dictionary<string, string>
        {
            ["query"] = string.Empty,
        };

        var диагност = СоздатьДиагноста(ответы, вызовы);

        using var форма = new FormDbServiceDoctor(диагност);

        var результат = форма.УдалитьСлужбу("picket302-db", подтверждено: false);

        Assert.False(результат.IsSuccess);
        Assert.Contains("не подтверждено", результат.Error, StringComparison.Ordinal);

        // Команда удаления не выполнялась.
        Assert.DoesNotContain(вызовы, аргументы => аргументы.StartsWith("delete", StringComparison.Ordinal));
    }

    [Fact]
    public void УдалитьСлужбу_НераспознанныйВывод_СырыйТекстЧеловеку()
    {
        var ответы = new Dictionary<string, string>
        {
            ["delete"] = "Какой-то нераспознанный вывод утилиты",
            ["query"] = string.Empty,
        };

        var диагност = СоздатьДиагноста(ответы);

        using var форма = new FormDbServiceDoctor(диагност);

        var результат = форма.УдалитьСлужбу("осиротевшая-служба", подтверждено: true);

        Assert.False(результат.IsSuccess);

        // Сырой вывод показан человеку (без обрезки и без скрытия).
        Assert.Contains("нераспознанный вывод", результат.Error, StringComparison.Ordinal);
    }
}
