using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace Picket302.Infrastructure.Templating;

/// <summary>
/// Ошибка XSLT-преобразования (ТЗ §8.5, функция 67: «Ошибка парсинга XSLT
/// прерывает трансформацию с указанием строкового смещения»).
/// </summary>
/// <remarks>
/// Текст ошибки формируется на русском языке и содержит строковое смещение
/// в файле таблицы стилей, чтобы пользователь мог найти ошибочное место
/// в шаблоне без установленного Microsoft Office.
/// </remarks>
public sealed class XsltTransformException : Exception
{
    /// <summary>Создаёт ошибку XSLT-преобразования.</summary>
    /// <param name="message">Подробный текст ошибки на русском языке.</param>
    /// <param name="inner">Вложенное исключение.</param>
    public XsltTransformException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Движок XSLT-преобразования бланков и документов без Microsoft Office
/// (ТЗ §8.5, функция 67 — «Шаблоны документов на связке XML + XSLT»).
/// </summary>
/// <remarks>
/// <para>
/// Преобразование выполняется штатным <see cref="XslCompiledTransform"/>:
/// ни OLE, ни COM, ни Interop-библиотеки Microsoft Office не требуются,
/// поэтому экспорт работает на сервере под Windows Server Core.
/// </para>
/// <para>
/// Безопасность: обработка инструкций DTD и внешних сущностей запрещена
/// (<see cref="DtdProcessing.Prohibit"/>, внешние разрешения отключены),
/// а блоки сценариев <c>msxsl:script</c> отклоняются — преобразование не
/// исполняет код и не обращается к внешним ресурсам.
/// </para>
/// </remarks>
public sealed class XsltTransformEngine
{
    /// <summary>Пространство имён расширений MS XSLT (блоки сценариев).</summary>
    private const string MsXsltNamespace = "urn:schemas-microsoft-com:xslt";

    /// <summary>
    /// Преобразует XML-документ через таблицу стилей XSLT и возвращает результат
    /// (ТЗ §8.5, функция 67: <c>public string TransformToOutput(string xmlData, string xsltPath)</c>).
    /// </summary>
    /// <param name="xmlData">Исходные XML-данные.</param>
    /// <param name="xsltPath">Путь к файлу таблицы стилей XSLT.</param>
    /// <returns>Результат преобразования в виде XML-текста.</returns>
    /// <exception cref="XsltTransformException">
    /// Файл таблицы стилей не найден, не разбирается либо преобразование не выполнено.
    /// </exception>
    public string TransformToOutput(string xmlData, string xsltPath)
    {
        ArgumentNullException.ThrowIfNull(xmlData);
        ArgumentException.ThrowIfNullOrWhiteSpace(xsltPath);

        var transform = LoadTransform(xsltPath);

        using var input = new StringReader(xmlData);
        using var inputReader = XmlReader.Create(input, CreateReaderSettings());

        return Run(transform, inputReader, xsltPath);
    }

