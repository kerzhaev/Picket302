using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.App.UI.Shell;
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
}
