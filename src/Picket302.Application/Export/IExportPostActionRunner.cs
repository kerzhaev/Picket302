namespace Picket302.Application.Export;

/// <summary>
/// Выполнение действия после выгрузки файла (ТЗ §8.12, функция 160:
/// «после сохранения — диалог … с кнопками “Открыть файл” / “Открыть папку” /
/// Закрыть»).
/// </summary>
/// <remarks>
/// Выполнение вынесено за интерфейс, чтобы мастер экспорта не зависел от
/// оболочки операционной системы: в тестах подставляется подставная
/// реализация, которая записывает вызванные пути.
/// </remarks>
public interface IExportPostActionRunner
{
    /// <summary>Открывает выгруженный файл в приложении по умолчанию.</summary>
    /// <param name="filePath">Путь к выгруженному файлу.</param>
    void OpenFile(string filePath);

    /// <summary>Открывает папку с выгруженным файлом в проводнике.</summary>
    /// <param name="folderPath">Путь к папке с выгруженным файлом.</param>
    void OpenFolder(string folderPath);
}
