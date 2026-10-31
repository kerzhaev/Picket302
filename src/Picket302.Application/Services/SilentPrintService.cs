using Picket302.Domain.Queries;
using Picket302.Printing.Reports;

namespace Picket302.Application.Services;

/// <summary>
/// Коды ошибок тихой печати реестров (ТЗ §8.4, функция 62: «Ошибки спулера
/// возвращаются кодом ошибки в вызывающий процесс»).
/// </summary>
public enum SilentPrintErrorCode
{
    /// <summary>Задание отправлено на принтер.</summary>
    Success = 0,

    /// <summary>Принтер недоступен или отключен.</summary>
    PrinterUnavailable = 1,

    /// <summary>Выборка отправлений пуста.</summary>
    EmptySelection = 2,

    /// <summary>Выборка содержит письма разных отправителей.</summary>
    MixedSenders = 3,

    /// <summary>Ошибка спулера Windows.</summary>
    SpoolerFailure = 4,

    /// <summary>Не удалось сформировать данные реестра.</summary>
    DataFailure = 5,
}

/// <summary>Результат тихой печати.</summary>
/// <param name="ErrorCode">Код ошибки; 0 — успех.</param>
/// <param name="Message">Текст ошибки для журнала; пуст при успехе.</param>
public sealed record SilentPrintResult(SilentPrintErrorCode ErrorCode, string Message = "")
{
    /// <summary>Успешный результат.</summary>
    public bool Success => ErrorCode == SilentPrintErrorCode.Success;
}

/// <summary>
/// Канал отправки печатной формы в спулер Windows без GUI
/// (реализуется слоем печати/инфраструктуры).
/// </summary>
public interface ISilentPrintChannel
{
    /// <summary>
    /// Отправляет печатную форму в спулер без показа окон.
    /// </summary>
    /// <param name="document">Печатная форма.</param>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="copies">Число экземпляров.</param>
    /// <returns><see langword="null"/> при успехе либо текст ошибки спулера.</returns>
    string? SendToSpooler(ReportDocument document, string printerName, int copies);
}

/// <summary>
/// Печать реестра без показа формы — тихий режим (ТЗ §8.4, функция 62:
/// <c>Application.Services.SilentPrintService.PrintRegistrySilently</c>).
/// </summary>
/// <remarks>
/// Выполняет сбор данных, рендеринг и прямую отправку реестра в Windows
/// Spooler без инициализации GUI (для автоматизации из внешних систем).
/// Ошибки возвращаются кодом ошибки вызывающему процессу.
/// </remarks>
public sealed class SilentPrintService
{
    private readonly IReportDataProvider _данные;
    private readonly ISilentPrintChannel _канал;

    /// <summary>Текст последней ошибки; пуст при успешной печати.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Создаёт службу тихой печати.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    /// <param name="channel">Канал отправки в спулер.</param>
    public SilentPrintService(IReportDataProvider dataProvider, ISilentPrintChannel channel)
    {
        ArgumentNullException.ThrowIfNull(dataProvider);
        ArgumentNullException.ThrowIfNull(channel);

        _данные = dataProvider;
        _канал = channel;
    }

    /// <summary>
    /// Выполняет сбор данных реестра ф.103, рендеринг и прямую отправку
    /// в спулер без инициализации GUI (ТЗ §8.4, функция 62).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений реестра.</param>
    /// <param name="templateName">Наименование шаблона реестра.</param>
    /// <param name="printerName">Имя принтера вывода.</param>
    /// <param name="copies">Число экземпляров.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Код ошибки вызывающему процессу; 0 — успех.</returns>
    public async Task<SilentPrintResult> PrintRegistrySilently(
        long[] mailIds,
        string templateName,
        string printerName,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (mailIds.Length == 0)
        {
            return Ошибка(SilentPrintErrorCode.EmptySelection, "Не выбрано ни одного отправления для печати реестра.");
        }

        ReportDocument документ;

        try
        {
            var идентификаторОтправителя = await ResolveSenderId(mailIds, cancellationToken).ConfigureAwait(false);
            var реестр = new Form103Report(_данные);
            документ = await реестр.GenerateF103(mailIds, идентификаторОтправителя, cancellationToken).ConfigureAwait(false);
            документ = СПереносомЗаголовка(документ, templateName);
        }
        catch (ReportValidationException ошибка) when (ошибка.Message == Form103Report.MixedSendersError)
        {
            return Ошибка(SilentPrintErrorCode.MixedSenders, ошибка.Message);
        }
        catch (ReportValidationException ошибка)
        {
            return Ошибка(SilentPrintErrorCode.DataFailure, ошибка.Message);
        }
        catch (Exception ошибка) when (ошибка is KeyNotFoundException or InvalidOperationException)
        {
            return Ошибка(SilentPrintErrorCode.DataFailure, ошибка.Message);
        }

        var ошибкаSpooler = _канал.SendToSpooler(документ, printerName, copies);

        return ошибкаSpooler is null
            ? new SilentPrintResult(SilentPrintErrorCode.Success)
            : Ошибка(SilentPrintErrorCode.SpoolerFailure, ошибкаSpooler);
    }

    /// <summary>Копия печатной формы с наименованием шаблона.</summary>
    /// <param name="source">Исходная форма.</param>
    /// <param name="title">Наименование шаблона.</param>
    /// <returns>Форма с изменённым заголовком.</returns>
    private static ReportDocument СПереносомЗаголовка(ReportDocument source, string title)
    {
        var документ = new ReportDocument
        {
            Title = title,
            QueryId = source.QueryId,
            PageWidthMm = source.PageWidthMm,
            PageHeightMm = source.PageHeightMm,
        };

        foreach (var блок in source.Blocks)
        {
            документ.Blocks.Add(блок);
        }

        return документ;
    }

    private async Task<long> ResolveSenderId(long[] mailIds, CancellationToken cancellationToken)
    {
        // Отправитель берётся из первой строки запроса реестра: слой данных
        // фильтрует выборку по отправителю и список идентификаторов.
        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", mailIds.ToList());

        var строки = await _данные.RunQueryAsync(Form103Report.QueryId, значения, cancellationToken).ConfigureAwait(false);

        if (строки.Count == 0)
        {
            throw new ReportValidationException(Form103Report.EmptySelectionError);
        }

        var значение = строки[0].GetValue("sender_id");

        return значение switch
        {
            long число => число,
            int число => число,
            decimal число => (long)число,
            string текст when long.TryParse(текст, out var разобрано) => разобрано,
            _ => throw new ReportValidationException("Запрос реестра не содержит отправителя."),
        };
    }

    private SilentPrintResult Ошибка(SilentPrintErrorCode код, string сообщение)
    {
        LastError = сообщение;

        return new SilentPrintResult(код, сообщение);
    }
}
