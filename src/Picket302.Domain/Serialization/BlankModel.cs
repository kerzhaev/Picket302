namespace Picket302.Domain.Serialization;

/// <summary>
/// Ориентация бланка (ТЗ §5.1, элемент <c>Geometry/Orientation</c>).
/// </summary>
public enum BlankOrientation
{
    /// <summary>Книжная ориентация.</summary>
    Portrait = 0,

    /// <summary>Альбомная ориентация.</summary>
    Landscape = 1,
}

/// <summary>
/// Оформление рамки поля (ТЗ §7.5, инспектор: «Frame (рамка/подчёркивание)»).
/// </summary>
public enum BlankFieldFrame
{
    /// <summary>Без рамки.</summary>
    None = 0,

    /// <summary>Рамка по контуру поля.</summary>
    Box = 1,

    /// <summary>Подчёркивание.</summary>
    Underline = 2,
}

/// <summary>
/// Виды полей бланка (ТЗ §5.1, атрибут <c>Field/@kind</c>; §3.1.5, колонка
/// <c>blank_fields.kind</c>).
/// </summary>
public static class BlankFieldKinds
{
    /// <summary>Текстовое поле.</summary>
    public const string Text = "text";

    /// <summary>Машиночитаемый почтовый индекс (кодовый штамп).</summary>
    public const string PostIndex = "post_index";

    /// <summary>Растровое изображение (логотип, эмблема).</summary>
    public const string Picture = "picture";

    /// <summary>QR-код.</summary>
    public const string Qr = "qr";

    /// <summary>Прямоугольная зона.</summary>
    public const string Zone = "zone";
}

/// <summary>
/// Геометрия бланка (ТЗ §5.1, элемент <c>Geometry</c>). Все линейные
/// величины — в миллиметрах.
/// </summary>
public sealed class BlankGeometry
{
    /// <summary>Ширина бланка, мм.</summary>
    public decimal WidthMm { get; set; }

    /// <summary>Высота бланка, мм.</summary>
    public decimal HeightMm { get; set; }

    /// <summary>Ориентация макета.</summary>
    public BlankOrientation Orientation { get; set; } = BlankOrientation.Portrait;

    /// <summary>Вылет под обрез, мм.</summary>
    public decimal BleedMm { get; set; }

    /// <summary>Признак меток реза.</summary>
    public bool CropMarks { get; set; }
}

/// <summary>
/// Нормативные параметры машиночитаемого индекса в составе поля бланка
/// (ТЗ §5.1, элемент <c>PostIndex</c>; §6.3, ГОСТ Р 51506-99).
/// </summary>
public sealed class BlankPostIndexSpec
{
    /// <summary>Ширина знака, мм (норматив 3,7 мм ±0,1).</summary>
    public decimal DigitWidthMm { get; set; } = 3.7m;

    /// <summary>Высота знака, мм (норматив 5,5 мм ±0,1).</summary>
    public decimal DigitHeightMm { get; set; } = 5.5m;

    /// <summary>Толщина линии элементов цифр, мм (норматив 0,45, диапазон 0,20–0,50).</summary>
    public decimal LineWidthMm { get; set; } = 0.45m;

    /// <summary>Шаг между знаками, мм (норматив 5,4 мм).</summary>
    public decimal SpacingMm { get; set; } = 5.4m;

    /// <summary>Число знаков штампа.</summary>
    public int DigitCount { get; set; } = 6;

    /// <summary>Отрисовка контурной сетки знакомест.</summary>
    public bool ShowGrid { get; set; } = true;
}

/// <summary>
/// Габариты поля на бланке, мм (ТЗ §5.1, элемент <c>Bounds</c>).
/// </summary>
public sealed class BlankBounds
{
    /// <summary>Положение по горизонтали, мм.</summary>
    public decimal XMm { get; set; }

    /// <summary>Положение по вертикали, мм.</summary>
    public decimal YMm { get; set; }

    /// <summary>Ширина поля, мм.</summary>
    public decimal WidthMm { get; set; }

    /// <summary>Высота поля, мм.</summary>
    public decimal HeightMm { get; set; }

    /// <summary>Правая граница, мм.</summary>
    public decimal RightMm => XMm + WidthMm;

    /// <summary>Нижняя граница, мм.</summary>
    public decimal BottomMm => YMm + HeightMm;
}

/// <summary>
/// Поле бланка (ТЗ §5.1, элемент <c>Field</c>; §7.5, инспектор свойств).
/// </summary>
public sealed class BlankFieldSpec
{
    /// <summary>Имя поля; совпадает с именем компонента в скриптах событий.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Вид поля: <see cref="BlankFieldKinds"/>.</summary>
    public string Kind { get; set; } = BlankFieldKinds.Text;

    /// <summary>Габариты поля, мм.</summary>
    public BlankBounds Bounds { get; set; } = new();

    /// <summary>Гарнитура шрифта.</summary>
    public string FontName { get; set; } = "Tahoma";

