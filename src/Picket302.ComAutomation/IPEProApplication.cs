using System.Runtime.InteropServices;

// По умолчанию среда выполнения считает сборку COM-видимой; скрываем всё, кроме
// явно помеченных типов контракта и класса (ТЗ §9.1, функция 113).
[assembly: ComVisible(false)]

namespace Picket302.ComAutomation;

/// <summary>
/// Идентификаторы COM-контракта автоматизации Picket302 (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// <para>
/// Значения соответствуют EVIDENCE/EVIDENCE/automation_idl.txt: библиотека типов
/// <c>22BC298D-DF0E-3C29-B589-7A50D3A8B871</c> версии 2.2, интерфейс
/// <c>IPEProApplication</c> — <c>3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6</c> версии 1.0,
/// класс <c>PEProApplication</c> — <c>5715286E-F98F-4C1C-AC86-AB943AFAE004</c> версии 1.0.
/// </para>
/// <para>
/// DISPID-ы назначены последовательно, без пропусков, начиная с <c>0x60020000</c>
/// (порядковые номера 1..23 таблицы 9.1 ТЗ соответствуют значениям
/// <c>0x60020000 + (номер − 1)</c>).
/// </para>
/// </remarks>
public static class ComAutomationIds
{
    /// <summary>ProgID COM-сервера автоматизации.</summary>
    public const string ProgId = "PEPro.Application";

    /// <summary>Идентификатор интерфейса <c>IPEProApplication</c>.</summary>
    public const string InterfaceId = "3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6";

    /// <summary>Идентификатор класса <c>PEProApplication</c>.</summary>
    public const string ClassId = "5715286E-F98F-4C1C-AC86-AB943AFAE004";

    /// <summary>Идентификатор библиотеки типов.</summary>
    public const string TypeLibId = "22BC298D-DF0E-3C29-B589-7A50D3A8B871";

    /// <summary>Версия библиотеки типов (Includes-версия 2.2).</summary>
    public const string TypeLibVersion = "2.2";

    /// <summary>Первый DISPID контракта (метод 1 по таблице 9.1 ТЗ).</summary>
    public const int FirstDispatchId = 0x60020000;

    /// <summary>
    /// Версия контракта автоматизации — целое, сквозная нумерация (ТЗ §9.3).
    /// Продукт версии 2.2 предоставляет API версии 1 (EVIDENCE/automation_api_overview.txt).
    /// </summary>
    public const int ApiVersion = 1;

    /// <summary>Число публикуемых методов контракта (таблица 9.1 ТЗ).</summary>
    public const int MethodCount = 23;
}

