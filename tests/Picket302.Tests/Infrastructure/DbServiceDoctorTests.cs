using Picket302.Infrastructure.Persistence;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессии F-B4 и F-B5 на фейковом делегате запуска команды.
///
/// F-B4 — путь к исполняемому файлу службы дополняется (реестр либо
/// qc-вывод): прежде <c>sc query</c> не содержит BINARY_PATH_NAME, и путь
/// оставался пустым, осиротевшие службы не находились.
/// F-B5 — метка состояния принимается двуязычно (STATE/СОСТОЯНИЕ) с
/// разбором цифрового кода; нераспознанный вывод sc delete трактуется
/// как отказ, а не успех.
/// </summary>
public sealed class DbServiceDoctorTests
{
    [Fact]
    public void СписокСлужб_РусскаяЛокальПутьИзQc_ПутьЗаполняется()
    {
        // ПутьСлужбы не подменяется: проверяется откат на qc-вывод, когда
        // реестр службы не содержит ImagePath (F-B4).
        var диагност = new DbServiceDoctor(
            аргументы =>
            {
                if (аргументы.StartsWith("query", StringComparison.Ordinal))
                {
                    return "SERVICE_NAME: picket302-db\n"
                        + "DISPLAY_NAME: База Picket302\n"
                        + "СОСТОЯНИЕ : 4  RUNNING\n"
                        + "\n";
                }

                if (аргументы.StartsWith("qc", StringComparison.Ordinal))
                {
                    return "BINARY_PATH_NAME : C:\\Program Files\\Picket302\\db.exe";
                }

                return string.Empty;
            },
            null,
            () => true);

        var службы = диагност.ListServices();

        var служба = Assert.Single(службы);
        Assert.Equal("picket302-db", служба.ServiceName);
        Assert.Equal("Работает", служба.State);

        // Путь из qc-вывода дополнил пустой ImagePath (F-B4).
        Assert.Contains("db.exe", служба.ImagePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void СписокСлужб_ОсиротевшаяСлужбаОпределяетсяПоПути()
    {
        var диагност = СобратьДиагност(
            аргументы =>
            {
                if (аргументы.StartsWith("query", StringComparison.Ordinal))
                {
                    return "SERVICE_NAME: postgres-old\n"
                        + "DISPLAY_NAME: PostgreSQL остаток\n"
                        + "STATE: 1  STOPPED\n"
                        + "\n";
                }

                if (аргументы.StartsWith("qc", StringComparison.Ordinal))
                {
                    return "BINARY_PATH_NAME : C:\\Program Files\\PostgreSQL\\17\\bin\\postgres.exe";
                }

                return string.Empty;
            },
            имяСлужбы => "C:\\Program Files\\PostgreSQL\\17\\bin\\postgres.exe");

        var отчёт = диагност.DiagnoseDbService();

        // Файл по пути не существует: служба осиротевшая (F-B4).
        Assert.True(отчёт.HasOrphanedServices);
        Assert.Contains(отчёт.OrphanedServices, служба => служба.ServiceName == "postgres-old");
    }

    [Fact]
    public void УдалениеСлужбы_НераспознанныйВывод_ОтказССырымТекстом()
    {
        var диагност = new DbServiceDoctor(_ => "неопознанный ответ службы", null, () => true);

        var результат = диагност.DeleteService("picket302-db");

        // Прежде нераспознанный вывод возвращался как успех (F-B5).
        Assert.True(результат.IsFailure);
        Assert.Contains("неопознанный ответ службы", результат.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void УдалениеСлужбы_ЯвныйМаркерРусскойЛокали_Успех()
    {
        var диагност = new DbServiceDoctor(_ => "УДАЛЕНИЕ СЛУЖБЫ выполнено.", null, () => true);

        var результат = диагност.DeleteService("picket302-db");

        Assert.True(результат.IsSuccess);
    }

    /// <summary>
    /// Собирает диагност с подменённым чтением пути службы и правами
    /// администратора.
    /// </summary>
    private static DbServiceDoctor СобратьДиагност(
        Func<string, string> запускКоманды,
        Func<string, string?> путьСлужбы)
        => new(запускКоманды, путьСлужбы, () => true);
}
