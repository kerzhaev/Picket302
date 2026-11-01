using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Хранение пароля подключения к внешней базе данных в защищённом виде средствами
/// Windows (DPAPI, CryptoAPI <c>crypt32.dll</c>) (ТЗ §8.5, функция 76:
/// «строка подключения вводится пользователем, пароль хранится в DPAPI»).
/// </summary>
/// <remarks>
/// <para>
/// Защита выполняется в области текущего пользователя Windows: защищённый блок
/// может расшифровать только тот же пользователь на том же компьютере. Пароль в
/// открытом виде не сохраняется и не пишется в журнал.
/// </para>
/// <para>
/// Пакет <c>System.Security.Cryptography.ProtectedData</c> в решение не добавляется:
/// вызовы <c>CryptProtectData</c> / <c>CryptUnprotectData</c> выполняются напрямую
/// через P/Invoke, поэтому список пакетов решения не меняется.
/// </para>
/// <para>
/// Файловый помощник хранит защищённый блок в
/// <c>%APPDATA%\Picket302\Secrets\&lt;имя&gt;.bin</c>.
/// </para>
/// </remarks>
public sealed class DpapiSecretStore
{
    /// <summary>Имя папки приложения в профиле пользователя.</summary>
    public const string ApplicationFolderName = "Picket302";

    /// <summary>Имя папки с защищёнными секретами.</summary>
    public const string SecretsFolderName = "Secrets";

    /// <summary>Расширение файла с защищённым блоком.</summary>
    public const string SecretFileExtension = ".bin";

    /// <summary>
    /// Флаг «не показывать интерфейс пользователя» — криптографические операции
    /// выполняются без диалогов Windows.
    /// </summary>
    private const int CryptProtectUiForbidden = 0x1;

    private readonly string _directory;

    /// <summary>
    /// Создаёт хранилище секретов в стандартной папке профиля пользователя:
    /// <c>%APPDATA%\Picket302\Secrets</c>.
    /// </summary>
    public DpapiSecretStore()
        : this(null)
    {
    }

