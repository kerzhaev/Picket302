using System.Runtime.InteropServices;
using Picket302.ComAutomation;

namespace Picket302.ComAutomation.Typelib;

/// <summary>
/// Класс-зеркало COM-класса <c>PEProApplication</c> для сборки библиотеки типов
/// (ТЗ §9.3, функция 114).
/// </summary>
/// <remarks>
/// <para>
/// Файл используется ТОЛЬКО служебным проектом
/// <c>tools/typelib/PrintEnvelopePro.Typelib.csproj</c>: он не входит в
/// Picket302.sln, не поставляется вместе с программой и никогда не
/// выполняется.
/// </para>
/// <para>
/// Назначение файла — дать RegAsm COM-метаданные класса с тем же
/// идентификатором и тем же набором интерфейсов, что и поставляемый
/// <see cref="Picket302.ComAutomation.PEProApplication"/>, чтобы в
/// библиотеке типов появился coclass <c>PEProApplication</c> с интерфейсом
/// <c>IPEProApplication</c> по умолчанию.
/// </para>
/// <para>
/// Методы намеренно не реализуют функциональность: она живёт в настоящем
/// классе <c>PEProApplication</c>. Соответствие состава и DISPID проверяется
/// модульным тестом, который читает собранную библиотеку типов и сверяет её
/// с реальным интерфейсом.
/// </para>
/// </remarks>
[ComVisible(true)]
[Guid(ComAutomationIds.ClassId)]
[ProgId(ComAutomationIds.ProgId)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PEProApplicationTypelibMirror : IPEProApplication
{
    /// <summary>Заглушка значения, возвращаемого методами.</summary>
    private const int NotBuilt = 0;

    /// <inheritdoc />
    public int PrintFromFileEnvelopes(string filename) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromFileNotifications(string filename) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromFileInventories(string filename) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromFileRegisters(string filename) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromXmlEnvelopes(string xmlText) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromXmlNotifications(string xmlText) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromXmlInventories(string xmlText) => NotBuilt;

    /// <inheritdoc />
    public int PrintFromXmlRegisters(string xmlText) => NotBuilt;

    /// <inheritdoc />
    public int PrintEnvelope(
        string from,
        string fromAddress,
        string fromZipCode,
        string to,
        string toAddress,
        string toZipCode) => NotBuilt;

    /// <inheritdoc />
    public int OpenDummyFormToSetAsForeground(out string pRetVal)
    {
        pRetVal = string.Empty;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int CloseDummyFormAndSetAsForeground(string process) => NotBuilt;

    /// <inheritdoc />
    public int DatabaseIsLoaded(out bool pRetVal)
    {
        pRetVal = false;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int SafeLoadDatabase(out bool pRetVal)
    {
        pRetVal = false;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetFlagParseZipIndexFromAddress(out bool pRetVal)
    {
        pRetVal = false;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int SetFlagParseZipIndexFromAddress(bool value, out bool pRetVal)
    {
        pRetVal = value;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetFlagShowFormsAsModal(out bool pRetVal)
    {
        pRetVal = false;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int SetFlagShowFormsAsModal(bool value, out bool pRetVal)
    {
        pRetVal = value;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetProductVersion(out string pRetVal)
    {
        pRetVal = string.Empty;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetAPIVersion(out int pRetVal)
    {
        pRetVal = 0;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetRegisterTemplateNames(out string[] pRetVal)
    {
        pRetVal = [];

        return NotBuilt;
    }

    /// <inheritdoc />
    public int PrintFromXmlRegistersWithoutForm(
        string xmlText,
        string nameTemplate,
        string resultFullPath) => NotBuilt;

    /// <inheritdoc />
    public int GetWarningType(out int pRetVal)
    {
        pRetVal = 0;

        return NotBuilt;
    }

    /// <inheritdoc />
    public int GetStdRegisterTemplateNames(out string[] pRetVal)
    {
        pRetVal = [];

        return NotBuilt;
    }
}
