using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Picket302.Tests.Architecture;

/// <summary>
/// Архитектурный контроль независимости экспорта от Microsoft Office
/// (ТЗ §8.5, функция 70 — «Полная независимость экспорта от Microsoft Office»).
/// </summary>
/// <remarks>
/// Проверяются два уровня: фактические ссылки сборок решения
/// (<c>Assembly.GetReferencedAssemblies</c>) и текст исходных файлов слоя
/// <c>src</c> — позднее связывание через COM не оставляет следов в ссылках
/// сборок, поэтому поиск по исходникам обязателен.
/// </remarks>
public sealed class OfficeInteropAbsenceTests
{
    /// <summary>Имя файла решения — признак корня репозитория.</summary>
    private const string SolutionFileName = "Picket302.sln";

    /// <summary>Идентификатор типа проекта «папка решения».</summary>
    private const string SolutionFolderTypeId = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>
    /// Каталоги исходников, в которых ищется позднее связывание с Microsoft
    /// Office: слой приложения и вспомогательные инструменты.
    /// </summary>
    private static readonly string[] SourceDirectories =
    {
        "src",
        "tools",
    };

    /// <summary>Имена сборок Microsoft Office, запрещённые к использованию.</summary>
    private static readonly string[] BannedAssemblyNames =
    {
        "Microsoft.Office.Core",
        "office",
    };

    /// <summary>Признаки позднего связывания с Microsoft Office в исходниках.</summary>
    private static readonly string[] BannedSourceFragments =
    {
        "Microsoft.Office.Interop",
        "Type.GetTypeFromProgID",
        "Excel.Application",
    };

    /// <summary>Расширения файлов, читаемых как текст.</summary>
    private static readonly string[] TextExtensions =
    {
        ".cs",
        ".csproj",
        ".config",
        ".xml",
        ".json",
        ".props",
        ".targets",
        ".sql",
        ".lng",
        ".txt",
        ".resx",
        ".xaml",
    };

    /// <summary>Каталоги артефактов сборки, не участвующие в проверке.</summary>
    private static readonly string[] BuildArtifactDirectories =
    {
        "obj",
        "bin",
    };

