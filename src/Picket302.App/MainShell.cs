using System.Drawing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Email;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Services;
using Picket302.Infrastructure.Export;
using Picket302.Infrastructure.Integration;
using Picket302.Infrastructure.Mail;
using Picket302.Infrastructure.Migration;
using Picket302.Infrastructure.Persistence;
using Picket302.Infrastructure.Support;
using Picket302.Infrastructure.Update;

using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Preview;
using Picket302.Printing.Validation;
namespace Picket302.App;

/// <summary>
/// Главное окно оператора (ТЗ §7.1: «Главное окно приложения (Схема «3+2»)»;
/// §8.12, функция 152: «Четыре вкладки главного окна»).
/// </summary>
/// <remarks>
/// <para>
/// Вкладка «Адресная книга» содержит рабочую область оператора по схеме «3+2»:
/// три верхние панели (ОТПРАВИТЕЛЬ, ПОЛУЧАТЕЛЬ, ОТОБРАЖЕНИЕ), строку примечания и
/// две таблицы — «СПРАВОЧНИК ОТПРАВИТЕЛЕЙ» и «АДРЕСНАЯ КНИГА / ПОЛУЧАТЕЛИ».
/// Внизу окна расположена строка состояния.
/// </para>
/// <para>
/// Плотность рабочего режима: шрифт таблиц 8pt Tahoma, высота строки ~17px
/// (ТЗ §7.10). Горячие клавиши описаны в ТЗ §7.4 и собраны в
/// <see cref="ShellHotKeyMap"/>.
/// </para>
/// </remarks>
public sealed class MainShell : Form
{
    private const int ИдентификаторГорячейКлавиши = 0xA302;
    private const int СообщениеГорячейКлавиши = 0x0312;

    /// <summary>
    /// Действующий состав приложения. Присваивается в конструкторе с составом
    /// либо при инициализации (путь по умолчанию): прежде поле было только
    /// для чтения и оставалось пустым, из-за чего обработчики молча пропускали
    /// работу с базой данных.
    /// </summary>
    private AppServices? _состав;

    /// <summary>
    /// Признак того, что состав создан самой оболочкой (путь по умолчанию):
    /// такой состав освобождается при закрытии окна, состав, переданный
    /// снаружи, остаётся во владении вызывающего.
    /// </summary>
    private bool _составСозданоВнутри;

    /// <summary>
    /// Признак однократной подписки на изменения сортировки адресной книги:
    /// повторная инициализация («Открыть базу…») не должна накапливать
    /// обработчики.
    /// </summary>
    private bool _сортировкаПодписана;

    private readonly SearchSession _поиск = new();
    private readonly List<long> _выбранныеОтправители = [];
    private readonly List<long> _выбранныеПолучатели = [];
    private readonly List<ЗаписьПолучателя> _текущиеПолучатели = [];

    /// <summary>
    /// Запись таблицы получателей в текущей выборке: используется механизмом
    /// поиска (ТЗ §7.7).
    /// </summary>
    /// <param name="Идентификатор">Идентификатор контрагента.</param>
    /// <param name="Наименование">Наименование контрагента.</param>
    private sealed record ЗаписьПолучателя(long Идентификатор, string Наименование);

    private GridAppearance _оформление = GridAppearance.Default;
    private GlobalHotKey? _глобальнаяГорячаяКлавиша;
    private bool _инициализировано;

    private readonly TabControl _вкладки = new();
    private readonly TabPage _страницаАдреснойКниги = new("Адресная книга");
    private readonly TabPage _страницаКонвертов = new("Конверты");
    private readonly TabPage _страницаРассылок = new("Рассылки");
    private readonly TabPage _страницаОтправленных = new("Отправленные");

    private readonly AddressEditor _адресОтправителя = new("ОТПРАВИТЕЛЬ:");
    private readonly AddressEditor _адресПолучателя = new("ПОЛУЧАТЕЛЬ:");
    private readonly TextBox _примечание = new();

    private readonly RadioButton _сЛоготипом = new() { Text = "С логотипом" };
    private readonly RadioButton _безЛоготипа = new() { Text = "Без логотипа" };
    private readonly RadioButton _переключательАдресОтправителя = new() { Text = "Адрес отправителя" };
    private readonly RadioButton _переключательАдресПолучателя = new() { Text = "Адрес получателя" };
    private readonly PictureBox _превью = new();

    private readonly DataGridView _таблицаОтправителей = new();
    private readonly TextBox _поискОтправителя = new();
    private readonly DataGridView _таблицаПолучателей = new();

    /// <summary>
    /// Индекс столбца адресной книги, над которым открыто контекстное меню
    /// (ТЗ §8.11, функция 131: «скрытие колонок»).
    /// </summary>
    private int _индексСтолбцаПодКурсором = -1;
    private readonly DataGridView _таблицаБланков = new();
    private readonly DataGridView _таблицаРассылок = new();
    private readonly DataGridView _таблицаОтправленных = new();
    private readonly TreeView _деревоПапок = new();

    private readonly Label _всего = new();
    private readonly Label _выбрано = new();
    private readonly Label _выведено = new();
    private readonly Label _копий = new();
    private readonly Label _состояниеБазы = new();

    private readonly TreeView _деревоКатегорий = new();
    private readonly ToolStripMenuItem _пунктИзбранного = new("Избранное (VIP)");
    private readonly ToolStripMenuItem _пунктОбъединить = new("Объединить в рассылку");
    private readonly ToolStripMenuItem _пунктПереместить = new("Переместить в категорию");
    private readonly ToolStripMenuItem _пунктПометитьОтправителем = new("Пометить как отправителя");

    /// <summary>
    /// Идентификатор таблицы адресной книги в настройках вида таблиц
    /// (ТЗ §8.11, функция 131).
    /// </summary>
    private const string ИдентификаторТаблицыПолучателей = "addressBook";

    /// <summary>
    /// Контроллер сортировки адресной книги по любой колонке
    /// (ТЗ §8.1, функция 9).
    /// </summary>
    private readonly AddressBookSortController _сортировкаПолучателей = new();

    /// <summary>
    /// Контроллер настроек вида таблиц: цвета, заголовки столбцов, скрытие
    /// столбцов, «полосатая таблица», сброс к умолчаниям (ТЗ §8.11, функция 131).
    /// </summary>
    private GridViewSettingsController? _настройкиВидаТаблиц;

    /// <summary>
    /// Служба сохранённых пользовательских фильтров адресной книги
    /// (ТЗ §8.1, функция 9).
    /// </summary>
    private SavedFilterService? _сохранённыеФильтры;

    /// <summary>
    /// Условия применённого сохранённого фильтра; <see langword="null"/> —
    /// сохранённый фильтр не применён.
    /// </summary>
    private Picket302.Domain.Contracts.FilterCriteria? _применённыйФильтр;

    /// <summary>
    /// Пункт меню «Печать» с галкой «Показывать список шаблонов перед
    /// печатью» (ТЗ §8.6, функция 82).
    /// </summary>
    private ToolStripMenuItem? _пунктСпискаШаблонов;

    /// <summary>
    /// Создаёт главное окно оператора.
    /// </summary>
    public MainShell()
    {
        Text = "Picket302";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 700);

        ПостроитьМеню();
        ПостроитьВкладки();
        ПостроитьСтрокуСостояния();

