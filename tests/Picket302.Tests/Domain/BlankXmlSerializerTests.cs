using Picket302.Domain.Serialization;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Сериализация формата бланка (ТЗ §5.1; §8.3, функция 39: round-trip,
/// XSD-отклонение с указанием строки).
/// </summary>
public sealed class BlankXmlSerializerTests
{
    private readonly BlankXmlSerializer _сериализатор = new();

    [Fact]
    public void SerializeBlank_Листинг51_ТочныеЭлементы()
    {
        var бланк = ПримерЛистинга();

        var xml = _сериализатор.SerializeBlank(бланк);

        Assert.Contains("<CustomBlank version=\"2.0\">", xml);
        Assert.Contains("<Name>Конверт C5 с логотипом ГОСТ</Name>", xml);
        Assert.Contains("<Country>RUS</Country>", xml);
        Assert.Contains("<StandardLayout>true</StandardLayout>", xml);
        Assert.Contains("<Width mm=\"229.0\" />", xml);
        Assert.Contains("<Height mm=\"162.0\" />", xml);
        Assert.Contains("<Orientation>Landscape</Orientation>", xml);
        Assert.Contains("<Bleed mm=\"0.0\" />", xml);
        Assert.Contains("<CropMarks>false</CropMarks>", xml);
        Assert.Contains("<Page number=\"1\">", xml);
        Assert.Contains("<Background file=\"background.png\" opacity=\"1.0\" />", xml);
        Assert.Contains("kind=\"post_index\"", xml);
        Assert.Contains("mmDigitWidth=\"3.7\"", xml);
        Assert.Contains("mmSpacing=\"5.4\"", xml);
        Assert.Contains("digitCount=\"6\"", xml);
        Assert.Contains("showGrid=\"true\"", xml);
    }

    [Fact]
    public void Deserialize_RoundTrip_СохраняетМодель()
    {
        var бланк = ПримерЛистинга();
        бланк.Pages[0].Fields[1].OnBeforePrint = "ZipCode_to.text := <edtIndeks_to>;";

        var xml = _сериализатор.SerializeBlank(бланк);
        var восстановленный = _сериализатор.Deserialize(xml);
        Assert.Equal(бланк.Name, восстановленный.Name);
        Assert.Equal(бланк.Country, восстановленный.Country);
        Assert.True(восстановленный.StandardLayout);
        Assert.Equal(бланк.Geometry.WidthMm, восстановленный.Geometry.WidthMm);
        Assert.Equal(бланк.Geometry.HeightMm, восстановленный.Geometry.HeightMm);
        Assert.Equal(BlankOrientation.Landscape, восстановленный.Geometry.Orientation);
        Assert.Single(восстановленный.Pages);
        Assert.Equal(3, восстановленный.Pages[0].Fields.Count);

        var штамп = восстановленный.Pages[0].Fields[0];
        Assert.Equal(BlankFieldKinds.PostIndex, штамп.Kind);
        Assert.NotNull(штамп.PostIndex);
        Assert.Equal(3.7m, штамп.PostIndex!.DigitWidthMm);
        Assert.Equal(5.5m, штамп.PostIndex.DigitHeightMm);
        Assert.Equal(0.45m, штамп.PostIndex.LineWidthMm);
        Assert.Equal(5.4m, штамп.PostIndex.SpacingMm);
        Assert.Equal(145.0m, штамп.Bounds.XMm);
        Assert.Equal(125.0m, штамп.Bounds.YMm);

        var текст = восстановленный.Pages[0].Fields[1];
        Assert.Equal("Tahoma", текст.FontName);
        Assert.Equal(9.0m, текст.FontSizePt);
        Assert.Equal(6.5m, текст.MinFontSizePt);
        Assert.Equal(11.0m, текст.MaxFontSizePt);
        Assert.True(текст.WordWrap);
        Assert.Equal("Left", текст.AlignHorizontal);
        Assert.Equal("Center", текст.AlignVertical);

        var qr = восстановленный.Pages[0].Fields[2];
        Assert.False(qr.Antialiasing);
        Assert.True(qr.Center);
        Assert.False(qr.Transparent);
        Assert.Contains("ZipCode_to.text", текст.OnBeforePrint);
    }

    [Fact]
    public void Deserialize_XSDОтклонение_УказаниеСтроки()
    {
        // Неизвестный элемент и неверное значение перечисления — отклонение
        // с номером строки (ТЗ §8.3, функция 39).
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <CustomBlank version="2.0">
              <Name>Бланк</Name>
              <Country>RUS</Country>
              <StandardLayout>true</StandardLayout>
              <Geometry>
                <Width mm="100.0" />
                <Height mm="50.0" />
                <Orientation>Sideways</Orientation>
                <Bleed mm="0.0" />
                <CropMarks>false</CropMarks>
              </Geometry>
              <Pages>
                <Page number="1">
                  <Fields>
                    <Field name="f" kind="text">
                      <Bounds x="1" y="1" width="10" height="4" />
                    </Field>
                  </Fields>
                </Page>
              </Pages>
            </CustomBlank>
            """;

        var ошибка = Assert.Throws<BlankXmlException>(() => _сериализатор.Deserialize(xml));

        Assert.Contains("XSD", ошибка.Message);
        Assert.True(ошибка.LineNumber > 0);
        Assert.Equal(9, ошибка.LineNumber);
    }

    [Fact]
    public void Deserialize_НекорректныйXml_ПозицияОшибки()
    {
        const string xml = "<CustomBlank version=\"2.0\">\n  <Name>Обрыв";

        var ошибка = Assert.Throws<BlankXmlException>(() => _сериализатор.Deserialize(xml));

        Assert.True(ошибка.LineNumber >= 1);
    }

    [Fact]
    public void Deserialize_ШтампБезPostIndex_Отклонение()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <CustomBlank version="2.0">
              <Name>Бланк</Name>
              <Country>RUS</Country>
              <StandardLayout>false</StandardLayout>
              <Geometry>
                <Width mm="100.0" />
                <Height mm="50.0" />
                <Orientation>Portrait</Orientation>
                <Bleed mm="0.0" />
                <CropMarks>false</CropMarks>
              </Geometry>
              <Pages>
                <Page number="1">
                  <Fields>
                    <Field name="s" kind="post_index">
                      <Bounds x="1" y="1" width="10" height="4" />
                    </Field>
                  </Fields>
                </Page>
              </Pages>
            </CustomBlank>
            """;

        Assert.Throws<BlankXmlException>(() => _сериализатор.Deserialize(xml));
    }

    [Fact]
    public void Deserialize_ФлагиVisiblePrintable_СемантикаПо75()
    {
        var бланк = ПримерЛистинга();
        бланк.Pages[0].Fields[1].Visible = false;
        бланк.Pages[0].Fields[2].Printable = false;

        var xml = _сериализатор.SerializeBlank(бланк);
        var восстановленный = _сериализатор.Deserialize(xml);

        Assert.False(восстановленный.Pages[0].Fields[1].Visible);
        Assert.True(восстановленный.Pages[0].Fields[1].Printable);
        Assert.True(восстановленный.Pages[0].Fields[2].Visible);
        Assert.False(восстановленный.Pages[0].Fields[2].Printable);
    }

    private static CustomBlank ПримерЛистинга()
    {
        var бланк = new CustomBlank
        {
            Version = "2.0",
            Name = "Конверт C5 с логотипом ГОСТ",
            Country = "RUS",
            StandardLayout = true,
            Geometry = new BlankGeometry
            {
                WidthMm = 229.0m,
                HeightMm = 162.0m,
                Orientation = BlankOrientation.Landscape,
                BleedMm = 0.0m,
                CropMarks = false,
            },
        };

        var страница = new BlankPage
        {
            Number = 1,
            BackgroundFile = "background.png",
            BackgroundOpacity = 1.0m,
        };

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "edtIndeks_to",
            Kind = BlankFieldKinds.PostIndex,
            Bounds = new BlankBounds { XMm = 145.0m, YMm = 125.0m, WidthMm = 70.0m, HeightMm = 20.0m },
            PostIndex = new BlankPostIndexSpec
            {
                DigitWidthMm = 3.7m,
                DigitHeightMm = 5.5m,
                LineWidthMm = 0.45m,
                SpacingMm = 5.4m,
                DigitCount = 6,
                ShowGrid = true,
            },
        });

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "edt_str_to1",
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 110.0m, YMm = 85.0m, WidthMm = 105.0m, HeightMm = 6.0m },
            FontName = "Tahoma",
            FontSizePt = 9.0m,
            MinFontSizePt = 6.5m,
            MaxFontSizePt = 11.0m,
            Bold = false,
            AlignHorizontal = "Left",
            AlignVertical = "Center",
            WordWrap = true,
            MaxLines = 1,
        });

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "qr_code",
            Kind = BlankFieldKinds.Qr,
            Bounds = new BlankBounds { XMm = 180.0m, YMm = 20.0m, WidthMm = 35.0m, HeightMm = 35.0m },
            Antialiasing = false,
            Center = true,
            Transparent = false,
        });

        бланк.Pages.Add(страница);

        return бланк;
    }
}
