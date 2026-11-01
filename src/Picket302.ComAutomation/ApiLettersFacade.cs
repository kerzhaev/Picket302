using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Exchange;

namespace Picket302.ComAutomation;

/// <summary>
/// Фасад создания писем и импорта контрагентов из внешних систем
/// (ТЗ §8.9, функция 116; форматы обмена — ТЗ §9.4).
/// </summary>
/// <remarks>
/// <para>
/// Фасад принимает данные внешних систем в виде XML-контрактов
/// <c>EnvelopesData</c> (исходящие письма) и <c>EconomicMembers</c>
/// (контрагенты), преобразует их в доменные сущности и проводит через штатный
/// конвейер импорта: дедупликация (функция 75) и тонкое слияние с базой
/// (функция 74), после чего создаёт учётные записи писем.
/// </para>
/// <para>
/// Логика не дублируется: фасад работает через среду исполнения
/// (<see cref="IAutomationEnvironment"/>), а та — через прикладные службы
/// <see cref="ImportDeduplicator"/>, <see cref="ImportMergeService"/>,
/// <see cref="MailService"/> и <see cref="MailFactory"/>.
/// </para>
/// </remarks>
public sealed class ApiLettersFacade
{
    private readonly IAutomationEnvironment _environment;

    /// <summary>
    /// Создаёт фасад.
    /// </summary>
    /// <param name="environment">Среда исполнения (база данных, печать, шаблоны).</param>
    public ApiLettersFacade(IAutomationEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Обновлять ли существующих контрагентов при импорте (флаг контекста
    /// <c>FlagUpdateContragents</c>).
    /// </summary>
    public bool FlagUpdateContragents { get; set; }

    /// <summary>
    /// Выкусывать ли почтовый индекс из строки адреса (флаг контекста
    /// <c>FlagParseZipIndexFromAddress</c>).
    /// </summary>
    public bool FlagParseZipIndexFromAddress { get; set; } = true;

    /// <summary>
    /// Записывать ли примечание при импорте (флаг контекста
    /// <c>WriteToNoteOnImport</c>).
    /// </summary>
    public bool WriteToNoteOnImport { get; set; }

    /// <summary>Текст последней ошибки; пуст — ошибок нет.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Текст последнего результата операции.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Создаёт исходящие письма из XML-строки формата <c>EnvelopesData</c>.
    /// </summary>
    /// <param name="xml">XML-текст контракта.</param>
    /// <returns>Число созданных писем; 0 — при ошибке (код ошибки — в <see cref="LastError"/>).</returns>
    public int CreateFromXmlLetters(string xml)
    {
        LastError = string.Empty;

        var разбор = EnvelopesData.ReadListFromXmlAndFixForPrint(
            xml ?? string.Empty,
            FlagParseZipIndexFromAddress);

        if (разбор.IsFailure)
        {
            return Ошибка(разбор.Error ?? "Ошибка чтения XML-данных.");
        }

        if (!_environment.IsDatabaseLoaded)
        {
            return Ошибка(
                "База данных не загружена: вызовите SafeLoadDatabase.",
                AutomationWarningCodes.DatabaseNotLoaded);
        }

        var результат = _environment
            .CreateLettersAsync(разбор.Value, print: false)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        LastMessage = результат.Message;

        return результат.Succeeded
            ? результат.Created
            : Ошибка(результат.Message, AutomationWarningCodes.DatabaseNotLoaded);
    }

    /// <summary>
    /// Создаёт исходящие письма из файла формата <c>EnvelopesData</c>.
    /// </summary>
    /// <param name="path">Путь к файлу с данными.</param>
    /// <returns>Число созданных писем; 0 — при ошибке.</returns>
    public int CreateFromFileLetters(string path)
    {
        LastError = string.Empty;

        var текст = ПрочитатьФайл(path);

        if (текст is null)
        {
            return Ошибка(LastError, AutomationWarningCodes.InvalidXml);
        }

        return CreateFromXmlLetters(текст);
    }

    /// <summary>
    /// Импортирует контрагентов из файла: XML-контракт <c>EconomicMembers</c>
    /// либо текстовая выгрузка 1С:Предприятие.
    /// </summary>
    /// <param name="path">Путь к файлу с данными.</param>
    /// <returns>Число импортированных контрагентов; 0 — при ошибке.</returns>
    public int ImportContragents(string path)
    {
        LastError = string.Empty;

        var текст = ПрочитатьФайл(path);

        if (текст is null)
        {
            return Ошибка(LastError, AutomationWarningCodes.InvalidXml);
        }

        if (текст.TrimStart().StartsWith('<'))
        {
            return ImportContragentsFromXml(текст);
        }

        return ИмпортироватьВыгрузку1С(path);
    }

    /// <summary>
    /// Импортирует контрагентов из XML-строки формата <c>EconomicMembers</c>.
    /// </summary>
    /// <param name="xml">XML-текст контракта.</param>
    /// <returns>Число импортированных контрагентов; 0 — при ошибке.</returns>
    public int ImportContragentsFromXml(string xml)
    {
        LastError = string.Empty;

        Result<IReadOnlyList<EconomicMember>> разбор;

        try
        {
            using var reader = ExchangeXmlReader.CreateReader(xml ?? string.Empty);

            разбор = EconomicMembers.ReadListFromXmlAndFixForPrint(reader, FlagParseZipIndexFromAddress);
        }
        catch (Exception error)
        {
            return Ошибка("Ошибка чтения XML-данных: " + error.Message);
        }

        if (разбор.IsFailure)
        {
            return Ошибка(разбор.Error ?? "Ошибка чтения XML-данных.");
        }

        if (!_environment.IsDatabaseLoaded)
        {
            return Ошибка(
                "База данных не загружена: вызовите SafeLoadDatabase.",
                AutomationWarningCodes.DatabaseNotLoaded);
        }

        var черновики = разбор.Value.Select(ВЧерновик).ToArray();

        var результат = _environment
            .ImportCounterpartiesAsync(черновики, FlagUpdateContragents, WriteToNoteOnImport)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        LastMessage = результат.Message;

        return результат.Succeeded
            ? результат.Created
            : Ошибка(результат.Message, AutomationWarningCodes.DatabaseNotLoaded);
    }

    /// <summary>
    /// Преобразует участника контракта обмена в черновик контрагента.
    /// </summary>
    /// <param name="member">Участник контракта <c>EconomicMembers</c>.</param>
    /// <returns>Черновик контрагента.</returns>
    public static CounterpartyDraft ВЧерновик(EconomicMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var черновик = new CounterpartyDraft
        {
            Type = CounterpartyType.LegalEntity,
            Name = member switch
            {
                LegalPerson юридическое => юридическое.Name,
                NaturalPerson физическое => string.Join(
                    " ",
                    new[] { физическое.LastName, физическое.FirstName, физическое.MiddleName }
                        .Where(часть => !string.IsNullOrWhiteSpace(часть))),
                _ => string.Empty,
            },
            Email = member.Email,
            Note = member.Note,
        };

        switch (member)
        {
            case LegalPerson юридическое:
                черновик.ShortName = юридическое.ShortName;
                break;

            case NaturalPerson физическое:
                черновик.Type = CounterpartyType.NaturalPerson;
                черновик.ShortName = физическое.LastName;
                break;
        }

        черновик.PostalZip = member.Address.ZipCode;
        черновик.PostalLines = СоставитьСтрокиАдреса(member.Address);

        return черновик;
    }

    private static string[] СоставитьСтрокиАдреса(EconomicMemberAddress адрес)
    {
        var строки = new string[CounterpartyDraft.AddressLineCount];
        var части = new List<string>();

        if (!string.IsNullOrWhiteSpace(адрес.City))
        {
            части.Add(адрес.City);
        }

        if (!string.IsNullOrWhiteSpace(адрес.AddressLine))
        {
            части.Add(адрес.AddressLine);
        }

        if (!string.IsNullOrWhiteSpace(адрес.Province))
        {
            части.Add(адрес.Province);
        }

        for (var i = 0; i < части.Count && i < строки.Length; i++)
        {
            строки[i] = части[i];
        }

        return строки;
    }

    private int ИмпортироватьВыгрузку1С(string path)
    {
        var парсер = new OneCImportParser();

        ImportBatchResult? разбор = null;

        foreach (var версия in new[] { OneCVersion.V8x, OneCVersion.V77 })
        {
            разбор = парсер.ImportFromOneC(path, версия);

            if (разбор.RecordCount > 0)
            {
                break;
            }
        }

        if (разбор is null || разбор.RecordCount == 0)
        {
            return Ошибка("Не удалось распознать формат файла с данными.");
        }

        if (!_environment.IsDatabaseLoaded)
        {
            return Ошибка(
                "База данных не загружена: вызовите SafeLoadDatabase.",
                AutomationWarningCodes.DatabaseNotLoaded);
        }

        var черновики = разбор.Records
            .Select(запись => запись.Draft)
            .Where(черновик => черновик is not null)
            .ToArray();

        var результат = _environment
            .ImportCounterpartiesAsync(черновики, FlagUpdateContragents, WriteToNoteOnImport)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        LastMessage = результат.Message;

        return результат.Succeeded
            ? результат.Created
            : Ошибка(результат.Message, AutomationWarningCodes.DatabaseNotLoaded);
    }

    private string? ПрочитатьФайл(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var байты = File.ReadAllBytes(path);

            // Кодировка определяется штатным детектором прикладного слоя:
            // выгрузки 1С сохраняются в Windows-1251, XML-контракты — в UTF-8.
            var определение = TextEncodingDetector.Detect(байты);

            return определение.Encoding.GetString(байты);
        }
        catch (Exception error)
        {
            LastError = $"Не удалось прочитать файл «{path}»: {error.Message}";
            return null;
        }
    }

    /// <summary>
    /// Фиксирует ошибку и возвращает её код по таблице 9.2 ТЗ: 1 — база
    /// данных не загружена, 2 — ошибка чтения XML-данных.
    /// </summary>
    /// <param name="сообщение">Текст ошибки на русском языке.</param>
    /// <param name="код">Код ошибки по таблице 9.2 ТЗ.</param>
    /// <returns>Код ошибки.</returns>
    private int Ошибка(string сообщение, int код = AutomationWarningCodes.InvalidXml)
    {
        LastError = сообщение;
        LastMessage = сообщение;

        return код;
    }
}
