using System.IO.Compression;
using System.Text;
using Picket302.Application.Templating;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Контроллер мастера редактирования шаблонов документов: работа с шаблоном,
/// предпросмотр структуры, вставка доступных маркеров БД и проверка структуры
/// получаемого документа (ТЗ §8.5, функция 66: «Мастер редактирования шаблонов
/// документов»).
/// </summary>
/// <remarks>
/// <para>
/// Контроллер не зависит от окон: вся логика шага доступна без создания формы,
/// поэтому она проверяется модульными тестами. Форма лишь отображает состояние
/// контроллера и передаёт ему действия оператора.
/// </para>
/// <para>
/// Редактируется одна часть шаблона: текст самого файла для текстовых
/// шаблонов и таблиц стилей XSLT либо часть <c>word/document.xml</c> пакета
/// OpenXML. Сохранение пакета перезаписывает только эту часть, остальные части
/// переносятся без изменений.
/// </para>
/// </remarks>
public sealed class DocTemplateWizardController
{
    /// <summary>Кодировка сохранения текстовых шаблонов: UTF-8 без маркера BOM.</summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DocumentMarkerCatalog _catalog;

    private readonly DocumentTemplateAnalyzer _analyzer;

    private readonly IReadOnlyDictionary<string, object>? _parameters;

    private string _templatePath = string.Empty;

    private string _partName = string.Empty;

    private DocumentTemplateKind _kind = DocumentTemplateKind.Text;

    /// <summary>
    /// Создаёт контроллер мастера.
    /// </summary>
    /// <param name="catalog">
    /// Каталог доступных маркеров БД; при отсутствии — общий каталог.
    /// </param>
    /// <param name="analyzer">
    /// Анализатор шаблона; при отсутствии — анализатор по каталогу маркеров.
    /// </param>
    /// <param name="parameters">
    /// Значения маркеров для предпросмотра (например, реквизиты выбранного
    /// контрагента); при отсутствии значения маркеров не разрешаются.
    /// </param>
    public DocTemplateWizardController(
        DocumentMarkerCatalog? catalog = null,
        DocumentTemplateAnalyzer? analyzer = null,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        _catalog = catalog ?? new DocumentMarkerCatalog();
        _analyzer = analyzer ?? new DocumentTemplateAnalyzer(_catalog);
        _parameters = parameters;
    }

    /// <summary>Каталог доступных маркеров БД.</summary>
    public DocumentMarkerCatalog Catalog => _catalog;

    /// <summary>Путь к открытому шаблону; пустая строка — шаблон не выбран.</summary>
    public string TemplatePath => _templatePath;

    /// <summary>Вид открытого шаблона.</summary>
    public DocumentTemplateKind TemplateKind => _kind;

    /// <summary>Текст редактируемой части шаблона.</summary>
    public string TemplateText { get; private set; } = string.Empty;

    /// <summary>Позиция курсора в редактируемом тексте.</summary>
    public int CursorPosition { get; private set; }

    /// <summary>Признак несохранённых изменений шаблона.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Признак того, что шаблон открыт.</summary>
    public bool HasTemplate => _templatePath.Length > 0;

    /// <summary>
    /// Маркеры шаблона в порядке появления со строкой, позицией и найденным
    /// значением (предпросмотр структуры документа).
    /// </summary>
    public IReadOnlyList<DocumentMarkerOccurrence> Markers { get; private set; } =
        Array.Empty<DocumentMarkerOccurrence>();

