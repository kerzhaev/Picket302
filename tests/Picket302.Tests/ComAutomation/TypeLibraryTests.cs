using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Picket302.ComAutomation;
using Xunit;

namespace Picket302.Tests.ComAutomation;

/// <summary>
/// Проверка библиотеки типов COM-сервера (ТЗ §9.3, функция 114:
/// «После регистрации <c>oleview</c> показывает типилибу и 23 метода
/// интерфейса»; §9.2: «Includes-версия типилиба 2.2»).
/// </summary>
/// <remarks>
/// Тест читает собранный файл <c>PrintEnvelopePro.tlb</c> средствами
/// <c>oleaut32</c> и сверяет его с поставляемым интерфейсом
/// <see cref="IPEProApplication"/>: состав методов, их имена и DISPID.
/// </remarks>
public sealed class TypeLibraryTests
{
    /// <summary>
    /// Идентификатор библиотеки типов (совпадает с идентификатором из
    /// EVIDENCE/EVIDENCE/automation_idl.txt).
    /// </summary>
    private static readonly Guid ОжидаемыйИдентификаторБиблиотеки =
        new("22BC298D-DF0E-3C29-B589-7A50D3A8B871");

    /// <summary>
    /// Идентификатор интерфейса <c>IPEProApplication</c>.
    /// </summary>
    private static readonly Guid ОжидаемыйИдентификаторИнтерфейса =
        new("3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6");

    /// <summary>
    /// Проверяет, что библиотека типов поставляется вместе со сборкой.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_ПоставляетсяВКаталогеСборки()
    {
        var путь = Path.Combine(AppContext.BaseDirectory, ComRegistration.TypeLibraryFileName);

        Assert.True(File.Exists(путь), $"Файл библиотеки типов '{путь}' не найден.");
        Assert.True(new FileInfo(путь).Length > 0, "Файл библиотеки типов пуст.");
    }

    /// <summary>
    /// Проверяет, что библиотека типов имеет версию 2.2 (Includes-версия) и
    /// идентификатор из контракта.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_ИмеетВерсию22()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var описание = Marshal.PtrToStructure<LibDesc>(ПолучитьОписаниеБиблиотеки(библиотека));

