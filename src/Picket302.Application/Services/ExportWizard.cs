using Picket302.Application.Export;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Services;

/// <summary>
/// Мастер выгрузки адресной книги: выбор формата файла и настроек даты,
/// запись файла и действие после сохранения (ТЗ §8.12, функция 160:
/// «Экспорт с настройками и пост-действиями»).
/// </summary>
/// <remarks>
/// <para>
/// Формат выгрузки (TXT, CSV, HTML, RTF, XLS, XLSX) и формат представления
/// дат задаются запросом <see cref="ExportRequest"/>; тип файла определяется
/// расширением, но при его несовпадении с форматом выгрузки используется
/// формат запроса.
/// </para>
/// <para>
/// Если целевой файл занят другим приложением, мастер не прерывает выгрузку:
/// подбирается свободное имя вида «address_book (2).csv», а фактический путь
/// возвращается в <see cref="ExportResult"/> (ТЗ §8.12, функция 160: «Файл
/// занят — предложение альтернативного имени»; ТЗ §8.12, функция 61).
/// </para>
/// <para>
/// Действие после сохранения («Открыть файл» / «Открыть папку» / «Закрыть»)
/// выполняет внедряемый исполнитель <see cref="IExportPostActionRunner"/>.
/// </para>
/// </remarks>
public sealed class ExportWizard
{
    /// <summary>Предельное число вариантов имени занятого файла.</summary>
    private const int МаксимумВариантовИмени = 1000;

    private readonly UniversalAddressExchange _обмен;
    private readonly IExportPostActionRunner _исполнитель;

    /// <summary>
    /// Создаёт мастер с собственными реализациями обмена и выполнения
    /// пост-действия.
    /// </summary>
    public ExportWizard()
        : this(new UniversalAddressExchange(), new ExportPostActionRunner())
    {
    }

    /// <summary>
    /// Создаёт мастер с заданными реализациями обмена и выполнения пост-действия.
    /// </summary>
    /// <param name="обмен">Служба универсального обмена адресными списками.</param>
    /// <param name="исполнитель">
    /// Исполнитель действия после выгрузки; при <see langword="null"/> применяется
    /// реализация по умолчанию.
    /// </param>
    public ExportWizard(UniversalAddressExchange обмен, IExportPostActionRunner? исполнитель = null)
    {
        ArgumentNullException.ThrowIfNull(обмен);

        _обмен = обмен;
        _исполнитель = исполнитель ?? new ExportPostActionRunner();
    }

    /// <summary>
    /// Выгружает данные в файл согласно запросу и выполняет пост-действие
    /// (ТЗ §8.12, функция 160).
    /// </summary>
    /// <param name="request">Запрос выгрузки: формат, путь, настройки дат, пост-действие.</param>
    /// <returns>
    /// Результат выгрузки: фактический путь записанного файла, число записей
    /// и формат выгрузки.
    /// </returns>
    /// <exception cref="ArgumentException">Путь итогового файла не задан.</exception>
    public ExportResult Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("Путь итогового файла выгрузки не задан.", nameof(request));
        }

        var путь = Path.GetFullPath(request.OutputPath);

        if (ФайлЗанят(путь))
        {
            путь = SuggestAvailableFilePath(путь);
        }

        var каталог = Path.GetDirectoryName(путь);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var параметры = new AddressExchangeOptions
        {
            Delimiter = request.Delimiter,
            Encoding = request.Encoding,
            IncludeHeader = request.IncludeHeader,
            DateFormat = request.ResolveDateFormat(),
        };

        using (var поток = new FileStream(путь, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            _обмен.ExportData(request.Items ?? [], request.Format, поток, параметры);
        }

        ВыполнитьПостДействие(request.PostAction, путь);

        return new ExportResult(путь, request.Items?.Count ?? 0, request.Format);
    }

    /// <summary>
    /// Подбирает свободное имя файла рядом с занятым: к имени без расширения
    /// добавляется номер в скобках («address_book (2).csv»).
    /// </summary>
    /// <param name="путь">Путь к занятому файлу.</param>
    /// <returns>Путь к свободному файлу.</returns>
    /// <exception cref="InvalidOperationException">
    /// Свободное имя не найдено: рядом занято слишком много файлов.
    /// </exception>
    public static string SuggestAvailableFilePath(string путь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путь);

        var полныйПуть = Path.GetFullPath(путь);
        var каталог = Path.GetDirectoryName(полныйПуть) ?? string.Empty;
        var имя = Path.GetFileNameWithoutExtension(полныйПуть);
        var расширение = Path.GetExtension(полныйПуть);

        for (var номер = 2; номер <= МаксимумВариантовИмени; номер++)
        {
            var кандидат = Path.Combine(каталог, $"{имя} ({номер}){расширение}");

            if (!ФайлЗанят(кандидат))
            {
                return кандидат;
            }
        }

        throw new InvalidOperationException(
            $"Не удалось подобрать свободное имя файла рядом с «{полныйПуть}».");
    }

    /// <summary>
    /// Выполняет действие после сохранения файла (ТЗ §8.12, функция 160).
    /// </summary>
    /// <param name="действие">Действие после выгрузки.</param>
    /// <param name="путь">Путь к выгруженному файлу.</param>
    public void RunPostAction(ExportPostAction действие, string путь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путь);

        ВыполнитьПостДействие(действие, путь);
    }

    /// <summary>
    /// Выполняет действие после сохранения файла (ТЗ §8.12, функция 160).
    /// </summary>
    /// <param name="действие">Действие после выгрузки.</param>
    /// <param name="путь">Путь к выгруженному файлу.</param>
    private void ВыполнитьПостДействие(ExportPostAction действие, string путь)
    {
        switch (действие)
        {
            case ExportPostAction.OpenFile:
                _исполнитель.OpenFile(путь);
                break;

            case ExportPostAction.OpenFolder:
                var каталог = Path.GetDirectoryName(путь);
                _исполнитель.OpenFolder(string.IsNullOrEmpty(каталог) ? путь : каталог);
                break;

            case ExportPostAction.None:
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(действие),
                    действие,
                    "Неизвестное действие после выгрузки.");
        }
    }

    /// <summary>
    /// Определяет, занят ли файл другим приложением.
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <returns><see langword="true"/>, если файл занят.</returns>
    private static bool ФайлЗанят(string путь)
    {
        if (!File.Exists(путь))
        {
            return false;
        }

        try
        {
            using var поток = new FileStream(путь, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