    /// <summary>
    /// Преобразует XML-документ из потока через таблицу стилей XSLT
    /// (ТЗ §8.5, функция 67).
    /// </summary>
    /// <param name="xmlInput">Поток с исходными XML-данными.</param>
    /// <param name="xsltPath">Путь к файлу таблицы стилей XSLT.</param>
    /// <returns>Результат преобразования в виде XML-текста.</returns>
    /// <exception cref="XsltTransformException">
    /// Таблица стилей не разбирается либо преобразование не выполнено.
    /// </exception>
    public string TransformToOutput(Stream xmlInput, string xsltPath)
    {
        ArgumentNullException.ThrowIfNull(xmlInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(xsltPath);

        var transform = LoadTransform(xsltPath);

        using var inputReader = XmlReader.Create(xmlInput, CreateReaderSettings());

        return Run(transform, inputReader, xsltPath);
    }

    /// <summary>
    /// Выполняет преобразование без исключения: ошибка возвращается текстом
    /// на русском языке (ТЗ §8.5, функция 67).
    /// </summary>
    /// <param name="xmlData">Исходные XML-данные.</param>
    /// <param name="xsltPath">Путь к файлу таблицы стилей XSLT.</param>
    /// <param name="output">Результат преобразования; при ошибке — пустая строка.</param>
    /// <param name="error">Текст ошибки на русском языке; при успехе — пустая строка.</param>
    /// <returns><see langword="true"/>, если преобразование выполнено.</returns>
    public bool TryTransform(string xmlData, string xsltPath, out string output, out string error)
    {
        try
        {
            output = TransformToOutput(xmlData, xsltPath);
            error = string.Empty;

            return true;
        }
        catch (XsltTransformException ошибка)
        {
            output = string.Empty;
            error = ошибка.Message;

            return false;
        }
    }

    /// <summary>
    /// Загружает и компилирует таблицу стилей XSLT с запретом внешних
    /// разрешений и исполняемых сценариев.
    /// </summary>
    /// <param name="xsltPath">Путь к файлу таблицы стилей XSLT.</param>
    /// <returns>Скомпилированное преобразование.</returns>
    /// <exception cref="XsltTransformException">Таблица стилей не найдена или не разбирается.</exception>
    private static XslCompiledTransform LoadTransform(string xsltPath)
    {
        if (!File.Exists(xsltPath))
        {
            throw new XsltTransformException(
                $"Файл таблицы стилей XSLT не найден: {xsltPath}.");
        }

        XDocument stylesheet;

        try
        {
            using var reader = XmlReader.Create(xsltPath, CreateReaderSettings());

            // SetLineInfo сохраняет номер строки каждого элемента — он
            // указывается в тексте ошибки (ТЗ §8.5, функция 67).
            stylesheet = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ошибка)
        {
            throw new XsltTransformException(
                $"Ошибка разбора таблицы стилей XSLT «{xsltPath}» "
                + $"(строка {ошибка.LineNumber}, позиция {ошибка.LinePosition}): {ошибка.Message}",
                ошибка);
        }
        catch (IOException ошибка)
        {
            throw new XsltTransformException(
                $"Не удалось прочитать таблицу стилей XSLT «{xsltPath}»: {ошибка.Message}",
                ошибка);
        }

        EnsureNoScriptBlocks(stylesheet, xsltPath);

        var settings = new XsltSettings
        {
            // Исполнение сценариев и функция document() запрещены.
            EnableScript = false,
            EnableDocumentFunction = false,
        };

        var transform = new XslCompiledTransform();

        // Включения xsl:include и xsl:import разрешены только внутри каталога
        // шаблонов: сетевые адреса и файлы вне каталога недоступны. Шаблоны
        // поставки (EVIDENCE/template_analysis.txt) используют включения
        // вида «../styles/MSExcelStyles.xslt», поэтому каталог шаблона и его
        // родительский каталог считаются доверенной зоной.
        var разрешитель = new КаталоговыйРазрешитель(КореньШаблонов(xsltPath));

        try
        {
            // Таблица стилей читается из исходных байтов, чтобы нумерация
            // строк в тексте ошибки совпадала с файлом шаблона.
            using var поток = new MemoryStream(File.ReadAllBytes(xsltPath));
            using var reader = XmlReader.Create(
                поток,
                CreateReaderSettings(),
                new Uri(Path.GetFullPath(xsltPath)).AbsoluteUri);

            transform.Load(reader, settings, разрешитель);
        }
        catch (XsltCompileException ошибка)
        {
            throw new XsltTransformException(
                $"Ошибка разбора таблицы стилей XSLT «{xsltPath}» "
                + $"(строка {ошибка.LineNumber}, позиция {ошибка.LinePosition}): {ошибка.Message}",
                ошибка);
        }
        catch (XsltException ошибка)
        {
            throw new XsltTransformException(
                $"Ошибка разбора таблицы стилей XSLT «{xsltPath}» "
                + $"(строка {ошибка.LineNumber}, позиция {ошибка.LinePosition}): {ошибка.Message}",
                ошибка);
        }
        catch (XmlException ошибка)
        {
            throw new XsltTransformException(
                $"Ошибка разбора таблицы стилей XSLT «{xsltPath}» "
                + $"(строка {ошибка.LineNumber}, позиция {ошибка.LinePosition}): {ошибка.Message}",
                ошибка);
        }

        return transform;
    }

    /// <summary>
    /// Определяет корень, в котором разрешаются включения xsl:include и
    /// xsl:import: каталог самой таблицы стилей и его родительский каталог.
    /// </summary>
    /// <param name="xsltPath">Путь к файлу таблицы стилей XSLT.</param>
    /// <returns>Путь корневого каталога шаблонов.</returns>
    private static string КореньШаблонов(string xsltPath)
    {
        var каталог = Path.GetDirectoryName(Path.GetFullPath(xsltPath)) ?? string.Empty;
        var родитель = Path.GetDirectoryName(каталог);

        return string.IsNullOrWhiteSpace(родитель) ? каталог : родитель;
    }

    /// <summary>
    /// Выполняет скомпилированное преобразование, возвращая результат текстом.
    /// </summary>
    /// <param name="transform">Скомпилированное преобразование.</param>
    /// <param name="inputReader">Читатель исходного XML.</param>
    /// <param name="xsltPath">Путь к таблице стилей (для текста ошибки).</param>
    /// <returns>Результат преобразования.</returns>
    /// <exception cref="XsltTransformException">Преобразование не выполнено.</exception>
    private static string Run(XslCompiledTransform transform, XmlReader inputReader, string xsltPath)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        };

