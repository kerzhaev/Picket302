using System.Xml.Linq;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Сохранение промежуточных данных реестра во временный XML (ТЗ §8.4, функция 58:
/// <c>Application.Services.ReportDataDumper.DumpReportDataToTempXml</c>).
/// </summary>
/// <remarks>
/// Перед формированием визуального отчета сбрасывает датасет в
/// <c>%TEMP%\Picket302_Report_&lt;GUID&gt;.xml</c>, позволяя пользователю вручную
/// отредактировать данные перед спулингом. Ошибка записи во временную папку
/// приводит к переходу на генерацию в памяти (метод возвращает
/// <see langword="null"/>).
/// </remarks>
public class ReportDataDumper
{
    /// <summary>Ключ параметра «Сохранять промежуточный XML данных».</summary>
    public const string OptionKey = "DumpReportDataToTempXml";

    private readonly IParameterRepository _параметры;

    /// <summary>Признак последнего успешного сохранения во временный XML.</summary>
    public bool LastDumpSucceeded { get; private set; }

    /// <summary>
    /// Путь последнего созданного файла дампа; <see langword="null"/>, если
    /// дамп ещё не выполнялся или последняя запись не удалась.
    /// </summary>
    public string? LastDumpPath { get; private set; }

    /// <summary>Создаёт дампер данных отчётов.</summary>
    /// <param name="parameters">Хранилище параметров настройки (чекбокс).</param>
    public ReportDataDumper(IParameterRepository parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _параметры = parameters;
    }

    /// <summary>
    /// Читает чекбокс настройки «Сохранять промежуточный XML данных»
    /// (ТЗ §8.4, ф.58). По умолчанию дамп выключен.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Значение настройки.</returns>
    public async Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        var значение = await _параметры.GetValueAsync(OptionKey, cancellationToken).ConfigureAwait(false);

        return string.Equals(значение, "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Сохраняет чекбокс настройки «Сохранять промежуточный XML данных».
    /// </summary>
    /// <param name="enabled">Значение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var параметр = await _параметры.GetByNameAsync(OptionKey, cancellationToken).ConfigureAwait(false);

        if (параметр is null)
        {
            await _параметры
                .AddAsync(
                    new Domain.Entities.Parameter { Name = OptionKey, Value = enabled ? "1" : "0" },
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        параметр.Value = enabled ? "1" : "0";
        await _параметры.UpdateAsync(параметр, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Сбрасывает датасет отчёта во временный XML (ТЗ §8.4, функция 58).
    /// </summary>
    /// <param name="reportDataSet">
    /// Данные отчёта: словарь «коллекция строк → колонки», либо произвольный
    /// объект, сериализуемый в XML.
    /// </param>
    /// <param name="reportName">Наименование отчёта.</param>
    /// <returns>Путь к созданному файлу либо <see langword="null"/> при ошибке записи.</returns>
    public virtual string? DumpReportDataToTempXml(object reportDataSet, string reportName)
    {
        ArgumentNullException.ThrowIfNull(reportDataSet);

        try
        {
            var путь = Path.Combine(
                Path.GetTempPath(),
                $"Picket302_Report_{Guid.NewGuid():N}.xml");

            var документ = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "ReportData",
                    new XAttribute("report", reportName),
                    new XAttribute("createdAt", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                    ЗаписатьДанные(reportDataSet)));

            документ.Save(путь);
            LastDumpSucceeded = true;
            LastDumpPath = путь;

            return путь;
        }
        catch (IOException)
        {
            // Ошибка записи во временную папку приводит к переходу на
            // генерацию в памяти (ТЗ §8.4, ф.58).
            LastDumpSucceeded = false;
            LastDumpPath = null;

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            LastDumpSucceeded = false;
            LastDumpPath = null;

            return null;
        }
    }

    private static XElement ЗаписатьДанные(object данные)
    {
        if (данные is IEnumerable<KeyValuePair<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> таблицы)
        {
            var корень = new XElement("Tables");

            foreach (var (имя, строки) in таблицы)
            {
                корень.Add(new XElement(
                    "Table",
                    new XAttribute("name", имя),
                    строки.Select(строка => new XElement(
                        "Row",
                        строка.OrderBy(пара => пара.Key, StringComparer.Ordinal)
                            .Select(пара => new XElement(
                                "Field",
                                new XAttribute("name", пара.Key),
                                пара.Value is null ? string.Empty : пара.Value.ToString()))))));
            }

            return корень;
        }

        return new XElement("Payload", данные.ToString() ?? string.Empty);
    }
}
