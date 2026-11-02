using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка импорта контрагентов из выгрузок 1С:Предприятие 7.7 и 8.x
/// (ТЗ §8.5, функция 72: «Импорт контрагентов из выгрузок 1С:Предприятие 7.7
/// и 8.x»).
/// </summary>
public sealed class OneCImportParserTests
{
    private readonly OneCImportParser _разборщик = new();

    [Fact]
    public void РазборXml_Выгрузка83_ДвеОрганизацииИмпортированыСИннИАдресом()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты xmlns="http://v8.1c.ru/8.3/data/core">
              <Контрагент>
                <Наименование>ООО «Вектор Трейд»</Наименование>
                <ИНН>7701234567</ИНН>
                <КПП>773001001</КПП>
                <Телефон>+7 (495) 987-65-43</Телефон>
                <Адрес>РФ, 350000, г. Краснодар, ул. Красная, д. 35</Адрес>
                <Счет>40702810100000012345</Счет>
                <Банк>ПАО Сбербанк</Банк>
              </Контрагент>
              <Контрагент>
                <Наименование>ООО «Ромашка»</Наименование>
                <ИНН>7707654321</ИНН>
                <КПП>773001002</КПП>
                <Адрес>125009, г. Москва, ул. Тверская, д. 1</Адрес>
              </Контрагент>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        Assert.Equal(2, результат.RecordCount);
        Assert.Empty(результат.Warnings);

        var первая = результат.Records[0];

        Assert.Equal(1, первая.RowNumber);
        Assert.Equal(ImportRecordStatus.Unverified, первая.Status);
        Assert.Equal("ООО «Вектор Трейд»", первая.Draft.Name);
        Assert.Equal("7701234567", первая.Draft.Inn);
        Assert.Equal("773001001", первая.Draft.Kpp);
        Assert.Equal("+7 (495) 987-65-43", первая.Draft.Phone);
        Assert.Equal("350000", первая.Draft.PostalZip);
        Assert.Contains("г. Краснодар", первая.Draft.PostalLines);
        Assert.Contains("ул. Красная", первая.Draft.PostalLines);
        Assert.Contains("д. 35", первая.Draft.PostalLines);
        Assert.False(первая.Draft.NeedsVerification);
        Assert.Contains("Счёт: 40702810100000012345", первая.Draft.Note);
        Assert.Contains("Банк: ПАО Сбербанк", первая.Draft.Note);

        var вторая = результат.Records[1];