        try
        {
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                transform.Transform(inputReader, writer);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (XsltException ошибка)
        {
            throw new XsltTransformException(
                $"Ошибка XSLT-преобразования по таблице стилей «{xsltPath}»: {ошибка.Message}",
                ошибка);
        }
        catch (XmlException ошибка)
        {
            throw new XsltTransformException(
                $"Некорректные исходные XML-данные "
                + $"(строка {ошибка.LineNumber}, позиция {ошибка.LinePosition}): {ошибка.Message}",
                ошибка);
        }
    }

    /// <summary>
    /// Проверяет отсутствие исполняемых сценариев в таблице стилей: блоки
    /// <c>msxsl:script</c> запрещены политикой безопасного преобразования.
    /// </summary>
    /// <param name="stylesheet">Разобранная таблица стилей.</param>
    /// <param name="xsltPath">Путь к таблице стилей (для текста ошибки).</param>
    /// <exception cref="XsltTransformException">Найден блок сценария.</exception>
    private static void EnsureNoScriptBlocks(XDocument stylesheet, string xsltPath)
    {
        var scripts = stylesheet.Descendants()
            .Where(element => element.Name.LocalName == "script"
                && string.Equals(element.Name.NamespaceName, MsXsltNamespace, StringComparison.Ordinal))
            .ToArray();

        if (scripts.Length == 0)
        {
            return;
        }

        var line = (scripts[0] as IXmlLineInfo)?.LineNumber ?? 0;

        throw new XsltTransformException(
            $"Исполняемые сценарии (msxsl:script) в таблице стилей XSLT «{xsltPath}» запрещены "
            + $"(строка {line}).");
    }

    /// <summary>
    /// Параметры чтения XML с запретом инструкций DTD и внешних сущностей.
    /// </summary>
    /// <returns>Безопасные параметры чтения.</returns>
    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = false,
        IgnoreWhitespace = false,
        IgnoreProcessingInstructions = false,
        CloseInput = false,
    };

    /// <summary>
    /// Разрешает включения <c>xsl:include</c> и <c>xsl:import</c> только
    /// внутри корня шаблонов: сетевые адреса и файлы вне корня недоступны,
    /// поэтому таблица стилей не может обратиться к посторонним ресурсам.
    /// </summary>
    private sealed class КаталоговыйРазрешитель : XmlResolver
    {
        private readonly string _корень;

        /// <summary>
        /// Создаёт разрешитель для указанного корня шаблонов.
        /// </summary>
        /// <param name="корень">Корневой каталог шаблонов.</param>
        public КаталоговыйРазрешитель(string корень)
        {
            _корень = Path.GetFullPath(корень).TrimEnd(Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Разрешает относительный адрес включения относительно включающего
        /// документа.
        /// </summary>
        /// <param name="baseUri">Адрес включающего документа.</param>
        /// <param name="relativeUri">Относительный адрес включения.</param>
        /// <returns>Адрес файла либо <see langword="null"/>, если адрес вне корня.</returns>
        /// <remarks>
        /// Возврат <see langword="null"/> означает, что адрес не разрешён: именно
        /// так XmlResolver сообщает вызывающему коду о неразрешённом ресурсе.
        /// </remarks>
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            if (string.IsNullOrWhiteSpace(relativeUri))
            {
                return null!;
            }

            Uri? целевой;

            if (Uri.TryCreate(relativeUri, UriKind.Absolute, out var абсолютный))
            {
                целевой = абсолютный;
            }
            else if (baseUri is null)
            {
                return null!;
            }
            else
            {
                целевой = new Uri(baseUri, relativeUri);
            }

            if (!целевой.IsFile)
            {
                return null!;
            }

            var путь = Path.GetFullPath(целевой.LocalPath);

            if (!путь.StartsWith(_корень + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null!;
            }

            return new Uri(путь);
        }

        /// <summary>
        /// Открывает файл включения только для чтения.
        /// </summary>
        /// <param name="absoluteUri">Адрес файла включения.</param>
        /// <param name="role">Роль ресурса.</param>
        /// <param name="ofObjectToReturn">Требуемый тип объекта.</param>
        /// <returns>Поток файла либо <see langword="null"/>, если файл недоступен.</returns>
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile)
            {
                return null;
            }

            var путь = absoluteUri.LocalPath;

            if (!File.Exists(путь))
            {
                return null;
            }

            return new FileStream(путь, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// Разрешает поток и текстовый чтение файлов включения.
        /// </summary>
        /// <param name="absoluteUri">Адрес файла включения.</param>
        /// <param name="type">Требуемый тип объекта.</param>
        /// <returns><see langword="true"/>, если тип поддерживается.</returns>
        public override bool SupportsType(Uri absoluteUri, Type? type) =>
            type == typeof(Stream) || type == typeof(TextReader);
    }
}
