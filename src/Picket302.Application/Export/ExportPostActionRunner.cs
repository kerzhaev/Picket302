using System.Diagnostics;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Export;

/// <summary>
/// Выполнение действий после выгрузки средствами оболочки Windows
/// (ТЗ §8.12, функция 160).
/// </summary>
/// <remarks>
/// Файл открывается через системный обработчик (shell verb «open»), папка —
/// через проводник Windows. Сборки Microsoft.Office.Interop.* и OLE/COM не
/// используются.
/// </remarks>
public sealed class ExportPostActionRunner : IExportPostActionRunner
{
    /// <summary>Имя исполняемого файла проводника Windows.</summary>
    private const string Проводник = "explorer.exe";

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Путь к файлу не задан.</exception>
    /// <exception cref="InvalidOperationException">Не удалось открыть файл.</exception>
    public void OpenFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ошибка) when (ошибка is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Не удалось открыть файл «{filePath}»: {ошибка.Message}",
                ошибка);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Путь к папке не задан.</exception>
    /// <exception cref="InvalidOperationException">Не удалось открыть папку.</exception>
    public void OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        try
        {
            Process.Start(new ProcessStartInfo(Проводник, $"\"{folderPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ошибка) when (ошибка is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Не удалось открыть папку «{folderPath}»: {ошибка.Message}",
                ошибка);
        }
    }

    /// <summary>
    /// Выполняет действие после выгрузки согласно запросу.
    /// </summary>
    /// <param name="действие">Действие после выгрузки.</param>
    /// <param name="filePath">Путь к выгруженному файлу.</param>
    /// <exception cref="InvalidOperationException">Не удалось выполнить действие.</exception>
    public static void Run(ExportPostAction действие, string filePath)
    {
        var исполнитель = new ExportPostActionRunner();

        switch (действие)
        {
            case ExportPostAction.OpenFile:
                исполнитель.OpenFile(filePath);
                break;

            case ExportPostAction.OpenFolder:
                var папка = Path.GetDirectoryName(Path.GetFullPath(filePath));
                исполнитель.OpenFolder(string.IsNullOrEmpty(папка) ? filePath : папка);
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
}