        Assert.Equal((ushort)2, описание.MajorVersion);
        Assert.Equal((ushort)2, описание.MinorVersion);
        Assert.Equal(ОжидаемыйИдентификаторБиблиотеки, описание.Guid);
    }

    /// <summary>
    /// Проверяет, что библиотека типов содержит интерфейс
    /// <c>IPEProApplication</c> с идентификатором из контракта.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_СодержитИнтерфейсIPEProApplication()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var типы = ПрочитатьТипы(библиотека);

        Assert.Contains(типы, пара => пара.Имя == "IPEProApplication");
        Assert.Contains(типы, пара => пара.Идентификатор == ОжидаемыйИдентификаторИнтерфейса);
    }

    /// <summary>
    /// Проверяет, что библиотека типов содержит класс <c>PEProApplication</c>.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_СодержитКлассPEProApplication()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var типы = ПрочитатьТипы(библиотека);

        Assert.Contains(типы, пара => пара.Имя == "PEProApplication");
    }

    /// <summary>
    /// Проверяет, что интерфейс в библиотеке типов содержит ровно 23 метода
    /// контракта с теми же именами и DISPID, что и поставляемый интерфейс.
    /// </summary>
    /// <remarks>
    /// Кроме 23 методов контракта библиотека типов содержит унаследованные
    /// методы IUnknown (DISPID 0x60000000…0x60000002) и IDispatch
    /// (0x60010000…0x60010003): они описаны в том же файле, что и интерфейс,
    /// поэтому MIDL включает их в описание интерфейса. Их набор и значения
    /// DISPID совпадают со стандартными значениями OLE.
    /// </remarks>
    [Fact]
    public void БиблиотекаТипов_Содержит23МетодаКонтракта()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var методы = ПрочитатьМетоды(библиотека, "IPEProApplication");

        var контрактные = методы
            .Where(пара => пара.Идентификатор >= 0x60020000)
            .ToList();

        Assert.Equal(23, контрактные.Count);

        var ожидаемые = ОжидаемыеМетоды();

        Assert.Equal(ожидаемые.Count, контрактные.Count);

        for (var индекс = 0; индекс < ожидаемые.Count; индекс++)
        {
            Assert.Equal(ожидаемые[индекс].Имя, контрактные[индекс].Имя);
            Assert.Equal(ожидаемые[индекс].Идентификатор, контрактные[индекс].Идентификатор);
        }
    }

    /// <summary>
    /// Проверяет, что кроме методов контракта интерфейс содержит только
    /// стандартные унаследованные методы IUnknown и IDispatch.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_УнаследованныеМетодыСтандартные()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var методы = ПрочитатьМетоды(библиотека, "IPEProApplication");

        var унаследованные = методы
            .Where(пара => пара.Идентификатор < 0x60020000)
            .Select(пара => пара.Идентификатор)
            .OrderBy(значение => значение)
            .ToList();

        Assert.Equal(
            new[] { 0x60000000, 0x60000001, 0x60000002, 0x60010000, 0x60010001, 0x60010002, 0x60010003 },
            унаследованные);
    }

    /// <summary>
    /// Проверяет, что DISPID методов контракта идут подряд, без пропусков.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_DispIdНазначеныПодряд()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var методы = ПрочитатьМетоды(библиотека, "IPEProApplication");

        var контрактные = методы
            .Where(пара => пара.Идентификатор >= 0x60020000)
            .ToList();

        for (var индекс = 0; индекс < контрактные.Count; индекс++)
        {
            Assert.Equal(0x60020000 + индекс, контрактные[индекс].Идентификатор);
        }
    }

    /// <summary>
    /// Проверяет, что идентификатор библиотеки типов совпадает с контрактом.
    /// </summary>
    [Fact]
    public void БиблиотекаТипов_ИдентификаторСовпадаетСКонтрактом()
    {
        var библиотека = ЗагрузитьБиблиотеку();

        var описание = Marshal.PtrToStructure<LibDesc>(ПолучитьОписаниеБиблиотеки(библиотека));

        Assert.Equal(ОжидаемыйИдентификаторБиблиотеки, описание.Guid);
    }

    private static ITypeLib ЗагрузитьБиблиотеку()
    {
        var путь = Path.Combine(AppContext.BaseDirectory, ComRegistration.TypeLibraryFileName);

        LoadTypeLibEx(путь, RegKindNone, out var библиотека);

        return библиотека;
    }

    /// <summary>
    /// Читает описание библиотеки: идентификатор и номера версий.
    /// </summary>
    private static IntPtr ПолучитьОписаниеБиблиотеки(ITypeLib библиотека)
    {
        var описание = (ITypeLibVersion)библиотека;

        описание.GetLibDesc(out var указатель);

        return указатель;
    }

    private static List<(string Имя, Guid Идентификатор)> ПрочитатьТипы(ITypeLib библиотека)
    {
        var типы = new List<(string, Guid)>();

        var количество = библиотека.GetTypeInfoCount();

        for (var индекс = 0; индекс < количество; индекс++)
        {
            библиотека.GetTypeInfo(индекс, out var информация);

            информация.GetDocumentation(-1, out var имя, out _, out _, out _);

            var атрибуты = Marshal.PtrToStructure<TypeAttr>(ПолучитьАтрибуты(информация));

            типы.Add((имя, атрибуты.Guid));

            Marshal.ReleaseComObject(информация);
        }

        return типы;
    }

    private static List<(string Имя, int Идентификатор)> ПрочитатьМетоды(ITypeLib библиотека, string имяТипа)
    {
        var методы = new List<(string, int)>();

        var количество = библиотека.GetTypeInfoCount();

        for (var индекс = 0; индекс < количество; индекс++)
        {
            библиотека.GetTypeInfo(индекс, out var информация);

            информация.GetDocumentation(-1, out var имя, out _, out _, out _);

            if (!string.Equals(имя, имяТипа, StringComparison.OrdinalIgnoreCase))
            {
                Marshal.ReleaseComObject(информация);

                continue;
            }

            var атрибуты = Marshal.PtrToStructure<TypeAttr>(ПолучитьАтрибуты(информация));

            for (var номер = 0; номер < атрибуты.FuncCount; номер++)
            {
                var описание = Marshal.PtrToStructure<FuncDesc>(ПолучитьОписаниеМетода(информация, номер));

                var имена = new string[описание.ParameterCount + 1];

                информация.GetNames(описание.MemberId, имена, имена.Length, out var получено);

                методы.Add((получено > 0 ? имена[0] : string.Empty, описание.MemberId));
            }

            Marshal.ReleaseComObject(информация);
        }

        return методы;
    }

    private static List<(string Имя, int Идентификатор)> ОжидаемыеМетоды()
    {
        var методы = new List<(string, int)>();

        var тип = typeof(IPEProApplication);

        foreach (var метод in тип.GetMethods())
        {
            var атрибут = (DispIdAttribute?)Attribute.GetCustomAttribute(метод, typeof(DispIdAttribute));

            Assert.NotNull(атрибут);

            методы.Add((метод.Name, атрибут!.Value));
        }

        return методы;
    }

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(
        [MarshalAs(UnmanagedType.LPWStr)] string путь,
        int видРегистрации,
        out ITypeLib библиотека);

    /// <summary>
    /// Чтение описания библиотеки: <c>GetLibDesc</c> отсутствует в
    /// <see cref="ITypeLib"/> класса <c>System.Runtime.InteropServices.ComTypes</c>,
    /// поэтому метод объявлен вручную в порядке таблицы виртуальных методов
    /// интерфейса <c>ITypeLib</c>.
    /// </summary>
    [ComImport]
    [Guid("00020402-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITypeLibVersion
    {
        void GetTypeInfoCount(out int количество);

        void GetTypeInfo(int индекс, out IntPtr информация);

        void FindType(
            [MarshalAs(UnmanagedType.LPWStr)] string имя,
            int lcid,
            int вид,
            out IntPtr информация);

        void GetTypeInfoType(
            [MarshalAs(UnmanagedType.LPWStr)] string имя,
            int lcid,
            out int вид);

        void GetLibDesc(out IntPtr описание);
    }

    private static IntPtr ПолучитьАтрибуты(ITypeInfo информация)
    {
        информация.GetTypeAttr(out var указатель);

        return указатель;
    }

    private static IntPtr ПолучитьОписаниеМетода(ITypeInfo информация, int номер)
    {
        информация.GetFuncDesc(номер, out var указатель);

        return указатель;
    }

    /// <summary>Значение REGKIND_NONE: библиотека загружается без регистрации.</summary>
    private const int RegKindNone = 2;

    /// <summary>
    /// Разметка LIBDESC: идентификатор библиотеки, служебные поля и номера
    /// версий. Номера версий расположены после идентификатора и двух
    /// 32-разрядных полей.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LibDesc
    {
        public Guid Guid;

        public int Reserved;

        public int Flags;

        public ushort MajorVersion;

        public ushort MinorVersion;

        public int ReservedTail;
    }

    /// <summary>
    /// Разметка TYPEATTR повторяет определение objidl.h: счётчики методов
    /// и переменных хранятся в 16-разрядных полях, а тип элемента — в
    /// 32-разрядном.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TypeAttr
    {
        public Guid Guid;

        public int Lcid;

        public int Reserved;

        public int ConstructorMemberId;

        public int DestructorMemberId;

        public IntPtr Schema;

        public uint InstanceSize;

        public int TypeKind;

        public short FuncCount;

        public short VarCount;

        public short ImplTypeCount;

        public short VftSize;

        public short Alignment;

        public short TypeFlags;

        public short MajorVersion;

        public short MinorVersion;
    }

    /// <summary>
    /// Разметка FUNCDESC повторяет определение oaidl.idl: идентификатор
    /// метода находится в начале структуры, виды вызова — в 32-разрядных
    /// полях.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FuncDesc
    {
        public int MemberId;

        public IntPtr Codes;

        public IntPtr ElementDescriptions;

        public int Kind;

        public int InvokeKind;

        public int CallConvention;

        public short ParameterCount;

        public short OptionalParameterCount;

        public short VftOffset;

        public short CodeCount;
    }
}
