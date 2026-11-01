using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// Архитектурный контроль отсутствия агрессивной самозащиты процесса
/// (ТЗ §8.10, функция 125; §10.7 — «ЯВНЫЙ ЗАПРЕТ (архитектурное
/// требование): не реализовывать агрессивную самозащиту процесса»).
/// </summary>
/// <remarks>
/// <para>
/// Проверяются исходники слоя <c>src</c>: перехват отладчика, обращение к
/// портам ввода-вывода, антимониторинговые хуки и вызовы ядра Windows для
/// детекции отладки или виртуализации в проекте отсутствуют.
/// </para>
/// <para>
/// Поиск ведётся по исполняемому коду: комментарии (в том числе
/// XML-документация) удаляются перед поиском, поэтому упоминание запрета в
/// описании типа нарушением не считается.
/// </para>
/// </remarks>
public sealed class AntiDebuggingGuardTests
{
    /// <summary>Имя файла решения — признак корня репозитория.</summary>
    private const string ИмяФайлаРешения = "Picket302.sln";

    /// <summary>
    /// Признаки запрещённых приёмов самозащиты в исходном коде.
    /// </summary>
    private static readonly string[] ЗапрещённыеПризнаки =
    {
        "Debugger.",
        "IsDebuggerPresent",
        "CheckRemoteDebuggerPresent",
        "NtQueryInformationProcess",
        "NtSetInformationThread",
        "NtQuerySystemInformation",
        "OutputDebugString",
        "GetThreadContext",
        "MainWindowTitle",
        "__cpuid",
        "rdtsc",
        "антиотладка",
        "антидебаг",
    };

    /// <summary>
    /// Шаблоны обращения к портам ввода-вывода на уровне исходного кода:
    /// инструкции чтения и записи в порт (x86).
    /// </summary>
    private static readonly string[] ШаблоныОбращенияКПортам =
    {
        @"(?<![0-9A-Za-z_])(in|out)\s+(al|ax|eax|bl|bx|ebx|cl|cx|ecx|dl|dx|edx)\s*,",
        @"(?<![0-9A-Za-z_])(in|out)\s+0x[0-9A-Fa-f]+",
    };

    /// <summary>
    /// Адреса портов ввода-вывода, обращение к которым запрещено.
    /// </summary>
    private static readonly string[] ЗапрещённыеПорты =
    {
        "0x3F8",
        "0x2E8",
        "0x60",
    };

    /// <summary>
    /// Каталоги артефактов сборки, не участвующие в проверке.
    /// </summary>
    private static readonly string[] КаталогиАртефактов =
    {
        "obj",
        "bin",
    };

    [Fact]
    public void Исходники_НеСодержатЗапрещённыхПриёмовСамозащиты()
    {
        var корень = НайтиКореньРешения();
        var каталогИсходников = Path.Combine(корень, "src");

        Assert.True(Directory.Exists(каталогИсходников), "Каталог исходников не найден: " + каталогИсходников);

        var нарушения = new List<string>();

        foreach (var файл in Directory.EnumerateFiles(каталогИсходников, "*.cs", SearchOption.AllDirectories))
        {
            if (ЭтоАртефактСборки(файл))
            {
                continue;
            }

            var код = УдалитьКомментарии(File.ReadAllText(файл));
            var относительныйПуть = Path.GetRelativePath(корень, файл);

            foreach (var признак in ЗапрещённыеПризнаки)
            {
                if (код.Contains(признак, StringComparison.OrdinalIgnoreCase))
                {
                    нарушения.Add(относительныйПуть + ": " + признак);
                }
            }

            foreach (var порт in ЗапрещённыеПорты)
            {
                if (Regex.IsMatch(
                        код,
                        "(?<![0-9A-Za-z_])" + Regex.Escape(порт) + "(?![0-9A-Za-z_])"))
                {
                    нарушения.Add(относительныйПуть + ": обращение к порту ввода-вывода " + порт);
                }
            }

            foreach (var шаблон in ШаблоныОбращенияКПортам)
            {
                if (Regex.IsMatch(код, шаблон))
                {
                    нарушения.Add(относительныйПуть + ": обращение к порту ввода-вывода");
                }
            }

            нарушения.AddRange(НайтиЗапрещённыеИмпорты(код, относительныйПуть));
        }

        Assert.True(
            нарушения.Count == 0,
            "Обнаружены запрещённые приёмы самозащиты процесса: " + string.Join("; ", нарушения));
    }

