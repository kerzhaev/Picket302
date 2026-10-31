using Picket302.Domain.Entities;
using Picket302.Domain.Rules;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка валидации ИНН и УНП (ТЗ §3.1.1: «ИНН (10 знаков для ЮЛ, 12 для ФЛ) / УНП»;
/// §8.1, функция 1: «валидирует ИНН (10 или 12 цифр по контрольным суммам)»).
/// </summary>
public sealed class InnValidatorTests
{
    /// <summary>
    /// Контрольные цифры десятизначного ИНН проверяются по опубликованному
    /// алгоритму: контрольной является десятая цифра.
    /// </summary>
    [Theory]
    [InlineData("7707083893")]
    [InlineData("7740000005")]
    [InlineData("500100732259")]
    public void Validate_КорректныйИнн_Принимается(string инн)
    {
        var тип = инн.Length == InnValidator.NaturalPersonLength
            ? CounterpartyType.NaturalPerson
            : CounterpartyType.LegalEntity;

        var результат = InnValidator.Validate(инн, тип);

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Equal(инн, результат.Value);
    }

    /// <summary>
    /// ИНН с неверной контрольной суммой отклоняется.
    /// </summary>
    [Theory]
    [InlineData("7707083894")]
    [InlineData("7707083892")]
    [InlineData("7740000003")]
    [InlineData("500100732258")]
    [InlineData("500100732260")]
    public void Validate_НевернаяКонтрольнаяСумма_Отклоняется(string инн)
    {
        var тип = инн.Length == InnValidator.NaturalPersonLength
            ? CounterpartyType.NaturalPerson
            : CounterpartyType.LegalEntity;

        var результат = InnValidator.Validate(инн, тип);

        Assert.True(результат.IsFailure);
        Assert.Contains("контроль", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Десятизначный ИНН не принимается для физического лица и наоборот
    /// (ТЗ §3.1.1: 10 знаков для ЮЛ, 12 для ФЛ).
    /// </summary>
    [Fact]
    public void Validate_ДесятизначныйИннДляФизЛица_Отклоняется()
    {
        var результат = InnValidator.Validate("7707083893", CounterpartyType.NaturalPerson);

        Assert.True(результат.IsFailure);
        Assert.Contains("12", результат.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ДвенадцатизначныйИннДляЮрЛица_Отклоняется()
    {
        var результат = InnValidator.Validate("500100732259", CounterpartyType.LegalEntity);

        Assert.True(результат.IsFailure);
        Assert.Contains("10", результат.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// УНП — девять цифр; алгоритм контрольной суммы в ТЗ не приведён,
    /// поэтому проверяется формат.
    /// </summary>
    [Fact]
    public void Validate_УнпДевятиЗнаков_Принимается()
    {
        var результат = InnValidator.Validate("123456789", CounterpartyType.LegalEntity);

        Assert.True(результат.IsSuccess);
        Assert.Equal("123456789", результат.Value);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678A")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_НедопустимоеЗначение_Отклоняется(string? инн)
    {
        var результат = InnValidator.Validate(инн, CounterpartyType.LegalEntity);

        Assert.True(результат.IsFailure);
        Assert.NotNull(результат.Error);
    }
}
