using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Shared.Dtos;

namespace Picket302.Shared.Commands;

/// <summary>
/// Сохранение контрагента: создание новой записи либо изменение существующей.
/// </summary>
/// <param name="Counterparty">Карточка контрагента.</param>
/// <param name="ExpectedVersion">
/// Ожидаемая версия записи. Для создания — 0. Для изменения — версия,
/// прочитанная клиентом; при несовпадении служба отклоняет сохранение.
/// </param>
public sealed record SaveCounterpartyRequest(
    CounterpartyDto Counterparty,
    long ExpectedVersion);

/// <summary>
/// Ответ сохранения контрагента.
/// </summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="Version">Версия записи после сохранения.</param>
public sealed record SaveCounterpartyResponse(long Id, long Version);

/// <summary>
/// Мягкое удаление либо восстановление контрагента.
/// </summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="ExpectedVersion">Ожидаемая версия записи.</param>
/// <param name="Restore">Восстановить запись вместо удаления.</param>
public sealed record ChangeCounterpartyStateRequest(
    long Id,
    long ExpectedVersion,
    bool Restore = false);

/// <summary>
/// Пакетное удаление либо восстановление контрагентов.
/// </summary>
/// <param name="Items">Записи с прочитанными версиями.</param>
/// <param name="Restore">Восстановить записи вместо удаления.</param>
public sealed record BatchChangeCounterpartyStateRequest(
    IReadOnlyList<VersionedId> Items,
    bool Restore = false);

/// <summary>
/// Пакетное изменение одного поля контрагентов.
/// </summary>
/// <param name="Items">Записи с прочитанными версиями.</param>
/// <param name="PropertyName">Имя изменяемого поля.</param>
/// <param name="NewValue">Новое значение.</param>
public sealed record BatchUpdateCounterpartyFieldRequest(
    IReadOnlyList<VersionedId> Items,
    string PropertyName,
    string? NewValue);

/// <summary>
/// Запись значения пользовательского поля контрагента.
/// </summary>
/// <param name="CounterpartyId">Контрагент.</param>
/// <param name="FieldName">Имя поля.</param>
/// <param name="Value">Значение поля.</param>
public sealed record SetCounterpartyFieldRequest(
    long CounterpartyId,
    string FieldName,
    string Value);

/// <summary>
/// Удаление значения пользовательского поля контрагента.
/// </summary>
/// <param name="CounterpartyId">Контрагент.</param>
/// <param name="FieldName">Имя поля.</param>
public sealed record RemoveCounterpartyFieldRequest(
    long CounterpartyId,
    string FieldName);

/// <summary>
/// Создание отправителя.
/// </summary>
/// <param name="Name">Наименование отправителя.</param>
/// <param name="Inn">ИНН.</param>
/// <param name="Kpp">КПП.</param>
/// <param name="BankName">Наименование банка.</param>
/// <param name="Zip">Почтовый индекс основного адреса.</param>
public sealed record CreateAgentRequest(
    string Name,
    string? Inn = null,
    string? Kpp = null,
    string? BankName = null,
    string? Zip = null);

/// <summary>
/// Слияние импорта адресной книги: записи пакета сливаются с общими данными
/// по правилам <see cref="MergePolicy"/>.
/// </summary>
/// <param name="SourceFormat">Формат источника.</param>
/// <param name="SourceFile">Имя исходного файла.</param>
/// <param name="ImportedAt">Момент чтения файла.</param>
/// <param name="Records">Записи пакета.</param>
/// <param name="Policy">Правило слияния.</param>
public sealed record ImportMergeRequest(
    string SourceFormat,
    string SourceFile,
    DateTime ImportedAt,
    IReadOnlyList<ImportRecord> Records,
    MergePolicy Policy);

/// <summary>
/// Выборочная очистка общей базы.
/// </summary>
/// <param name="Scope">Область очистки.</param>
public sealed record ClearDatabaseRequest(ClearScope Scope);

/// <summary>
/// Ответ выборочной очистки общей базы.
/// </summary>
/// <param name="Cleared">Число удалённых записей.</param>
public sealed record ClearDatabaseResponse(int Cleared);

/// <summary>
/// Наполнение общей базы демонстрационными данными.
/// </summary>
public sealed record SeedDemoRequest;

/// <summary>
/// Ответ наполнения демонстрационными данными.
/// </summary>
/// <param name="Seeded">Число созданных записей.</param>
public sealed record SeedDemoResponse(int Seeded);
