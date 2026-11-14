using Picket302.Shared.Dtos;

namespace Picket302.Shared.Commands;

/// <summary>
/// Сохранение шаблона конверта или бланка.
/// </summary>
/// <param name="Template">Шаблон.</param>
/// <param name="ExpectedVersion">Ожидаемая версия строки шаблона.</param>
public sealed record SavePrintTemplateRequest(
    PrintTemplateDto Template,
    long ExpectedVersion);

/// <summary>
/// Удаление шаблона.
/// </summary>
/// <param name="Id">Идентификатор шаблона.</param>
/// <param name="ExpectedVersion">Ожидаемая версия строки шаблона.</param>
public sealed record DeletePrintTemplateRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Сохранение поля бланка.
/// </summary>
/// <param name="Field">Поле бланка.</param>
/// <param name="ExpectedVersion">Ожидаемая версия поля.</param>
public sealed record SaveBlankFieldRequest(
    BlankFieldDto Field,
    long ExpectedVersion);

/// <summary>
/// Удаление поля бланка.
/// </summary>
/// <param name="Id">Идентификатор поля.</param>
/// <param name="ExpectedVersion">Ожидаемая версия поля.</param>
public sealed record DeleteBlankFieldRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Ответ со списком шаблонов.
/// </summary>
/// <param name="Templates">Шаблоны.</param>
public sealed record PrintTemplateListResponse(IReadOnlyList<PrintTemplateDto> Templates);

/// <summary>
/// Ответ со списком полей бланка.
/// </summary>
/// <param name="Fields">Поля бланка.</param>
public sealed record BlankFieldListResponse(IReadOnlyList<BlankFieldDto> Fields);

/// <summary>
/// Ответ со списком пакетов шаблонов.
/// </summary>
/// <param name="Packages">Метаданные пакетов.</param>
public sealed record TemplatePackageListResponse(IReadOnlyList<TemplatePackageDto> Packages);

/// <summary>
/// Удаление управляемого пакета шаблона.
/// </summary>
/// <param name="PackageId">Идентификатор пакета.</param>
public sealed record DeleteTemplatePackageRequest(string PackageId);

/// <summary>
/// Ответ удаления пакета шаблона.
/// </summary>
/// <param name="PackageId">Идентификатор удалённого пакета.</param>
public sealed record DeleteTemplatePackageResponse(string PackageId);

/// <summary>
/// Запрос загрузки пакета шаблона на сервер.
/// </summary>
/// <param name="Name">Наименование пакета.</param>
/// <param name="ContentBase64">
/// Содержимое .lbk-пакета в Base64: содержимое файла идёт в теле команды,
/// а не в пути. На сервере содержимое проверяется (ZIP, число и пути записей,
/// обязательный blank.xml) и записывается как новая версия пакета.
/// </param>
public sealed record UploadTemplatePackageRequest(
    string Name,
    string ContentBase64);
