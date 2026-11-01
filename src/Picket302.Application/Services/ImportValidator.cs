using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Проверка пакета импорта перед применением политики (ТЗ §8.12, функция 159:
/// «Application.Services.ImportValidator»).
/// </summary>
/// <remarks>
/// <para>
/// Проверка выполняется по каждой записи пакета: обязательные поля, ИНН,
/// почтовые индексы, телефон и электронная почта. Значения длиннее
/// 255 символов усекаются с предупреждением (ТЗ §9.7, п. 4: «ограничение
/// длины полей 255 символов с предупреждением об усечении»).
/// </para>
/// <para>
/// Результат проверки записывается в записи пакета: перечень ошибок и
/// предупреждений, а также состояние записи (<see cref="ImportRecordStatus"/>).
/// Проверенные оператором и пропущенные записи сохраняют своё состояние:
/// повторная проверка не отменяет решение оператора.
/// </para>
/// </remarks>
public sealed class ImportValidator
{
    /// <summary>
    /// Предельная длина значения поля (ТЗ §9.7, п. 4).
    /// </summary>
    public const int МаксимальнаяДлинаЗначения = 255;

    private readonly ImportBatch _пакет;

    /// <summary>
    /// Создаёт проверяющий пакет импорта.
    /// </summary>
    /// <param name="batch">Пакет импорта.</param>
    public ImportValidator(ImportBatch batch)
    {
        _пакет = batch ?? throw new ArgumentNullException(nameof(batch));
    }

    /// <summary>
    /// Проверяет все записи пакета (ТЗ §8.12, функция 159: «public ValidationSummary
    /// ValidateAll()»).
    /// </summary>
    /// <returns>Сводка проверки пакета.</returns>
    public ValidationSummary ValidateAll()
    {
        foreach (var запись in _пакет.Records)
        {
            var ошибки = new List<string>();
            var предупреждения = new List<string>();

            ПроверитьЗапись(запись, ошибки, предупреждения);

            запись.Errors = ошибки;
            запись.Warnings = предупреждения;

            if (ошибки.Count > 0)
            {
                запись.Status = ImportRecordStatus.HasErrors;
            }
            else if (запись.Status != ImportRecordStatus.Verified
                && запись.Status != ImportRecordStatus.Skipped)
            {
                запись.Status = ImportRecordStatus.Unverified;
            }
        }

        return new ValidationSummary(_пакет.Records);
    }

    /// <summary>
    /// Проверяет одну запись пакета после правки оператора и обновляет её
    /// состояние: отметка «проверено» снимается, запись с ошибками
    /// переводится в состояние «с ошибками».
    /// </summary>
    /// <param name="record">Запись предпросмотра с исправленными значениями.</param>
    public void ValidateRecord(ImportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var ошибки = new List<string>();
        var предупреждения = new List<string>();

        ПроверитьЗапись(record, ошибки, предупреждения);

        record.Errors = ошибки;
        record.Warnings = предупреждения;
        record.Status = ошибки.Count > 0
            ? ImportRecordStatus.HasErrors
            : ImportRecordStatus.Unverified;
    }

    /// <summary>
    /// Проверяет значения записи и усекает слишком длинные.
    /// </summary>
    /// <param name="record">Запись предпросмотра.</param>
    /// <param name="ошибки">Накопитель ошибок записи.</param>
    /// <param name="предупреждения">Накопитель предупреждений записи.</param>
    private static void ПроверитьЗапись(
        ImportRecord record,
        List<string> ошибки,
        List<string> предупреждения)
    {
        var черновик = record.Draft;

        Обрезать(черновик.Name, "Наименование", предупреждения, обрезанное => черновик.Name = обрезанное);
        Обрезать(черновик.ShortName, "Сокращённое наименование", предупреждения, обрезанное => черновик.ShortName = обрезанное);
        Обрезать(черновик.Phone, "Телефон", предупреждения, обрезанное => черновик.Phone = обрезанное);
        Обрезать(черновик.Email, "Электронная почта", предупреждения, обрезанное => черновик.Email = обрезанное);
        Обрезать(черновик.Note, "Примечание", предупреждения, обрезанное => черновик.Note = обрезанное);
        Обрезать(черновик.Inn, "ИНН", предупреждения, обрезанное => черновик.Inn = обрезанное);

        ОбрезатьАдресныеСтроки(черновик.PostalLines, "Почтовый адрес", предупреждения);
        ОбрезатьАдресныеСтроки(черновик.LegalLines, "Юридический адрес", предупреждения);
        ОбрезатьАдресныеСтроки(черновик.ActualLines, "Фактический адрес", предупреждения);

        if (string.IsNullOrWhiteSpace(черновик.Name))
        {
            ошибки.Add("Не заполнено наименование контрагента.");
        }

        if (!string.IsNullOrWhiteSpace(черновик.Inn))
        {
            if (!черновик.Inn.All(char.IsAsciiDigit))
            {
                ошибки.Add("ИНН должен состоять только из цифр.");
            }
            else
            {
                var ожидаемое = черновик.Type == CounterpartyType.NaturalPerson ? 12 : 10;
                var подсказка = ожидаемое == 10 ? "юридического лица" : "физического лица";

                if (черновик.Inn.Length != ожидаемое)
                {
                    ошибки.Add(
                        $"ИНН должен содержать {ожидаемое} цифр ({подсказка}), указано {черновик.Inn.Length}.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(черновик.PostalZip))
        {
            if (черновик.PostalZip.Length != 6 || !черновик.PostalZip.All(char.IsAsciiDigit))
            {
                ошибки.Add("Почтовый индекс должен состоять ровно из 6 цифр.");
            }
        }
        else
        {
            предупреждения.Add("Почтовый индекс не указан — адрес требует проверки оператором.");
        }

        if (!string.IsNullOrWhiteSpace(черновик.LegalZip)
            && (черновик.LegalZip.Length != 6 || !черновик.LegalZip.All(char.IsAsciiDigit)))
        {
            ошибки.Add("Почтовый индекс юридического адреса должен состоять ровно из 6 цифр.");
        }

        if (!string.IsNullOrWhiteSpace(черновик.ActualZip)
            && (черновик.ActualZip.Length != 6 || !черновик.ActualZip.All(char.IsAsciiDigit)))
        {
            ошибки.Add("Почтовый индекс фактического адреса должен состоять ровно из 6 цифр.");
        }

        if (!string.IsNullOrWhiteSpace(черновик.Phone) && !ПохожеНаТелефон(черновик.Phone))
        {
            ошибки.Add("Телефон указан в неверном формате.");
        }

        if (!string.IsNullOrWhiteSpace(черновик.Email) && !ПохожеНаПочту(черновик.Email))
        {
            ошибки.Add("Электронная почта указана в неверном формате.");
        }

        if (черновик.NeedsVerification)
        {
            предупреждения.Add("Адрес распознан не полностью — требуется проверка оператором.");
        }
    }

    /// <summary>
    /// Усекает строки адреса длиннее 255 символов и добавляет предупреждение
    /// (ТЗ §3.1.1, §9.7, п. 4).
    /// </summary>
    /// <param name="строки">Шесть строк адреса.</param>
    /// <param name="название">Название вида адреса.</param>
    /// <param name="предупреждения">Накопитель предупреждений.</param>
    private static void ОбрезатьАдресныеСтроки(
        string[] строки,
        string название,
        List<string> предупреждения)
    {
        for (var индекс = 0; индекс < строки.Length; индекс++)
        {
            var значение = строки[индекс];

            if (string.IsNullOrEmpty(значение) || значение.Length <= МаксимальнаяДлинаЗначения)
            {
                continue;
            }

            предупреждения.Add(
                $"Строка {индекс + 1} адреса «{название}» длиннее {МаксимальнаяДлинаЗначения} символов — значение обрезано.");

            строки[индекс] = значение[..МаксимальнаяДлинаЗначения];
        }
    }

    /// <summary>
    /// Усекает значение длиннее 255 символов и добавляет предупреждение.
    /// </summary>
    /// <param name="значение">Значение поля.</param>
    /// <param name="название">Название поля.</param>
    /// <param name="предупреждения">Накопитель предупреждений.</param>
    /// <param name="записать">Действие записи усечённого значения в поле черновика.</param>
    private static void Обрезать(
        string? значение,
        string название,
        List<string> предупреждения,
        Action<string> записать)
    {
        if (значение is null || значение.Length <= МаксимальнаяДлинаЗначения)
        {
            return;
        }

        предупреждения.Add(
            $"Поле «{название}» длиннее {МаксимальнаяДлинаЗначения} символов — значение обрезано.");

        записать(значение[..МаксимальнаяДлинаЗначения]);
    }

    /// <summary>
    /// Проверяет, похоже ли значение на телефон.
    /// </summary>
    /// <param name="значение">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение похоже на телефон.</returns>
    private static bool ПохожеНаТелефон(string значение)
    {
        var цифр = значение.Count(char.IsAsciiDigit);

        if (цифр < 5)
        {
            return false;
        }

        foreach (var символ in значение)
        {
            if (char.IsAsciiDigit(символ))
            {
                continue;
            }

            if (символ is '+' or '-' or '(' or ')' or ' ' or '.' or '/' or '#' or '*')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Проверяет, похоже ли значение на адрес электронной почты.
    /// </summary>
    /// <param name="значение">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение похоже на адрес почты.</returns>
    private static bool ПохожеНаПочту(string значение)
    {
        if (значение.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var собака = значение.IndexOf('@', StringComparison.Ordinal);

        if (собака <= 0 || собака == значение.Length - 1)
        {
            return false;
        }

        var точка = значение.IndexOf('.', собака + 1);

        return точка > собака + 1 && точка < значение.Length - 1;
    }
}