/// <summary>
/// Публичный COM-контракт автоматизации Picket302 (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// <para>
/// Контракт содержит 23 метода в порядке таблицы 9.1 ТЗ; каждому методу
/// назначен собственный DISPID из диапазона <c>0x60020000</c> без пропусков.
/// Методы не бросают исключений: ошибка операции кодируется значением
/// <see cref="GetWarningType"/> (ТЗ §9.2, таблица 9.2), HRESULT остаётся S_OK.
/// </para>
/// <para>
/// Интерфейс двуинтерфейсный (dual) и поддерживает OLE Automation: параметры
/// строк передаются как BSTR, логические значения — как VARIANT_BOOL, списки
/// имён — как SAFEARRAY(BSTR).
/// </para>
/// </remarks>
[ComVisible(true)]
[Guid(ComAutomationIds.InterfaceId)]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IPEProApplication
{
    /// <summary>
    /// Печать конвертов из файла формата EnvelopesData (ТЗ §9.1, метод 1;
    /// DISPID 0x60020000).
    /// </summary>
    /// <param name="filename">Путь к файлу с данными конвертов.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020000)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromFileEnvelopes([MarshalAs(UnmanagedType.BStr)] string filename);

    /// <summary>
    /// Печать уведомлений из файла (ТЗ §9.1, метод 2; DISPID 0x60020001).
    /// </summary>
    /// <param name="filename">Путь к файлу с данными уведомлений.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020001)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromFileNotifications([MarshalAs(UnmanagedType.BStr)] string filename);

    /// <summary>
    /// Печать описей из файла формата InventoriesData (ТЗ §9.1, метод 3;
    /// DISPID 0x60020002).
    /// </summary>
    /// <param name="filename">Путь к файлу с данными описей.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020002)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromFileInventories([MarshalAs(UnmanagedType.BStr)] string filename);

    /// <summary>
    /// Печать реестров из файла формата RegistersData (ТЗ §9.1, метод 4;
    /// DISPID 0x60020003).
    /// </summary>
    /// <param name="filename">Путь к файлу с данными реестров.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020003)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromFileRegisters([MarshalAs(UnmanagedType.BStr)] string filename);

    /// <summary>
    /// Печать конвертов из XML-строки (ТЗ §9.1, метод 5; DISPID 0x60020004).
    /// </summary>
    /// <param name="xmlText">XML-текст формата EnvelopesData.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020004)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromXmlEnvelopes([MarshalAs(UnmanagedType.BStr)] string xmlText);

    /// <summary>
    /// Печать уведомлений из XML-строки (ТЗ §9.1, метод 6; DISPID 0x60020005).
    /// </summary>
    /// <param name="xmlText">XML-текст формата EnvelopesData.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020005)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromXmlNotifications([MarshalAs(UnmanagedType.BStr)] string xmlText);

    /// <summary>
    /// Печать описей из XML-строки (ТЗ §9.1, метод 7; DISPID 0x60020006).
    /// </summary>
    /// <param name="xmlText">XML-текст формата InventoriesData.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020006)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromXmlInventories([MarshalAs(UnmanagedType.BStr)] string xmlText);

    /// <summary>
    /// Печать реестров из XML-строки (ТЗ §9.1, метод 8; DISPID 0x60020007).
    /// </summary>
    /// <param name="xmlText">XML-текст формата RegistersData.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020007)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromXmlRegisters([MarshalAs(UnmanagedType.BStr)] string xmlText);

    /// <summary>
    /// Печать одного конверта из шести строковых параметров (ТЗ §9.1, метод 9;
    /// DISPID 0x60020008).
    /// </summary>
    /// <param name="from">Наименование отправителя.</param>
    /// <param name="fromAddress">Адрес отправителя.</param>
    /// <param name="fromZipCode">Почтовый индекс отправителя.</param>
    /// <param name="to">Наименование получателя.</param>
    /// <param name="toAddress">Адрес получателя.</param>
    /// <param name="toZipCode">Почтовый индекс получателя.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020008)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintEnvelope(
        [MarshalAs(UnmanagedType.BStr)] string from,
        [MarshalAs(UnmanagedType.BStr)] string fromAddress,
        [MarshalAs(UnmanagedType.BStr)] string fromZipCode,
        [MarshalAs(UnmanagedType.BStr)] string to,
        [MarshalAs(UnmanagedType.BStr)] string toAddress,
        [MarshalAs(UnmanagedType.BStr)] string toZipCode);

    /// <summary>
    /// Открыть вспомогательное немодальное окно и перехватить фокус переднего
    /// плана (ТЗ §9.2, метод 10; DISPID 0x60020009).
    /// </summary>
    /// <param name="pRetVal">Заголовок окна, находившегося на переднем плане.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x60020009)]
    [return: MarshalAs(UnmanagedType.Error)]
    int OpenDummyFormToSetAsForeground(
        [MarshalAs(UnmanagedType.BStr)] out string pRetVal);

    /// <summary>
    /// Закрыть вспомогательное окно, вернув фокус вызывающему процессу
    /// (ТЗ §9.2, метод 11; DISPID 0x6002000A).
    /// </summary>
    /// <param name="process">Имя или идентификатор вызывающего процесса.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000A)]
    [return: MarshalAs(UnmanagedType.Error)]
    int CloseDummyFormAndSetAsForeground([MarshalAs(UnmanagedType.BStr)] string process);

    /// <summary>
    /// Признак загруженной базы данных (ТЗ §9.1, метод 12; DISPID 0x6002000B).
    /// </summary>
    /// <param name="pRetVal"><see langword="true"/> — база загружена.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000B)]
    [return: MarshalAs(UnmanagedType.Error)]
    int DatabaseIsLoaded([MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Идемпотентная безопасная загрузка базы данных (ТЗ §9.1, метод 13;
    /// DISPID 0x6002000C).
    /// </summary>
    /// <param name="pRetVal"><see langword="true"/> — база загружена.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000C)]
    [return: MarshalAs(UnmanagedType.Error)]
    int SafeLoadDatabase([MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Прочитать флаг «выкусывать индекс из строки адреса» (ТЗ §9.1, метод 14;
    /// DISPID 0x6002000D).
    /// </summary>
    /// <param name="pRetVal">Значение флага.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000D)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetFlagParseZipIndexFromAddress(
        [MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Установить флаг разбора индекса (ТЗ §9.1, метод 15; DISPID 0x6002000E).
    /// </summary>
    /// <param name="value">Новое значение флага.</param>
    /// <param name="pRetVal">Установленное значение флага.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000E)]
    [return: MarshalAs(UnmanagedType.Error)]
    int SetFlagParseZipIndexFromAddress(
        [MarshalAs(UnmanagedType.VariantBool)] bool value,
        [MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Прочитать флаг модального показа окон (ТЗ §9.1, метод 16; DISPID 0x6002000F).
    /// </summary>
    /// <param name="pRetVal">Значение флага; по умолчанию — <see langword="false"/>.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x6002000F)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetFlagShowFormsAsModal(
        [MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Установить флаг модального показа окон (ТЗ §9.1, метод 17; DISPID 0x60020010).
    /// </summary>
    /// <param name="value">Новое значение флага.</param>
    /// <param name="pRetVal">Установленное значение флага.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x60020010)]
    [return: MarshalAs(UnmanagedType.Error)]
    int SetFlagShowFormsAsModal(
        [MarshalAs(UnmanagedType.VariantBool)] bool value,
        [MarshalAs(UnmanagedType.VariantBool)] out bool pRetVal);

    /// <summary>
    /// Версия продукта строкой вида «x.y.z.w» (ТЗ §9.1, метод 18; DISPID 0x60020011).
    /// </summary>
    /// <param name="pRetVal">Строка версии продукта.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x60020011)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetProductVersion([MarshalAs(UnmanagedType.BStr)] out string pRetVal);

    /// <summary>
    /// Версия контракта автоматизации — целое, сквозная нумерация (ТЗ §9.3;
    /// DISPID 0x60020012).
    /// </summary>
    /// <param name="pRetVal">Номер версии API.</param>
    /// <returns>S_OK.</returns>
    [DispId(0x60020012)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetAPIVersion( out int pRetVal);

    /// <summary>
    /// Список пользовательских шаблонов реестров (ТЗ §9.1, метод 20;
    /// DISPID 0x60020013).
    /// </summary>
    /// <param name="pRetVal">Имена пользовательских шаблонов реестров.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020013)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetRegisterTemplateNames(
        [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)]
        out string[] pRetVal);

    /// <summary>
    /// Печать реестра без показа формы с сохранением результата по указанному
    /// пути (ТЗ §9.1, метод 21; DISPID 0x60020014).
    /// </summary>
    /// <param name="xmlText">XML-текст формата RegistersData.</param>
    /// <param name="nameTemplate">Имя шаблона реестра.</param>
    /// <param name="resultFullPath">Полный путь сохраняемого результата.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020014)]
    [return: MarshalAs(UnmanagedType.Error)]
    int PrintFromXmlRegistersWithoutForm(
        [MarshalAs(UnmanagedType.BStr)] string xmlText,
        [MarshalAs(UnmanagedType.BStr)] string nameTemplate,
        [MarshalAs(UnmanagedType.BStr)] string resultFullPath);

    /// <summary>
    /// Код результата последней операции (ТЗ §9.2, таблица 9.2; DISPID 0x60020015).
    /// </summary>
    /// <param name="pRetVal">
    /// 0 — успех; 1 — база не загружена; 2 — ошибка XML; 3 — ошибка документа;
    /// 4 — не найден шаблон.
    /// </param>
    /// <returns>S_OK.</returns>
    [DispId(0x60020015)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetWarningType( out int pRetVal);

    /// <summary>
    /// Список стандартных шаблонов реестров (ТЗ §9.1, метод 23; DISPID 0x60020016).
    /// </summary>
    /// <param name="pRetVal">Имена стандартных шаблонов реестров.</param>
    /// <returns>S_OK; код результата — в <see cref="GetWarningType"/>.</returns>
    [DispId(0x60020016)]
    [return: MarshalAs(UnmanagedType.Error)]
    int GetStdRegisterTemplateNames(
        [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)]
        out string[] pRetVal);
}
