using Picket302.Domain.Entities;
using Picket302.Domain.Contracts;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;

namespace Picket302.Shared.Mapping;

/// <summary>
/// Преобразования справочных сущностей общей базы в контрактную форму.
/// </summary>
/// <remarks>
/// Справочные сущности (категории, реестры, документы, описи) переносятся
/// отдельно от основных: они не меняются конкурентно из интерфейса по
/// карточке, но всё равно приходят к клиенту по общей базе.
/// </remarks>
public static class SharedCatalogMapping
{
    /// <summary>
    /// Преобразует категорию в контрактную форму.
    /// </summary>
    /// <param name="категория">Категория.</param>
    /// <param name="версия">Маркер версии группы.</param>
    /// <returns>DTO категории.</returns>
    public static CategoryDto ККатегории(Category категория, long версия) => new(
        категория.Id,
        категория.Name,
        категория.ParentId,
        категория.IsDefault,
        категория.IsClosed,
        категория.SortOrder,
        категория.CounterpartyCount,
        версия);

    /// <summary>
    /// Преобразует почтовый реестр в контрактную форму.
    /// </summary>
    /// <param name="реестр">Реестр.</param>
    /// <returns>DTO реестра.</returns>
    public static PostRegisterDto КРеестру(PostRegister реестр) => new(
        реестр.Id,
        реестр.RegisterNumber,
        реестр.BatchNumber,
        реестр.SendingDate,
        реестр.ShippingPoint,
        реестр.CreatedAt);

    /// <summary>
    /// Преобразует документ отправления в контрактную форму.
    /// </summary>
    /// <param name="документ">Документ.</param>
    /// <returns>DTO документа.</returns>
    public static MailDocumentDto КДокументу(MailDocument документ) => new(
        документ.Id,
        документ.MailId,
        документ.DocNumber);

    /// <summary>
    /// Преобразует движение документа в контрактную форму.
    /// </summary>
    /// <param name="движение">Движение.</param>
    /// <returns>DTO движения.</returns>
    public static DocumentMovementDto КДвижению(DocumentMovement движение) => new(
        движение.Id,
        движение.MailId,
        движение.MovedAt,
        движение.TargetDepartment,
        движение.Executor,
        движение.Action);

    /// <summary>
    /// Преобразует опись в контрактную форму.
    /// </summary>
    /// <param name="опись">Опись.</param>
    /// <returns>DTO описи.</returns>
    public static InventoryDto КОписи(Inventory опись) => new(
        опись.Id,
        опись.Name,
        опись.CounterpartyId,
        опись.Destination,
        опись.Note,
        опись.CreatedAt);

    /// <summary>
    /// Преобразует позицию описи в контрактную форму.
    /// </summary>
    /// <param name="позиция">Позиция описи.</param>
    /// <returns>DTO позиции.</returns>
    public static InventoryItemDto КПозиции(InventoryItem позиция) => new(
        позиция.Id,
        позиция.InventoryId,
        позиция.Name,
        позиция.Quantity,
        позиция.Cost,
        позиция.Note,
        позиция.CreatedAt);
}
