using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.App.UI.Shell;
using Picket302.Infrastructure;
using Picket302.Infrastructure.Logging;
using Picket302.Infrastructure.Persistence;

namespace Picket302.App;

/// <summary>
/// Точка входа оболочки Picket302.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Запускает программу.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.SetDefaultFont(EnvelopeUiTheme.ШрифтТекста());

        // Глобальные обработчики исключений (F-E17): необработанная ошибка
        // интерфейса или фонового потока фиксируется в журнале и показывает
        // форму отчёта о сбое вместо падения приложения.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, событие) => ПоказатьФормуСбоя(событие.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, событие) =>
        {
            if (событие.ExceptionObject is Exception исключение)
            {
                ЗаписатьСбой(исключение);
            }
        };

        var перехватчик = new ShortcutInterceptor();

        var параметры = ShortcutInterceptor.Build(
            args,
            (Control.ModifierKeys & Keys.Control) == Keys.Control);

        if (перехватчик.HandleShortcutLaunch(параметры))
        {
            // Скрытый вход администратора: открывается только диалог подключения
            // к базе данных, главное окно не загружается (ТЗ §8.11, функция 134).
            using var диалог = new FormConnectionSettings(new ConnectionManager(), new ServerRegistry());

            System.Windows.Forms.Application.Run(диалог);

            return;
        }

        // Тип указан полностью: пространство имён Picket302.Application вложено в
        // Picket302 и затеняет System.Windows.Forms.Application при поиске имени.
        System.Windows.Forms.Application.Run(new MainShell());
    }

    /// <summary>
    /// Показывает форму отчёта о сбое и фиксирует исключение в журнале.
    /// </summary>
    /// <param name="исключение">Необработанное исключение.</param>
    private static void ПоказатьФормуСбоя(Exception исключение)
    {
        ЗаписатьСбой(исключение);

        try
        {
            using var форма = new FormExceptionReport(исключение);

            форма.ShowDialog();
        }
        catch
        {
            // Форма отчёта недоступна: сбой уже зафиксирован в журнале.
        }
    }

    /// <summary>
    /// Фиксирует необработанное исключение в журнале ошибок приложения.
    /// </summary>
    /// <param name="исключение">Исключение.</param>
    private static void ЗаписатьСбой(Exception исключение)
    {
        try
        {
            new LogHub(Picket302ProfilePaths.ПоУмолчанию().LogsDirectory)
                .LogError(исключение);
        }
        catch
        {
            // Журнал недоступен: падение обработчика сбоя недопустимо.
        }
    }
}