        Assert.Equal(2, вторая.RowNumber);
        Assert.Equal("ООО «Ромашка»", вторая.Draft.Name);
        Assert.Equal("7707654321", вторая.Draft.Inn);
        Assert.Equal("125009", вторая.Draft.PostalZip);
    }

    [Fact]
    public void РазборXml_ИменованныеЭлементы8x_Читаются()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты>
              <Контрагент>
                <Наименование Имя="Наименование">ООО «Вектор Трейд»</Наименование>
                <ИНН Имя="ИНН">7701234567</ИНН>
                <Телефон Имя="Телефон">+7 (495) 987-65-43</Телефон>
                <ЮридическийАдрес Имя="ЮридическийАдрес">350000, г. Краснодар, ул. Красная, д. 35</ЮридическийАдрес>
                <ФактическийАдрес Имя="ФактическийАдрес">125009, г. Москва, ул. Тверская, д. 1</ФактическийАдрес>
              </Контрагент>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        var запись = Assert.Single(результат.Records);

        Assert.Equal("ООО «Вектор Трейд»", запись.Draft.Name);
        Assert.Equal("7701234567", запись.Draft.Inn);
        Assert.Equal("+7 (495) 987-65-43", запись.Draft.Phone);
        Assert.Equal("350000", запись.Draft.LegalZip);
        Assert.Contains("ул. Красная", запись.Draft.LegalLines);
        Assert.Equal("125009", запись.Draft.ActualZip);
        Assert.Contains("ул. Тверская", запись.Draft.ActualLines);
    }

    [Fact]
    public void РазборXml_ПакетныйФормат1С_РеквизитыЧитаются()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Файл Версия="2.0">
              <Пакет Содержимое="Справочник.Контрагенты">
                <Свойство Имя="Наименование"><Значение>ООО «Вектор Трейд»</Значение></Свойство>
                <Свойство Имя="ИНН"><Значение>7701234567</Значение></Свойство>
                <Свойство Имя="Адрес"><Значение>350000, г. Краснодар</Значение></Свойство>
              </Пакет>
            </Файл>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        var запись = Assert.Single(результат.Records);

        Assert.Equal("ООО «Вектор Трейд»", запись.Draft.Name);
        Assert.Equal("7701234567", запись.Draft.Inn);
        Assert.Equal("350000", запись.Draft.PostalZip);
    }

    [Fact]
    public void РазборXml_БлокБезТегаКонтрагента_ПропущенСПредупреждением()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты>
              <Контрагент>
                <Наименование>ООО «Вектор Трейд»</Наименование>
                <ИНН>7701234567</ИНН>
              </Контрагент>
              <Запись>
                <Ссылка>http://сервер/объект</Ссылка>
              </Запись>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        var запись = Assert.Single(результат.Records);
        Assert.Equal("ООО «Вектор Трейд»", запись.Draft.Name);

        var предупреждение = Assert.Single(результат.Warnings);

        Assert.Contains("Запись", предупреждение);
        Assert.Contains("не найден тег контрагента", предупреждение);
    }

    [Fact]
    public void РазборТекста_РазделительТочкаСЗапятой_Читается()
    {
        const string содержимое = """
            Наименование;ИНН;КПП;Телефон;Адрес
            ООО «Вектор Трейд»;7701234567;773001001;+7 (495) 987-65-43;350000, г. Краснодар, ул. Красная, д. 35
            ООО «Ромашка»;7707654321;773001002;+7 (495) 111-22-33;125009, г. Москва, ул. Тверская, д. 1
            """;

        using var файл = new ВременныйФайл(содержимое, ".txt");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V77);

        Assert.Equal(2, результат.RecordCount);

        var первая = результат.Records[0];

        Assert.Equal("ООО «Вектор Трейд»", первая.Draft.Name);
        Assert.Equal("7701234567", первая.Draft.Inn);
        Assert.Equal("773001001", первая.Draft.Kpp);
        Assert.Equal("+7 (495) 987-65-43", первая.Draft.Phone);
        Assert.Equal("350000", первая.Draft.PostalZip);
        Assert.Contains("ул. Красная", первая.Draft.PostalLines);

        Assert.Equal("ООО «Ромашка»", результат.Records[1].Draft.Name);
        Assert.Equal("125009", результат.Records[1].Draft.PostalZip);
    }

    [Fact]
    public void РазборТекста_РазделительТабуляция_Читается()
    {
        const string содержимое = """
            Наименование	ИНН	Адрес
            ООО «Вектор Трейд»	7701234567	125009, г. Москва, ул. Тверская, д. 1
            """;

        using var файл = new ВременныйФайл(содержимое, ".txt");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V77);

        var запись = Assert.Single(результат.Records);

        Assert.Equal("ООО «Вектор Трейд»", запись.Draft.Name);
        Assert.Equal("7701234567", запись.Draft.Inn);
        Assert.Equal("125009", запись.Draft.PostalZip);
    }

    [Fact]
    public void РазборТекста_ПарыКлючЗначение_Читаются()
    {
        const string содержимое = """
            Наименование=ООО «Вектор Трейд»|ИНН=7701234567|Телефон=+7 (495) 987-65-43
            Наименование=ООО «Ромашка»|ИНН=7707654321|Телефон=+7 (495) 111-22-33
            """;

        using var файл = new ВременныйФайл(содержимое, ".txt");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V77);

        Assert.Equal(2, результат.RecordCount);
        Assert.Equal("ООО «Вектор Трейд»", результат.Records[0].Draft.Name);
        Assert.Equal("7701234567", результат.Records[0].Draft.Inn);
        Assert.Equal("ООО «Ромашка»", результат.Records[1].Draft.Name);
        Assert.Equal("7707654321", результат.Records[1].Draft.Inn);
    }

    /// <summary>
    /// Регрессия F-D3: ANSI-файл (cp1251) с символами диапазона 0x80–0xBF —
    /// тире «—», кавычки «„“» и знак «№» — декодируется штатной кодировкой.
    /// Прежде ручная таблица не переводила этот диапазон, и символы искажались.
    /// </summary>
    [Fact]
    public void РазборТекста_AnsiCp1251_СпецсимволыДекодируются()
    {
        const string содержимое = "Наименование;ИНН\nООО „Ромашка“ — № 1;7701234567";

        var путь = Path.Combine(Path.GetTempPath(), $"onec_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllBytes(путь, TextEncodingDetector.Cp1251.GetBytes(содержимое));

            var результат = _разборщик.ImportFromOneC(путь, OneCVersion.V77);

            var запись = Assert.Single(результат.Records);

            Assert.Equal("ООО „Ромашка“ — № 1", запись.Draft.Name);
            Assert.Equal("7701234567", запись.Draft.Inn);
        }
        finally
        {
            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    [Fact]
    public void РазборТекста_СтрокаСлишкомДлинная_ОбрезаетсяСПредупреждением()
    {
        var длинноеИмя = new string('А', 300);

        var содержимое = $"Наименование;ИНН\n{длинноеИмя};7701234567";

        using var файл = new ВременныйФайл(содержимое, ".txt");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V77);

        var запись = Assert.Single(результат.Records);

        Assert.Equal(255, запись.Draft.Name.Length);
        Assert.Contains(результат.AllWarnings, текст => текст.Contains("255"));
    }

    [Fact]
    public void РазборXml_ЗначениеДлиннее255_ОбрезаетсяСПредупреждением()
    {
        var длинноеИмя = new string('Б', 300);

        var содержимое = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты>
              <Контрагент>
                <Наименование>{длинноеИмя}</Наименование>
                <ИНН>7701234567</ИНН>
              </Контрагент>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        var запись = Assert.Single(результат.Records);

        Assert.Equal(255, запись.Draft.Name.Length);
        Assert.Contains(запись.Warnings, текст => текст.Contains("обрезано"));
        Assert.Contains(результат.AllWarnings, текст => текст.Contains("обрезано"));
    }

    [Fact]
    public void РазборXml_АдресБезИндекса_ТребуетПроверки()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты>
              <Контрагент>
                <Наименование>ООО «Вектор Трейд»</Наименование>
                <Адрес>г. Краснодар, ул. Красная, д. 35</Адрес>
              </Контрагент>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        var запись = Assert.Single(результат.Records);

        Assert.True(запись.Draft.NeedsVerification);
        Assert.Equal(string.Empty, запись.Draft.PostalZip);
        Assert.Contains("г. Краснодар", запись.Draft.PostalLines);
    }

    [Fact]
    public void ОтсутствующийФайл_ВыбрасываетОшибкуСРусскимТекстом()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"onec_missing_{Guid.NewGuid():N}.xml");

        var исключение = Assert.Throws<FileNotFoundException>(
            () => _разборщик.ImportFromOneC(путь, OneCVersion.V8x));

        Assert.Contains("Файл выгрузки 1С не найден", исключение.Message);
        Assert.Contains(путь, исключение.Message);
    }

    [Fact]
    public void ОписаниеИсточникаПакета_ИмеетОжидаемыйВид()
    {
        const string содержимое = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Контрагенты>
              <Контрагент>
                <Наименование>ООО «Вектор Трейд»</Наименование>
              </Контрагент>
            </Контрагенты>
            """;

        using var файл = new ВременныйФайл(содержимое, ".xml");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V8x);

        Assert.Matches(@"^Импорт 1С 8\.x от \d{2}\.\d{2}\.\d{4} из .+\.xml$", результат.Batch.SourceDescription);
        Assert.Equal("1С 8.x", результат.Batch.SourceFormat);
        Assert.Equal(Path.GetFileName(файл.Путь), результат.Batch.SourceFile);

        var запись = Assert.Single(результат.Records);

        Assert.Equal(результат.Batch.SourceDescription, запись.Draft.Source);
    }

    [Fact]
    public void ОписаниеИсточникаПакета_ДляВерсии77_СодержитНазваниеФормата()
    {
        const string содержимое = """
            Наименование;ИНН
            ООО «Вектор Трейд»;7701234567
            """;

        using var файл = new ВременныйФайл(содержимое, ".txt");

        var результат = _разборщик.ImportFromOneC(файл.Путь, OneCVersion.V77);

        Assert.Matches(@"^Импорт 1С 7\.7 от \d{2}\.\d{2}\.\d{4} из .+\.txt$", результат.Batch.SourceDescription);
    }

    /// <summary>
    /// Временный файл выгрузки, удаляемый после проверки.
    /// </summary>
    private sealed class ВременныйФайл : IDisposable
    {
        public ВременныйФайл(string содержимое, string расширение)
        {
            Путь = Path.Combine(Path.GetTempPath(), $"onec_{Guid.NewGuid():N}{расширение}");
            File.WriteAllText(Путь, содержимое);
        }

        public string Путь { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Путь))
                {
                    File.Delete(Путь);
                }
            }
            catch (IOException)
            {
                // Временный файл остаётся в каталоге — на результат проверки не влияет.
            }
        }
    }
}
