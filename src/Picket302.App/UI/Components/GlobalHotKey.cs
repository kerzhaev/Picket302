using System.Runtime.InteropServices;

namespace Picket302.App.UI.Components;

/// <summary>
/// Глобальная системная горячая клавиша (ТЗ §7.4: «Ctrl + Space: Глобальный
/// системный хоткей (регистрируется через RegisterHotKey Win32 API)»).
/// </summary>
/// <remarks>
/// Регистрация выполняется через Win32 API <c>RegisterHotKey</c>: сочетание
/// работает даже когда окно приложения не в фокусе.
/// </remarks>
public sealed class GlobalHotKey : IDisposable
{
    /// <summary>Идентификатор зарегистрированной горячей клавиши.</summary>
    public const int Идентификатор = 0xA302;

    /// <summary>Модификатор Ctrl.</summary>
    public const int МодификаторCtrl = 0x0002;

    /// <summary>Модификатор Alt.</summary>
    public const int МодификаторAlt = 0x0001;

    /// <summary>Модификатор Shift.</summary>
    public const int МодификаторShift = 0x0004;

    /// <summary>
    /// Идентификатор виртуальной клавиши «Пробел».
    /// </summary>
    public const int ВиртуальнаяКлавишаПробел = 0x20;

    private readonly Action _приНажатии;
    private readonly int _модификаторы;
    private readonly int _виртуальнаяКлавиша;
    private bool _зарегистрирована;

    /// <summary>
    /// Создаёт глобальную горячую клавишу.
    /// </summary>
    /// <param name="модификаторы">Модификаторы Win32 (<c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c>).</param>
    /// <param name="виртуальнаяКлавиша">Код виртуальной клавиши.</param>
    /// <param name="приНажатии">Действие при нажатии сочетания.</param>
    public GlobalHotKey(int модификаторы, int виртуальнаяКлавиша, Action приНажатии)
    {
        ArgumentNullException.ThrowIfNull(приНажатии);

        _модификаторы = модификаторы;
        _виртуальнаяКлавиша = виртуальнаяКлавиша;
        _приНажатии = приНажатии;
    }

    /// <summary>
    /// Создаёт глобальное сочетание Ctrl+Space (ТЗ §7.4).
    /// </summary>
    /// <param name="приНажатии">Действие при нажатии сочетания.</param>
    public static GlobalHotKey CtrlSpace(Action приНажатии)
        => new(МодификаторCtrl, ВиртуальнаяКлавишаПробел, приНажатии);

    /// <summary>
    /// Признак успешной регистрации горячей клавиши в системе.
    /// </summary>
    public bool Зарегистрирована => _зарегистрирована;

    /// <summary>
    /// Регистрирует горячую клавишу в системе.
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, если сочетание зарегистрировано; <see langword="false"/> —
    /// если сочетание уже занято другим приложением.
    /// </returns>
    public bool Зарегистрировать()
    {
        if (_зарегистрирована)
        {
            return true;
        }

        _зарегистрирована = RegisterHotKey(IntPtr.Zero, Идентификатор, _модификаторы, _виртуальнаяКлавиша);
        return _зарегистрирована;
    }

    /// <summary>
    /// Выполняет действие, связанное с горячей клавишей. Вызывается обработчиком
    /// сообщения <c>WM_HOTKEY</c>.
    /// </summary>
    public void Выполнить()
    {
        if (_зарегистрирована)
        {
            _приНажатии();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_зарегистрирована)
        {
            return;
        }

        UnregisterHotKey(IntPtr.Zero, Идентификатор);
        _зарегистрирована = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