        Controls.Add(_вкладки);
        Controls.Add(_строкаСостояния);
        MainMenuStrip = _меню;
    }

    /// <summary>
    /// Создаёт главное окно оператора с готовым составом приложения.
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    public MainShell(AppServices состав)
        : this()
    {
        _состав = состав;
    }

    /// <summary>Главное меню окна.</summary>
    private readonly MenuStrip _меню = new();

    /// <summary>Панель строки состояния.</summary>
    private readonly Panel _строкаСостояния = new();

    /// <summary>Метка состояния печатной сессии в строке состояния (ТЗ §7.2).</summary>
    private readonly Label _состояниеПечати = new();

    /// <summary>
    /// Метка срока пробного периода в строке состояния (ТЗ §8.10, функция 119:
    /// «индикатор остатка дней»).
    /// </summary>
    private readonly Label _лицензия = new();

    /// <summary>
    /// Признак готовности окна к работе: база данных открыта и данные загружены.
    /// </summary>
    public bool Инициализировано => _инициализировано;

    /// <summary>Вкладки главного окна (ТЗ §8.12, функция 152).</summary>
    public TabControl Вкладки => _вкладки;

    /// <summary>Панель строки состояния (ТЗ §7.1).</summary>
    public Panel ПанельСостояния => _строкаСостояния;

    /// <summary>Таблица «СПРАВОЧНИК ОТПРАВИТЕЛЕЙ».</summary>
    public DataGridView ТаблицаОтправителей => _таблицаОтправителей;

    /// <summary>Таблица «АДРЕСНАЯ КНИГА / ПОЛУЧАТЕЛИ».</summary>
    public DataGridView ТаблицаПолучателей => _таблицаПолучателей;

    /// <summary>Редактор адреса отправителя.</summary>
    public AddressEditor АдресОтправителя => _адресОтправителя;

    /// <summary>Редактор адреса получателя.</summary>
    public AddressEditor АдресПолучателя => _адресПолучателя;

    /// <summary>Надпись счётчика «Всего».</summary>
    public Label СчётчикВсего => _всего;

    /// <summary>Надпись счётчика «Выбрано».</summary>
    public Label СчётчикВыбрано => _выбрано;

    /// <summary>Надпись счётчика «Выведено».</summary>
    public Label СчётчикВыведено => _выведено;

    /// <summary>Надпись счётчика «Копий».</summary>
    public Label СчётчикКопий => _копий;

    /// <summary>Надпись сведений о базе данных.</summary>
    public Label СчётчикБаза => _состояниеБазы;

    /// <summary>
    /// Надпись срока пробного периода: осталось дней (ТЗ §8.10, функция 119).
    /// </summary>
    public Label ИндикаторЛицензии => _лицензия;

    /// <summary>
    /// Действующий состав приложения: точка доступа автоматических проверок;
    /// до инициализации — <see langword="null"/>.
    /// </summary>
    public AppServices? ТекущийСостав => _состав;

    /// <summary>
    /// Формирует действующие условия отбора и сортировки адресной книги:
    /// сортировка по выбранной колонке и условия применённого сохранённого
    /// фильтра (ТЗ §8.1, функция 9).
    /// </summary>
    /// <returns>Условия отбора и сортировки.</returns>
    private Picket302.Domain.Contracts.FilterCriteria ТекущиеКритерииФильтра()
    {
        var критерии = _сортировкаПолучателей.ПостроитьКритерии();

        if (_применённыйФильтр is not { } фильтр)
        {
            return критерии;
        }

        критерии.Conditions.AddRange(фильтр.Conditions);
        критерии.OnlyFavorites = критерии.OnlyFavorites || фильтр.OnlyFavorites;
        критерии.GroupId ??= фильтр.GroupId;

        if (критерии.SortColumn is null && фильтр.SortColumn is not null)
        {
            критерии.SortColumn = фильтр.SortColumn;
            критерии.SortDescending = фильтр.SortDescending;
        }

        return критерии;
    }

    /// <summary>
    /// Сохраняет текущие условия отбора и сортировки под именем оператора
    /// (ТЗ §8.1, функция 9: «сохранённые пользовательские фильтры»).
    /// </summary>
    private async void СохранитьФильтр()
    {
        if (_сохранённыеФильтры is null)
        {
            return;
        }

        var имя = ВвестиИмяФильтра("Сохранить фильтр под именем:");

        if (string.IsNullOrWhiteSpace(имя))
        {
            return;
        }

        await _сохранённыеФильтры
            .СохранитьAsync(имя, ТекущиеКритерииФильтра())
            .ConfigureAwait(true);

        MessageBox.Show(
            this,
            $"Фильтр «{имя.Trim()}» сохранён.",
            "Фильтры адресной книги",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Применяет сохранённый фильтр адресной книги (ТЗ §8.1, функция 9).
    /// </summary>
    private async void ПрименитьФильтр()
    {
        if (_сохранённыеФильтры is null)
        {
            return;
        }

        var фильтры = await _сохранённыеФильтры.ПолучитьСписокAsync().ConfigureAwait(true);

        if (фильтры.Count == 0)
        {
            MessageBox.Show(
                this,
                "Сохранённые фильтры отсутствуют.",
                "Фильтры адресной книги",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var имена = фильтры.Select(фильтр => фильтр.Name).ToArray();
        var выбранное = ВыбратьИзСписка("Применить фильтр:", имена);

        if (выбранное is null)
        {
            return;
        }

        _применённыйФильтр = await _сохранённыеФильтры
            .ПолучитьКритерииAsync(выбранное)
            .ConfigureAwait(true);

        if (_состав is not null)
        {
            await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Удаляет сохранённый фильтр адресной книги (ТЗ §8.1, функция 9).
    /// </summary>
    private async void УдалитьФильтр()
    {
        if (_сохранённыеФильтры is null)
        {
            return;
        }

        var фильтры = await _сохранённыеФильтры.ПолучитьСписокAsync().ConfigureAwait(true);

        if (фильтры.Count == 0)
        {
            MessageBox.Show(
                this,
                "Сохранённые фильтры отсутствуют.",
                "Фильтры адресной книги",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var имена = фильтры.Select(фильтр => фильтр.Name).ToArray();
        var выбранное = ВыбратьИзСписка("Удалить фильтр:", имена);

        if (выбранное is null)
        {
            return;
        }

        await _сохранённыеФильтры.УдалитьAsync(выбранное).ConfigureAwait(true);

        if (_применённыйФильтр is not null)
        {
            var оставшийся = await _сохранённыеФильтры
                .ПолучитьКритерииAsync(выбранное)
                .ConfigureAwait(true);

            if (оставшийся is null)
            {
                _применённыйФильтр = null;
            }
        }

        if (_состав is not null)
        {
            await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Сбрасывает сортировку адресной книги (ТЗ §8.1, функция 9).
    /// </summary>
    private async void СброситьСортировку()
    {
        _сортировкаПолучателей.Сбросить();

        if (_состав is not null)
        {
            await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Запрашивает у оператора имя фильтра (ТЗ §8.1, функция 9).
    /// </summary>
    /// <param name="заголовок">Текст приглашения.</param>
    /// <returns>Введённое имя либо <see langword="null"/>.</returns>
    private static string? ВвестиИмяФильтра(string заголовок)
    {
        using var форма = new FormFilterName(заголовок);

        return форма.ShowDialog() == DialogResult.OK ? форма.Имя : null;
    }

    /// <summary>
    /// Предлагает оператору выбрать значение из списка (ТЗ §8.1, функция 9).
    /// </summary>
    /// <param name="заголовок">Текст приглашения.</param>
    /// <param name="значения">Доступные значения.</param>
    /// <returns>Выбранное значение либо <see langword="null"/>.</returns>
    private static string? ВыбратьИзСписка(string заголовок, IReadOnlyList<string> значения)
    {
        using var форма = new FormValuePicker(заголовок, значения);

        return форма.ShowDialog() == DialogResult.OK ? форма.ВыбранноеЗначение : null;
    }

    /// <summary>
    /// Инициализирует окно: открывает базу данных, применяет оформление и
    /// загружает данные.
    /// </summary>
    /// <param name="состав">Состав приложения; при <see langword="null"/> используется
    /// путь по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task ИнициализироватьAsync(
        AppServices? состав = null,
        CancellationToken cancellationToken = default)
        => ИнициализироватьЯдроAsync(состав, AppServices.ПутьБазыПоУмолчанию(), cancellationToken);

    /// <summary>
    /// Инициализирует окно с составом, созданным по указанному пути базы
    /// данных: точка повторного подключения («Открыть базу…») и автоматических
    /// проверок, которым недоступен путь по умолчанию.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task ИнициализироватьAsync(
        string путьБазы,
        CancellationToken cancellationToken = default)
        => ИнициализироватьЯдроAsync(null, путьБазы, cancellationToken);

    /// <summary>
    /// Общая цепочка инициализации: получает состав (создаёт при
    /// необходимости), открывает данные и применяет оформление.
    /// </summary>
    /// <param name="состав">Переданный состав; при <see langword="null"/> состав
    /// создаётся по <paramref name="путьБазы"/>.</param>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ИнициализироватьЯдроAsync(
        AppServices? состав,
        string путьБазы,
        CancellationToken cancellationToken)
    {
        if (состав is not null)
        {
            // Состав передан снаружи: владение остаётся у вызывающего.
            _состав = состав;
            _составСозданоВнутри = false;
        }
        else if (_состав is null)
        {
            _состав = await AppServices
                .СоздатьAsync(путьБазы, true, cancellationToken)
                .ConfigureAwait(true);
            _составСозданоВнутри = true;
        }

        var активный = _состав!;

        // Подключение к базе состоялось: после первого подключения имя базы
        // данных изменению не подлежит (ТЗ §8.11, функция 127).
        активный.МенеджерПодключения.MarkConnectionEstablished();

        _оформление = await активный.НастройкиОтображения.LoadAppearanceAsync(cancellationToken)
            .ConfigureAwait(true);

        GridAppearanceManager.Применить(_таблицаОтправителей, _оформление);
        GridAppearanceManager.Применить(_таблицаПолучателей, _оформление);
        GridAppearanceManager.Применить(_таблицаБланков, _оформление);
        GridAppearanceManager.Применить(_таблицаРассылок, _оформление);
        GridAppearanceManager.Применить(_таблицаОтправленных, _оформление);

        // Сортировка по любой колонке адресной книги и настройки вида таблицы:
        // цвета, заголовки столбцов, скрытие столбцов, «полосатая таблица»
        // (ТЗ §8.1, функция 9; §8.11, функция 131).
        _сортировкаПолучателей.Подключить(_таблицаПолучателей);

        // Подписка однократная: повторная инициализация («Открыть базу…»)
        // не должна накапливать обработчики.
        if (!_сортировкаПодписана)
        {
            _сортировкаПодписана = true;

            _сортировкаПолучателей.СортировкаИзменена += async (_, _) =>
            {
                if (_состав is not null)
                {
                    await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
                }
            };
        }

        _настройкиВидаТаблиц = new GridViewSettingsController(активный.НастройкиОтображения);

        // Чтение настроек и изменение таблицы разнесены по шагам: элементы
        // таблицы изменяются в потоке интерфейса (ТЗ §8.11, функция 131).
        var настройкиТаблицы = await _настройкиВидаТаблиц
            .ЗагрузитьAsync(ИдентификаторТаблицыПолучателей, cancellationToken)
            .ConfigureAwait(true);

        _оформление = _настройкиВидаТаблиц.Применить(_таблицаПолучателей, настройкиТаблицы);

        _сохранённыеФильтры = new SavedFilterService(активный.ХранилищеНастроек);

        await ЗагрузитьОтправителейAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПолучателейAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьБланкиAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьРассылкиAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(активный, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПапкиAsync(активный, cancellationToken).ConfigureAwait(true);

        _инициализировано = true;
        ОбновитьСтрокуСостояния(активный);
        ОбновитьИндикаторЛицензии(активный);
    }

    /// <summary>
    /// Строит главное меню окна (ТЗ §7.1: «Файл Правка Вид Сервис Печать
    /// Рассылки Справка»).
    /// </summary>
    private void ПостроитьМеню()
    {
        _меню.Font = new Font("Tahoma", 8f);
        _меню.Dock = DockStyle.None;

        var файл = new ToolStripMenuItem("Файл");
        файл.DropDownItems.Add("Открыть базу…", null, (_, _) => ОткрытьБазу());
        файл.DropDownItems.Add("Архивирование и восстановление базы…", null, (_, _) => ОткрытьАрхивацию());
        файл.DropDownItems.Add("Установить на USB-флешку…", null, (_, _) => ОткрытьУстановкуНаUsb());
        файл.DropDownItems.Add(new ToolStripSeparator());
        файл.DropDownItems.Add("Журнал ошибок", null, (_, _) => ОткрытьЖурналОшибок());
        файл.DropDownItems.Add(new ToolStripSeparator());
        файл.DropDownItems.Add("Выход", null, (_, _) => Close());

        var правка = new ToolStripMenuItem("Правка");
        правка.DropDownItems.Add("Новая запись", null, (_, _) => ДобавитьПолучателя());
        правка.DropDownItems.Add("Изменить запись", null, (_, _) => ИзменитьПолучателя());
        правка.DropDownItems.Add("Удалить запись", null, (_, _) => УдалитьВыделенные());
        правка.DropDownItems.Add(new ToolStripSeparator());
        правка.DropDownItems.Add("Пакетное редактирование…", null, (_, _) => ПакетноеРедактирование());
        правка.DropDownItems.Add("Очистить базу…", null, (_, _) => ОткрытьФормуОчистки());

        var вид = new ToolStripMenuItem("Вид");
        вид.DropDownItems.Add("Настройка вида таблиц…", null, (_, _) => НастроитьОформление());
        вид.DropDownItems.Add("Настройка таблицы…", null, (_, _) => НастроитьТаблицу());
        вид.DropDownItems.Add(new ToolStripSeparator());

        // Фильтры и сортировка адресной книги (ТЗ §8.1, функция 9).
        var фильтры = new ToolStripMenuItem("Фильтры");
        фильтры.DropDownItems.Add("Сохранить фильтр…", null, (_, _) => СохранитьФильтр());
        фильтры.DropDownItems.Add("Применить фильтр…", null, (_, _) => ПрименитьФильтр());
        фильтры.DropDownItems.Add("Удалить фильтр…", null, (_, _) => УдалитьФильтр());
        фильтры.DropDownItems.Add(new ToolStripSeparator());
        фильтры.DropDownItems.Add("Сбросить сортировку", null, (_, _) => СброситьСортировку());

        вид.DropDownItems.Add(фильтры);
        вид.DropDownItems.Add(new ToolStripSeparator());
        вид.DropDownItems.Add("Обновить", null, (_, _) => ОбновитьВсе());

        var данные = new ToolStripMenuItem("Данные");
        данные.DropDownItems.Add("Мастер импорта адресной книги…", null, (_, _) => ОткрытьМастерИмпорта());
        данные.DropDownItems.Add(new ToolStripSeparator());
        данные.DropDownItems.Add("Импорт из 1С…", null, (_, _) => ИмпортИз1С());
        данные.DropDownItems.Add("Импорт из внешней БД…", null, (_, _) => ИмпортИзВнешнейБазы());
        данные.DropDownItems.Add("Загрузить данные из старой версии…", null, (_, _) => ОткрытьЗагрузкуИзСтаройВерсии());

        var сервис = new ToolStripMenuItem("Сервис");
        сервис.DropDownItems.Add("Демо-база", null, (_, _) => НаполнитьДемоБазу());
        сервис.DropDownItems.Add("Восстановить удалённые записи", null, (_, _) => ВосстановитьУдалённые());
        сервис.DropDownItems.Add("Создать отправителя…", null, (_, _) => СоздатьОтправителя());
        сервис.DropDownItems.Add(new ToolStripSeparator());
        сервис.DropDownItems.Add("Выгрузить данные…", null, (_, _) => ВыгрузитьДанные());
        сервис.DropDownItems.Add("Сформировать документ…", null, (_, _) => СформироватьДокумент());
        сервис.DropDownItems.Add("Мастер шаблонов документов…", null, (_, _) => ОткрытьМастерШаблонов());
        сервис.DropDownItems.Add(new ToolStripSeparator());
        сервис.DropDownItems.Add("Настройки интеграции (COM)…", null, (_, _) => ОткрытьНастройкиИнтеграции());
        сервис.DropDownItems.Add(new ToolStripSeparator());
        сервис.DropDownItems.Add("Подключение к базе данных…", null, (_, _) => ОткрытьНастройкиПодключения());
        сервис.DropDownItems.Add("Диагностика сервера базы данных…", null, (_, _) => ОткрытьДиагностикуСервера());
        сервис.DropDownItems.Add(new ToolStripSeparator());
        сервис.DropDownItems.Add("Найти это в интернете", null, (_, _) => ОткрытьПоискВИнтернете());
        сервис.DropDownItems.Add("Удалённый доступ (поддержка)…", null, (_, _) => ОткрытьУдалённыйДоступ());
        сервис.DropDownItems.Add("Проверить обновления", null, (_, _) => ПроверитьОбновления());
        сервис.DropDownItems.Add(new ToolStripSeparator());
        сервис.DropDownItems.Add("Калькулятор", null, (_, _) => ОткрытьКалькулятор());
        сервис.DropDownItems.Add("Календарь", null, (_, _) => ОткрытьКалендарь());
        сервис.DropDownItems.Add("Язык интерфейса…", null, (_, _) => ОткрытьВыборЯзыка());
        сервис.DropDownItems.Add("Регистрация программы…", null, (_, _) => ОткрытьРегистрацию());

        var печать = new ToolStripMenuItem("Печать");
        печать.DropDownItems.Add("Текущий конверт (F5)", null, (_, _) => ПечатьТекущийКонверт());
        печать.DropDownItems.Add("Выделенные с предпросмотром (F6)", null, (_, _) => ПечатьВыделенные(сПредпросмотром: true));
        печать.DropDownItems.Add("Выделенные без предпросмотра (F7)", null, (_, _) => ПечатьВыделенные(сПредпросмотром: false));
        печать.DropDownItems.Add(new ToolStripSeparator());
        печать.DropDownItems.Add("Настройки принтера…", null, (_, _) => НастройкиПринтера());
        печать.DropDownItems.Add(new ToolStripSeparator());

        _пунктСпискаШаблонов = new ToolStripMenuItem("Показывать список шаблонов перед печатью")
        {
            CheckOnClick = true,
            Checked = true,
        };

        _пунктСпискаШаблонов.CheckedChanged += async (_, _) =>
            await СохранитьОпциюСпискаШаблоновAsync(_пунктСпискаШаблонов.Checked).ConfigureAwait(true);

        печать.DropDownItems.Add(_пунктСпискаШаблонов);

        var рассылки = new ToolStripMenuItem("Рассылки");
        рассылки.DropDownItems.Add("Новая рассылка…", null, (_, _) => СоздатьРассылку());
        рассылки.DropDownItems.Add("Повторная отправка…", null, (_, _) => ПовторнаяОтправка());
        рассылки.DropDownItems.Add("Объединить выделенные в рассылку", null, (_, _) => ОбъединитьВРассылку());
        рассылки.DropDownItems.Add(new ToolStripSeparator());
        рассылки.DropDownItems.Add("Электронная рассылка…", null, (_, _) => ОткрытьЭлектроннуюРассылку());

        var справка = new ToolStripMenuItem("Справка");
        справка.DropDownItems.Add("Справка (F1)", null, (_, _) => ОткрытьСправку());

        _меню.Items.AddRange(
        [
            файл,
            правка,
            вид,
            данные,
            сервис,
            печать,
            рассылки,
            справка,
        ]);
    }

    /// <summary>
    /// Строит вкладки главного окна (ТЗ §8.12, функция 152).
    /// </summary>
    private void ПостроитьВкладки()
    {
        _вкладки.Dock = DockStyle.Fill;
        _вкладки.Font = new Font("Tahoma", 8f);

        _страницаАдреснойКниги.Controls.Add(ПостроитьРабочуюОбласть());
        _страницаКонвертов.Controls.Add(ПостроитьПанельТаблицы(_таблицаБланков));
        _страницаРассылок.Controls.Add(ПостроитьПанельТаблицы(_таблицаРассылок));
        _страницаОтправленных.Controls.Add(ПостроитьПанельОтправленных());

        _вкладки.TabPages.Add(_страницаАдреснойКниги);
        _вкладки.TabPages.Add(_страницаКонвертов);
        _вкладки.TabPages.Add(_страницаРассылок);
        _вкладки.TabPages.Add(_страницаОтправленных);

        _вкладки.SelectedIndexChanged += (_, _) =>
        {
            if (_состав is not null && _вкладки.SelectedIndex == 0)
            {
                ОбновитьСтрокуСостояния(_состав);
            }
        };
    }

    /// <summary>
    /// Строит рабочую область вкладки «Адресная книга» по схеме «3+2».
    /// </summary>
    private Control ПостроитьРабочуюОбласть()
    {
        var верх = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 175,
            ColumnCount = 3,
        };

        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        верх.Controls.Add(_адресОтправителя, 0, 0);
        верх.Controls.Add(_адресПолучателя, 1, 0);
        верх.Controls.Add(ПостроитьПанельОтображения(), 2, 0);

        // Подбор значения поля из имеющихся записей: пустое поле и BackSpace
        // (ТЗ §7.7; §8.1, функция 133).
        _адресОтправителя.ТребуетсяПодборЗначения += (_, _) => ПодобратьЗначениеПоля(_адресОтправителя);
        _адресПолучателя.ТребуетсяПодборЗначения += (_, _) => ПодобратьЗначениеПоля(_адресПолучателя);

        var панельПримечания = new Panel { Dock = DockStyle.Top, Height = 26 };

        var меткаПримечания = new Label
        {
            Text = "ПРИМЕЧАНИЕ:",
            AutoSize = true,
            Location = new Point(6, 6),
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        _примечание.Location = new Point(110, 3);
        _примечание.Width = 600;
        _примечание.Font = new Font("Tahoma", 8f);

        панельПримечания.Controls.Add(меткаПримечания);
        панельПримечания.Controls.Add(_примечание);

        var разделитель = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 330,
        };

        разделитель.Panel1.Controls.Add(ПостроитьПанельОтправителей());
        разделитель.Panel2.Controls.Add(ПостроитьПанельПолучателей());

        var рабочаяОбласть = new Panel { Dock = DockStyle.Fill };
        рабочаяОбласть.Controls.Add(разделитель);
        рабочаяОбласть.Controls.Add(панельПримечания);
        рабочаяОбласть.Controls.Add(верх);

        return рабочаяОбласть;
    }

    /// <summary>
    /// Строит панель «ОТОБРАЖЕНИЕ» с параметрами печати и превью конверта.
    /// </summary>
    private Control ПостроитьПанельОтображения()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        var заголовок = new Label
        {
            Text = "ОТОБРАЖЕНИЕ:",
            AutoSize = true,
            Location = new Point(4, 4),
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        _сЛоготипом.Location = new Point(14, 24);
        _сЛоготипом.AutoSize = true;
        _безЛоготипа.Location = new Point(14, 44);
        _безЛоготипа.AutoSize = true;
        _переключательАдресОтправителя.Location = new Point(14, 68);
        _адресОтправителя.AutoSize = true;
        _переключательАдресПолучателя.Location = new Point(14, 88);
        _адресПолучателя.AutoSize = true;

        _сЛоготипом.Checked = true;
        _переключательАдресОтправителя.Checked = true;

        _превью.BorderStyle = BorderStyle.FixedSingle;
        _превью.Size = new Size(260, 120);
        _превью.Location = new Point(10, 112);
        _превью.BackColor = SystemColors.Window;
        _превью.Paint += (_, событие) =>
        {
            using var шрифт = new Font("Tahoma", 8f);
            событие.Graphics.DrawString(
                "Превью конверта C5",
                шрифт,
                SystemBrushes.ControlText,
                new Point(8, 8));
        };

        панель.Controls.Add(заголовок);
        панель.Controls.Add(_сЛоготипом);
        панель.Controls.Add(_безЛоготипа);
        панель.Controls.Add(_переключательАдресОтправителя);
        панель.Controls.Add(_переключательАдресПолучателя);
        панель.Controls.Add(_превью);

        return панель;
    }

    /// <summary>
    /// Строит панель «СПРАВОЧНИК ОТПРАВИТЕЛЕЙ».
    /// </summary>
    private Control ПостроитьПанельОтправителей()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var добавить = Кнопка("+", "Добавить отправителя");
        добавить.Click += (_, _) => СоздатьОтправителя();

        var изменить = Кнопка("Edit", "Изменить отправителя");
        изменить.Click += (_, _) => ИзменитьОтправителя();

        var удалить = Кнопка("Del", "Удалить отправителя");
        удалить.Click += (_, _) => УдалитьВыделенные();

        кнопки.Controls.Add(добавить);
        кнопки.Controls.Add(изменить);
        кнопки.Controls.Add(удалить);

        var заголовок = new Label
        {
            Text = "СПРАВОЧНИК ОТПРАВИТЕЛЕЙ:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        _таблицаОтправителей.Columns.Add("Индекс", "Индекс");
        _таблицаОтправителей.Columns.Add("Наименование", "Наименование");
        _таблицаОтправителей.Columns[0].Width = 70;
        _таблицаОтправителей.Columns[1].Width = 220;
        _таблицаОтправителей.SelectionChanged += (_, _) => ОбновитьВыделенное();

        // Выбор отправителя по первым буквам наименования
        // (ТЗ §8.1, функция 16).
        var меткаПоиска = new Label
        {
            Text = "Быстрый выбор:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Height = 16,
            Font = new Font("Tahoma", 8f),
        };

        _поискОтправителя.Dock = DockStyle.Top;
        _поискОтправителя.Height = 20;
        _поискОтправителя.TextChanged += (_, _) => ВыбратьОтправителяПоПрефиксу();

        панель.Controls.Add(_таблицаОтправителей);
        панель.Controls.Add(_поискОтправителя);
        панель.Controls.Add(меткаПоиска);
        панель.Controls.Add(кнопки);
        панель.Controls.Add(заголовок);

        return панель;
    }

    /// <summary>
    /// Выбирает отправителя по первым буквам наименования и назначает его
    /// текущим (ТЗ §8.1, функция 16: «выбор отправителя по первым буквам»).
    /// </summary>
    private async void ВыбратьОтправителяПоПрефиксу()
    {
        if (_состав is null || !_инициализировано)
        {
            return;
        }

        var префикс = _поискОтправителя.Text;

        if (string.IsNullOrWhiteSpace(префикс))
        {
            return;
        }

        var отправители = _таблицаОтправителей.Rows
            .Cast<DataGridViewRow>()
            .Where(строка => строка.Tag is long)
            .Select(строка => ((long)строка.Tag!, (строка.Cells[1].Value as string) ?? string.Empty))
            .ToList();

        var найденный = SenderQuickSelect.FindByPrefix(отправители, префикс);

        if (найденный is null)
        {
            return;
        }

        foreach (DataGridViewRow строка in _таблицаОтправителей.Rows)
        {
            if (строка.Tag is long идентификатор && идентификатор == найденный.Value)
            {
                строка.Selected = true;
                _состав.ТекущийОтправитель.SetGlobalSender(идентификатор);

                await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);

                break;
            }
        }
    }

    /// <summary>
    /// Строит панель «АДРЕСНАЯ КНИГА / ПОЛУЧАТЕЛИ».
    /// </summary>
    private Control ПостроитьПанельПолучателей()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var добавить = Кнопка("+", "Добавить получателя");
        добавить.Click += (_, _) => ДобавитьПолучателя();

        var изменить = Кнопка("Edit", "Изменить получателя");
        изменить.Click += (_, _) => ИзменитьПолучателя();

        var удалить = Кнопка("Del", "Удалить получателя");
        удалить.Click += (_, _) => УдалитьВыделенные();

        var печать = Кнопка("F5 Печать", "Напечатать текущий конверт (F5)");
        печать.Click += (_, _) => ПечатьТекущийКонверт();

        var превью = Кнопка("F6 Превью", "Печать выделенных с предпросмотром (F6)");
        превью.Click += (_, _) => ПечатьВыделенные(сПредпросмотром: true);

        var настройки = Кнопка("⚙", "Настройка таблицы");
        настройки.Click += (_, _) => НастроитьТаблицу();

        var вExcel = Кнопка("Открыть список в Excel", "Выгрузить текущую таблицу получателей в Excel (XLSX)");
        вExcel.Click += (_, _) => ОткрытьСписокВExcel();

        кнопки.Controls.Add(добавить);
        кнопки.Controls.Add(изменить);
        кнопки.Controls.Add(удалить);
        кнопки.Controls.Add(печать);
        кнопки.Controls.Add(превью);
        кнопки.Controls.Add(вExcel);
        кнопки.Controls.Add(настройки);

        var заголовок = new Label
        {
            Text = "АДРЕСНАЯ КНИГА / ПОЛУЧАТЕЛИ:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        _таблицаПолучателей.Columns.Add("VIP", "VIP");
        _таблицаПолучателей.Columns.Add("Индекс", "Индекс");
        _таблицаПолучателей.Columns.Add("Получатель", "Получатель");
        _таблицаПолучателей.Columns.Add("Город", "Город");
        _таблицаПолучателей.Columns.Add("Улица", "Улица");
        _таблицаПолучателей.Columns.Add("Копий", "Копий");
        _таблицаПолучателей.Columns.Add("Группа", "Группа");
        _таблицаПолучателей.Columns[0].Width = 34;
        _таблицаПолучателей.Columns[1].Width = 70;
        _таблицаПолучателей.Columns[2].Width = 200;
        _таблицаПолучателей.Columns[3].Width = 110;
        _таблицаПолучателей.Columns[4].Width = 110;
        _таблицаПолучателей.Columns[5].Width = 45;
        _таблицаПолучателей.Columns[6].Width = 110;
        _таблицаПолучателей.MouseDown += ОбработатьНажатиеЗаголовка;
        _таблицаПолучателей.SelectionChanged += (_, _) => ОбновитьВыделенное();
        _таблицаПолучателей.CellDoubleClick += (_, _) => ИзменитьПолучателя();
        _таблицаПолучателей.KeyDown += ОбработатьНаборВТаблице;

        var контекстноеМеню = new ContextMenuStrip { Font = new Font("Tahoma", 8f) };
        _пунктИзбранного.Checked = false;
        _пунктИзбранного.Click += (_, _) => ПереключитьФильтрИзбранного();
        _пунктОбъединить.Click += (_, _) => ОбъединитьВРассылку();
        _пунктПереместить.Click += (_, _) => ПереместитьВКатегорию();
        _пунктПометитьОтправителем.Click += (_, _) => ПометитьОтправителем();

        контекстноеМеню.Items.Add(_пунктИзбранного);
        контекстноеМеню.Items.Add(_пунктОбъединить);
        контекстноеМеню.Items.Add(_пунктПереместить);
        контекстноеМеню.Items.Add(_пунктПометитьОтправителем);

        _таблицаПолучателей.ContextMenuStrip = контекстноеМеню;

        панель.Controls.Add(_таблицаПолучателей);
        панель.Controls.Add(кнопки);
        панель.Controls.Add(заголовок);

        return панель;
    }

    /// <summary>
    /// Строит панель вкладки «Отправленные»: дерево папок писем и таблица
    /// отправлений (ТЗ §8.1, функция 22: «Папки для писем»).
    /// </summary>
    private Control ПостроитьПанельОтправленных()
    {
        var разделитель = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 220,
        };

        _деревоПапок.Dock = DockStyle.Fill;
        _деревоПапок.Font = new Font("Tahoma", 8f);
        _деревоПапок.AllowDrop = true;
        _деревоПапок.DragEnter += (_, аргументы) =>
        {
            аргументы.Effect = аргументы.Data?.GetDataPresent(typeof(long)) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };
        _деревоПапок.DragDrop += РазместитьПисьмоВПапке;

        var заголовок = new Label
        {
            Text = "ПАПКИ ПИСЕМ:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        var панельПапок = new Panel { Dock = DockStyle.Fill };
        панельПапок.Controls.Add(_деревоПапок);
        панельПапок.Controls.Add(заголовок);

        разделитель.Panel1.Controls.Add(панельПапок);
        разделитель.Panel2.Controls.Add(ПостроитьПанельТаблицы(_таблицаОтправленных));

        return разделитель;
    }

    /// <summary>
    /// Начинает перетаскивание выделенного письма в папку
    /// (ТЗ §8.1, функция 22: «Перетаскивание письма в папку изменяет ее счетчик
    /// вложенности»).
    /// </summary>
    private void ОтправитьПеретаскиваниеПисьма(object? отправитель, MouseEventArgs аргументы)
    {
        if (аргументы.Button != MouseButtons.Left || _таблицаОтправленных.CurrentRow?.Tag is not long)
        {
            return;
        }

        var идентификатор = (long)_таблицаОтправленных.CurrentRow.Tag;

        _таблицаОтправленных.DoDragDrop(идентификатор, DragDropEffects.Copy);
    }

    /// <summary>
    /// Помещает перетащенное письмо в папку и пересчитывает счётчик вложенности.
    /// </summary>
    private async void РазместитьПисьмоВПапке(object? отправитель, DragEventArgs аргументы)
    {
        if (_состав is null || аргументы.Data?.GetData(typeof(long)) is not long идентификаторПисьма)
        {
            return;
        }

        var узел = _деревоПапок.SelectedNode;

        if (узел?.Tag is not long идентификаторПапки)
        {
            return;
        }

        try
        {
            var письмо = await _состав.Письма.GetByIdAsync(идентификаторПисьма).ConfigureAwait(true);

            if (письмо is null)
            {
                return;
            }

            письмо.FolderId = идентификаторПапки;

            await _состав.Письма.UpdateAsync(письмо).ConfigureAwait(true);

            await ЗагрузитьПапкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
            await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось поместить письмо в папку: {исключение.Message}",
                "Папки писем",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Загружает дерево папок писем со счётчиками вложенности.
    /// </summary>
    private async Task ЗагрузитьПапкиAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var папки = await состав.Папки.GetAllAsync(cancellationToken).ConfigureAwait(true);
        var письма = await состав.Письма
            .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 200), cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        var счётчики = письма.Items
            .Where(письмо => письмо.FolderId is not null)
            .GroupBy(письмо => письмо.FolderId!.Value)
            .ToDictionary(группа => группа.Key, группа => группа.Count());

        _деревоПапок.Nodes.Clear();

        foreach (var папка in папки.Where(сущ => сущ.ParentId is null))
        {
            var узел = _деревоПапок.Nodes.Add(ОбозначитьПапку(папка, счётчики));

            узел.Tag = папка.Id;

            foreach (var дочерняя in папки.Where(сущ => сущ.ParentId == папка.Id))
            {
                var дочернийУзел = узел.Nodes.Add(ОбозначитьПапку(дочерняя, счётчики));

                дочернийУзел.Tag = дочерняя.Id;
            }
        }

        _деревоПапок.ExpandAll();
    }

    /// <summary>
    /// Формирует подпись узла дерева папок: наименование и счётчик вложенности.
    /// </summary>
    private static string ОбозначитьПапку(Folder папка, IReadOnlyDictionary<long, int> счётчики)
    {
        счётчики.TryGetValue(папка.Id, out var число);

        return $"{папка.Name} ({число})";
    }

    /// <summary>
    /// Строит панель с таблицей для вкладок «Конверты», «Рассылки» и
    /// «Отправленные».
    /// </summary>
    private static Control ПостроитьПанельТаблицы(DataGridView таблица)
    {
        var панель = new Panel { Dock = DockStyle.Fill };
        таблица.Dock = DockStyle.Fill;
        панель.Controls.Add(таблица);
        return панель;
    }

    /// <summary>
    /// Строит строку состояния: «Всего», «Выбрано», «Выведено», «Копий» и
    /// состояние базы (ТЗ §7.1).
    /// </summary>
    private void ПостроитьСтрокуСостояния()
    {
        _строкаСостояния.Dock = DockStyle.Bottom;
        _строкаСостояния.Height = 22;

        void Настроить(Label метка, string текст, int позиция)
        {
            метка.Text = текст;
            метка.AutoSize = true;
            метка.Location = new Point(позиция, 4);
            метка.Font = new Font("Tahoma", 8f);
        }

        Настроить(_всего, "Всего: 0", 8);
        Настроить(_выбрано, "Выбрано: 0", 110);
        Настроить(_выведено, "Выведено: 0", 220);
        Настроить(_копий, "Копий: 0", 330);
        Настроить(_состояниеБазы, "База: не подключена", 460);
        Настроить(_состояниеПечати, string.Empty, 620);
        Настроить(_лицензия, string.Empty, 780);

        _строкаСостояния.Controls.Add(_всего);
        _строкаСостояния.Controls.Add(_выбрано);
        _строкаСостояния.Controls.Add(_выведено);
        _строкаСостояния.Controls.Add(_копий);
        _строкаСостояния.Controls.Add(_состояниеБазы);
        _строкаСостояния.Controls.Add(_состояниеПечати);
        _строкаСостояния.Controls.Add(_лицензия);
    }

    /// <summary>
    /// Выгружает текущую таблицу получателей в книгу Excel и открывает файл
    /// в приложении по умолчанию (ТЗ §8.5, функция 61: «Прямой экспорт текущей
    /// таблицы в MS Excel»).
    /// </summary>
    /// <remarks>
    /// Файл формируется как пакет OpenXML без вызовов OLE/COM Excel, поэтому
    /// Microsoft Office на машине оператора не требуется. Если файл занят другой
    /// программой, оператору предлагается альтернативное имя.
    /// </remarks>
    private void ОткрытьСписокВExcel()
    {
        using var диалог = new SaveFileDialog
        {
            Title = "Открыть список в Excel",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
            FileName = $"Адресная книга_{DateTime.Now:yyyy-MM-dd}.xlsx",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var путь = диалог.FileName;
        var экспортёр = new SpreadsheetMlExporter();

        try
        {
            экспортёр.ExportGridToExcel(_таблицаПолучателей, путь);
        }
        catch (IOException)
        {
            var альтернатива = Picket302.Application.Services.ExportWizard.SuggestAvailableFilePath(путь);
            var ответ = MessageBox.Show(
                this,
                $"Файл «{путь}» занят другой программой.\nСохранить список под другим именем?\n{альтернатива}",
                "Открыть список в Excel",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (ответ != DialogResult.Yes)
            {
                return;
            }

            путь = альтернатива;

            try
            {
                экспортёр.ExportGridToExcel(_таблицаПолучателей, путь);
            }
            catch (IOException ошибка)
            {
                MessageBox.Show(
                    this,
                    $"Не удалось сохранить файл: {ошибка.Message}",
                    "Открыть список в Excel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        экспортёр.OpenInAssociatedApplication(путь);
    }

    /// <summary>
    /// Выгружает адресную книгу через мастер выгрузки (ТЗ §8.12, функция 160:
    /// «Экспорт с настройками и пост-действиями»).
    /// </summary>
    private async void ВыгрузитьДанные()
    {
        if (_состав is null)
        {
            return;
        }

        try
        {
            var контрагенты = await СобратьКонтрагентовAsync().ConfigureAwait(true);
            using var форма = new FormExportWizard(контрагенты);
            форма.ShowDialog(this);
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось выгрузить данные: {ошибка.Message}",
                "Выгрузка данных",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Собирает все записи адресной книги постранично.
    /// </summary>
    /// <returns>Список контрагентов.</returns>
    private async Task<IReadOnlyList<Counterparty>> СобратьКонтрагентовAsync()
    {
        if (_состав is null)
        {
            return Array.Empty<Counterparty>();
        }

        var контрагенты = new List<Counterparty>();
        var смещение = 0;

        while (true)
        {
            var страница = await _состав.Контрагенты
                .GetPagedAsync(new PageRequest(смещение, PageRequest.MaxLimit))
                .ConfigureAwait(true);

            контрагенты.AddRange(страница.Items);

            if (страница.Items.Count == 0 || контрагенты.Count >= страница.TotalCount)
            {
                break;
            }

            смещение += страница.Items.Count;
        }

        return контрагенты;
    }

    /// <summary>
    /// Открывает мастер шаблонов документов (ТЗ §8.5, функция 66).
    /// </summary>
    private void ОткрытьМастерШаблонов()
    {
        using var форма = new FormDocTemplateWizard();
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Формирует документ произвольного вида по шаблону (ТЗ §8.5, функция 65).
    /// </summary>
    private void СформироватьДокумент()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Шаблон документа",
            Filter = "Шаблоны документов (*.txt;*.html;*.htm;*.xml;*.xslt;*.docx)|*.txt;*.html;*.htm;*.xml;*.xslt;*.docx|Все файлы (*.*)|*.*",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var форма = new FormDocTemplateWizard(диалог.FileName);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает настройки интеграции: регистрация COM-сервера автоматизации
    /// и настройки печати конвертов (ТЗ §9.3, функция 114).
    /// </summary>
    private void ОткрытьНастройкиИнтеграции()
    {
        using var форма = new FormIntegrationSettings();
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает диалог архивирования и восстановления базы адресов
    /// (ТЗ §8.11, функции 128 и 129; §10.4 и §10.5).
    /// </summary>
    private void ОткрытьАрхивацию()
    {
        using var форма = new FormBackupRestore(ПутьБазыПрограммы());
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает мастер установки на USB-флешку (ТЗ §8.8, функция 105;
    /// §8.11, функция 130).
    /// </summary>
    private void ОткрытьУстановкуНаUsb()
    {
        using var форма = new FormUsbInstallWizard(ПутьБазыПрограммы());
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает окно «Журнал ошибок» (ТЗ §8.11, функция 111; §10.3).
    /// </summary>
    private void ОткрытьЖурналОшибок()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormErrorLog(_состав.Журнал);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает мастер загрузки данных из прежней установки
    /// (ТЗ §8.8, функция 107; §9.8).
    /// </summary>
    private void ОткрытьЗагрузкуИзСтаройВерсии()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormLegacyUpgradeWizard(new LegacyUpgradeImporter(_состав.Сессия));
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает диалог «Подключение к базе данных» (ТЗ §8.8, функции 101 и 102;
    /// §8.11, функция 134).
    /// </summary>
    private void ОткрытьНастройкиПодключения()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormConnectionSettings(_состав.МенеджерПодключения, _состав.РеестрСерверов);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает другую базу данных по выбору оператора (ТЗ §8.8, функция 102:
    /// «Открыть базу…»). Выбран полный цикл переподключения в рамках живой
    /// формы: диалог подключения, освобождение состава, созданного оболочкой,
    /// и повторная инициализация. Состав передаётся по пути из диалога, поэтому
    /// перезапуск приложения не требуется (F-E3: прежде пункт открывал форму
    /// очистки базы).
    /// </summary>
    private async void ОткрытьБазу()
    {
        // Диалог создаётся с независимым менеджером подключения, как при
        // запуске приложения (Program.Main): выбранные реквизиты не должны
        // зависеть от текущей сессии.
        using var диалог = new FormConnectionSettings(new ConnectionManager(), new ServerRegistry());

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var дескриптор = диалог.Descriptor;

        // Состав приложения работает с файлом SQLite; серверные реквизиты
        // из диалога не открываются в живой форме.
        if (!дескриптор.IsLocal || string.IsNullOrWhiteSpace(дескриптор.DatabasePath))
        {
            MessageBox.Show(
                this,
                "Открытие в работающем окне возможно только для файла базы данных "
                + "на этом компьютере. Серверные реквизиты применяются при перезапуске.",
                "Открыть базу…",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Состав, созданный оболочкой, освобождается перед подключением
        // к другой базе; переданный снаружи остаётся во владении вызывающего.
        if (_составСозданоВнутри && _состав is not null)
        {
            await _состав.DisposeAsync().ConfigureAwait(true);
            _состав = null;
        }

        _инициализировано = false;

        try
        {
            await ИнициализироватьAsync(дескриптор.DatabasePath).ConfigureAwait(true);
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                ошибка.Message,
                "Открытие базы данных",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Открывает диагностику службы СУБД: обнаружение остатков прежней
    /// установки и предложение их корректного удаления (ТЗ §8.11, функция 136).
    /// </summary>
    private void ОткрытьДиагностикуСервера()
    {
        using var форма = new FormDbServiceDoctor(new DbServiceDoctor());
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает поиск адреса по ИНН/УНН в интернете (ТЗ §8.8, функция 108).
    /// </summary>
    private async void ОткрытьПоискВИнтернете()
    {
        var инн = await ВыделитьИдентификаторНалогоплательщикаAsync().ConfigureAwait(true);

        if (инн.Length == 0)
        {
            MessageBox.Show(
                this,
                "Выберите запись, у которой заполнен ИНН или УНН.",
                "Найти это в интернете",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var запускатель = new WebLookupLauncher();

        запускатель.OpenWebLookup(инн);
    }

    /// <summary>
    /// Запускает утилиту удалённого доступа службы поддержки
    /// (ТЗ §8.8, функция 109).
    /// </summary>
    private void ОткрытьУдалённыйДоступ()
    {
        var запускатель = new RemoteAssistantLauncher();

        запускатель.LaunchOrUpdateRemoteAssistant();
    }

    /// <summary>
    /// Проверяет наличие обновлений (ТЗ §8.12, функция 163).
    /// </summary>
    private async void ПроверитьОбновления()
    {
        if (_состав is null)
        {
            return;
        }

        var проверка = new UpdateChecker(
            "https://updates.picket302.local/updates.txt",
            "1.0.0",
            сообщение => _состав.Журнал.LogError(new InvalidOperationException(сообщение)));

        var сведения = await проверка.CheckForUpdatesAsync().ConfigureAwait(true);

        MessageBox.Show(
            this,
            сведения.IsUpdateAvailable
                ? $"Доступна версия {сведения.Version}.{Environment.NewLine}{сведения.Notes}"
                : "Обновлений не обнаружено.",
            "Проверка обновлений",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Открывает встроенный калькулятор (ТЗ §8.12, функция 163).
    /// </summary>
    private void ОткрытьКалькулятор()
    {
        using var форма = new FormCalculator();
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает всплывающий календарь (ТЗ §8.12, функция 163).
    /// </summary>
    private void ОткрытьКалендарь()
    {
        var календарь = new PopupCalendar();

        календарь.ShowPopup(_состояниеБазы);
    }

    /// <summary>
    /// Открывает диалог выбора языка интерфейса (ТЗ §8.11, функция 112).
    /// </summary>
    private void ОткрытьВыборЯзыка()
    {
        if (_состав is null)
        {
            return;
        }

        var языки = _состав.МенеджерЯзыка.AvailableLanguages();

        if (языки.Count == 0)
        {
            MessageBox.Show(
                this,
                "Языковые файлы не найдены: проверьте наличие каталога lang рядом с программой.",
                "Язык интерфейса",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var форма = new FormLanguageSelection(_состав.МенеджерЯзыка);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает окно регистрации программы (ТЗ §8.10, функции 122 и 123;
    /// §8.12, функция 162).
    /// </summary>
    private void ОткрытьРегистрацию()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormLicense(
            _состав.МенеджерЛицензии,
            _состав.АвтоматЛицензии,
            _состав.АктивацияЛицензии,
            _состав.Машина);

        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает мастер электронной рассылки (ТЗ §8.7, функции 92–100).
    /// </summary>
    /// <remarks>
    /// Электронная рассылка — отдельный канал отправки: она не создаёт записей
    /// таблицы <c>mails</c> (ТЗ §8.7, функция 92).
    /// </remarks>
    private async void ОткрытьЭлектроннуюРассылку()
    {
        if (_состав is null)
        {
            return;
        }

        var опции = new SmtpOptions();
        var настройки = опции.Load();
        var прокси = опции.LoadProxy();

        var писатель = new SqliteEmailLogWriter(_состав.Сессия);
        var журнал = new EmailLogWriterAdapter(писатель);

        var клиент = new SmtpClientFacade(настройки, прокси, опции);

        var маркеры = new EmailMarkerResolver();

        var служба = new EmailDistributionService(
            (письмо, токен) => Task.FromResult(клиент.SendOne(письмо, токен)),
            журнал,
            маркеры);

        var получатели = await СобратьПолучателейДляРассылкиAsync().ConfigureAwait(true);

        using var форма = new FormEmailDistributionWizard(
            new EmailDistributionWizardController(служба, маркеры, получатели));

        форма.ShowDialog(this);
    }

    /// <summary>
    /// Собирает выборку получателей для электронной рассылки: адрес берётся
    /// из карточки контрагента, получатели без адреса исключаются из отправки,
    /// но перечисляются в отчёте (ТЗ §8.7, функция 92).
    /// </summary>
    /// <returns>Перечень получателей рассылки.</returns>
    private async Task<IReadOnlyList<EmailRecipient>> СобратьПолучателейДляРассылкиAsync()
    {
        var получатели = new List<EmailRecipient>();

        if (_состав is null)
        {
            return получатели;
        }

        foreach (var идентификатор in _выбранныеПолучатели)
        {
            var контрагент = await _состав.Контрагенты
                .GetByIdAsync(идентификатор)
                .ConfigureAwait(true);

            if (контрагент is null)
            {
                continue;
            }

            получатели.Add(new EmailRecipient(
                контрагент.Id,
                контрагент.Name,
                контрагент.Email ?? string.Empty));
        }

        return получатели;
    }

    /// <summary>
    /// Возвращает путь к файлу базы данных программы.
    /// </summary>
    /// <returns>Путь к файлу базы данных.</returns>
    private string ПутьБазыПрограммы()
        => _состав?.ПутьБазы ?? AppServices.ПутьБазыПоУмолчанию();

    /// <summary>
    /// Возвращает ИНН или УНН выделенной записи адресной книги.
    /// </summary>
    /// <returns>Идентификатор налогоплательщика; пустая строка — не заполнен.</returns>
    private async Task<string> ВыделитьИдентификаторНалогоплательщикаAsync()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return string.Empty;
        }

        var получатель = await _состав.Контрагенты
            .GetByIdAsync(_выбранныеПолучатели[0])
            .ConfigureAwait(true);

        return получатель?.Inn ?? string.Empty;
    }

    /// <summary>
    /// Импортирует контрагентов из выгрузки 1С:Предприятие
    /// (ТЗ §8.5, функция 72: «Импорт контрагентов из выгрузок 1С:Предприятие
    /// 7.7 и 8.x»).
    /// </summary>
    private void ИмпортИз1С()
    {
        if (_состав is null)
        {
            return;
        }

        using var диалог = new OpenFileDialog
        {
            Title = "Выгрузка 1С",
            Filter = "Выгрузка 1С (*.xml;*.txt)|*.xml;*.txt|Все файлы (*.*)|*.*",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var версия = ОпределитьВерсию1С(диалог.FileName);
            var разбор = new Picket302.Application.Services.OneCImportParser();
            var пакет = разбор.ImportFromOneC(диалог.FileName, версия);

            var сервис = new Picket302.Application.Services.ImportMergeService(_состав.Контрагенты);
            var отчёт = сервис.MergeImported(пакет.Batch, MergePolicy.SkipExisting);

            var сообщение =
                $"Прочитано записей: {пакет.RecordCount}.\n" +
                $"Создано: {отчёт.Created}, обновлено: {отчёт.Updated}, " +
                $"пропущено: {отчёт.Skipped}, конфликтов: {отчёт.Conflicts}.";

            if (пакет.AllWarnings.Count > 0)
            {
                сообщение += "\n\nПредупреждения:\n" +
                    string.Join("\n", пакет.AllWarnings.Take(10));
            }

            MessageBox.Show(this, сообщение, "Импорт из 1С", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ОбновитьВсе();
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось импортировать выгрузку 1С: {ошибка.Message}",
                "Импорт из 1С",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Определяет версию выгрузки 1С по первому значащему символу файла.
    /// </summary>
    /// <param name="путь">Путь к файлу выгрузки.</param>
    /// <returns>Версия выгрузки 1С.</returns>
    private static Picket302.Application.Import.OneCVersion ОпределитьВерсию1С(string путь)
    {
        var начало = new byte[16];
        var прочитано = 0;

        using (var поток = File.OpenRead(путь))
        {
            прочитано = поток.Read(начало, 0, начало.Length);
        }

        var началоТекста = System.Text.Encoding.UTF8.GetString(начало, 0, прочитано).TrimStart();
        return началоТекста.StartsWith('<') || началоТекста.StartsWith("﻿<", StringComparison.Ordinal)
            ? Picket302.Application.Import.OneCVersion.V8x
            : Picket302.Application.Import.OneCVersion.V77;
    }

    /// <summary>
    /// Открывает мастер импорта адресной книги (ТЗ §8.12, функция 159:
    /// «Мастер импорта адресной книги (7 экранов)»).
    /// </summary>
    private void ОткрытьМастерИмпорта()
    {
        if (_состав is null)
        {
            return;
        }

        var контроллер = new ImportWizardController(_состав.Контрагенты, _состав.Группы);

        using var форма = new FormImportWizard(контроллер);

        if (форма.ShowDialog(this) == DialogResult.OK)
        {
            ОбновитьВсе();
        }
    }

    /// <summary>
    /// Импортирует контрагентов из внешней базы данных (ТЗ §8.5, функция 76).
    /// </summary>
    private void ИмпортИзВнешнейБазы()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormExternalDbImport(_состав.Контрагенты);

        if (форма.ShowDialog(this) == DialogResult.OK)
        {
            ОбновитьВсе();
        }
    }

    /// <summary>
    /// Создаёт кнопку панели инструментов таблицы.
    /// </summary>
    private static Button Кнопка(string текст, string подсказка) => new()
    {
        Text = текст,
        AutoSize = true,
        Height = 24,
        FlatStyle = FlatStyle.System,
        Font = new Font("Tahoma", 8f),
    };

    /// <summary>
    /// Загружает справочник отправителей.
    /// </summary>
    private async Task ЗагрузитьОтправителейAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var отправители = await состав.Контрагенты.GetSendersAsync(cancellationToken)
            .ConfigureAwait(true);

        _таблицаОтправителей.Rows.Clear();

        foreach (var отправитель in отправители)
        {
            var адрес = отправитель.GetAddress(отправитель.AddressMainType);

            var индекс = _таблицаОтправителей.Rows.Add(
                адрес?.Zip ?? string.Empty,
                отправитель.Name);

            _таблицаОтправителей.Rows[индекс].Tag = отправитель.Id;
        }

        var текущий = состав.ТекущийОтправитель.CurrentSenderId;

        if (текущий is not null)
        {
            var отправитель = отправители.FirstOrDefault(сущ => сущ.Id == текущий.Value);

            if (отправитель is not null)
            {
                _адресОтправителя.УстановитьАдрес(отправитель.GetAddress(отправитель.AddressMainType));
            }
        }
        else
        {
            // Сохранённый отправитель отсутствует — контекст сбрасывается на
            // первую запись справочника (ТЗ §8.1, функция 16).
            var восстановленный = состав.ТекущийОтправитель
                .Resolve(отправители.Select(сущ => сущ.Id).ToList());

            if (восстановленный is not null)
            {
                var отправитель = отправители.First(сущ => сущ.Id == восстановленный.Value);
                _адресОтправителя.УстановитьАдрес(отправитель.GetAddress(отправитель.AddressMainType));
            }
        }
    }

    /// <summary>
    /// Загружает адресную книгу — получателей.
    /// </summary>
    private async Task ЗагрузитьПолучателейAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var фильтр = new Picket302.Domain.Repositories.CounterpartyFilter(
            NameFragment: string.IsNullOrWhiteSpace(_поиск.ПрямойНабор) ? null : _поиск.ПрямойНабор,
            IsVip: _поиск.ТолькоИзбранное ? true : null,
            GroupId: _поиск.Группа,
            Criteria: ТекущиеКритерииФильтра());

        var страница = await состав.Контрагенты
            .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 500), фильтр, cancellationToken)
            .ConfigureAwait(true);

        _таблицаПолучателей.Rows.Clear();
        _текущиеПолучатели.Clear();

        var группы = await состав.Группы.GetAllAsync(cancellationToken).ConfigureAwait(true);

        foreach (var получатель in страница.Items)
        {
            var адрес = получатель.GetAddress(получатель.AddressMainType);
            var группа = группы.FirstOrDefault(сущ => сущ.Id == получатель.GroupId);

            var индекс = _таблицаПолучателей.Rows.Add(
                получатель.IsVip ? "[*]" : "[ ]",
                адрес?.Zip ?? string.Empty,
                получатель.Name,
                адрес?.Lines.ElementAtOrDefault(4) ?? string.Empty,
                адрес?.Lines.ElementAtOrDefault(1) ?? string.Empty,
                1,
                группа?.Name ?? string.Empty);

            _таблицаПолучателей.Rows[индекс].Tag = получатель.Id;
            _текущиеПолучатели.Add(new ЗаписьПолучателя(получатель.Id, получатель.Name));
        }

        _всего.Text = $"Всего: {страница.TotalCount}";
        ОбновитьСтрокуСостояния(состав);
    }

    /// <summary>
    /// Загружает дерево категорий.
    /// </summary>
    private async Task ЗагрузитьКатегорииAsync(AppServices состав, CancellationToken cancellationToken)
    {
        _деревоКатегорий.Nodes.Clear();

        var категории = await состав.СлужбаКатегорий.GetTreeAsync(cancellationToken)
            .ConfigureAwait(true);

        foreach (var категория in категории.Where(сущ => сущ.ParentId is null))
        {
            var узел = _деревоКатегорий.Nodes.Add(
                $"{категория.Name} ({категория.CounterpartyCount})");

            узел.Tag = категория.Id;

            foreach (var дочерняя in категории.Where(сущ => сущ.ParentId == категория.Id))
            {
                var дочернийУзел = узел.Nodes.Add(
                    $"{дочерняя.Name} ({дочерняя.CounterpartyCount})");

                дочернийУзел.Tag = дочерняя.Id;
            }
        }

        _деревоКатегорий.ExpandAll();
        _деревоКатегорий.AfterSelect += (_, событие) =>
        {
            if (событие.Node?.Tag is not long идентификатор)
            {
                return;
            }

            _поиск.ЗадатьФильтрГрупп(идентификатор);
            _ = ОбновитьПолучателейAsync();
        };
    }

    /// <summary>
    /// Загружает каталог бланков для вкладки «Конверты».
    /// </summary>
    private async Task ЗагрузитьБланкиAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var шаблоны = await new Picket302.Infrastructure.Repositories.PrintTemplateRepository(
            состав.Сессия).GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(true);

        _таблицаБланков.Rows.Clear();
        _таблицаБланков.Columns.Clear();
        _таблицаБланков.Columns.Add("Наименование", "Наименование");
        _таблицаБланков.Columns.Add("Формат", "Формат");
        _таблицаБланков.Columns.Add("Размер", "Размер");
        _таблицаБланков.Columns.Add("Вид", "Вид");

        foreach (var шаблон in шаблоны)
        {
            _таблицаБланков.Rows.Add(
                шаблон.Name,
                шаблон.Format,
                $"{шаблон.WidthMm}×{шаблон.HeightMm}",
                шаблон.Kind);
        }
    }

    /// <summary>
    /// Загружает список рассылок для вкладки «Рассылки».
    /// </summary>
    private async Task ЗагрузитьРассылкиAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var рассылки = await состав.СлужбаРассылок.ListDistributionsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        _таблицаРассылок.Rows.Clear();
        _таблицаРассылок.Columns.Clear();
        _таблицаРассылок.Columns.Add("Наименование", "Наименование");
        _таблицаРассылок.Columns.Add("Вид", "Вид");
        _таблицаРассылок.Columns.Add("Состояние", "Состояние");
        _таблицаРассылок.Columns.Add("Писем", "Писем");
        _таблицаРассылок.Columns.Add("Конвертов", "Конвертов");
        _таблицаРассылок.Columns.Add("Создана", "Создана");

        foreach (var рассылка in рассылки)
        {
            var индекс = _таблицаРассылок.Rows.Add(
                рассылка.Name,
                рассылка.Kind == DistributionKind.ManyToOne ? "многие к одному" : "один ко многим",
                рассылка.State.ToString(),
                рассылка.RecipientCount,
                рассылка.EnvelopesRequired ?? 0,
                рассылка.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

            _таблицаРассылок.Rows[индекс].Tag = рассылка.Id;
        }
    }

    /// <summary>
    /// Загружает отправленные письма для вкладки «Отправленные».
    /// </summary>
    private async Task ЗагрузитьОтправленныеAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var страница = await состав.Письма
            .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 500), cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        _таблицаОтправленных.Rows.Clear();
        _таблицаОтправленных.Columns.Clear();
        _таблицаОтправленных.Columns.Add("Получатель", "Получатель");
        _таблицаОтправленных.Columns.Add("Индекс", "Индекс");
        _таблицаОтправленных.Columns.Add("Адрес", "Адрес");
        _таблицаОтправленных.Columns.Add("Статус", "Статус");
        _таблицаОтправленных.Columns.Add("Создано", "Создано");
        _таблицаОтправленных.Columns.Add("Напечатано", "Напечатано");
        _таблицаОтправленных.Columns.Add("Отправлено", "Отправлено");
        _таблицаОтправленных.MouseDown += ОтправитьПеретаскиваниеПисьма;

        foreach (var письмо in страница.Items)
        {
            var индекс = _таблицаОтправленных.Rows.Add(
                письмо.CounterpartyId.ToString(CultureInfo.InvariantCulture),
                письмо.RecipientAddress?.Zip ?? string.Empty,
                письмо.RecipientAddress?.Line1 ?? string.Empty,
                письмо.Status.ToString(),
                письмо.DateCreated.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                письмо.DatePrinted?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                письмо.DateDispatched?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty);

            _таблицаОтправленных.Rows[индекс].Tag = письмо.Id;
        }
    }

    /// <summary>
    /// Обновляет счётчики строки состояния по текущему выделению.
    /// </summary>
    private void ОбновитьВыделенное()
    {
        _выбранныеОтправители.Clear();
        _выбранныеПолучатели.Clear();

        foreach (DataGridViewRow строка in _таблицаОтправителей.SelectedRows)
        {
            if (строка.Tag is long идентификатор)
            {
                _выбранныеОтправители.Add(идентификатор);
            }
        }

        foreach (DataGridViewRow строка in _таблицаПолучателей.SelectedRows)
        {
            if (строка.Tag is long идентификатор)
            {
                _выбранныеПолучатели.Add(идентификатор);
            }
        }

        _выбрано.Text = $"Выбрано: {_выбранныеПолучатели.Count}";
    }

    /// <summary>
    /// Обновляет строку состояния: «Выведено» и «Копий» считаются по письмам
    /// выбранных получателей.
    /// </summary>
    private async void ОбновитьСтрокуСостояния(AppServices состав)
    {
        if (!_инициализировано || _выбранныеПолучатели.Count == 0)
        {
            _выведено.Text = "Выведено: 0";
            _копий.Text = "Копий: 0";
            _состояниеБазы.Text = $"База: {Path.GetFileName(состав.ПутьБазы)}";
            return;
        }

        try
        {
            var страница = await состав.Письма
                .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 500))
                .ConfigureAwait(true);

            var выведено = 0;
            var копий = 0;

            foreach (var письмо in страница.Items)
            {
                if (!_выбранныеПолучатели.Contains(письмо.CounterpartyId))
                {
                    continue;
                }

                выведено++;
                копий += письмо.CopyCount;
            }

            _выведено.Text = $"Выведено: {выведено}";
            _копий.Text = $"Копий: {копий}";
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            _выведено.Text = "Выведено: 0";
            _копий.Text = "Копий: 0";
        }

        _состояниеБазы.Text = $"База: {Path.GetFileName(состав.ПутьБазы)}";
    }

    /// <summary>
    /// Обновляет индикатор срока пробного периода в строке состояния
    /// (ТЗ §8.10, функция 119: «индикатор остатка дней»).
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    public void ОбновитьИндикаторЛицензии(AppServices состав)
    {
        ArgumentNullException.ThrowIfNull(состав);

        var статус = состав.ВоротаЛицензии.ТекущийСтатус;

        _лицензия.Text = статус.State switch
        {
            LicenseState.Licensed => "Лицензия: бессрочная",
            LicenseState.Trial => $"Пробный период: осталось дней {статус.DaysRemaining}",
            _ => "Пробный период закончился",
        };
    }

    /// <summary>
    /// Показывает NAG-окно пробного периода при старте приложения и при
    /// истечении срока (ТЗ §8.10, функция 119: «NAG-экран при старте в
    /// trial-режиме», «Осталось дней: 0»).
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    /// <returns><see langword="true"/>, если NAG-окно было показано.</returns>
    public bool ПоказатьNagЛицензии(AppServices состав)
    {
        ArgumentNullException.ThrowIfNull(состав);

        var статус = состав.ВоротаЛицензии.ТекущийСтатус;

        if (статус.State == LicenseState.Licensed)
        {
            return false;
        }

        using var форма = new FormLicenseNag(статус);
        форма.ShowDialog(this);

        if (форма.ВыбранноеДействие == FormLicenseNag.КнопкаВводаКлюча)
        {
            ОткрытьРегистрацию();
        }

        return true;
    }

    /// <summary>
    /// Обрабатывает прямой набор с клавиатуры в таблице получателей
    /// (ТЗ §7.7: «Прямой набор с клавиатуры»).
    /// </summary>
    private void ОбработатьНаборВТаблице(object? отправитель, KeyEventArgs аргументы)
    {
        if (аргументы.KeyCode == Keys.Escape)
        {
            аргументы.Handled = true;
            СброситьПоиск();
            return;
        }

        if (аргументы.Control || аргументы.Alt)
        {
            return;
        }

        if (аргументы.KeyCode == Keys.Back)
        {
            _поиск.УдалитьИзПрямогоНабора();
        }
        else if (!char.IsControl((char)аргументы.KeyCode))
        {
            _поиск.ДобавитьВПрямойНабор((char)аргументы.KeyCode);
        }
        else
        {
            return;
        }

        аргументы.Handled = true;
        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Перезагружает список получателей с учётом текущих условий поиска.
    /// </summary>
    private async Task ОбновитьПолучателейAsync()
    {
        if (_состав is null)
        {
            return;
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Сбрасывает условия поиска по клавише Esc (ТЗ §7.7).
    /// </summary>
    private void СброситьПоиск()
    {
        var сброс = _поиск.СброситьПоEsc();

        if (сброс == СбросПоиска.СброшенФильтрГрупп
            || сброс == СбросПоиска.СброшенФильтрИзбранного)
        {
            _пунктИзбранного.Checked = _поиск.ТолькоИзбранное;
        }

        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Переключает встроенный фильтр «Избранное» (VIP).
    /// </summary>
    private void ПереключитьФильтрИзбранного()
    {
        _поиск.ЗадатьФильтрИзбранного(!_поиск.ТолькоИзбранное);
        _пунктИзбранного.Checked = _поиск.ТолькоИзбранное;
        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Добавляет новую запись получателя в адресную книгу.
    /// </summary>
    private async void ДобавитьПолучателя()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormCounterpartyEdit();

        if (форма.ShowDialog(this) != DialogResult.OK || форма.Результат is null)
        {
            return;
        }

        var результат = await _состав.СлужбаКонтрагентов.SaveAsync(форма.Результат).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var (имя, значение) in форма.ПользовательскиеПоля)
        {
            await _состав.СлужбаПолей
                .SetCustomValueAsync(результат.Value.Id, имя, значение)
                .ConfigureAwait(true);
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Изменяет выбранную запись получателя.
    /// </summary>
    private async void ИзменитьПолучателя()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var контрагент = await _состав.Контрагенты
            .GetByIdAsync(_выбранныеПолучатели[0])
            .ConfigureAwait(true);

        if (контрагент is null)
        {
            return;
        }

        using var форма = new FormCounterpartyEdit(контрагент);

        if (форма.ShowDialog(this) != DialogResult.OK || форма.Результат is null)
        {
            return;
        }

        var результат = await _состав.СлужбаКонтрагентов.SaveAsync(форма.Результат).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Создаёт отправителя (агента) и назначает его текущим.
    /// </summary>
    private async void СоздатьОтправителя()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormCounterpartyEdit();

        if (форма.ShowDialog(this) != DialogResult.OK || форма.Результат is null)
        {
            return;
        }

        var агент = new AgentDto
        {
            Name = форма.Результат.Name,
            Type = форма.Результат.Type,
            Inn = форма.Результат.Inn,
            Kpp = форма.Результат.Kpp,
            Bik = форма.Результат.Bik,
            Account = форма.Результат.Rs,
            BankName = форма.Результат.BankName,
        };

        var результат = await _состав.СлужбаАгентов.CreateAgentAsync(агент).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Отправитель", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _состав.ТекущийОтправитель.SetGlobalSender(результат.Value.Id);

        await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Изменяет выбранного отправителя.
    /// </summary>
    private async void ИзменитьОтправителя()
    {
        if (_состав is null || _выбранныеОтправители.Count == 0)
        {
            return;
        }

        var отправитель = await _состав.Контрагенты
            .GetByIdAsync(_выбранныеОтправители[0])
            .ConfigureAwait(true);

        if (отправитель is null)
        {
            return;
        }

        using var форма = new FormCounterpartyEdit(отправитель);

        if (форма.ShowDialog(this) != DialogResult.OK || форма.Результат is null)
        {
            return;
        }

        var результат = await _состав.СлужбаКонтрагентов.SaveAsync(форма.Результат).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Отправитель", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Помечает выделенные записи как удалённые (мягкое удаление).
    /// </summary>
    private async void УдалитьВыделенные()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var подтверждение = MessageBox.Show(
            this,
            $"Пометить выделенные записи ({_выбранныеПолучатели.Count}) как удалённые?",
            "Адресная книга",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (подтверждение != DialogResult.Yes)
        {
            return;
        }

        var результат = await _состав.СлужбаМенеджера
            .FastDeleteMarkAsync(_выбранныеПолучатели)
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Безвозвратно удаляет выделенные записи — защищённая комбинация
    /// Ctrl+Alt+F8 (ТЗ §8.1, функция 19).
    /// </summary>
    private async void УдалитьВыделенныеБезвозвратно()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var подтверждение = MessageBox.Show(
            this,
            $"Безвозвратно удалить выделенные записи ({_выбранныеПолучатели.Count})? "
            + "Операция необратима.",
            "Адресная книга",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (подтверждение != DialogResult.Yes)
        {
            return;
        }

        var удалено = 0;

        foreach (var идентификатор in _выбранныеПолучатели)
        {
            if (await _состав.Контрагенты.DeleteAsync(идентификатор).ConfigureAwait(true))
            {
                удалено++;
            }
        }

        MessageBox.Show(
            this,
            $"Безвозвратно удалено записей: {удалено}.",
            "Адресная книга",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Восстанавливает мягко удалённые записи.
    /// </summary>
    private async void ВосстановитьУдалённые()
    {
        if (_состав is null)
        {
            return;
        }

        var страница = await _состав.Контрагенты
            .GetPagedAsync(
                new Picket302.Domain.Common.PageRequest(0, 500),
                new Picket302.Domain.Repositories.CounterpartyFilter(IncludeDeleted: true))
            .ConfigureAwait(true);

        var удалённые = страница.Items.Where(контрагент => контрагент.IsDeleted).ToList();

        if (удалённые.Count == 0)
        {
            MessageBox.Show(this, "Удалённых записей нет.", "Сервис", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var запись in удалённые)
        {
            await _состав.Контрагенты.RestoreAsync(запись.Id).ConfigureAwait(true);
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Помечает выделенные записи как отправителей (ТЗ §8.11, функция 139:
    /// «Флаг "Это отправитель" у записи адресной книги»).
    /// </summary>
    private async void ПометитьОтправителем()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        foreach (var идентификатор in _выбранныеПолучатели)
        {
            var контрагент = await _состав.Контрагенты.GetByIdAsync(идентификатор).ConfigureAwait(true);

            if (контрагент is null)
            {
                continue;
            }

            контрагент.IsSender = true;
            контрагент.UpdatedAt = DateTime.UtcNow;

            await _состав.Контрагенты.UpdateAsync(контрагент).ConfigureAwait(true);
        }

        await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Перемещает выделенные записи в выбранную категорию (ТЗ §8.12,
    /// функция 154).
    /// </summary>
    private async void ПереместитьВКатегорию()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var категории = await _состав.СлужбаКатегорий.GetTreeAsync().ConfigureAwait(true);

        using var выбор = new Form();
        выбор.Font = new Font("Tahoma", 8f);
        выбор.Text = "Переместить в категорию";
        выбор.StartPosition = FormStartPosition.CenterParent;
        выбор.FormBorderStyle = FormBorderStyle.FixedDialog;
        выбор.ClientSize = new Size(320, 60);

        var список = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top,
        };

        foreach (var элемент in категории)
        {
            список.Items.Add(элемент.Name);
        }

        if (список.Items.Count == 0)
        {
            MessageBox.Show(this, "Справочник категорий пуст.", "Категории", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        список.SelectedIndex = 0;

        var кнопка = new Button { Text = "Переместить", Dock = DockStyle.Bottom, Height = 26 };

        выбор.Controls.Add(список);
        выбор.Controls.Add(кнопка);

        кнопка.Click += (_, _) =>
        {
            выбор.DialogResult = DialogResult.OK;
            выбор.Close();
        };

        if (выбор.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var имя = список.SelectedItem as string;

        var категория = категории.FirstOrDefault(сущ =>
            string.Equals(сущ.Name, имя, StringComparison.OrdinalIgnoreCase));

        if (категория is null)
        {
            return;
        }

        foreach (var идентификатор in _выбранныеПолучатели)
        {
            await _состав.СлужбаКатегорий
                .MoveCounterpartyAsync(идентификатор, категория.Id)
                .ConfigureAwait(true);
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Объединяет выделенные записи в рассылку (ТЗ §8.11, функция 141).
    /// </summary>
    private async void ОбъединитьВРассылку()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var результат = await _состав.СлужбаРассылок
            .CreateFromSelectionAsync(
                _выбранныеПолучатели,
                _состав.ТекущийОтправитель.CurrentSenderId,
                "Рассылка")
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Рассылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(
            this,
            $"Создана рассылка «{результат.Value.Name}». "
            + $"Конвертов необходимо: {результат.Value.EnvelopesRequired ?? 0}.",
            "Рассылка",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает редактор новой рассылки.
    /// </summary>
    private async void СоздатьРассылку()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormDistributionEditor(_состав);

        if (форма.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Создаёт копию выбранной рассылки для повторной отправки (ТЗ §8.11,
    /// функция 142).
    /// </summary>
    private async void ПовторнаяОтправка()
    {
        if (_состав is null)
        {
            return;
        }

        var идентификатор = ВыбраннаяРассылка();

        if (идентификатор is null)
        {
            MessageBox.Show(this, "Выберите рассылку.", "Рассылки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var результат = await _состав.СлужбаРассылок
            .CloneForResendAsync(идентификатор.Value)
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Рассылки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var текст = $"Создана копия рассылки «{результат.Value.Distribution.Name}». "
            + $"Писем: {результат.Value.LettersCreated}.";

        if (результат.Value.SkippedCounterparties.Count > 0)
        {
            текст += Environment.NewLine
                + "Пропущены удалённые контрагенты: "
                + string.Join(", ", результат.Value.SkippedCounterparties) + ".";
        }

        MessageBox.Show(this, текст, "Рассылки", MessageBoxButtons.OK, MessageBoxIcon.Information);

        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Возвращает идентификатор выбранной рассылки.
    /// </summary>
    private long? ВыбраннаяРассылка()
    {
        if (_таблицаРассылок.CurrentRow?.Tag is long идентификатор)
        {
            return идентификатор;
        }

        return _таблицаРассылок.SelectedRows.Count > 0
            && _таблицаРассылок.SelectedRows[0].Tag is long выбранная
                ? выбранная
                : null;
    }

    /// <summary>
    /// Выполняет пакетное редактирование выделенных записей (ТЗ §8.1,
    /// функция 18).
    /// </summary>
    private async void ПакетноеРедактирование()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            MessageBox.Show(this, "Не выбрано ни одной записи.", "Правка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var выбор = new Form();
        выбор.Font = new Font("Tahoma", 8f);
        выбор.Text = "Пакетное редактирование";
        выбор.StartPosition = FormStartPosition.CenterParent;
        выбор.FormBorderStyle = FormBorderStyle.FixedDialog;
        выбор.ClientSize = new Size(360, 70);

        var списокПолей = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top,
        };

        foreach (var имяПоля in Picket302.Application.Services.BatchEditService.EditableProperties)
        {
            списокПолей.Items.Add(имяПоля);
        }

        списокПолей.SelectedIndex = 0;

        var значение = new TextBox { Dock = DockStyle.Top };

        var кнопка = new Button { Text = "Применить", Dock = DockStyle.Bottom, Height = 26 };

        выбор.Controls.Add(значение);
        выбор.Controls.Add(списокПолей);
        выбор.Controls.Add(кнопка);

        кнопка.Click += (_, _) =>
        {
            выбор.DialogResult = DialogResult.OK;
            выбор.Close();
        };

        if (выбор.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var поле = списокПолей.SelectedItem as string;

        // ТЗ §8.1, функция 18: массовая правка требует предварительного
        // подтверждения, если затронуто больше порога (по умолчанию 10 записей).
        var порог = await _состав.СлужбаПакетногоРедактирования
            .GetConfirmationThresholdAsync()
            .ConfigureAwait(true);

        if (BatchEditService.ТребуетсяПодтверждение(_выбранныеПолучатели.Count, порог))
        {
            var подтверждение = MessageBox.Show(
                this,
                $"Изменить поле «{поле}» у {_выбранныеПолучатели.Count} записей?",
                "Пакетное редактирование",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (подтверждение != DialogResult.Yes)
            {
                return;
            }
        }

        var результат = await _состав.СлужбаПакетногоРедактирования
            .BatchUpdateFieldAsync(_выбранныеПолучатели, поле!, значение.Text)
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Правка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Наполняет базу демонстрационными данными (ТЗ §8.11, функция 137).
    /// </summary>
    private async void НаполнитьДемоБазу()
    {
        if (_состав is null)
        {
            return;
        }

        var создано = await _состав.НаполнитьДемоБазуЕслиПустоAsync().ConfigureAwait(true);

        MessageBox.Show(
            this,
            создано > 0
                ? $"В базу добавлено демонстрационных записей: {создано}."
                : "Адресная книга не пуста — демо-база не создавалась.",
            "Демо-база",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает экран «Очистка базы» (ТЗ §8.12, функция 153).
    /// </summary>
    private async void ОткрытьФормуОчистки()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormClearDatabase(_состав);

        if (форма.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(_состав, CancellationToken.None).ConfigureAwait(false);
        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает диалог настройки оформления таблиц (ТЗ §8.11, функция 131).
    /// </summary>
    private async void НастроитьОформление()
    {
        using var форма = new FormGridAppearance(_оформление);

        if (форма.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _оформление = форма.Оформление;

        GridAppearanceManager.Применить(_таблицаОтправителей, _оформление);
        GridAppearanceManager.Применить(_таблицаПолучателей, _оформление);
        GridAppearanceManager.Применить(_таблицаБланков, _оформление);
        GridAppearanceManager.Применить(_таблицаРассылок, _оформление);
        GridAppearanceManager.Применить(_таблицаОтправленных, _оформление);

        // Переключатель заголовков столбцов действует на все таблицы
        // программы (ТЗ §8.11, функция 131).
        _таблицаОтправителей.ColumnHeadersVisible = _оформление.HeadersVisible;
        _таблицаПолучателей.ColumnHeadersVisible = _оформление.HeadersVisible;
        _таблицаБланков.ColumnHeadersVisible = _оформление.HeadersVisible;
        _таблицаРассылок.ColumnHeadersVisible = _оформление.HeadersVisible;
        _таблицаОтправленных.ColumnHeadersVisible = _оформление.HeadersVisible;

        if (_состав is not null)
        {
            await _состав.НастройкиОтображения.SaveAppearanceAsync(_оформление).ConfigureAwait(true);
        }

        if (форма.СброшеноКУмолчаниям)
        {
            // Сброс к умолчаниям возвращает и состояние столбцов
            // (ТЗ §8.11, функция 131: «сброс к умолчаниям»).
            if (_настройкиВидаТаблиц is not null)
            {
                await _настройкиВидаТаблиц
                    .СброситьКУмолчаниямAsync(_таблицаПолучателей, ИдентификаторТаблицыПолучателей, CancellationToken.None)
                    .ConfigureAwait(true);
            }

            return;
        }

        if (_настройкиВидаТаблиц is not null)
        {
            await _настройкиВидаТаблиц
                .СохранитьAsync(_таблицаПолучателей, ИдентификаторТаблицыПолучателей, CancellationToken.None)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Открывает диалог «Настройка таблицы» (ТЗ §7.6).
    /// </summary>
    private async void НастроитьТаблицу()
    {
        if (_состав is null)
        {
            return;
        }

        // Сохраняется фактическое состояние столбцов адресной книги:
        // видимость, подписи и ширина (ТЗ §8.11, функция 131; §7.6).
        if (_настройкиВидаТаблиц is not null)
        {
            await _настройкиВидаТаблиц
                .СохранитьAsync(_таблицаПолучателей, ИдентификаторТаблицыПолучателей, CancellationToken.None)
                .ConfigureAwait(true);
        }

        MessageBox.Show(
            this,
            "Настройка таблицы сохранена: видимость, подписи и ширина столбцов.",
            "Настройка таблицы",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Обрабатывает нажатие правой кнопки мыши по заголовку столбца
    /// адресной книги: показывает контекстное меню столбцов (ТЗ §8.11,
    /// функция 131: «скрытие колонок»).
    /// </summary>
    /// <param name="отправитель">Таблица адресной книги.</param>
    /// <param name="аргументы">Аргументы нажатия.</param>
    private void ОбработатьНажатиеЗаголовка(object? отправитель, MouseEventArgs аргументы)
    {
        if (аргументы.Button != MouseButtons.Right)
        {
            return;
        }

        var попадание = _таблицаПолучателей.HitTest(аргументы.X, аргументы.Y);

        if (попадание.ColumnIndex < 0)
        {
            return;
        }

        _индексСтолбцаПодКурсором = попадание.ColumnIndex;

        var меню = МенюСтолбцовАдреснойКниги();
        меню.Show(_таблицаПолучателей, аргументы.Location);
    }

    /// <summary>
    /// Строит контекстное меню заголовков столбцов адресной книги:
    /// скрытие столбца и возврат всех столбцов (ТЗ §8.11, функция 131:
    /// «скрытие колонок»).
    /// </summary>
    /// <returns>Контекстное меню заголовков столбцов.</returns>
    private ContextMenuStrip МенюСтолбцовАдреснойКниги()
    {
        var меню = new ContextMenuStrip { Font = new Font("Tahoma", 8f) };

        var скрыть = new ToolStripMenuItem("Скрыть столбец")
        {
            Enabled = _индексСтолбцаПодКурсором >= 0,
        };
        скрыть.Click += (_, _) => СкрытьСтолбецАдреснойКниги();

        var показать = new ToolStripMenuItem("Показать все столбцы");
        показать.Click += (_, _) => ПоказатьВсеСтолбцыАдреснойКниги();

        меню.Items.Add(скрыть);
        меню.Items.Add(показать);

        return меню;
    }

    /// <summary>
    /// Скрывает столбец адресной книги, над которым открыто контекстное меню,
    /// и сохраняет новое состояние столбцов.
    /// </summary>
    private async void СкрытьСтолбецАдреснойКниги()
    {
        if (_настройкиВидаТаблиц is null)
        {
            return;
        }

        await _настройкиВидаТаблиц
            .СкрытьСтолбецAsync(
                _таблицаПолучателей,
                ИдентификаторТаблицыПолучателей,
                _индексСтолбцаПодКурсором,
                CancellationToken.None)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Возвращает все столбцы адресной книги и сохраняет новое состояние.
    /// </summary>
    private async void ПоказатьВсеСтолбцыАдреснойКниги()
    {
        if (_настройкиВидаТаблиц is null)
        {
            return;
        }

        await _настройкиВидаТаблиц
            .ПоказатьВсеСтолбцыAsync(_таблицаПолучателей, ИдентификаторТаблицыПолучателей, CancellationToken.None)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Перезагружает данные всех таблиц.
    /// </summary>
    private async void ОбновитьВсе()
    {
        if (_состав is null)
        {
            return;
        }

        await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьПапкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Запрашивает у оператора параметры отправления в карточке отправления
    /// (ТЗ §8.1, функции 23 и 32).
    /// </summary>
    /// <returns>
    /// Выбранные значения либо <see langword="null"/>, если оператор отказался
    /// от печати.
    /// </returns>
    private MailCardValues? ЗапроситьПараметрыОтправления()
    {
        using var карточка = new FormMailEdit();

        if (карточка.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        return new MailCardValues(
            карточка.Категория,
            карточка.ВидУведомления,
            карточка.НужноУведомление,
            карточка.ФорматКонверта,
            карточка.ЧислоКопий);
    }

    /// <summary>
    /// Переносит выбранные параметры карточки в отправление.
    /// </summary>
    /// <param name="письмо">Карточка отправления.</param>
    /// <param name="значения">Выбранные значения.</param>
    private static void ПрименитьПараметрыОтправления(
        Picket302.Domain.Entities.Mail письмо,
        MailCardValues значения)
    {
        ArgumentNullException.ThrowIfNull(письмо);
        ArgumentNullException.ThrowIfNull(значения);

        письмо.MailCategory = значения.Категория;
        письмо.NoticeType = значения.ВидУведомления == Picket302.Domain.Entities.NotificationType.None
            ? null
            : значения.ВидУведомления.Ключ();
        письмо.NeedNotification = значения.НужноУведомление;
        письмо.CopyCount = значения.ЧислоКопий;

        if (!string.IsNullOrWhiteSpace(значения.ФорматКонверта))
        {
            письмо.EnvelopeType = значения.ФорматКонверта;
        }
    }

    /// <summary>
    /// Формирует отправление для текущего выбранного получателя и переводит его
    /// в состояние «Напечатано» — подготовка конверта к печати.
    /// </summary>
    private async void ПечатьТекущийКонверт()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var отправитель = _состав.ТекущийОтправитель.CurrentSenderId;

        if (отправитель is null or <= 0)
        {
            MessageBox.Show(
                this,
                "Не выбран текущий отправитель.",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var контрагент = await _состав.Контрагенты
            .GetByIdAsync(_выбранныеПолучатели[0])
            .ConfigureAwait(true);

        if (контрагент is null)
        {
            return;
        }

        var адрес = контрагент.GetAddress(контрагент.AddressMainType);

        if (адрес is null)
        {
            MessageBox.Show(
                this,
                $"У получателя «{контрагент.Name}» не задан основной адрес.",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var письмо = Picket302.Application.Services.MailFactory.CreateOutcoming(
            контрагент.Id,
            отправитель.Value,
            "C6",
            адрес,
            контрагент.IsVip);

        письмо.OutgoingNumber = await _состав.СлужбаНумерации
            .GenerateNextOutgoingNumberAsync()
            .ConfigureAwait(true);

        // Карточка отправления: категория и вид уведомления выбираются
        // оператором до сохранения (ТЗ §8.1, функции 23 и 32).
        var параметры = ЗапроситьПараметрыОтправления();

        if (параметры is null)
        {
            return;
        }

        ПрименитьПараметрыОтправления(письмо, параметры);

        var результат = await _состав.СлужбаПисем.CreateAsync(письмо).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Печать", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ВыполнитьПечатьAsync(new[] { письмо.Id }, сПредпросмотром: true).ConfigureAwait(true);

        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        ОбновитьСтрокуСостояния(_состав);
    }

    /// <summary>
    /// Формирует отправления для всех выделенных получателей.
    /// </summary>
    /// <param name="сПредпросмотром">
    /// Признак печати с интерактивным предпросмотром (F6); без предпросмотра —
    /// прямая печать в спулер (F7).
    /// </param>
    private async void ПечатьВыделенные(bool сПредпросмотром)
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        var отправитель = _состав.ТекущийОтправитель.CurrentSenderId;

        if (отправитель is null or <= 0)
        {
            MessageBox.Show(
                this,
                "Не выбран текущий отправитель.",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var создано = 0;
        var идентификаторы = new List<long>();

        // Карточка отправления запрашивается один раз для всей партии:
        // категория и вид уведомления применяются ко всем отправлениям
        // (ТЗ §8.1, функции 23 и 32).
        var параметры = ЗапроситьПараметрыОтправления();

        if (параметры is null)
        {
            return;
        }

        foreach (var идентификатор in _выбранныеПолучатели)
        {
            var контрагент = await _состав.Контрагенты.GetByIdAsync(идентификатор).ConfigureAwait(true);

            if (контрагент is null)
            {
                continue;
            }

            var адрес = контрагент.GetAddress(контрагент.AddressMainType);

            if (адрес is null)
            {
                continue;
            }

            var письмо = Picket302.Application.Services.MailFactory.CreateOutcoming(
                контрагент.Id,
                отправитель.Value,
                "C6",
                адрес,
                контрагент.IsVip);

            письмо.OutgoingNumber = await _состав.СлужбаНумерации
                .GenerateNextOutgoingNumberAsync()
                .ConfigureAwait(true);

            ПрименитьПараметрыОтправления(письмо, параметры);

            var результат = await _состав.СлужбаПисем.CreateAsync(письмо).ConfigureAwait(true);

            if (результат.IsSuccess)
            {
                создано++;
                идентификаторы.Add(письмо.Id);
            }
        }

        MessageBox.Show(
            this,
            $"Отправлений сформировано: {создано}.",
            "Печать",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        await ВыполнитьПечатьAsync(идентификаторы, сПредпросмотром, пакетнаяПечать: true).ConfigureAwait(true);

        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        ОбновитьСтрокуСостояния(_состав);
    }

    /// <summary>
    /// Выполняет печатную сессию: диалог печати, интерактивный предпросмотр,
    /// затем передача задания в спулер (ТЗ §8.6, функция 79).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="сПредпросмотром">Признак показа интерактивного предпросмотра.</param>
    /// <param name="пакетнаяПечать">Признак пакетной печати (F6 / F7).</param>
    private async Task ВыполнитьПечатьAsync(
        IReadOnlyList<long> mailIds,
        bool сПредпросмотром,
        bool пакетнаяПечать = false)
    {
        var состав = _состав;

        if (состав is null || mailIds.Count == 0)
        {
            return;
        }

        // ТЗ §8.6, функция 78: без принтера по умолчанию печать блокируется.
        if (состав.СлужбаПринтеров.GetDefaultPrinter() is null)
        {
            MessageBox.Show(
                this,
                PrinterManager.NoDefaultPrinterMessage,
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        var запрос = await ПоказатьДиалогПечатиAsync(mailIds, сПредпросмотром, пакетнаяПечать)
            .ConfigureAwait(true);

        if (запрос is null)
        {
            return;
        }

        if (await состав.ПодсказкаПодачи.ShouldShowAsync().ConfigureAwait(true))
        {
            using var подсказка = new FormFeedOrientationHint();

            if (подсказка.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (подсказка.ПодавитьПредупреждение)
            {
                await состав.ПодсказкаПодачи.SetSuppressedAsync(true).ConfigureAwait(true);
            }
        }

        for (var попытка = 0; попытка < 3; попытка++)
        {
            // ТЗ §8.6, функция 80: предпросмотр показывается до передачи
            // задания в спулер; отказ оператора отменяет печать.
            if (сПредпросмотром && !await ПоказатьПредпросмотрAsync(запрос).ConfigureAwait(true))
            {
                return;
            }

            var итог = await ЗапуститьПечатьAsync(mailIds, запрос).ConfigureAwait(true);

            if (!string.Equals(итог.Status, PrintJobStatus.BlockedSize, StringComparison.Ordinal))
            {
                if (!итог.IsSuccess && !string.IsNullOrEmpty(итог.ErrorMessage))
                {
                    MessageBox.Show(
                        this,
                        итог.ErrorMessage!,
                        "Печать",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return;
            }

            var решение = await ПоказатьДиалогРазмераAsync(запрос).ConfigureAwait(true);

            if (решение == PaperSizeBlockedDecision.Отмена)
            {
                return;
            }

            if (решение == PaperSizeBlockedDecision.ЗафиксироватьA4)
            {
                запрос = запрос with { FixA4 = true };
                continue;
            }

            var новыйЗапрос = await ПоказатьДиалогПечатиAsync(mailIds, сПредпросмотром, пакетнаяПечать)
                .ConfigureAwait(true);

            if (новыйЗапрос is null)
            {
                return;
            }

            запрос = новыйЗапрос;
        }
    }

    /// <summary>
    /// Показывает диалог печати и формирует параметры печатной операции
    /// (ТЗ §8.6, функция 79, §8.6, функция 82).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="сПредпросмотром">Признак показа интерактивного предпросмотра.</param>
    /// <param name="пакетнаяПечать">Признак пакетной печати (F6 / F7).</param>
    /// <returns>Параметры печати либо <see langword="null"/>, если оператор отказался.</returns>
    private async Task<PrintRequest?> ПоказатьДиалогПечатиAsync(
        IReadOnlyList<long> mailIds,
        bool сПредпросмотром,
        bool пакетнаяПечать)
    {
        var состав = _состав;

        if (состав is null)
        {
            return null;
        }

        var шаблоны = await состав.ШаблоныПечати
            .GetAllAsync(PrintTemplateKind.Envelope)
            .ConfigureAwait(true);

        var принтеры = состав.СлужбаПринтеров.EnumeratePrinters();

        // ТЗ §8.6, функция 82: опция «Показывать список шаблонов перед печатью».
        var показыватьСписокШаблонов = await состав.ПараметрыПечати
            .GetShowTemplateListAsync()
            .ConfigureAwait(true);

        СинхронизироватьПунктСпискаШаблонов(показыватьСписокШаблонов);

        var последнийШаблон = await НайтиПоследнийШаблонAsync(состав, шаблоны).ConfigureAwait(true);

        // ТЗ §8.6, функция 89: предупреждение о соответствии ГОСТ Р 51506-99.
        var показыватьПредупреждениеРазлиновки = await состав.РежимРазлиновки
            .ShouldShowAsync()
            .ConfigureAwait(true);

        using var диалог = new FormEnvelopePrint(
            шаблоны,
            принтеры,
            показыватьСписокШаблонов,
            последнийШаблон: последнийШаблон,
            службаПринтеров: состав.СлужбаПринтеров,
            показыватьПредупреждениеРазлиновки: показыватьПредупреждениеРазлиновки);

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        if (диалог.ПоказатьСписокШаблонов != показыватьСписокШаблонов)
        {
            показыватьСписокШаблонов = диалог.ПоказатьСписокШаблонов;

            await состав.ПараметрыПечати
                .SetShowTemplateListAsync(показыватьСписокШаблонов)
                .ConfigureAwait(true);

            СинхронизироватьПунктСпискаШаблонов(показыватьСписокШаблонов);
        }

        var запрос = диалог.СформироватьЗапрос(mailIds) with
        {
            ShowPreview = сПредпросмотром,
            PrintMode = пакетнаяПечать
                ? (сПредпросмотром ? PrintModes.BatchPreview : PrintModes.BatchDirect)
                : PrintModes.Selected,
        };

        if (запрос.TemplateId is not null)
        {
            await состав.ПараметрыПечати
                .SetLastTemplateIdAsync(запрос.TemplateId)
                .ConfigureAwait(true);
        }

        if (запрос.Preprinted && диалог.ПодавитьПредупреждениеРазлиновки)
        {
            await состав.РежимРазлиновки.SetSuppressedAsync(true).ConfigureAwait(true);
        }

        return запрос;
    }

    /// <summary>
    /// Показывает интерактивный предпросмотр печатного задания (ТЗ §8.6,
    /// функция 80).
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <returns><see langword="false"/>, если оператор отказался от печати.</returns>
    private async Task<bool> ПоказатьПредпросмотрAsync(PrintRequest запрос)
    {
        var состав = _состав;

        if (состав is null)
        {
            return false;
        }

        var сборщик = new PrintPreviewBuilder(
            состав.ШаблоныПечати,
            состав.ПоляШаблонов,
            состав.ДанныеПечати,
            состав.СлужбаПринтеров,
            состав.СлужбаПредпросмотра);

        PreviewDocument документ;

        try
        {
            документ = await сборщик.ПостроитьAsync(запрос).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Сбой построения предпросмотра не должен прерывать приложение.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show(
                this,
                $"Не удалось построить предпросмотр: {ex.Message}",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        using (документ)
        using (var окно = new FormPrintPreview(документ))
        {
            return окно.ShowDialog(this) == DialogResult.OK;
        }
    }

    /// <summary>
    /// Передаёт печатное задание в спулер (ТЗ §8.6, функция 79).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="запрос">Параметры печати.</param>
    /// <returns>Итог печатной сессии.</returns>
    private async Task<PrintResult> ЗапуститьПечатьAsync(IReadOnlyList<long> mailIds, PrintRequest запрос)
    {
        var состав = _состав;

        if (состав is null)
        {
            return new PrintResult { Status = PrintJobStatus.Aborted };
        }

        var прогресс = new Progress<PrintProgress>(состояние =>
        {
            _состояниеПечати.Text = состояние.Message;
        });

        try
        {
            return запрос.ShowPreview
                ? await состав.СлужбаПечати
                    .PrintSelectionPreviewedAsync(mailIds, запрос, CancellationToken.None, прогресс)
                    .ConfigureAwait(true)
                : await состав.СлужбаПечати
                    .PrintSelectionDirectAsync(mailIds, запрос, CancellationToken.None, прогресс)
                    .ConfigureAwait(true);
        }
        finally
        {
            _состояниеПечати.Text = string.Empty;
        }
    }

    /// <summary>
    /// Показывает диалог проверки размера бумаги и блокирующее предупреждение
    /// о слишком большом конверте (ТЗ §8.6, функции 83 и 85).
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <returns>Решение оператора.</returns>
    private async Task<PaperSizeBlockedDecision> ПоказатьДиалогРазмераAsync(PrintRequest запрос)
    {
        var имяПринтера = запрос.PrinterName;

        var форматы = ПолучитьДоступныеФорматы(имяПринтера);

        var (ширинаБланка, высотаБланка) = await ПолучитьРазмерБланкаAsync(запрос).ConfigureAwait(true);
        var (ширинаНосителя, высотаНосителя) = ПолучитьРазмерНосителя(имяПринтера);

        // ТЗ §8.6, функция 85: блокирующее предупреждение с обеими величинами
        // (мм) и перечнем доступных форматов принтера.
        var предупреждение = new OversizeWarning(new OversizeWarningPresenter(форматы), форматы);

        предупреждение.ShowOversizeDialog(
            new SizeF((float)ширинаБланка, (float)высотаБланка),
            new SizeF((float)ширинаНосителя, (float)высотаНосителя));

        // ТЗ §8.6, функция 83: выбор действия оператора.
        using var диалог = new FormPaperSizeBlocked(
            new SizeF((float)ширинаБланка, (float)высотаБланка),
            new SizeF((float)ширинаНосителя, (float)высотаНосителя),
            имяПринтера);

        диалог.ShowDialog(this);

        return диалог.Решение;
    }

    /// <summary>
    /// Возвращает перечень форматов носителя, доступных на выбранном принтере.
    /// </summary>
    /// <param name="имяПринтера">Имя принтера.</param>
    /// <returns>Наименования доступных форматов.</returns>
    private IReadOnlyList<string> ПолучитьДоступныеФорматы(string имяПринтера)
    {
        var состав = _состав;

        if (состав is null || string.IsNullOrWhiteSpace(имяПринтера))
        {
            return Array.Empty<string>();
        }

        try
        {
            return состав.СлужбаПринтеров.GetSupportedPaperSizeNames(имяПринтера);
        }
#pragma warning disable CA1031 // Отсутствие сведений о форматах не должно прерывать печать.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Возвращает размер бланка выбранного шаблона, мм.
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <returns>Ширина и высота бланка, мм.</returns>
    private async Task<(decimal Ширина, decimal Высота)> ПолучитьРазмерБланкаAsync(PrintRequest запрос)
    {
        var состав = _состав;

        if (состав is null || запрос.TemplateId is null)
        {
            return (0m, 0m);
        }

        var шаблон = await состав.ШаблоныПечати
            .GetByIdAsync(запрос.TemplateId.Value)
            .ConfigureAwait(true);

        return шаблон is null ? (0m, 0m) : (шаблон.WidthMm, шаблон.HeightMm);
    }

    /// <summary>
    /// Возвращает размер печатной области носителя выбранного принтера, мм.
    /// </summary>
    /// <param name="имяПринтера">Имя принтера.</param>
    /// <returns>Ширина и высота печатной области, мм.</returns>
    private (decimal Ширина, decimal Высота) ПолучитьРазмерНосителя(string имяПринтера)
    {
        var состав = _состав;

        if (состав is null || string.IsNullOrWhiteSpace(имяПринтера))
        {
            return (0m, 0m);
        }

        try
        {
            var (ширина, высота) = состав.СлужбаПринтеров.GetPrintableArea(имяПринтера);

            return (ширина, высота);
        }
#pragma warning disable CA1031 // Недоступность драйвера не должна прерывать печать.
        catch (Exception)
#pragma warning restore CA1031
        {
            return (0m, 0m);
        }
    }

    /// <summary>
    /// Находит последний использованный шаблон печати (ТЗ §8.6, функция 82).
    /// </summary>
    /// <param name="шаблоны">Доступные шаблоны.</param>
    /// <returns>Последний использованный шаблон либо <see langword="null"/>.</returns>
    private async Task<PrintTemplate?> НайтиПоследнийШаблонAsync(
        AppServices состав,
        IReadOnlyList<PrintTemplate> шаблоны)
    {
        var идентификатор = await состав.ПараметрыПечати
            .GetLastTemplateIdAsync()
            .ConfigureAwait(true);

        if (идентификатор is null)
        {
            return null;
        }

        return шаблоны.FirstOrDefault(шаблон => шаблон.Id == идентификатор.Value);
    }

    /// <summary>
    /// Синхронизирует состояние галки меню «Показывать список шаблонов перед
    /// печатью» с профилем пользователя.
    /// </summary>
    /// <param name="значение">Значение опции.</param>
    private void СинхронизироватьПунктСпискаШаблонов(bool значение)
    {
        if (_пунктСпискаШаблонов is null || _пунктСпискаШаблонов.Checked == значение)
        {
            return;
        }

        _пунктСпискаШаблонов.Checked = значение;
    }

    /// <summary>
    /// Сохраняет опцию «Показывать список шаблонов перед печатью» в профиле
    /// пользователя (ТЗ §8.6, функция 82).
    /// </summary>
    /// <param name="значение">Значение опции.</param>
    private async Task СохранитьОпциюСпискаШаблоновAsync(bool значение)
    {
        if (_состав is null)
        {
            return;
        }

        await _состав.ПараметрыПечати.SetShowTemplateListAsync(значение).ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает системный диалог свойств принтера по умолчанию (ТЗ §8.6,
    /// функция 78).
    /// </summary>
    private void НастройкиПринтера()
    {
        if (_состав is null)
        {
            return;
        }

        var принтер = _состав.СлужбаПринтеров.GetDefaultPrinter();

        if (принтер is null)
        {
            MessageBox.Show(
                this,
                PrinterManager.NoDefaultPrinterMessage,
                "Настройки принтера",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _состав.СлужбаПринтеров.ShowPrinterProperties(принтер.Name);
    }

    /// <summary>
    /// Открывает контекстную справочную систему (F1).
    /// </summary>
    private void ОткрытьСправку()
    {
        MessageBox.Show(
            this,
            "Picket302 — адресная книга и рассылки.\r\n\r\n"
            + "F1 — справка\r\n"
            + "F4 — раскрыть дерево категорий\r\n"
            + "F5 — текущий конверт\r\n"
            + "F6 — выделенные с предпросмотром\r\n"
            + "F7 — выделенные без предпросмотра\r\n"
            + "F8 — редактор бланков\r\n"
            + "Esc — отмена действия, сброс поиска\r\n"
            + "Ctrl+Space — свернуть в область уведомлений\r\n"
            + "Ctrl+A — выделить все записи\r\n"
            + "Ctrl+Alt+F8 — безвозвратное удаление",
            "Справка",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <inheritdoc />
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (!_инициализировано)
        {
            await ИнициализироватьAsync().ConfigureAwait(true);
        }

        // NAG-экран пробного периода показывается при старте приложения
        // (ТЗ §8.10, функция 119). В тестах окно не отображается, поэтому
        // диалог не блокирует автоматические проверки.
        if (_состав is not null)
        {
            ПоказатьNagЛицензии(_состав);
        }
    }

    /// <inheritdoc />
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var действие = ShellHotKeyMap.Найти(keyData);

        if (действие is null)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (keyData == (Keys.Control | Keys.Space))
        {
            // Глобальная горячая клавиша обрабатывается сообщением WM_HOTKEY.
            return true;
        }

        if (keyData == (Keys.Control | Keys.F))
        {
            // Форма поиска (ТЗ §7.7: «Форма поиска (Ctrl+F)»).
            НачатьПоиск();
            return true;
        }

        ВыполнитьДействие(действие.Value);
        return true;
    }

    /// <summary>
    /// Открывает встроенный редактор бланков по F8
    /// (ТЗ §7.4, §7.5; §8.3, функция 38).
    /// </summary>
    private void ОткрытьРедакторБланков()
    {
        // Новый шаблон создаётся копированием существующего, а не с нуля:
        // редактор открывает эталонный бланк библиотеки (ТЗ §7.5).
        var бланк = new Infrastructure.Templates.BuiltinTemplatesCatalog()
            .GetBuiltinTemplate(Infrastructure.Templates.BuiltinFormType.Form112ep);

        using var редактор = new UI.Forms.FormBlankEditor(бланк);
        редактор.ShowDialog(this);
    }

    /// <summary>
    /// Открывает форму поиска и переходит к следующей совпавшей записи
    /// (ТЗ §7.7: «последовательный перебор, кнопка «Найти далее», показывает
    /// одну следующую запись, не подсвечивает все совпадения»).
    /// </summary>
    private void НачатьПоиск()
    {
        var совпадений = SearchMechanism.Совпадения(
            _текущиеПолучатели,
            получатель => получатель.Наименование,
            _поиск.ФормаПоиска).Count;

        using var форма = new FormSearch(_поиск.ФормаПоиска, совпадений);

        if (форма.ShowDialog(this) != DialogResult.OK || форма.Запрос.Length == 0)
        {
            return;
        }

        _поиск.ЗадатьФормуПоиска(форма.Запрос);

        var начало = форма.НайтиСНачала ? -1 : _текущийИндексПолучателя();
        var индекс = SearchMechanism.НайтиДалее(
            _текущиеПолучатели,
            получатель => получатель.Наименование,
            форма.Запрос,
            начало);

        if (индекс < 0)
        {
            MessageBox.Show(
                this,
                $"Совпадений по запросу «{форма.Запрос}» не найдено.",
                "Поиск",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ПоказатьПолучателя(индекс);
    }

    /// <summary>
    /// Индекс текущей выделенной записи таблицы получателей.
    /// </summary>
    private int _текущийИндексПолучателя()
    {
        foreach (DataGridViewRow строка in _таблицаПолучателей.SelectedRows)
        {
            return строка.Index;
        }

        return _таблицаПолучателей.CurrentRow?.Index ?? -1;
    }

    /// <summary>
    /// Показывает запись таблицы получателей и устанавливает на неё курсор.
    /// </summary>
    private void ПоказатьПолучателя(int индекс)
    {
        if (индекс < 0 || индекс >= _таблицаПолучателей.Rows.Count)
        {
            return;
        }

        _таблицаПолучателей.ClearSelection();
        _таблицаПолучателей.Rows[индекс].Selected = true;
        _таблицаПолучателей.CurrentCell = _таблицаПолучателей.Rows[индекс].Cells[0];
        _таблицаПолучателей.FirstDisplayedScrollingRowIndex = индекс;
    }

    /// <summary>
    /// Подбирает значение поля из имеющихся записей: курсор в пустом поле, нажата
    /// клавиша BackSpace (ТЗ §7.7; §8.1, функция 133).
    /// </summary>
    /// <param name="редактор">Редактор адреса, в котором выполнен запрос.</param>
    private async void ПодобратьЗначениеПоля(AddressEditor редактор)
    {
        if (_состав is null)
        {
            return;
        }

        var значения = await _состав.Контрагенты
            .GetDistinctFieldValuesAsync("post_zip")
            .ConfigureAwait(true);

        if (значения.Count == 0)
        {
            return;
        }

        using var выбор = new FormValuePicker("Подбор значения поля", значения, редактор.ПолеИндекса.Text);

        if (выбор.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        редактор.ПолеИндекса.Text = выбор.ВыбранноеЗначение;
    }

    /// <summary>
    /// Выполняет действие, назначенное горячей клавише (ТЗ §7.4).
    /// </summary>
    /// <param name="действие">Действие.</param>
    private void ВыполнитьДействие(ShellAction действие)
    {
        switch (действие)
        {
            case ShellAction.ОткрытьСправку:
                ОткрытьСправку();
                break;
            case ShellAction.РаскрытьФильтрКатегорий:
                _деревоКатегорий.ExpandAll();
                _деревоКатегорий.Focus();
                break;
            case ShellAction.ПечатьТекущийКонверт:
                ПечатьТекущийКонверт();
                break;
            case ShellAction.ПечатьВыделенныеСПредпросмотром:
                ПечатьВыделенные(сПредпросмотром: true);
                break;
            case ShellAction.ПечатьВыделенныеБезПредпросмотра:
                ПечатьВыделенные(сПредпросмотром: false);
                break;
            case ShellAction.ОткрытьРедакторБланков:
                ОткрытьРедакторБланков();
                break;
            case ShellAction.ОтменитьДействие:
                СброситьПоиск();
                break;
            case ShellAction.СвернутьВОбластьУведомлений:
                СвернутьВОбластьУведомлений();
                break;
            case ShellAction.ВыделитьВсеЗаписи:
                ВыделитьВсеЗаписи();
                break;
            case ShellAction.УдалитьВыделенныеБезвозвратно:
                УдалитьВыделенныеБезвозвратно();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Выделяет все записи активной таблицы (Ctrl+A).
    /// </summary>
    private void ВыделитьВсеЗаписи()
    {
        var таблица = АктивнаяТаблица();

        таблица.ClearSelection();

        foreach (DataGridViewRow строка in таблица.Rows)
        {
            строка.Selected = true;
        }

        if (таблица.Rows.Count > 0 && таблица.Rows[0].Selected)
        {
            таблица.CurrentCell = таблица.Rows[0].Cells[0];
        }

        ОбновитьВыделенное();
    }

    /// <summary>
    /// Возвращает таблицу активной вкладки.
    /// </summary>
    private DataGridView АктивнаяТаблица() => _вкладки.SelectedIndex switch
    {
        0 => _таблицаПолучателей,
        1 => _таблицаБланков,
        2 => _таблицаРассылок,
        3 => _таблицаОтправленных,
        _ => _таблицаПолучателей,
    };

    /// <summary>
    /// Сворачивает приложение в область уведомлений либо возвращает окно на экран.
    /// </summary>
    private void СвернутьВОбластьУведомлений()
    {
        if (Visible && WindowState == FormWindowState.Minimized)
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            return;
        }

        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        _глобальнаяГорячаяКлавиша = GlobalHotKey.CtrlSpace(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(() => СвернутьВОбластьУведомлений());
        });

        _глобальнаяГорячаяКлавиша.Зарегистрировать();
    }

    /// <inheritdoc />
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == СообщениеГорячейКлавиши
            && m.WParam.ToInt32() == ИдентификаторГорячейКлавиши)
        {
            _глобальнаяГорячаяКлавиша?.Выполнить();
            return;
        }

        base.WndProc(ref m);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _глобальнаяГорячаяКлавиша?.Dispose();
            _глобальнаяГорячаяКлавиша = null;

            // Состав, созданный оболочкой по пути по умолчанию, освобождается
            // вместе с окном; переданный конструктором остаётся во владении
            // вызывающего. Закрытие соединения SQLite не требует потока
            // интерфейса, синхронное ожидание здесь безопасно.
            if (_составСозданоВнутри && _состав is not null)
            {
                _состав.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _состав = null;
            }
        }

        base.Dispose(disposing);
    }
}
