using Picket302.App.Printing;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Преобразование результата печатного конвейера в итог операции (ТЗ §8.6,
/// функция 79). Статус сессии первичен: отмена оператора (ABORTED, в том
/// числе с production-текстом «Печать прервана оператором.») ошибкой не
/// считается, а отказ FAILED без текста отменой не становится.
/// </summary>
public sealed class PrintOutcomeMappingTests
{
    [Fact]
    public void Успех_СогласованнаяПара_ВозвращаетВыполнено()
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
    public void Отмена_БезТекста_ВозвращаетОтменено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Aborted,
            ErrorMessage = null,
        });

        Assert.Equal(ПечатьКод.Отменено, итог.Код);
        Assert.Null(итог.Сообщение);
    }

    /// <summary>
    /// Обязательный регрессионный тест: production-контроллер при отмене
    /// возвращает ABORTED с текстом «Печать прервана оператором.» — текст
    /// отмены не должен превращаться в ошибку.
    /// </summary>
    [Fact]
    public void Отмена_ProductionТекстПрерванаОператором_ВозвращаетОтменено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Aborted,
            ErrorMessage = "Печать прервана оператором.",
        });

        Assert.Equal(ПечатьКод.Отменено, итог.Код);
        Assert.Null(итог.Сообщение);
    }

    [Fact]
    public void Ошибка_FailedСТекстом_ВозвращаетОшибкуСТемЖеТекстом()
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
    public void Ошибка_FailedБезТекста_ВозвращаетОшибкуСОбщимТекстом()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Failed,
            ErrorMessage = null,
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal(ПечатьИтог.ОбщийТекстОшибки, итог.Сообщение);
    }

    [Fact]
    public void Ошибка_FailedСПробелами_ВозвращаетОшибкуСОбщимТекстом()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Failed,
            ErrorMessage = "   ",
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal(ПечатьИтог.ОбщийТекстОшибки, итог.Сообщение);
    }

    [Fact]
    public void Ошибка_BlockedSizeСТекстом_ВозвращаетОшибкуСТекстомОператора()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.BlockedSize,
            ErrorMessage = "Размер не поддерживается.",
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal("Размер не поддерживается.", итог.Сообщение);
    }

    [Fact]
    public void Ошибка_BlockedSizeБезТекста_ВозвращаетОшибкуБлокировкиРазмера()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.BlockedSize,
            ErrorMessage = null,
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal(ПечатьИтог.ТекстБлокировкиРазмера, итог.Сообщение);
    }

    [Fact]
    public void Ошибка_НеизвестныйНеуспешныйСтатус_ВозвращаетОшибкуСДиагностикой()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = "UNKNOWN",
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Contains("UNKNOWN", итог.Сообщение, StringComparison.Ordinal);
        Assert.Contains("несогласованный", итог.Сообщение, StringComparison.Ordinal);
    }

    /// <summary>
    /// Несогласованный результат (IsSuccess=true при статусе FAILED) не
    /// должен превращаться в ложный успех: политика FAILED — всегда ошибка
    /// (с текстом оператора или общим стабильным текстом).
    /// </summary>
    [Fact]
    public void Несогласованный_УспехПриСтатусеFailed_НеВозвращаетВыполнено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = true,
            Status = PrintJobStatus.Failed,
        });

        Assert.NotEqual(ПечатьКод.Выполнено, итог.Код);
        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Equal(ПечатьИтог.ОбщийТекстОшибки, итог.Сообщение);
    }

    /// <summary>
    /// Несогласованный результат IsSuccess=false при статусе SUCCESS — тоже
    /// не успех.
    /// </summary>
    [Fact]
    public void Несогласованный_НеуспехПриСтатусеSuccess_НеВозвращаетВыполнено()
    {
        var итог = ПечатьИтог.ИзРезультатаКонвейера(new PrintResult
        {
            IsSuccess = false,
            Status = PrintJobStatus.Success,
        });

        Assert.Equal(ПечатьКод.Ошибка, итог.Код);
        Assert.Contains("несогласованный", итог.Сообщение, StringComparison.Ordinal);
    }

    [Fact]
    public void NullРезультат_ОтклоняетсяАргументом()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ПечатьИтог.ИзРезультатаКонвейера(null!));
    }

    /// <summary>
    /// Сбой построения предпросмотра отличается от отказа оператора: итог
    /// сбоя несёт текст «Не удалось построить предпросмотр: …» и отменой
    /// не является.
    /// </summary>
    [Fact]
    public void Предпросмотр_СбойПостроения_ОтличаетсяОтОтменыИСохраняетТекст()
    {
        var сбой = ПредпросмотрИтог.ИзИсключения(
            new InvalidOperationException("файл бланка не найден"));

        Assert.Equal(ПредпросмотрКод.Ошибка, сбой.Код);
        Assert.Contains(
            "Не удалось построить предпросмотр: файл бланка не найден",
            сбой.Сообщение,
            StringComparison.Ordinal);
        Assert.False(сбой.СообщениеПоказано);

        // Отказ оператора — отдельное состояние без текста и без ошибки.
        var отказ = ПредпросмотрИтог.Отменено;

        Assert.Equal(ПредпросмотрКод.Отменено, отказ.Код);
        Assert.Null(отказ.Сообщение);
        Assert.NotEqual(сбой.Код, отказ.Код);
    }

    [Fact]
    public void Предпросмотр_NullИсключение_ОтклоняетсяАргументом()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ПредпросмотрИтог.ИзИсключения(null!));
    }
}