    /// <summary>
    /// Ищет вызовы ядра Windows, объявленные для детекции отладки или
    /// виртуализации.
    /// </summary>
    /// <param name="код">Исходный текст без комментариев.</param>
    /// <param name="относительныйПуть">Путь к проверяемому файлу.</param>
    /// <returns>Перечень нарушений.</returns>
    /// <remarks>
    /// Импорт <c>ntdll</c> запрещён всегда: функции этого модуля не имеют
    /// отношения к работе программы. Импорт <c>kernel32</c> допускается
    /// только когда файл не содержит признаков детекции отладки: служебный
    /// импорт (например, освобождение памяти) к самозащите не относится.
    /// </remarks>
    private static IEnumerable<string> НайтиЗапрещённыеИмпорты(string код, string относительныйПуть)
    {
        var совпадения = Regex.Matches(код, "DllImport\\s*\\(\\s*\"([^\"]+)\"");

        var естьПризнакДетекции = ЗапрещённыеПризнаки.Any(признак =>
            код.Contains(признак, StringComparison.OrdinalIgnoreCase));

        foreach (Match совпадение in совпадения)
        {
            var библиотека = совпадение.Groups[1].Value;

            if (библиотека.StartsWith("ntdll", StringComparison.OrdinalIgnoreCase))
            {
                yield return относительныйПуть + ": DllImport " + библиотека;
            }
            else if (библиотека.StartsWith("kernel32", StringComparison.OrdinalIgnoreCase) && естьПризнакДетекции)
            {
                yield return относительныйПуть + ": DllImport " + библиотека + " в файле с признаками детекции отладки";
            }
        }
    }

    /// <summary>
    /// Признак файла в каталоге артефактов сборки.
    /// </summary>
    /// <param name="файл">Путь к файлу.</param>
    /// <returns><see langword="true"/> — файл относится к obj или bin.</returns>
    private static bool ЭтоАртефактСборки(string файл)
    {
        var сегменты = файл.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return сегменты.Any(сегмент =>
            КаталогиАртефактов.Contains(сегмент, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Находит корень репозитория по файлу решения.
    /// </summary>
    /// <returns>Полный путь к корню репозитория.</returns>
    private static string НайтиКореньРешения()
    {
        var каталог = new DirectoryInfo(AppContext.BaseDirectory);

        while (каталог is not null)
        {
            if (File.Exists(Path.Combine(каталог.FullName, ИмяФайлаРешения)))
            {
                return каталог.FullName;
            }

            каталог = каталог.Parent;
        }

        throw new InvalidOperationException(
            "Файл решения " + ИмяФайлаРешения + " не найден выше каталога " + AppContext.BaseDirectory + ".");
    }

    /// <summary>
    /// Удаляет из исходного текста комментарии (в том числе XML-документацию),
    /// сохраняя содержимое строковых литералов.
    /// </summary>
    /// <param name="source">Исходный текст файла.</param>
    /// <returns>Текст файла без комментариев.</returns>
    private static string УдалитьКомментарии(string source)
    {
        var результат = new StringBuilder(source.Length);

        var index = 0;
        var inString = false;
        var inChar = false;
        var inVerbatimString = false;
        var inLineComment = false;
        var inBlockComment = false;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    результат.Append(current);
                }

                index++;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index += 2;
                    continue;
                }

                if (current == '\n')
                {
                    результат.Append(current);
                }

                index++;
                continue;
            }

            if (inVerbatimString)
            {
                результат.Append(current);

                if (current == '"')
                {
                    if (next == '"')
                    {
                        результат.Append(next);
                        index += 2;
                        continue;
                    }

                    inVerbatimString = false;
                }

                index++;
                continue;
            }

            if (inString)
            {
                результат.Append(current);

                if (current == '\\' && next != '\0')
                {
                    результат.Append(next);
                    index += 2;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                index++;
                continue;
            }

            if (inChar)
            {
                результат.Append(current);

                if (current == '\\' && next != '\0')
                {
                    результат.Append(next);
                    index += 2;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = false;
                }

                index++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index += 2;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index += 2;
                continue;
            }

            if (current == '@' && next == '"')
            {
                inVerbatimString = true;
                результат.Append(current);
                index++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                результат.Append(current);
                index++;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                результат.Append(current);
                index++;
                continue;
            }

            результат.Append(current);
            index++;
        }

        return результат.ToString();
    }
}
