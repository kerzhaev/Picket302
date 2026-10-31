namespace Picket302.Printing.Codes;

/// <summary>
/// Сформированный QR-код: матрица модулей (ГОСТ Р 56042-2014).
/// </summary>
public sealed class QrCode
{
    private readonly bool[] _модули;

    /// <summary>
    /// Создаёт описание QR-кода.
    /// </summary>
    /// <param name="version">Версия кода (1–40).</param>
    /// <param name="errorCorrection">Уровень коррекции ошибок.</param>
    /// <param name="maskPattern">Номер маски (0–7).</param>
    /// <param name="modules">Модули матрицы по строкам.</param>
    public QrCode(int version, QrErrorCorrectionLevel errorCorrection, int maskPattern, bool[] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        Version = version;
        ErrorCorrection = errorCorrection;
        MaskPattern = maskPattern;
        _модули = modules;
        Size = (version * 4) + 17;
    }

    /// <summary>Версия кода (1–40).</summary>
    public int Version { get; }

    /// <summary>Уровень коррекции ошибок.</summary>
    public QrErrorCorrectionLevel ErrorCorrection { get; }

    /// <summary>Номер применённой маски (0–7).</summary>
    public int MaskPattern { get; }

    /// <summary>Размер стороны матрицы в модулях.</summary>
    public int Size { get; }

    /// <summary>
    /// Возвращает состояние модуля.
    /// </summary>
    /// <param name="x">Номер столбца.</param>
    /// <param name="y">Номер строки.</param>
    /// <returns><see langword="true"/>, если модуль тёмный.</returns>
    public bool GetModule(int x, int y) => _модули[(y * Size) + x];

    /// <summary>
    /// Возвращает строку матрицы модулей.
    /// </summary>
    /// <param name="y">Номер строки.</param>
    /// <returns>Массив модулей строки.</returns>
    public bool[] GetRow(int y)
    {
        var строка = new bool[Size];
        Array.Copy(_модули, y * Size, строка, 0, Size);
        return строка;
    }
}

/// <summary>
/// Ошибка превышения пропускной способности выбранной версии QR-кода
/// (ТЗ §8.4, функция 48: «Превышение лимита символов для выбранной версии
/// QR-кода вызывает ошибку генерации»).
/// </summary>
public sealed class QrPayloadTooLongException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение переполнения.
    /// </summary>
    /// <param name="message">Текст ошибки.</param>
    public QrPayloadTooLongException(string message)
        : base(message)
    {
    }
}
