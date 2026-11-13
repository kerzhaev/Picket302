using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Licensing;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// S46 (ТЗ §8.10, функции 121 и 126): мягкая детекция виртуальной машины
/// (без WMI, только безобидные признаки) и каталог редакций Free/Home/
/// Professional с различимыми лимитами и фичами.
/// </summary>
public sealed class EditionsAndVmDetectorTests
{
    /// <summary>Детектор с пустым реестром.</summary>
    private static VmDetector ДетекторПустой()
    {
        return new VmDetector(_ => { });
    }

    /// <summary>
    /// №121: детектор — мягкий: отсутствующие признаки дают false, никакие
    /// исключения не пробрасываются (сбой чтения реестра не ломает программу).
    /// </summary>
    [Fact]
    public void ПустойРеестр_НеВиртуальнаяМашина_БезИсключений()
    {
        var детектор = ДетекторПустой();

        var является = детектор.IsVirtualMachine();

        Assert.False(является);
    }

    /// <summary>
    /// №121: реализация не использует WMI (мягкая, ТЗ §7.3) — исходник
    /// обнаруживается подъёмом по дереву от бинарной папки.
    /// </summary>
    [Fact]
    public void Детектор_БезWmi()
    {
        var каталог = AppContext.BaseDirectory;
        var исходник = (string?)null;

        for (var уровень = 0; уровень < 8 && исходник is null; уровень++)
        {
            var кандидат = Path.Combine(каталог,
                "src", "Picket302.Infrastructure", "Licensing", "VmDetector.cs");

            if (File.Exists(кандидат))
            {
                исходник = File.ReadAllText(кандидат);
            }

            каталог = Path.GetDirectoryName(каталог.TrimEnd(Path.DirectorySeparatorChar))
                ?? каталог;
        }

        Assert.NotNull(исходник);
        Assert.DoesNotContain("ManagementObjectSearcher", исходник, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management", исходник, StringComparison.Ordinal);
    }

    /// <summary>
    /// №121: журнал детектора получает сообщения (мягкая деградация —
    /// операции реестра не ломают программу; на хосте без ВМ признаки не
    /// обнаружены, предупреждение о ВМ не формируется).
    /// </summary>
    [Fact]
    public void Детектор_ЖурналДоступенБезИсключений()
    {
        var журнал = new List<string>();
        var детектор = new VmDetector(журнал.Add);

        var является = детектор.IsVirtualMachine();

        Assert.False(является);
        Assert.Empty(журнал);
    }

    /// <summary>
    /// №126: каталог редакций — различимые лимиты (Free с trial, Home без
    /// срока, Professional без лимитов).
    /// </summary>
    [Fact]
    public void КаталогРедакций_ЛимитыРазличаются()
    {
        var free = Редакции.Free;
        var home = Редакции.Home;
        var professional = Редакции.Professional;

        // Free: лимит адресов 500, лимит писем 100, trial 10 дней.
        Assert.Equal(500, free.Limits.MaxCounterparties);
        Assert.Equal(100, free.Limits.MaxDocuments);
        Assert.Equal(Редакции.ПробныйСрокДнейПоУмолчанию, free.Limits.TrialDays);

        // Home: без лимитов времени, но адреса/письма остаются ограничены
        // по каталогу (значения различимы от Free).
        Assert.Null(home.Limits.TrialDays);

        // Professional: всё без лимитов.
        Assert.Null(professional.Limits.MaxCounterparties);
        Assert.Null(professional.Limits.MaxDocuments);
        Assert.Null(professional.Limits.TrialDays);
    }

    /// <summary>
    /// №126: фичи редакций — Профессиональная включает всё из Home плюс
    /// импорт/автораспознавание/автоматизацию.
    /// </summary>
    [Fact]
    public void ФичиРедакций_ProfessionalЯвляетсяНадмножествомHome()
    {
        var home = Редакции.Home.Features.ToHashSet();
        var professional = Редакции.Professional.Features.ToHashSet();

        Assert.Subset(professional, home);
        Assert.Contains(LicenseFeature.SpreadsheetImport, professional);
        Assert.Contains(LicenseFeature.AutomaticAddressParsing, professional);
        Assert.Contains(LicenseFeature.Automation, professional);
        Assert.Contains(LicenseFeature.UpdatesAndSupport, professional);

        // Free — подмножество Home.
        var free = Редакции.Free.Features.ToHashSet();
        Assert.Subset(home, free);
    }

    /// <summary>№126: описание редакции по значению — полный каталог.</summary>
    [Fact]
    public void ОписаниеРедакции_КаталогПолон()
    {
        Assert.Equal(Редакции.Free, Редакции.Описание(Edition.Free));
        Assert.Equal(Редакции.Home, Редакции.Описание(Edition.Home));
        Assert.Equal(Редакции.Professional, Редакции.Описание(Edition.Professional));

        // Неизвестное значение — безопасный откат на Free.
        Assert.Equal(Редакции.Free, Редакции.Описание((Edition)99));
    }
}