    /// <summary>Кегль шрифта, пунктов.</summary>
    public decimal FontSizePt { get; set; } = 9.0m;

    /// <summary>Минимальный кегль автоподбора, пунктов (инспектор: MinFontHeight).</summary>
    public decimal MinFontSizePt { get; set; } = 6.5m;

    /// <summary>Максимальный кегль автоподбора, пунктов (инспектор: MaxFontHeight).</summary>
    public decimal MaxFontSizePt { get; set; } = 11.0m;

    /// <summary>Признак полужирного начертания.</summary>
    public bool Bold { get; set; }

    /// <summary>Признак курсивного начертания.</summary>
    public bool Italic { get; set; }

    /// <summary>Горизонтальное выравнивание: Left, Center, Right.</summary>
    public string AlignHorizontal { get; set; } = "Left";

    /// <summary>Вертикальное выравнивание: Top, Center, Bottom.</summary>
    public string AlignVertical { get; set; } = "Center";

    /// <summary>Перенос по словам.</summary>
    public bool WordWrap { get; set; } = true;

    /// <summary>Максимальное число строк.</summary>
    public int MaxLines { get; set; } = 1;

    /// <summary>Максимальное число знаков (инспектор: MaxChars).</summary>
    public int MaxChars { get; set; } = 255;

    /// <summary>Поворот, градусов: 0/90/180/270 (инспектор: Rotation).</summary>
    public int Rotation { get; set; }

    /// <summary>Оформление рамки (инспектор: Frame).</summary>
    public BlankFieldFrame Frame { get; set; } = BlankFieldFrame.None;

    /// <summary>
    /// Виден ли элемент (ТЗ §7.5: Visible=false — элемент исключён везде:
    /// и на холсте экрана, и при спулинге на принтер).
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Печатается ли элемент (ТЗ §7.5: Printable=false — элемент выводится
    /// на экране, но отсекается в потоке команд физического принтера).
    /// </summary>
    public bool Printable { get; set; } = true;

    /// <summary>Сглаживание при отрисовке (элемент Rendering/@antialiasing).</summary>
    public bool Antialiasing { get; set; }

    /// <summary>Центрирование содержимого в габаритах (элемент Rendering/@center).</summary>
    public bool Center { get; set; } = true;

    /// <summary>Прозрачный фон (элемент Rendering/@transparent).</summary>
    public bool Transparent { get; set; }

    /// <summary>Спецификация кодового штампа; только для вида <c>post_index</c>.</summary>
    public BlankPostIndexSpec? PostIndex { get; set; }

    /// <summary>
    /// Идентификатор элемента клипарта; только для вида <c>picture</c>
    /// (ТЗ §8.3, функция 158, остаток S26: «вставка клипарта в
    /// произвольный макет»). NULL — старые blank.xml (совместимость:
    /// такие поля рисуют логотип отправителя как раньше).
    /// </summary>
    public string? ClipartId { get; set; }

    /// <summary>
    /// Скрипт <c>onBeforePrint</c> (ТЗ §7.5, вкладка «События»; например
    /// <c>ZipCode_to.text := &lt;edtIndeks_to&gt;;</c>).
    /// </summary>
    public string? OnBeforePrint { get; set; }
}

/// <summary>
/// Страница бланка (ТЗ §5.1, элемент <c>Page</c>).
/// </summary>
public sealed class BlankPage
{
    /// <summary>Номер страницы, начиная с единицы.</summary>
    public int Number { get; set; } = 1;

    /// <summary>Имя файла растровой подложки; <see langword="null"/> — подложки нет.</summary>
    public string? BackgroundFile { get; set; }

    /// <summary>Непрозрачность подложки, 0.0–1.0.</summary>
    public decimal BackgroundOpacity { get; set; } = 1.0m;

    /// <summary>Поля страницы.</summary>
    public List<BlankFieldSpec> Fields { get; } = new();
}

/// <summary>
/// Векторное координатное дерево бланка (ТЗ §5.1, корень <c>CustomBlank</c>).
/// </summary>
public sealed class CustomBlank
{
    /// <summary>Версия формата.</summary>
    public string Version { get; set; } = "2.0";

    /// <summary>Наименование бланка.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Код страны норматива разметки: RUS, UKR.</summary>
    public string Country { get; set; } = "RUS";

    /// <summary>Признак «стандартная разметка» (пресет адресных зон).</summary>
    public bool StandardLayout { get; set; }

    /// <summary>Геометрия бланка.</summary>
    public BlankGeometry Geometry { get; set; } = new();

    /// <summary>Страницы бланка.</summary>
    public List<BlankPage> Pages { get; } = new();

    /// <summary>Возвращает поля всех страниц в порядке следования страниц.</summary>
    public IEnumerable<BlankFieldSpec> AllFields() =>
        Pages.SelectMany(страница => страница.Fields);
}
