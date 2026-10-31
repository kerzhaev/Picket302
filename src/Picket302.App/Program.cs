namespace Picket302.App;

/// <summary>
/// Точка входа оболочки Picket302.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Тип указан полностью: пространство имён Picket302.Application вложено в
        // Picket302 и затеняет System.Windows.Forms.Application при поиске имени.
        System.Windows.Forms.Application.Run(new MainShell());
    }
}
