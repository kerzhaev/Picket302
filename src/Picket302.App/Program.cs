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
        Application.Run(new MainShell());
    }
}
