using System.Runtime.InteropServices;

namespace Picket302.Printing.Devices;

/// <summary>
/// Обращения к драйверу через контекст устройства GDI: создание контекста
/// принтера и запрос его возможностей (печатная область, разрешение).
/// </summary>
internal static class Gdi32
{
    /// <summary>Ширина печатной области в пикселях.</summary>
    public const int HORZRES = 8;

    /// <summary>Высота печатной области в пикселях.</summary>
    public const int VERTRES = 10;

    /// <summary>Разрешение по горизонтали, пикселей на дюйм.</summary>
    public const int LOGPIXELSX = 88;

    /// <summary>Разрешение по вертикали, пикселей на дюйм.</summary>
    public const int LOGPIXELSY = 90;

    /// <summary>
    /// Создаёт контекст устройства для принтера.
    /// </summary>
    /// <param name="driver">Имя драйвера («WINSPOOL» — системный спулер).</param>
    /// <param name="device">Имя принтера.</param>
    /// <param name="output">Имя порта вывода.</param>
    /// <param name="devMode">Указатель на DEVMODE.</param>
    /// <returns>Дескриптор контекста устройства.</returns>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDCW(
        string driver,
        string? device,
        string? output,
        IntPtr devMode);

    /// <summary>
    /// Возвращает значение возможности контекста устройства.
    /// </summary>
    /// <param name="hdc">Дескриптор контекста устройства.</param>
    /// <param name="index">Индекс возможности.</param>
    /// <returns>Значение возможности.</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    /// <summary>
    /// Удаляет контекст устройства.
    /// </summary>
    /// <param name="hdc">Дескриптор контекста устройства.</param>
    /// <returns><see langword="true"/>, если контекст удалён.</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);
}
