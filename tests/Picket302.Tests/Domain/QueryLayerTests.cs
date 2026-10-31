using Picket302.Domain.Queries;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Слой запросов и динамическая проекция отчетов (ТЗ §4.1: формат query.xml,
/// параметры DateFrom/DateTo/SenderId/MailCategory, правила DateRange и
/// SqlInjectionGuard StrictParametric).
/// </summary>
public sealed class QueryLayerTests
{
    private const string Листинг41 = """
        <?xml version="1.0" encoding="utf-8"?>
        <ReportQueryDefinition id="Report_F103_Standard" version="1.0">
          <Name>Формирование почтового реестра ф.103</Name>
          <Parameters>
            <Parameter name="DateFrom" type="DateTime" required="true" default="Today-30d" />
            <Parameter name="DateTo" type="DateTime" required="true" default="Today" />
            <Parameter name="SenderId" type="Int64" required="true" />
            <Parameter name="MailCategory" type="Int32" required="false" />
          </Parameters>
          <ValidationRules>
            <Rule type="DateRange" start="DateFrom" end="DateTo" maxDays="90" />
            <Rule type="SqlInjectionGuard" mode="StrictParametric" />
          </ValidationRules>
          <Command dialect="PostgreSql"><![CDATA[SELECT 1 FROM mails WHERE sender_id = :SenderId;]]></Command>
          <Command dialect="Sqlite"><![CDATA[SELECT 1 FROM mails WHERE sender_id = :SenderId;]]></Command>
        </ReportQueryDefinition>
        """;

    private readonly ReportQueryXmlSerializer _сериализатор = new();
    private readonly QueryValidator _валидатор = new();

    [Fact]
    public void Deserialize_Листинг41_ПараметрыИПравила()
    {
        var определение = _сериализатор.Deserialize(Листинг41);

        Assert.Equal("Report_F103_Standard", определение.Id);
        Assert.Equal(4, определение.Parameters.Count);
        Assert.Equal("DateFrom", определение.Parameters[0].Name);
        Assert.True(определение.Parameters[0].Required);
        Assert.Equal("Today-30d", определение.Parameters[0].Default);
        Assert.False(определение.Parameters[3].Required);
        Assert.Equal(2, определение.ValidationRules.Count);
        Assert.Equal(90, определение.ValidationRules[0].MaxDays);
        Assert.Equal("StrictParametric", определение.ValidationRules[1].Mode);

        var postgres = определение.GetCommand("PostgreSql");
        var sqlite = определение.GetCommand("Sqlite");

        Assert.Contains("mails", postgres.Sql);
        Assert.Contains(":SenderId", sqlite.Sql);
    }

    [Fact]
    public void Serialize_RoundTrip_СохраняетОпределение()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var xml = _сериализатор.Serialize(определение);
        var восстановленное = _сериализатор.Deserialize(xml);

        Assert.Equal(определение.Id, восстановленное.Id);
        Assert.Equal(определение.Parameters.Count, восстановленное.Parameters.Count);
        Assert.Equal(определение.ValidationRules.Count, восстановленное.ValidationRules.Count);
        Assert.Equal(2, восстановленное.Commands.Count);
        Assert.Contains(":SenderId", восстановленное.GetCommand("Sqlite").Sql);
    }

    [Fact]
    public void ApplyDefaults_Today30d_ВычисляетДату()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();
        значения.Set("SenderId", 7L);

        var сейчас = new DateTime(2024, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        определение.ApplyDefaults(значения, сейчас);

        Assert.Equal(new DateTime(2024, 4, 10), ((DateTime)значения.Get("DateFrom")!).Date);
        Assert.Equal(new DateTime(2024, 5, 10), ((DateTime)значения.Get("DateTo")!).Date);
        Assert.Equal(7L, значения.Get("SenderId"));
    }

    [Fact]
    public void ValidateValues_Диапазон90дней_Допустим()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();
        значения.Set("SenderId", 1L);
        значения.Set("DateFrom", new DateTime(2024, 1, 1));
        значения.Set("DateTo", new DateTime(2024, 3, 30));

        _валидатор.ValidateValues(определение, значения);
    }

    [Fact]
    public void ValidateValues_Диапазон91день_Отклонение()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();
        значения.Set("SenderId", 1L);
        значения.Set("DateFrom", new DateTime(2024, 1, 1));
        значения.Set("DateTo", new DateTime(2024, 4, 1));

        var ошибка = Assert.Throws<QueryValidationException>(
            () => _валидатор.ValidateValues(определение, значения));

        Assert.Contains("90", ошибка.Message);
    }

    [Fact]
    public void ValidateValues_ОбязательныйПараметрПуст_Отклонение()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();

        var ошибка = Assert.Throws<QueryValidationException>(
            () => _валидатор.ValidateValues(определение, значения));

        // Первый обязательный параметр объявления — DateFrom.
        Assert.Equal("DateFrom", ошибка.ParameterName);
    }

    [Fact]
    public void ValidateValues_НеобъявленныйПараметр_Отклонение()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();
        значения.Set("SenderId", 1L);
        значения.Set("HackerInput", "1 OR 1=1");

        Assert.Throws<QueryValidationException>(
            () => _валидатор.ValidateValues(определение, значения));
    }

    [Fact]
    public void ValidateValues_ИнъекцияВСтроке_StrictParametricОтклоняет()
    {
        var определение = _сериализатор.Deserialize(Листинг41);
        var значения = new QueryParameterValues();
        значения.Set("SenderId", 1L);
        значения.Set("DateFrom", new DateTime(2024, 1, 1));
        значения.Set("DateTo", new DateTime(2024, 1, 2));

        // Добавляем строковый параметр с вредоносной строкой через отдельный
        // запрос с параметром типа String.
        var сСтрокой = new ReportQueryDefinition
        {
            Id = "qrQRcode_Fetch",
            Name = "Выборка растра QR-кода адресата",
        };
        сСтрокой.Parameters.Add(new QueryParameter { Name = "PayloadHash", Type = "String", Required = true });
        сСтрокой.ValidationRules.Add(new QueryRule { Type = QueryRuleTypes.SqlInjectionGuard, Mode = SqlGuardModes.StrictParametric });
        сСтрокой.Commands.Add(new QueryCommand
        {
            Dialect = QueryDialects.Sqlite,
            Sql = "SELECT image_blob AS img_qrcode FROM qr_cache WHERE payload_hash = :PayloadHash;",
        });

        var атака = new QueryParameterValues();
        атака.Set("PayloadHash", "abc'; DROP TABLE qr_cache;--");

        Assert.Throws<QueryValidationException>(() => _валидатор.ValidateValues(сСтрокой, атака));

        var корректный = new QueryParameterValues();
        корректный.Set("PayloadHash", "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b");

        _валидатор.ValidateValues(сСтрокой, корректный);
    }

    [Fact]
    public void ValidateDefinition_НеобъявленныйПлейсхолдер_Отклонение()
    {
        var определение = new ReportQueryDefinition { Id = "qrBroken" };
        определение.Commands.Add(new QueryCommand
        {
            Dialect = QueryDialects.Sqlite,
            Sql = "SELECT * FROM mails WHERE sender_id = :SenderId AND note = :Undeclared;",
        });

        Assert.Throws<QueryValidationException>(() => _валидатор.ValidateDefinition(определение));
    }

    [Fact]
    public void ExtractPlaceholders_ЛитералыИгнорируются()
    {
        var sql = "SELECT ':not_a_param' AS a, :Real1 AS b, x:Real2 FROM t WHERE c = :Real1;";

        var имена = QueryValidator.ExtractPlaceholders(sql);

        Assert.Equal(new[] { "Real1", "Real2" }, имена);
    }
}