    /// <summary>
    /// Ни одна сборка решения не ссылается на сборки Microsoft Office.
    /// </summary>
    /// <remarks>
    /// Перечень сборок берётся из файла решения, поэтому проверка охватывает
    /// все проекты, включая добавленные позднее.
    /// </remarks>
    [Fact]
    public void Assemblies_DoNotReferenceOfficeInterop()
    {
        var сборки = ReadSolutionProjects();

        Assert.True(сборки.Count > 0, "В файле решения не найдено ни одного проекта.");

        foreach (var assemblyName in сборки)
        {
            var assembly = LoadAssembly(assemblyName);

            var banned = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(IsOfficeAssembly)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                banned.Length == 0,
                $"Сборка {assemblyName} ссылается на сборки Microsoft Office: {string.Join(", ", banned)}.");
        }
    }

    /// <summary>
    /// Исходники слоя <c>src</c> и вспомогательных инструментов <c>tools</c> не
    /// содержат признаков позднего связывания с Microsoft Office.
    /// </summary>
    /// <remarks>
    /// Проверяется исполняемый код файлов: упоминания запрещённых имён
    /// в комментариях (в том числе в XML-документации) описывают запрет и
    /// использованием Office не являются.
    /// </remarks>
    [Fact]
    public void SourceFiles_DoNotUseOfficeInteropOrLateBoundCom()
    {
        var root = FindSolutionRoot();

        var offenders = new List<string>();

        foreach (var directory in SourceDirectories)
        {
            var sourceDirectory = Path.Combine(root, directory);

            Assert.True(Directory.Exists(sourceDirectory), $"Каталог исходников не найден: {sourceDirectory}");

            foreach (var file in EnumerateSourceFiles(sourceDirectory))
            {
                var code = RemoveComments(File.ReadAllText(file));

                foreach (var fragment in BannedSourceFragments)
                {
                    if (code.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}: {fragment}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Обнаружено обращение к Microsoft Office в исходниках: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Файлы свойств решения (в том числе <c>Directory.Packages.props</c>)
    /// не содержат ссылок на сборки Microsoft Office: запрет действует и на
    /// уровне управления пакетами.
    /// </summary>
    [Fact]
    public void PackageProps_DoNotReferenceOfficeInterop()
    {
        var root = FindSolutionRoot();

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.props", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file))
            {
                continue;
            }

            var code = RemoveComments(File.ReadAllText(file));

            foreach (var fragment in BannedSourceFragments)
            {
                if (code.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {fragment}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Обнаружено обращение к Microsoft Office в свойствах пакетов: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Читает перечень проектов решения из файла <c>Picket302.sln</c>: имена
    /// проектов совпадают с именами сборок.
    /// </summary>
    /// <returns>Имена сборок решения.</returns>
    private static IReadOnlyList<string> ReadSolutionProjects()
    {
        var root = FindSolutionRoot();
        var solutionPath = Path.Combine(root, SolutionFileName);

        var pattern = new Regex(
            "Project\\(\"\\{([^}]+)\\}\"\\)\\s*=\\s*\"([^\"]+)\"",
            RegexOptions.Compiled);

        var names = new List<string>();

        foreach (Match match in pattern.Matches(File.ReadAllText(solutionPath)))
        {
            var тип = match.Groups[1].Value;

            // Папки решения (тип 2150E333-8FDC-42A3-9474-1A3956D46DE8) и
            // проекты-папки сборок не имеют.
            if (string.Equals(тип, SolutionFolderTypeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = match.Groups[2].Value;

            if (name.Contains('\\', StringComparison.Ordinal))
            {
                continue;
            }

            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Признак сборки Microsoft Office.</summary>
    /// <param name="name">Имя сборки.</param>
    /// <returns><see langword="true"/>, если сборка относится к Microsoft Office.</returns>
    private static bool IsOfficeAssembly(string name) =>
        name.StartsWith("Microsoft.Office.Interop", StringComparison.OrdinalIgnoreCase)
        || BannedAssemblyNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Загружает сборку решения из каталога тестов.</summary>
    /// <param name="assemblyName">Имя сборки.</param>
    /// <returns>Загруженная сборка.</returns>
    private static Assembly LoadAssembly(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

        Assert.True(File.Exists(path), $"Сборка {assemblyName} отсутствует рядом с тестами.");

        return Assembly.LoadFrom(path);
    }

    /// <summary>Перечисляет текстовые файлы исходников без артефактов сборки.</summary>
    /// <param name="directory">Корневой каталог исходников.</param>
    /// <returns>Пути к текстовым файлам.</returns>
    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file))
            {
                continue;
            }

            var extension = Path.GetExtension(file);

            if (TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    /// <summary>Признак файла в каталоге артефактов сборки.</summary>
    /// <param name="file">Путь к файлу.</param>
    /// <returns><see langword="true"/>, если файл относится к obj или bin.</returns>
    private static bool IsBuildArtifact(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment =>
            BuildArtifactDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Удаляет из исходного текста комментарии (включая XML-документацию),
    /// сохраняя содержимое строковых литералов.
    /// </summary>
    /// <param name="source">Исходный текст файла.</param>
    /// <returns>Текст файла без комментариев.</returns>
    private static string RemoveComments(string source)
    {
        var result = new StringBuilder(source.Length);

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
                    result.Append(current);
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
                    result.Append(current);
                }

                index++;
                continue;
            }

            if (inVerbatimString)
            {
                result.Append(current);

                if (current == '"')
                {
                    if (next == '"')
                    {
                        result.Append(next);
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
                result.Append(current);

                if (current == '\\' && next != '\0')
                {
                    result.Append(next);
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
                result.Append(current);

                if (current == '\\' && next != '\0')
                {
                    result.Append(next);
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
                result.Append(current);
                index++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                result.Append(current);
                index++;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                result.Append(current);
                index++;
                continue;
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    /// <summary>Находит корень репозитория по файлу решения.</summary>
    /// <returns>Полный путь к корню репозитория.</returns>
    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Файл решения {SolutionFileName} не найден выше каталога {AppContext.BaseDirectory}.");
    }
}
