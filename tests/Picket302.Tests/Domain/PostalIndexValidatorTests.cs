using Picket302.Domain.Rules;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка валидации почтового индекса (ТЗ §3.1.1: индекс <c>VARCHAR(6)</c>;
/// §8.1, функция 4).
/// </summary>
public sealed class PostalIndexValidatorTests
{
    [Theory]
    [InlineData("101000")]
    [InlineData("000000")]
    [InlineData("999999")]
    public void Validate_ШестьЦифр_Принимается(string индекс)
    {
        var результат = PostalIndexValidator.Validate(индекс);

        Assert.True(результат.IsSuccess);
        Assert.Equal(индекс, результат.Value);
    }

    [Theory]
    [InlineData("10100")]
    [InlineData("1010000")]
    [InlineData("10100A")]
    [InlineData("101 00")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_НеШестьЦифр_Отклоняется(string? индекс)
    {
        var результат = PostalIndexValidator.Validate(индекс);

        Assert.True(результат.IsFailure);
        Assert.NotNull(результат.Error);
    }

    [Fact]
    public void IsValid_ПроверяетФорматИндекса()
    {
        Assert.True(PostalIndexValidator.IsValid("101000"));
        Assert.False(PostalIndexValidator.IsValid("10100A"));
        Assert.False(PostalIndexValidator.IsValid(null));
    }
}

/// <summary>
/// Проверка валидации банковских реквизитов (ТЗ §8.1, функция 1: «КПП
/// (9 символов), БИК (9 цифр)»).
/// </summary>
public sealed class BankRequisitesValidatorTests
{
    [Fact]
    public void ValidateKpp_ДевятьЦифр_Принимается()
    {
        var результат = BankRequisitesValidator.ValidateKpp("770708001");

        Assert.True(результат.IsSuccess);
        Assert.Equal("770708001", результат.Value);
    }

    [Theory]
    [InlineData("77070800")]
    [InlineData("7707080010")]
    [InlineData("77070800A")]
    [InlineData("")]
    public void ValidateKpp_НекорректноеЗначение_Отклоняется(string кпп)
    {
        Assert.True(BankRequisitesValidator.ValidateKpp(кпп).IsFailure);
    }

    [Fact]
    public void ValidateBik_ДевятьЦифр_Принимается()
    {
        var результат = BankRequisitesValidator.ValidateBik("044525225");

        Assert.True(результат.IsSuccess);
        Assert.Equal("044525225", результат.Value);
    }

    [Theory]
    [InlineData("04452522")]
    [InlineData("0445252250")]
    [InlineData("04452522X")]
    [InlineData("")]
    public void ValidateBik_НекорректноеЗначение_Отклоняется(string bik)
    {
        Assert.True(BankRequisitesValidator.ValidateBik(bik).IsFailure);
    }
}