    /// <summary>
    /// Создаёт хранилище секретов в указанной папке (используется в тестах).
    /// </summary>
    /// <param name="directory">
    /// Папка хранения защищённых блоков; при значении <see langword="null"/> —
    /// <c>%APPDATA%\Picket302\Secrets</c>.
    /// </param>
    public DpapiSecretStore(string? directory)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationFolderName,
            SecretsFolderName);
    }

    /// <summary>Папка, в которой хранятся защищённые блоки.</summary>
    public string DirectoryPath => _directory;

    /// <summary>
    /// Защищает секрет средствами Windows (DPAPI) в области текущего пользователя.
    /// </summary>
    /// <param name="secret">Открытый текст секрета (пароля).</param>
    /// <returns>Защищённый блок, который может расшифровать только текущий пользователь.</returns>
    /// <exception cref="CryptographicException">
    /// Windows отклонила операцию защиты.
    /// </exception>
    public byte[] Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var input = new DataBlob
        {
            cbData = plainBytes.Length,
            pbData = Marshal.AllocHGlobal(plainBytes.Length == 0 ? 1 : plainBytes.Length),
        };

        try
        {
            if (plainBytes.Length > 0)
            {
                Marshal.Copy(plainBytes, 0, input.pbData, plainBytes.Length);
            }

            if (!CryptProtectData(
                    ref input,
                    ApplicationFolderName,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(
                    $"Не удалось защитить секрет средствами Windows (DPAPI). Код ошибки: {Marshal.GetLastWin32Error()}.");
            }

            try
            {
                return CopyToArray(output);
            }
            finally
            {
                FreeBlob(output);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    /// <summary>
    /// Расшифровывает защищённый блок средствами Windows (DPAPI).
    /// </summary>
    /// <param name="protectedData">Защищённый блок.</param>
    /// <returns>Исходный текст секрета.</returns>
    /// <exception cref="CryptographicException">
    /// Блок защищён другим пользователем или повреждён.
    /// </exception>
    public string Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);

        var input = new DataBlob
        {
            cbData = protectedData.Length,
            pbData = Marshal.AllocHGlobal(protectedData.Length == 0 ? 1 : protectedData.Length),
        };

        try
        {
            if (protectedData.Length > 0)
            {
                Marshal.Copy(protectedData, 0, input.pbData, protectedData.Length);
            }

            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(
                    $"Не удалось расшифровать секрет средствами Windows (DPAPI). Код ошибки: {Marshal.GetLastWin32Error()}.");
            }

            try
            {
                return Encoding.UTF8.GetString(CopyToArray(output));
            }
            finally
            {
                FreeBlob(output);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    /// <summary>
    /// Возвращает путь файла с защищённым секретом.
    /// </summary>
    /// <param name="name">Имя секрета (имя файла без расширения).</param>
    /// <returns>Полный путь файла защищённого блока.</returns>
    public string GetSecretPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Path.Combine(_directory, SanitizeFileName(name) + SecretFileExtension);
    }

    /// <summary>
    /// Защищает секрет и сохраняет защищённый блок в файл.
    /// </summary>
    /// <param name="name">Имя секрета (имя файла без расширения).</param>
    /// <param name="secret">Открытый текст секрета (пароля).</param>
    /// <returns>Полный путь сохранённого файла.</returns>
    public string Save(string name, string secret)
    {
        var path = GetSecretPath(name);
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(path, Protect(secret));
        return path;
    }

    /// <summary>
    /// Читает защищённый блок из файла и расшифровывает его.
    /// </summary>
    /// <param name="name">Имя секрета (имя файла без расширения).</param>
    /// <returns>Исходный текст секрета.</returns>
    /// <exception cref="FileNotFoundException">Файл с секретом отсутствует.</exception>
    public string Load(string name)
    {
        var path = GetSecretPath(name);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Секрет «{name}» не найден в хранилище DPAPI.", path);
        }

        return Unprotect(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Определяет, сохранён ли секрет в хранилище.
    /// </summary>
    /// <param name="name">Имя секрета (имя файла без расширения).</param>
    /// <returns><see langword="true"/>, если защищённый блок существует.</returns>
    public bool Exists(string name) => File.Exists(GetSecretPath(name));

    /// <summary>
    /// Удаляет защищённый блок секрета.
    /// </summary>
    /// <param name="name">Имя секрета (имя файла без расширения).</param>
    public void Delete(string name)
    {
        var path = GetSecretPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Заменяет недопустимые символы имени файла на подчёркивание.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var symbol in name)
        {
            builder.Append(Array.IndexOf(invalid, symbol) >= 0 ? '_' : symbol);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "secret" : result;
    }

    /// <summary>
    /// Копирует содержимое блоба в массив байтов.
    /// </summary>
    private static byte[] CopyToArray(DataBlob blob)
    {
        var result = new byte[blob.cbData];
        if (blob.cbData > 0)
        {
            Marshal.Copy(blob.pbData, result, 0, blob.cbData);
        }

        return result;
    }

    /// <summary>
    /// Освобождает память, выделенную криптографической функцией Windows.
    /// </summary>
    private static void FreeBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero)
        {
            LocalFree(blob.pbData);
            blob.pbData = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Защищает данные средствами Windows DPAPI (область текущего пользователя).
    /// </summary>
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDescription,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    /// <summary>
    /// Расшифровывает данные, защищённые средствами Windows DPAPI.
    /// </summary>
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    /// <summary>
    /// Освобождает память, выделенную функциями Windows.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>
    /// Блок данных Windows API, используемый DPAPI.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        /// <summary>Размер данных в байтах.</summary>
        public int cbData;

        /// <summary>Указатель на данные.</summary>
        public IntPtr pbData;
    }
}
