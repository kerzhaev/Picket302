namespace Picket302.Application.Templating;

/// <summary>
/// Результат генерации документа по шаблону произвольного вида
/// (ТЗ §8.5, функция 65: «Генерация документов произвольного вида»).
/// </summary>
/// <remarks>
/// Содержит сформированный документ, имя и расширение файла, перенесённые от
/// шаблона, а также предупреждения: неизвестные маркеры оставляются в тексте
/// без изменений и перечисляются в <see cref="UnresolvedMarkers"/>.
/// </remarks>
public sealed class DocumentRenderResult
{
    /// <summary>
    /// Создаёт результат генерации документа.
    /// </summary>
    /// <param name="content">Содержимое сформированного документа.</param>
    /// <param name="fileName">Имя файла документа (перенесено от шаблона).</param>
    /// <param name="extension">Расширение файла документа (перенесено от шаблона).</param>
    /// <param name="warnings">Предупреждения на русском языке.</param>
    /// <param name="unresolvedMarkers">Маркеры, для которых не найдено значение.</param>
    /// <param name="markers">Маркеры шаблона в порядке появления.</param>
    public DocumentRenderResult(
        byte[] content,
        string fileName,
        string extension,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> unresolvedMarkers,
        IReadOnlyList<DocumentMarkerOccurrence> markers)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        FileName = fileName ?? string.Empty;
        Extension = extension ?? string.Empty;
        Warnings = warnings ?? Array.Empty<string>();
        UnresolvedMarkers = unresolvedMarkers ?? Array.Empty<string>();
        Markers = markers ?? Array.Empty<DocumentMarkerOccurrence>();
    }

    /// <summary>Содержимое сформированного документа.</summary>
    public byte[] Content { get; }

    /// <summary>Имя файла документа; переносится от шаблона без изменений.</summary>
    public string FileName { get; }

    /// <summary>Расширение файла документа; переносится от шаблона без изменений.</summary>
    public string Extension { get; }

    /// <summary>Предупреждения, возникшие при формировании документа.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Маркеры, для которых в наборе параметров не найдено значение: они
    /// оставлены в документе без изменений.
    /// </summary>
    public IReadOnlyList<string> UnresolvedMarkers { get; }

    /// <summary>Маркеры шаблона в порядке появления.</summary>
    public IReadOnlyList<DocumentMarkerOccurrence> Markers { get; }

    /// <summary>Признак наличия предупреждений.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Признак наличия неразрешённых маркеров.</summary>
    public bool HasUnresolvedMarkers => UnresolvedMarkers.Count > 0;
}
