using Picket302.Domain.Entities;

namespace Picket302.Shared.Dtos;

/// <summary>
/// Адрес в структуре «почтовый индекс + шесть адресных строк».
/// </summary>
/// <param name="Zip">Почтовый индекс: ровно 6 цифр.</param>
/// <param name="Lines">Шесть адресных строк по порядку.</param>
/// <param name="Raw">
/// Исходная строка импорта; служит только для round-trip импорта.
/// </param>
public sealed record AddressDto(
    string Zip,
    IReadOnlyList<string> Lines,
    string? Raw = null);

/// <summary>
/// Три независимых среза адресации контрагента.
/// </summary>
/// <param name="Legal">Юридический адрес.</param>
/// <param name="Actual">Фактический адрес.</param>
/// <param name="Postal">Почтовый адрес.</param>
/// <param name="MainType">Срез, используемый по умолчанию.</param>
public sealed record AddressSetDto(
    AddressDto? Legal,
    AddressDto? Actual,
    AddressDto? Postal,
    AddressMainType MainType);
