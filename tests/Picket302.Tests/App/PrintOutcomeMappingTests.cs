using Picket302.Domain.Entities;
using Picket302.App.Printing;
using Picket302.Printing;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Преобразование результата печатного конвейера в итог операции (ТЗ §8.6,
/// функция 79): успех, ошибка с сообщением и нейтральный отказ различаются —
/// результат печати не схлопывается в одно состояние.
/// </summary>
public sealed class PrintOutcomeMappingTests
{
    [Fact]
    public void ИзРезультатаКонвейера_Успех_ВозвращаетВыполнено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = true,
            Status = PrintJobStatus.Success,
        });

        Assert.Equal(ПечатьКод.Выполнено, итог.Код);
        Assert.Null(итог.Сообщение);
        Assert.False(итог.СообщениеПоказано);
    }

    [Fact]
    public void ИзРезультатаКонвейера_ОтказСоСообщением_ВозвращаетОшибкуСТекстом()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Failed,
            ErrorMessage = "Принтер недоступен.",
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal("Принтер недоступен.", итог.Сообщение);
        Assert.False(итог.СообщениеПоказано);
    }

    [Fact]
    public void ИзРезультатаКонвейера_ОтказБезСообщения_ВозвращаетОтменено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Aborted,
        });

        Assert.Equal(ПечатьКод.Отменено, итог.Код);
        Assert.Null(итог.Сообщение);
    }

    [Fact]
    public void ИзРезультатаКонвейера_NullРезультат_ОтклоняетсяАргументом()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ПечатьИтог.ИзРезультатаКонвейера(null!));
    }
}