    /// <summary>
    /// Проблемы структуры, найденные последней проверкой; пустой перечень —
    /// структура корректна.
    /// </summary>
    public IReadOnlyList<string> StructureProblems { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Открывает шаблон: определяет его вид, читает редактируемую часть и
    /// строит предпросмотр структуры.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    /// <exception cref="InvalidOperationException">
    /// Пакет OpenXML не содержит обязательной части <c>word/document.xml</c>.
    /// </exception>
    public void OpenTemplate(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

        var полныйПуть = Path.GetFullPath(templatePath);

        if (!File.Exists(полныйПуть))
        {
            throw new FileNotFoundException($"Файл шаблона не найден: {полныйПуть}", полныйПуть);
        }

        var вид = _analyzer.DetectKind(полныйПуть);

        string текст;
        string часть;

        if (вид == DocumentTemplateKind.OpenXml)
        {
            часть = DocumentTemplateAnalyzer.DocumentPart;
            текст = ПрочитатьЧастьПакета(полныйПуть, часть);
        }
        else
        {
            часть = Path.GetFileName(полныйПуть);
            текст = File.ReadAllText(полныйПуть, Encoding.UTF8);
        }

        _templatePath = полныйПуть;
        _kind = вид;
        _partName = часть;
        TemplateText = текст;
        CursorPosition = 0;
        IsDirty = false;
        StructureProblems = Array.Empty<string>();

        ОбновитьМаркеры();
    }

    /// <summary>
    /// Заменяет текст редактируемой части шаблона (изменение в редакторе).
    /// </summary>
    /// <param name="text">Новый текст шаблона.</param>
    public void SetTemplateText(string text)
    {
        TemplateText = text ?? string.Empty;

        if (CursorPosition > TemplateText.Length)
        {
            CursorPosition = TemplateText.Length;
        }

        IsDirty = true;

        ОбновитьМаркеры();
    }

    /// <summary>
    /// Задаёт позицию курсора в редактируемом тексте.
    /// </summary>
    /// <param name="position">Позиция курсора; значение сверх длины текста обрезается.</param>
    public void SetCursorPosition(int position)
        => CursorPosition = position < 0 ? 0 : Math.Min(position, TemplateText.Length);

    /// <summary>
    /// Вставляет текст маркера в позицию курсора редактора
    /// (ТЗ §8.5, функция 66: «Двойной клик по маркеру в списке вставляет его
    /// текст в позицию курсора редактора»).
    /// </summary>
    /// <param name="markerName">Имя маркера без решёток.</param>
    /// <returns>Вставленный текст маркера.</returns>
    /// <exception cref="ArgumentException">Маркер отсутствует в каталоге.</exception>
    public string InsertMarker(string markerName) => InsertMarkerAt(markerName, CursorPosition);

    /// <summary>
    /// Вставляет текст маркера в заданную позицию редактируемого текста.
    /// </summary>
    /// <param name="markerName">Имя маркера без решёток.</param>
    /// <param name="position">
    /// Позиция вставки; значение сверх длины текста обрезается до конца текста.
    /// </param>
    /// <returns>Вставленный текст маркера.</returns>
    /// <exception cref="ArgumentException">Маркер отсутствует в каталоге.</exception>
    public string InsertMarkerAt(string markerName, int position)
    {
        var маркер = _catalog.Find(markerName)
            ?? throw new ArgumentException(
                $"Маркер «{markerName}» отсутствует в каталоге доступных маркеров.",
                nameof(markerName));

        var текстМаркера = маркер.Text;
        var позиция = position < 0 ? 0 : Math.Min(position, TemplateText.Length);

        TemplateText = string.Concat(
            TemplateText[..позиция],
            текстМаркера,
            TemplateText[позиция..]);

        CursorPosition = позиция + текстМаркера.Length;
        IsDirty = true;

        ОбновитьМаркеры();

        return текстМаркера;
    }

    /// <summary>
    /// Отбирает доступные маркеры по подстроке имени, описания или группы.
    /// </summary>
    /// <param name="filter">Фильтр; пустое значение — все маркеры.</param>
    /// <returns>Отобранные маркеры.</returns>
    public IReadOnlyList<DocumentMarker> FindMarkers(string? filter) => _catalog.Search(filter);

    /// <summary>
    /// Проверяет структуру получаемого документа и запоминает найденные проблемы
    /// (ТЗ §8.5, функция 66: «проверки структуры получаемого XML»).
    /// </summary>
    /// <returns>Перечень проблем; пустой перечень — структура корректна.</returns>
    public IReadOnlyList<string> CheckStructure()
    {
        if (!HasTemplate)
        {
            StructureProblems = new[] { "Шаблон не выбран: проверять структуру нечего." };

            return StructureProblems;
        }

        var имяПроверки = _kind == DocumentTemplateKind.OpenXml
            ? DocumentTemplateAnalyzer.DocumentPart
            : Path.GetFileName(_templatePath);

        StructureProblems = _analyzer.ValidateContent(Utf8WithoutBom.GetBytes(TemplateText), имяПроверки);

        return StructureProblems;
    }

    /// <summary>
    /// Сохраняет отредактированный шаблон: текстовый шаблон записывается как
    /// файл, пакет OpenXML — перезаписывается только редактируемая часть.
    /// </summary>
    /// <param name="path">
    /// Путь сохранения; при отсутствии — путь открытого шаблона.
    /// </param>
    /// <returns><see langword="true"/>, если шаблон сохранён.</returns>
    /// <exception cref="InvalidOperationException">Путь сохранения не задан.</exception>
    public bool Save(string? path = null)
    {
        var цель = string.IsNullOrWhiteSpace(path) ? _templatePath : path.Trim();

        if (цель.Length == 0)
        {
            throw new InvalidOperationException("Путь сохранения шаблона не задан.");
        }

        цель = Path.GetFullPath(цель);

        var каталог = Path.GetDirectoryName(цель);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        if (_kind == DocumentTemplateKind.OpenXml)
        {
            if (!string.Equals(цель, _templatePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(_templatePath, цель, overwrite: true);
            }

            ЗаписатьЧастьПакета(цель, _partName, TemplateText);
        }
        else
        {
            File.WriteAllText(цель, TemplateText, Utf8WithoutBom);
        }

        _templatePath = цель;
        IsDirty = false;

        return true;
    }

    /// <summary>
    /// Читает часть пакета OpenXML и декодирует её как текст.
    /// </summary>
    /// <param name="path">Путь к пакету.</param>
    /// <param name="partName">Имя части пакета.</param>
    /// <returns>Текст части.</returns>
    /// <exception cref="InvalidOperationException">Часть отсутствует в пакете.</exception>
    private static string ПрочитатьЧастьПакета(string path, string partName)
    {
        using var архив = ZipFile.OpenRead(path);

        var запись = архив.GetEntry(partName);

        if (запись is null)
        {
            throw new InvalidOperationException(
                $"В пакете документа отсутствует обязательная часть «{partName}».");
        }

        using var поток = запись.Open();
        using var буфер = new MemoryStream();

        поток.CopyTo(буфер);

        return DocumentTemplateAnalyzer.DecodeText(буфер.ToArray());
    }

    /// <summary>
    /// Перезаписывает часть пакета OpenXML, сохраняя остальные части без
    /// изменений.
    /// </summary>
    /// <param name="path">Путь к пакету.</param>
    /// <param name="partName">Имя части пакета.</param>
    /// <param name="text">Новый текст части.</param>
    private static void ЗаписатьЧастьПакета(string path, string partName, string text)
    {
        using var архив = ZipFile.Open(path, ZipArchiveMode.Update);

        var запись = архив.GetEntry(partName);

        if (запись is null)
        {
            throw new InvalidOperationException(
                $"В пакете документа отсутствует обязательная часть «{partName}».");
        }

        запись.Delete();

        var новаяЗапись = архив.CreateEntry(partName, CompressionLevel.Optimal);

        using var поток = новаяЗапись.Open();

        var байты = Utf8WithoutBom.GetBytes(text);

        поток.Write(байты, 0, байты.Length);
    }

    /// <summary>
    /// Пересчитывает предпросмотр структуры по текущему тексту шаблона.
    /// </summary>
    private void ОбновитьМаркеры()
    {
        if (_partName.Length == 0)
        {
            Markers = Array.Empty<DocumentMarkerOccurrence>();

            return;
        }

        var часть = new DocumentTemplatePart(_partName, TemplateText);

        Markers = _analyzer.AnalyzeParts(new[] { часть }, _parameters);
    }
}
