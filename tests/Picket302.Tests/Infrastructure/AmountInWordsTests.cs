using Picket302.Domain.Queries;
using Picket302.Infrastructure.Text;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Сумма прописью и словари .lng (ТЗ §8.11, функции 150–151;
/// EVIDENCE/localization_dictionaries.txt).
/// </summary>
public sealed class AmountInWordsTests
{
    private readonly AmountInWords _пропись = new();

    [Fact]
    public void Руб_21_22_Склонение()
    {
        Assert.Equal("Двадцать один рубль 00 копеек", _пропись.ToWords(21.00m, "Rub"));
        Assert.Equal("Двадцать два рубля 00 копеек", _пропись.ToWords(22.00m, "Rub"));
        Assert.Equal("Один рубль 00 копеек", _пропись.ToWords(1.00m, "Rub"));
    }

    [Fact]
    public void Руб_КопейкиФормы()
    {
        Assert.Equal("Двадцать один рубль 01 копейка", _пропись.ToWords(21.01m, "Rub"));
        Assert.Equal("Двадцать один рубль 02 копейки", _пропись.ToWords(21.02m, "Rub"));
        Assert.Equal("Двадцать один рубль 05 копеек", _пропись.ToWords(21.05m, "Rub"));
    }

    [Fact]
    public void Uah_ОднаГрн_РодНеПрименяется()
    {
        // CurrencyMale=0: «одна грн» (ТЗ §8.11, ф.150).
        Assert.Equal("Одна грн 00 коп.", _пропись.ToWords(1.00m, "Uah"));
        Assert.Equal("Двi грн 00 коп.", _пропись.ToWords(2.00m, "Uah"));
        Assert.Equal("Двадцять одна грн 00 коп.", _пропись.ToWords(21.00m, "Uah"));
    }

    [Fact]
    public void Тысячи_Миллионы()
    {
        Assert.Equal("Одна тысяча рублей 00 копеек", _пропись.ToWords(1000.00m, "Rub"));
        Assert.Equal("Две тысячи рублей 00 копеек", _пропись.ToWords(2000.00m, "Rub"));
        Assert.Equal("Одиннадцать тысяч рублей 00 копеек", _пропись.ToWords(11000.00m, "Rub"));
        Assert.Equal("Один миллион рублей 00 копеек", _пропись.ToWords(1000000.00m, "Rub"));
        Assert.Equal("Один миллион одна тысяча двести тридцать четыре рубля 56 копеек", _пропись.ToWords(1001234.56m, "Rub"));
    }

    [Fact]
    public void Руб_НольИБольшеТысячиКопеек()
    {
        Assert.Equal("Нуль рублей 00 копеек", _пропись.ToWords(0.00m, "Rub"));
        Assert.Equal("Сто двадцать три рубля 45 копеек", _пропись.ToWords(123.45m, "Rub"));
    }

    [Fact]
    public void Byn_ИUsd_Загружаются()
    {
        Assert.Contains("рубл", _пропись.ToWords(10.00m, "Byn"));
        Assert.Contains("доллар", _пропись.ToWords(10.00m, "usd"));
        Assert.Contains("Десять долларов", _пропись.ToWords(10.00m, "usd"));
    }

    [Fact]
    public void НеполныйСловарь_СуммаЦифрами_Предупреждение()
    {
        var текст = "One=один;два;три;четыре;пять;шесть;семь;восемь;девять;одна;две;нуль\nComma=,\nCurrencyMale=1\n";

        var словарь = LngDictionary.Parse(текст);

        var сумма = _пропись.ToWords(21.50m, словарь);

        Assert.Equal("21,50", сумма);
        Assert.Single(_пропись.Warnings);
        Assert.Contains("Twen", _пропись.Warnings[0]);
    }

    [Fact]
    public void Валидатор_ПолнотыКлючей()
    {
        var полный = LngDictionary.LoadBuiltin("Rub");
        Assert.Empty(полный.Validate());
        Assert.True(полный.IsComplete);

        var неполный = LngDictionary.Parse("One=x\nCurrency=рубль;рубля;рублей\n");
        var недостающие = неполный.Validate();

        Assert.Contains("Ten", недостающие);
        Assert.Contains("Thou", недостающие);
        Assert.Contains("DayOfWeek", недостающие);
        Assert.Equal(13, недостающие.Count);
    }

    [Fact]
    public void Словарь_МесяцыИДниНедели()
    {
        var словарь = LngDictionary.LoadBuiltin("Rub");

        // Month2 — родительный падеж («1 января 2024 г.»).
        Assert.Equal("Января", словарь.Month(1, genitive: true));
        Assert.Equal("Январь", словарь.Month(1, genitive: false));
        Assert.Equal("Декабря", словарь.Month(12, genitive: true));

        // Uah: Month1 — именительный («січень»), Month2 — родительный («січня»).
        var украинский = LngDictionary.LoadBuiltin("Uah");
        Assert.Equal("січень", украинский.Month(1, genitive: false));
        Assert.Equal("січня", украинский.Month(1, genitive: true));

        Assert.Equal("воскресенье", словарь.DayOfWeekName(DayOfWeek.Sunday));
        Assert.Equal("суббота", словарь.DayOfWeekName(DayOfWeek.Saturday));
    }

    [Fact]
    public void FormatDate_ЧерезСловарь()
    {
        var дата = new DateTime(2024, 1, 1);

        Assert.Equal("1 января 2024 г.", _пропись.FormatDate(дата, "Rub"));
    }

    [Fact]
    public void НеизвестныйСловарь_Отклонение()
    {
        // Словарь без файла — отклонение (файл не найден).
        Assert.ThrowsAny<Exception>(() => _пропись.ToWords(10m, "Xxx"));
    }

    [Fact]
    public void ПустойШаблонКазахстана_СуммаЦифрамиСПредупреждением()
    {
        // Казахстан поставляется пустым шаблоном (ТЗ §10.8, ф.151);
        // неполный словарь — сумма цифрами + предупреждение (ТЗ §8.11, ф.150).
        var сумма = _пропись.ToWords(10m, "Kzt");

        Assert.Contains("10", сумма, StringComparison.Ordinal);
        Assert.Contains(_пропись.Warnings, предупреждение =>
            предупреждение.Contains("неполон", StringComparison.Ordinal));
    }

    [Fact]
    public void ВалютаВТрехФормах()
    {
        Assert.Equal("Один рубль 00 копеек", _пропись.ToWords(1m, "Rub"));
        Assert.Equal("Два рубля 00 копеек", _пропись.ToWords(2m, "Rub"));
        Assert.Equal("Пять рублей 00 копеек", _пропись.ToWords(5m, "Rub"));

        // 11–14 — вторая множественная форма.
        Assert.Equal("Одиннадцать рублей 00 копеек", _пропись.ToWords(11m, "Rub"));
    }
}
