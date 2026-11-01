using System.Globalization;
using System.Text;
using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Вложения электронной рассылки (ТЗ §8.7, функция 98:
/// <c>public void AddAttachment(EmailDistribution dist, string filePath)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Вложение копируется в папку хранилища отправителя до отправки: удаление
/// исходного файла не должно ломать повторную отправку сохранённого письма.
/// </para>
/// <para>
/// Файл более 25 МБ помечается предупреждением о возможном отказе почтового
/// сервера; отправка при этом не блокируется — решение принимает оператор.
/// </para>
/// </remarks>
public sealed class AttachmentService
{
    /// <summary>
    /// Предельный размер вложения, байт (ТЗ §8.7, функция 98: «Файл более
    /// 25 МБ — предупреждение о возможном отказе почтового сервера»).
    /// </summary>
    public const long МаксимальныйРазмерВложенияБайт = 25L * 1024 * 1024;

    /// <summary>Имя папки хранилища вложений по умолчанию.</summary>
    public const string ИмяПапкиХранилища = "Attachments";

    private readonly List<string> _предупреждения = [];

    /// <summary>
    /// Создаёт службу вложений.
    /// </summary>
    /// <param name="путьХранилища">
    /// Папка хранилища вложений; создаётся при необходимости.
    /// </param>
    public AttachmentService(string путьХранилища)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьХранилища);

        ПутьХранилища = Path.GetFullPath(путьХранилища);
    }

    /// <summary>Папка хранилища вложений отправителя.</summary>
    public string ПутьХранилища { get; }

    /// <summary>
    /// Предупреждения, накопленные службой: файлы свыше предельного размера и
    /// недоступные вложения.
    /// </summary>
    public IReadOnlyList<string> Warnings => _предупреждения;

    /// <summary>
    /// Прикрепляет файл к рассылке: копирует его в хранилище отправителя и
    /// указывает копию в списке вложений (ТЗ §8.7, функция 98).
    /// </summary>
    /// <param name="dist">Рассылка.</param>
    /// <param name="filePath">Путь к исходному файлу.</param>
    /// <exception cref="FileNotFoundException">Исходный файл не найден.</exception>
    public void AddAttachment(EmailDistribution dist, string filePath)
    {
        ArgumentNullException.ThrowIfNull(dist);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                string.Create(CultureInfo.CurrentCulture, $"Файл вложения «{filePath}» не найден."),
                filePath);
        }

        Directory.CreateDirectory(ПутьХранилища);

        var целевойПуть = Path.Combine(ПутьХранилища, СформироватьИмяФайла(filePath));

        File.Copy(filePath, целевойПуть, overwrite: true);

        if (!dist.Attachments.Contains(целевойПуть, StringComparer.OrdinalIgnoreCase))
        {
            dist.Attachments.Add(целевойПуть);
        }

        var размер = new FileInfo(целевойПуть).Length;

        if (размер > МаксимальныйРазмерВложенияБайт)
        {
            _предупреждения.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Файл «{Path.GetFileName(filePath)}» превышает {МаксимальныйРазмерВложенияБайт / (1024 * 1024)} МБ — почтовый сервер может отказать в отправке."));
        }
    }

    /// <summary>
    /// Удаляет вложение из рассылки; файл в хранилище не удаляется — он может
    /// использоваться при повторной отправке.
    /// </summary>
    /// <param name="dist">Рассылка.</param>
    /// <param name="путьВложения">Путь к вложению в хранилище.</param>
    /// <returns><see langword="true"/>, если вложение было удалено из рассылки.</returns>
    public bool RemoveAttachment(EmailDistribution dist, string путьВложения)
    {
        ArgumentNullException.ThrowIfNull(dist);
        ArgumentException.ThrowIfNullOrWhiteSpace(путьВложения);

        return dist.Attachments.RemoveAll(
            путь => string.Equals(путь, путьВложения, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>
    /// Собирает предупреждения по вложениям рассылки: превышение предельного
    /// размера и отсутствие файла в хранилище.
    /// </summary>
    /// <param name="dist">Рассылка.</param>
    /// <returns>Перечень предупреждений на русском языке.</returns>
    public IReadOnlyList<string> CollectWarnings(EmailDistribution dist)
    {
        ArgumentNullException.ThrowIfNull(dist);

        var предупреждения = new List<string>();

        foreach (var вложение in dist.Attachments)
        {
            if (!File.Exists(вложение))
            {
                предупреждения.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Файл вложения «{Path.GetFileName(вложение)}» не найден в хранилище — вложение будет пропущено."));
                continue;
            }

            var размер = new FileInfo(вложение).Length;

            if (размер > МаксимальныйРазмерВложенияБайт)
            {
                предупреждения.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Файл «{Path.GetFileName(вложение)}» превышает {МаксимальныйРазмерВложенияБайт / (1024 * 1024)} МБ — почтовый сервер может отказать в отправке."));
            }
        }

        return предупреждения;
    }

    /// <summary>
    /// Формирует безопасное имя файла в хранилище: без недопустимых символов,
    /// с сохранением расширения и без коллизий (ТЗ §8.7, функция 98).
    /// </summary>
    /// <param name="исходныйПуть">Путь к исходному файлу.</param>
    /// <returns>Имя файла в хранилище.</returns>
    private static string СформироватьИмяФайла(string исходныйПуть)
    {
        var имя = Path.GetFileNameWithoutExtension(исходныйПуть);
        var расширение = Path.GetExtension(исходныйПуть);

        var недопустимые = Path.GetInvalidFileNameChars();
        var очищенное = new StringBuilder(имя.Length);

        foreach (var символ in имя)
        {
            очищенное.Append(Array.IndexOf(недопустимые, символ) >= 0 ? '_' : символ);
        }

        var основа = очищенное.ToString().Trim();

        if (основа.Length == 0)
        {
            основа = "вложение";
        }

        if (основа.Length > 80)
        {
            основа = основа[..80];
        }

        var метка = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var случайнаяЧасть = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        return string.Create(CultureInfo.InvariantCulture, $"{основа}_{метка}_{случайнаяЧасть}{расширение}");
    }
}
