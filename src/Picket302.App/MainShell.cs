using System.Drawing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows.Forms;
using Picket302.App.Shared;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Email;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Services;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure;
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
/// Плотность рабочего режима: шрифт таблиц 8pt Segoe UI, высота строки ~17px
/// (ТЗ §7.10). Горячие клавиши описаны в ТЗ §7.4 и собраны в
/// <see cref="ShellHotKeyMap"/>.
/// </para>
/// </remarks>
public sealed class MainShell : Form
{
    /// <summary>
    /// Маркер версии раскладки панели отображения: печатается acceptance-
    /// LAYOUTCHECK-ом в evidence каждого прогона — расхождение с бинарником
    /// доказывает исполнение устаревшей сборки (ENV3-01, SMB-инкрементал).
    /// </summary>
    public const string МаркерРаскладкиПанелиОтображения = "lay3-01-top-dock";

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

    /// <summary>
    /// Признак однократной подписки на выбор категории в дереве (F-E9):
    /// загрузка дерева выполняется при каждом обновлении данных.
    /// </summary>
    private bool _деревоКатегорийПодписано;

    /// <summary>
    /// Признак однократной подписки на перетаскивание отправленных писем
    /// (F-E10): загрузка списка выполняется при каждом обновлении данных.
    /// </summary>
    private bool _перетаскиваниеПисемПодписано;

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

    private readonly AddressEditor _адресОтправителя = new("Отправитель", показыватьЗаголовок: false);
    private readonly AddressEditor _адресПолучателя = new("Получатель", показыватьЗаголовок: false);
    private readonly TextBox _примечание = new();
    private readonly ToolTip _подсказки = new();

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

    /// <summary>
    /// Подсказки строки состояния: полный путь базы данных виден при
    /// наведении, в самой строке остаётся только имя файла (аудит UX
    /// 2026-11-03, F3).
    /// </summary>
    private readonly ToolTip _подсказкиСтрокиСостояния = new();

    private readonly TreeView _деревоКатегорий = new();
    private readonly ToolStripMenuItem _пунктИзбранного = new("Избранное (VIP)");
    private readonly ToolStripMenuItem _пунктОбъединить = new("Объединить в рассылку");
    private readonly ToolStripMenuItem _пунктПереместить = new("Переместить в категорию");
    private readonly ToolStripMenuItem _пунктПометитьОтправителем = new("Скопировать в таблицу «От кого»");

    /// <summary>
    /// Контекстное меню строк адресной книги. Хранится отдельно от меню
    /// заголовка: команды строки и команды столбца имеют разные области
    /// применения (ТЗ §8.11, функции 131 и 139).
    /// </summary>
    private ContextMenuStrip? _контекстноеМенюАдреснойКниги;

    /// <summary>Контекстное меню заголовка столбца адресной книги.</summary>
    private ContextMenuStrip? _контекстноеМенюСтолбцовАдреснойКниги;

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
    /// Контроллер группировки строк адресной книги сворачиваемыми
    /// заголовками (ТЗ §8.1, функция 10).
    /// </summary>
    private readonly RowGroupingController _группировкаПолучателей = new();

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
    /// Имя применённого сохранённого фильтра (F-E18): сброс при удалении
    /// выполняется только для этого имени.
    /// </summary>
    private string? _применённыйФильтрИмя;

    /// <summary>
    /// Встроенные быстрые фильтры, визуально доступные из меню «Вид →
    /// Фильтры». Сохранённые пользовательские фильтры остаются ниже и не
    /// смешиваются с фильтрами по избранному/категории.
    /// </summary>
    private readonly ToolStripMenuItem _пунктМенюИзбранного = new("Избранные");
    private readonly ToolStripMenuItem _пунктМенюВсехКатегорий = new("Все категории");
    private readonly ToolStripSeparator _разделительБыстрыхФильтров = new();
    private readonly List<ToolStripMenuItem> _пунктыМенюКатегорий = [];
    private ToolStripMenuItem? _менюФильтров;

    /// <summary>
    /// Пункт меню «Печать» с галкой «Показывать список шаблонов перед
    /// печатью» (ТЗ §8.6, функция 82).
    /// </summary>
    private ToolStripMenuItem? _пунктСпискаШаблонов;

    /// <summary>Пункт меню «Сохранять промежуточный XML данных реестров»
    /// (ТЗ §8.4, функция 58).</summary>
    private ToolStripMenuItem? _пунктПромежуточногоДампа;

    /// <summary>
    /// Создаёт главное окно оператора.
    /// </summary>
    public MainShell()
    {
        Text = "Picket302";
        Font = EnvelopeUiTheme.ШрифтТекста();
        BackColor = EnvelopeUiTheme.WorkspaceBackground;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 700);

        ПостроитьМеню();
        ПостроитьВкладки();
        ПрименитьАкцентыТаблиц();
        ПостроитьСтрокуСостояния();

        Controls.Add(_вкладки);
        Controls.Add(_меню);
        Controls.Add(_строкаСостояния);
        MainMenuStrip = _меню;

        // Единая тема применяется после построения дерева контролов, поэтому
        // одинаковый визуальный язык получают и основной экран, и ранее
        // созданные панели/таблицы, без вмешательства в обработчики.
        EnvelopeUiTheme.ОформитьОкно(this);
        ПрименитьАкцентыТаблиц();
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

    /// <summary>
    /// Подсказка пустого дерева папок (аудит UX 2026-11-03, D1).
    /// </summary>
    private readonly Label _подсказкаПапок = new();

    /// <summary>Метка состояния печатной сессии в строке состояния (ТЗ §7.2).</summary>
    private readonly Label _состояниеПечати = new();

    /// <summary>
    /// Локальный каталог клипартов (ТЗ §8.10, функция 158): встроенные
    /// элементы, вычерчиваемые кодом, без сети и внешних файлов.
    /// </summary>
    private readonly Picket302.Infrastructure.Templates.ClipartLibrary _клипарты = new();

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
    /// Текущий текст прямого набора в адресной книге: точка доступа
    /// автоматических проверок (F-E6).
    /// </summary>
    public string ПрямойНабор => _поиск.ПрямойНабор;

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
    /// Обновляет быстрый список категорий в меню фильтров. Список строится из
    /// того же справочника, что и дерево слева от таблицы, поэтому меню не
    /// содержит выдуманных legacy-значений и не дублирует источник данных.
    /// </summary>
    /// <param name="категории">Актуальные узлы дерева категорий.</param>
    private void ОбновитьМенюФильтров(IEnumerable<Category> категории)
    {
        if (_менюФильтров is null)
        {
            return;
        }

        foreach (var пункт in _пунктыМенюКатегорий)
        {
            _менюФильтров.DropDownItems.Remove(пункт);
            пункт.Dispose();
        }

        _пунктыМенюКатегорий.Clear();

        var индекс = _менюФильтров.DropDownItems.IndexOf(_разделительБыстрыхФильтров);

        foreach (var категория in категории)
        {
            var идентификатор = категория.Id;
            var отступ = категория.ParentId is null ? string.Empty : "  ";
            var пункт = new ToolStripMenuItem(отступ + категория.Name)
            {
                Tag = идентификатор,
            };

            пункт.Click += (_, _) => ВыбратьФильтрКатегории(идентификатор);
            _менюФильтров.DropDownItems.Insert(индекс++, пункт);
            _пунктыМенюКатегорий.Add(пункт);
        }

        ОбновитьСостояниеМенюФильтров();
    }

    /// <summary>Синхронизирует отметки быстрых фильтров с SearchSession.</summary>
    private void ОбновитьСостояниеМенюФильтров()
    {
        _пунктМенюИзбранного.Checked = _поиск.ТолькоИзбранное;
        _пунктМенюВсехКатегорий.Checked = _поиск.Группа is null;

        foreach (var пункт in _пунктыМенюКатегорий)
        {
            пункт.Checked = пункт.Tag is long идентификатор
                && _поиск.Группа == идентификатор;
        }
    }

    /// <summary>Выбирает категорию из быстрого меню фильтров.</summary>
    private void ВыбратьФильтрКатегории(long идентификатор)
    {
        _поиск.ЗадатьФильтрГрупп(идентификатор);
        ОбновитьСостояниеМенюФильтров();
        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>Снимает быстрый фильтр категории, сохраняя «Избранные».</summary>
    private void СброситьФильтрКатегории()
    {
        _поиск.ЗадатьФильтрГрупп(null);
        ОбновитьСостояниеМенюФильтров();
        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Открывает тот же быстрый фильтр из полосы действий адресной книги.
    /// Legacy-интерфейс показывал funnel-кнопку непосредственно над таблицей;
    /// здесь используется единый dropdown, чтобы действия из меню «Вид» и
    /// из рабочей области всегда имели одинаковое состояние.
    /// </summary>
    /// <param name="якорь">Кнопка, под которой нужно показать меню.</param>
    private void ПоказатьБыстрыеФильтры(Control якорь)
    {
        var меню = _менюФильтров?.DropDown;

        if (меню is null)
        {
            return;
        }

        ОбновитьСостояниеМенюФильтров();
        меню.Show(якорь, new Point(0, якорь.Height));
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

        // Имя применённого фильтра запоминается (F-E18): удаление другого
        // фильтра не должно сбрасывать действующий.
        _применённыйФильтрИмя = выбранное;

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

        // Сброс только при удалении применённого фильтра (F-E18): прежде
        // удаление любого фильтра сбрасывало действующий отбор.
        if (_применённыйФильтр is not null
            && string.Equals(выбранное, _применённыйФильтрИмя, StringComparison.Ordinal))
        {
            var оставшийся = await _сохранённыеФильтры
                .ПолучитьКритерииAsync(выбранное)
                .ConfigureAwait(true);

            if (оставшийся is null)
            {
                _применённыйФильтр = null;
                _применённыйФильтрИмя = null;
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
        => ИнициализироватьЯдроAsync(состав, AppServices.ПутьБазыПоУмолчанию(), null, cancellationToken);

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
        => ИнициализироватьЯдроAsync(null, путьБазы, null, cancellationToken);

    /// <summary>
    /// Инициализирует окно с составом, созданным по указанному профилю путей:
    /// точка изолированных автоматических проверок — все файлы профиля
    /// создаются под переданным корнем, реальные файлы пользователя не
    /// затрагиваются.
    /// </summary>
    /// <param name="пути">Пути профиля приложения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task ИнициализироватьAsync(
        Picket302ProfilePaths пути,
        CancellationToken cancellationToken = default)
        => ИнициализироватьЯдроAsync(null, пути.DatabasePath, пути, cancellationToken);

    /// <summary>
    /// Общая цепочка инициализации: получает состав (создаёт при
    /// необходимости), открывает данные и применяет оформление.
    /// </summary>
    /// <param name="состав">Переданный состав; при <see langword="null"/> состав
    /// создаётся по <paramref name="путьБазы"/>.</param>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="пути">
    /// Пути профиля для создаваемого состава; <see langword="null"/> —
    /// производственный профиль по умолчанию.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ИнициализироватьЯдроAsync(
        AppServices? состав,
        string путьБазы,
        Picket302ProfilePaths? пути,
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
            if (пути is null)
            {
                // Производственный запуск: сначала проверяется профиль
                // подключения к общей базе. Сопряжённое устройство обязано
                // подключиться к службе; недоступный сервер не подменяется
                // местной базой — оператор решает явно.
                var профильКлиента = new SharedClientProfile(
                    Picket302ProfilePaths.ПоУмолчанию().DataDirectory);

                var реквизиты = профильКлиента.Прочитать();
                var секрет = профильКлиента.ПрочитатьСекрет();

                if (реквизиты.Готовы && секрет is not null)
                {
                    try
                    {
                        _состав = await AppServices
                            .СоздатьОбщийAsync(
                                Picket302ProfilePaths.ПоУмолчанию(),
                                реквизиты,
                                секрет,
                                cancellationToken)
                            .ConfigureAwait(true);

                        _составСозданоВнутри = true;

                        await ИнициализироватьОбщийСоставAsync(_состав, cancellationToken)
                            .ConfigureAwait(true);

                        _инициализировано = true;
                        ОбновитьСтрокуСостояния(_состав);
                        ОбновитьИндикаторЛицензии(_состав);

                        return;
                    }
                    catch (SharedModeException ошибка)
                    {
                        if (!ПодтвердитьЛокальныйЗапуск(ошибка.Message))
                        {
                            throw;
                        }

                        // Явное решение оператора продолжить локально: это не
                        // автоматический переход, а осознанный выбор после
                        // сообщения о недоступности общей базы. Созданный
                        // общий состав освобождается: его транспорт и таймер
                        // счётчика объёма не должны продолжать опрос
                        // недоступного сервера.
                        if (_состав is not null)
                        {
                            await _состав.DisposeAsync().ConfigureAwait(true);
                            _состав = null;
                            _составСозданоВнутри = false;
                        }
                    }
                }
            }

            if (_состав is null)
            {
                _состав = пути is null
                    ? await AppServices
                        .СоздатьAsync(путьБазы, true, cancellationToken)
                        .ConfigureAwait(true)
                    : await AppServices
                        .СоздатьAsync(пути, true, cancellationToken)
                        .ConfigureAwait(true);
                _составСозданоВнутри = true;
            }
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
        ПрименитьАкцентыТаблиц();

        // Сортировка по любой колонке адресной книги и настройки вида таблицы:
        // цвета, заголовки столбцов, скрытие столбцов, «полосатая таблица»
        // (ТЗ §8.1, функция 9; §8.11, функция 131).
        _сортировкаПолучателей.Подключить(_таблицаПолучателей);

        // Группировка строк сворачиваемыми заголовками (ТЗ §8.1, функция 10);
        // перестраивается после каждой загрузки/сортировки/поиска.
        _группировкаПолучателей.Подключить(_таблицаПолучателей);

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
    /// Обновляет оформление и загружает данные после подключения к общей базе.
    /// Демо-наполнение в общем режиме не запускается: общая база наполняется
    /// только решением оператора.
    /// </summary>
    private async Task ИнициализироватьОбщийСоставAsync(
        AppServices состав,
        CancellationToken cancellationToken)
    {
        _оформление = await состав.НастройкиОтображения.LoadAppearanceAsync(cancellationToken)
            .ConfigureAwait(true);

        GridAppearanceManager.Применить(_таблицаОтправителей, _оформление);
        GridAppearanceManager.Применить(_таблицаПолучателей, _оформление);
        GridAppearanceManager.Применить(_таблицаБланков, _оформление);
        GridAppearanceManager.Применить(_таблицаРассылок, _оформление);
        GridAppearanceManager.Применить(_таблицаОтправленных, _оформление);
        ПрименитьАкцентыТаблиц();

        await ЗагрузитьОтправителейAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПолучателейAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьБланкиAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьРассылкиAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПапкиAsync(состав, cancellationToken).ConfigureAwait(true);

        // Незавершённые операции прошлой сессии: сначала контролируемая
        // очистка сроков хранения и проверка доступности истории.
        var хранилищеОпераций = состав.НезавершённыеОперацииStore;

        if (хранилищеОпераций is not null)
        {
            var очистка = хранилищеОпераций.ОчиститьУстаревшие();

            if (очистка.ОтказЗаписи)
            {
                MessageBox.Show(
                    this,
                    "Не удалось обновить хранилище незавершённых операций "
                    + "(отказ записи): сведения сохранены в памяти и на диске, "
                    + "повтор остаётся безопасным.",
                    "Общая база",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            if (хранилищеОпераций.Заблокировано)
            {
                MessageBox.Show(
                    this,
                    хранилищеОпераций.ПричинаБлокировки!,
                    "Общая база",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                if (хранилищеОпераций.ТребуетсяЯвныйСброс)
                {
                    var сброс = MessageBox.Show(
                        this,
                        "После сверки отправлений на сервере можно сбросить "
                        + "повреждённую историю незавершённых операций: файл "
                        + "сохранится рядом для диагностики. Сверка выполнена — "
                        + "сбросить сейчас?",
                        "Общая база",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);

                    if (сброс == DialogResult.Yes
                        && хранилищеОпераций.ЯвныйСбросПовреждённогоФайла())
                    {
                        MessageBox.Show(
                            this,
                            "Повреждённая история сброшена, новые изменения "
                            + "разблокированы. Исход операций сброшенной "
                            + "истории неизвестен — проверьте отправления "
                            + "на сервере.",
                            "Общая база",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }

                // Список незавершённых операций недоступен: окно повтора не
                // открывается, мутации заблокированы хранилищем.
                return;
            }
        }

        if (состав.Данные.НезавершённыеОперации.Count > 0)
        {
            var ответ = MessageBox.Show(
                this,
                $"На этом компьютере есть незавершённые операции прошлой сессии "
                + $"({состав.Данные.НезавершённыеОперации.Count}): ответ сервера был потерян "
                + "при сетевом сбое, исход неизвестен — операция могла быть выполнена. "
                + "Открыть окно повтора? Операции будут отправлены с прежними "
                + "идентификаторами, дублей не возникнет.",
                "Общая база",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);

            if (ответ == DialogResult.Yes && FormSharedPendingOperations.Показать(this, состав.Данные))
            {
                ОбновитьВсе();
            }
        }
    }

    /// <summary>
    /// Спрашивает оператора, продолжать ли локально, когда общая база
    /// недоступна. Автоматического перехода нет: решение принимает человек.
    /// </summary>
    /// <param name="сообщение">Сообщение о недоступности.</param>
    /// <returns><see langword="true"/>, если оператор выбрал локальный запуск.</returns>
    private bool ПодтвердитьЛокальныйЗапуск(string сообщение)
    {
        var ответ = MessageBox.Show(
            this,
            сообщение + Environment.NewLine + Environment.NewLine
            + "Продолжить работу с локальной базой на этом компьютере? "
            + "Общие данные при этом не видны и не изменяются.",
            "Общая база недоступна",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return ответ == DialogResult.Yes;
    }

    /// <summary>
    /// Строит главное меню окна (ТЗ §7.1: «Файл Правка Вид Сервис Печать
    /// Рассылки Справка»).
    /// </summary>
    private void ПостроитьМеню()
    {
        _меню.Font = EnvelopeUiTheme.ШрифтТекста();

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
        _менюФильтров = фильтры;

        _пунктМенюИзбранного.Click += (_, _) => ПереключитьФильтрИзбранного();
        _пунктМенюВсехКатегорий.Click += (_, _) => СброситьФильтрКатегории();
        фильтры.DropDownItems.Add(_пунктМенюИзбранного);
        фильтры.DropDownItems.Add(_пунктМенюВсехКатегорий);
        фильтры.DropDownItems.Add(_разделительБыстрыхФильтров);
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
        сервис.DropDownItems.Add("Общая база…", null, (_, _) => ОткрытьПодключениеКОбщейБазе());
        сервис.DropDownItems.Add("Незавершённые операции…", null, (_, _) => ОткрытьНезавершённыеОперации());
        сервис.DropDownItems.Add("Демо-база", null, (_, _) => НаполнитьДемоБазу());
        сервис.DropDownItems.Add("Восстановить удалённые записи", null, (_, _) => ВосстановитьУдалённые());
        сервис.DropDownItems.Add("Создать отправителя…", null, (_, _) => СоздатьОтправителя());

        // Входящая корреспонденция и движение документов (ТЗ §8.2,
        // функции 29 и 30).
        сервис.DropDownItems.Add("Входящая корреспонденция…", null, (_, _) => ОткрытьВходящуюКорреспонденцию());

        // Трекер отправлений Почты России (ТЗ §8.8, функция 110).
        сервис.DropDownItems.Add("Трекер Почты России…", null, (_, _) => ОткрытьТрекерПочты());
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
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Текущий конверт (F5)",
            EnvelopeUiTheme.СоздатьЗначок("print"),
            (_, _) => ПечатьТекущийКонверт()));
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Выделенные с предпросмотром (F6)",
            EnvelopeUiTheme.СоздатьЗначок("preview"),
            (_, _) => ПечатьВыделенные(сПредпросмотром: true)));
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Выделенные без предпросмотра (F7)",
            EnvelopeUiTheme.СоздатьЗначок("print"),
            (_, _) => ПечатьВыделенные(сПредпросмотром: false)));
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Редактор бланков (F8)",
            EnvelopeUiTheme.СоздатьЗначок("edit"),
            (_, _) => ОткрытьРедакторБланков()));

        // Специальные бланки (ТЗ §8.3, функция 49): доверенность на почту
        // и заявление о включении в реестр кредиторов — подстановка
        // реквизитов выбранного отправителя, предпросмотр и печать/сохранение.
        var специальные = new ToolStripMenuItem(
            "Специальные бланки",
            EnvelopeUiTheme.СоздатьЗначок("form"));
        специальные.DropDownItems.Add(new ToolStripMenuItem(
            "Доверенность на получение почты",
            null,
            (_, _) => ПоказатьСпециальныйБланк(
                Picket302.Infrastructure.Templates.SpecialFormType.PowerOfAttorney)));
        специальные.DropDownItems.Add(new ToolStripMenuItem(
            "Заявление о включении в реестр кредиторов",
            null,
            (_, _) => ПоказатьСпециальныйБланк(
                Picket302.Infrastructure.Templates.SpecialFormType.CreditorApplication)));
        печать.DropDownItems.Add(специальные);

        // Пользовательские отчёты (ТЗ §8.4, функция 56): реестр пакетов
        // «макет .lbk + query.xml», регистрация, правка запроса и предпросмотр
        // данных поверх текущей базы.
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Пользовательские отчёты…",
            EnvelopeUiTheme.СоздатьЗначок("form"),
            (_, _) => ОткрытьПользовательскиеОтчёты()));

        печать.DropDownItems.Add(new ToolStripSeparator());
        печать.DropDownItems.Add(new ToolStripMenuItem(
            "Настройки принтера…",
            EnvelopeUiTheme.СоздатьЗначок("settings"),
            (_, _) => НастройкиПринтера()));
        печать.DropDownItems.Add(new ToolStripSeparator());

        _пунктСпискаШаблонов = new ToolStripMenuItem("Показывать список шаблонов перед печатью")
        {
            CheckOnClick = true,
            Checked = true,
        };

        _пунктСпискаШаблонов.CheckedChanged += async (_, _) =>
            await СохранитьОпциюСпискаШаблоновAsync(_пунктСпискаШаблонов.Checked).ConfigureAwait(true);

        печать.DropDownItems.Add(_пунктСпискаШаблонов);

        // Промежуточные данные реестров во временный XML (ТЗ §8.4, функция 58):
        // опция действует на тихую печать реестров и вывод дампа датасета.
        _пунктПромежуточногоДампа = new ToolStripMenuItem("Сохранять промежуточный XML данных реестров")
        {
            CheckOnClick = true,
            Checked = false,
        };

        _пунктПромежуточногоДампа.CheckedChanged += async (_, _) =>
            await СохранитьОпциюПромежуточногоДампаAsync(_пунктПромежуточногоДампа.Checked).ConfigureAwait(true);

        печать.DropDownItems.Add(_пунктПромежуточногоДампа);

        var рассылки = new ToolStripMenuItem("Рассылки");
        рассылки.DropDownItems.Add("Новая рассылка…", null, (_, _) => СоздатьРассылку());
        рассылки.DropDownItems.Add("Изменить рассылку…", null, (_, _) => ИзменитьРассылку());
        рассылки.DropDownItems.Add("Печать рассылки…", null, (_, _) => ПечатьРассылок());
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
        _вкладки.Font = EnvelopeUiTheme.ШрифтТекста();

        // Светлый нейтральный фон вместо серого Control (аудит UX 2026-11-03,
        // G3): пустые зоны экрана читаются как рабочее поле, а не как «поломка».
        foreach (var страница in new[] { _страницаАдреснойКниги, _страницаКонвертов, _страницаРассылок, _страницаОтправленных })
        {
            страница.BackColor = ЦветФона();
        }

        _страницаАдреснойКниги.Controls.Add(ПостроитьРабочуюОбласть());
        _страницаКонвертов.Controls.Add(ПостроитьПанельТаблицы(
            _таблицаБланков,
            ПолосаДействийБланков(),
            "Бланков нет. Нажмите «Редактор бланков (F8)», чтобы создать шаблон конверта."));
        _страницаРассылок.Controls.Add(ПостроитьПанельТаблицы(
            _таблицаРассылок,
            ПолосаДействийРассылок(),
            "Рассылок пока нет. Нажмите «Новая рассылка…», чтобы собрать получателей в рассылку."));
        _таблицаРассылок.CellDoubleClick += (_, _) => ИзменитьРассылку();
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
    ///
    /// Корневой горизонтальный <see cref="SplitContainer"/> занимает всю
    /// клиентскую площадь вкладки: сверху — редакторы адресов и параметры
    /// отображения, снизу — таблицы отправителей и получателей. Прежняя схема
    /// «фиксированный верх 220 px + нижний сплиттер» не адаптировалась к
    /// высоте окна (аудит UX 2026-11-03, G2; редизайн 2026-11-05, этап 1).
    /// </summary>
    private Control ПостроитьРабочуюОбласть()
    {
        var корень = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = EnvelopeUiTheme.WorkspaceBackground,
        };

        корень.Panel1.Controls.Add(ПостроитьВерхнююПанельАдреснойКниги());
        корень.Panel2.Controls.Add(ПостроитьНижнююПанельАдреснойКниги());

        // Верхняя зона занимает фиксированную долю высоты, но не меньше
        // роста редактора адреса и не больше, чем «таблицы + заголовок».
        // Минимальные размеры панелей задаются лениво: присвоение
        // Panel1MinSize/Panel2MinSize до лэйаута валидируется против
        // конструкторского размера 150×100 и выбрасывает исключение.
        НастроитьРазделитель(корень, желаемое: 252, минимумПервой: 236, минимумВторой: 220);

        return корень;
    }

    /// <summary>
    /// Безопасно задаёт минимальные размеры панелей и расстояние разделителя
    /// после создания дескриптора и первого достоверного лэйаута: значения
    /// ограничиваются свободным остатком, поэтому слишком маленькое окно не
    /// вызывает исключения и не даёт разделителю «уезжать» (аудит UX
    /// 2026-11-03, A2; этап 1, п. 5).
    /// </summary>
    /// <param name="разделитель">Разделитель.</param>
    /// <param name="желаемое">Желаемое расстояние до разделителя.</param>
    /// <param name="минимумПервой">Минимальный размер первой панели.</param>
    /// <param name="минимумВторой">Минимальный размер второй панели.</param>
    private static void НастроитьРазделитель(
        SplitContainer разделитель,
        int желаемое,
        int минимумПервой,
        int минимумВторой)
    {
        var применено = false;

        void Применить()
        {
            if (применено || !разделитель.IsHandleCreated)
            {
                return;
            }

            var доступно = разделитель.Orientation == Orientation.Horizontal
                ? разделитель.Height
                : разделитель.Width;

            if (доступно < минимумПервой + минимумВторой + разделитель.SplitterWidth)
            {
                return; // размер ещё не достоверен — ждём следующего лэйаута
            }

            try
            {
                разделитель.Panel1MinSize = минимумПервой;
                разделитель.Panel2MinSize = минимумВторой;

                var максимум = доступно - разделитель.SplitterWidth - минимумВторой;
                разделитель.SplitterDistance = Math.Clamp(желаемое, минимумПервой, максимум);
                применено = true;
            }
            catch (InvalidOperationException)
            {
                // Промежуточное состояние лэйаута: повторим на следующем
                // событии, без исключения наружу.
            }
        }

        разделитель.HandleCreated += (_, _) => Применить();
        разделитель.Layout += (_, _) => Применить();
    }

    /// <summary>
    /// Строит верхнюю панель вкладки «Адресная книга»: колонка отправителя,
    /// колонка получателя и колонка параметров отображения/логотипа
    /// (этап 1, п. 3). Прокрутка включается только на этой внутренней панели,
    /// когда колонки физически не помещаются в ширину окна.
    /// </summary>
    private Control ПостроитьВерхнююПанельАдреснойКниги()
    {
        var верх = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoScroll = true,
            Padding = new Padding(2, 2, 2, 0),
            BackColor = EnvelopeUiTheme.WorkspaceBackground,
        };

        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        верх.Controls.Add(ГруппаАдреса(
            "Отправитель",
            _адресОтправителя,
            EnvelopeUiTheme.SenderAccent), 0, 0);
        верх.Controls.Add(ГруппаАдреса(
            "Получатель",
            _адресПолучателя,
            EnvelopeUiTheme.RecipientAccent), 1, 0);
        верх.Controls.Add(ПостроитьПанельОтображения(), 2, 0);

        // Подбор значения поля из имеющихся записей: пустое поле и BackSpace
        // (ТЗ §7.7; §8.1, функция 133).
        _адресОтправителя.ТребуетсяПодборЗначения += (_, _) => ПодобратьЗначениеПоля(_адресОтправителя);
        _адресПолучателя.ТребуетсяПодборЗначения += (_, _) => ПодобратьЗначениеПоля(_адресПолучателя);

        return верх;
    }

    /// <summary>
    /// Строит нижнюю панель вкладки «Адресная книга»: вертикальный
    /// разделитель со справочником отправителей слева и адресной книгой
    /// получателей справа (этап 1, п. 4).
    /// </summary>
    private Control ПостроитьНижнююПанельАдреснойКниги()
    {
        var разделитель = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = EnvelopeUiTheme.WorkspaceBackground,
        };

        НастроитьРазделитель(разделитель, желаемое: 330, минимумПервой: 240, минимумВторой: 320);

        разделитель.Panel1.Controls.Add(ПостроитьПанельОтправителей());
        разделитель.Panel2.Controls.Add(ПостроитьПанельПолучателей());

        return разделитель;
    }

    /// <summary>
    /// Оборачивает редактор адреса в групповую рамку визуального рабочего
    /// экрана. Сам редактор остаётся отдельным компонентом, поэтому его
    /// поведение одинаково в главной форме и в карточках контрагентов.
    /// </summary>
    /// <param name="заголовок">Заголовок группы.</param>
    /// <param name="редактор">Редактор адреса.</param>
    /// <param name="акцент">Цвет заголовка.</param>
    private static GroupBox ГруппаАдреса(string заголовок, AddressEditor редактор, Color акцент)
    {
        var группа = new GroupBox
        {
            Text = заголовок,
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 0),
        };

        EnvelopeUiTheme.ОформитьГруппу(группа, акцент);

        редактор.Dock = DockStyle.Fill;
        редактор.Margin = Padding.Empty;
        редактор.ForeColor = SystemColors.ControlText;
        группа.Controls.Add(редактор);

        return группа;
    }

    /// <summary>
    /// Строит панель «ОТОБРАЖЕНИЕ» с параметрами печати и превью конверта.
    /// Пары переключателей разложены таблицей по двум подписанным строкам —
    /// «Логотип» и «Адрес на конверте» — вместо координатной раскладки
    /// (аудит UX 2026-11-03, A4/G2).
    /// </summary>
    private Control ПостроитьПанельОтображения()
    {
        var панель = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(2, 0, 2, 0),
            BackColor = EnvelopeUiTheme.WorkspaceBackground,
        };

        var группаЛоготипа = new GroupBox
        {
            Text = "Вид логотипа",
            Dock = DockStyle.Top,

            // Высота следует за содержимым: при переносе переключателей на
            // узкой колонке (окно 1000 px) рамка растёт, подписи не обрезаются
            // (аудит 2026-11-08).
            AutoSize = true,
        };
        EnvelopeUiTheme.ОформитьГруппу(группаЛоготипа, EnvelopeUiTheme.DisplayAccent);

        // Каждая пара переключателей — собственная строка таблицы без рамки
        // (F-E16): в общем контейнере отметка «Адрес получателя» снимала
        // «С логотипом».
        // Dock=Top, а не Fill (регрессия LAY3-01, 2026-11-09): при Fill
        // AutoSize таблицы игнорируется — PreferredSize считался по
        // НЕперенесённым высотам рядов, а при реальной раскладке в узкой
        // колонке ряды переносились и переполняли зажатую высоту (нижний
        // ряд «Клипарт» уходил за клиент на ~77 px на 96 DPI). Dock=Top
        // растит таблицу (и группу) по фактическому содержимому.
        var переключатели = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 2, 4, 2),
        };

        переключатели.RowCount = 5;
        for (var строка = 0; строка < 5; строка++)
        {
            переключатели.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var подписьЛоготипа = new Label
        {
            Text = "Логотип:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = EnvelopeUiTheme.ШрифтЗаголовка(8f),
            ForeColor = EnvelopeUiTheme.MutedText,
            Margin = new Padding(2, 2, 2, 0),
            AutoSize = true,
        };

        // Без Dock=Fill (регрессия LAY3-01): AutoSize-ряд с Dock=Fill давал
        // PreferredSize без учёта переноса; без Dock ряд честно считает
        // высоту при ширине ячейки и переносит содержимое.
        var рядЛоготипа = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,

            // Перенос вместо обрезания: подписи «С логотипом»/«Без логотипа»
            // остаются доступны на любой ширине колонки (аудит 2026-11-08).
            WrapContents = true,
            Margin = new Padding(2, 0, 2, 4),
        };

        var подписьАдреса = new Label
        {
            Text = "Адрес на конверте:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = EnvelopeUiTheme.ШрифтЗаголовка(8f),
            ForeColor = EnvelopeUiTheme.MutedText,
            Margin = new Padding(2, 2, 2, 0),
            AutoSize = true,
        };

        var рядАдреса = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(2, 0, 2, 4),
        };

        // AutoSize устанавливается переключателям, а не составным редакторам
        // адреса (F-E23).
        _сЛоготипом.AutoSize = true;
        _сЛоготипом.Margin = new Padding(2, 1, 12, 1);
        _сЛоготипом.Text = "С логотипом";
        _сЛоготипом.AccessibleName = "Печать с логотипом получателя";

        _безЛоготипа.AutoSize = true;
        _безЛоготипа.Margin = new Padding(2, 1, 12, 1);
        _безЛоготипа.Text = "Без логотипа";
        _безЛоготипа.AccessibleName = "Печать без логотипа";

        _переключательАдресОтправителя.AutoSize = true;
        _переключательАдресОтправителя.Margin = new Padding(2, 1, 12, 1);
        _переключательАдресОтправителя.Text = "Отправителя";
        _переключательАдресОтправителя.AccessibleName = "Печатать адрес отправителя";

        _переключательАдресПолучателя.AutoSize = true;
        _переключательАдресПолучателя.Margin = new Padding(2, 1, 12, 1);
        _переключательАдресПолучателя.Text = "Получателя";
        _переключательАдресПолучателя.AccessibleName = "Печатать адрес получателя";

        _сЛоготипом.Checked = true;
        _переключательАдресОтправителя.Checked = true;

        рядЛоготипа.Controls.Add(_сЛоготипом);
        рядЛоготипа.Controls.Add(_безЛоготипа);
        рядАдреса.Controls.Add(_переключательАдресОтправителя);
        рядАдреса.Controls.Add(_переключательАдресПолучателя);

        // Локальный каталог клипартов (ТЗ §8.10, функция 158): выбор
        // графического элемента для выбранного получателя и его удаление.
        var рядКлипарта = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(2, 2, 2, 0),
        };

        var клипарт = Кнопка("Клипарт…", "Выбрать графический элемент из локального каталога и назначить его логотипом получателя");
        клипарт.Click += (_, _) => ВыбратьКлипартПолучателя();

        var убратьЛоготип = Кнопка("Убрать логотип", "Удалить логотип у выбранного получателя");
        убратьЛоготип.Click += (_, _) => УбратьЛоготипПолучателя();

        рядКлипарта.Controls.Add(клипарт);
        рядКлипарта.Controls.Add(убратьЛоготип);

        переключатели.Controls.Add(подписьЛоготипа, 0, 0);
        переключатели.Controls.Add(рядЛоготипа, 0, 1);
        переключатели.Controls.Add(подписьАдреса, 0, 2);
        переключатели.Controls.Add(рядАдреса, 0, 3);
        переключатели.Controls.Add(рядКлипарта, 0, 4);

        var контейнерПревью = new Panel
        {
            Dock = DockStyle.Right,

            // Заголовок «Превью конверта C5» при 8 px отступе требует
            // ~115 px текста: 148 px не обрезает его на любом масштабе
            // шрифта (аудит 2026-11-08).
            Width = 148,
            Padding = new Padding(4, 4, 4, 4),
        };

        _превью.BorderStyle = BorderStyle.FixedSingle;
        _превью.Dock = DockStyle.Fill;
        _превью.BackColor = SystemColors.Window;
        _превью.Paint += (_, событие) =>
        {
            using var шрифт = EnvelopeUiTheme.ШрифтТекста(8.5f);
            событие.Graphics.DrawString(
                "Превью конверта C5",
                шрифт,
                SystemBrushes.ControlText,
                new Point(8, 8));
        };

        контейнерПревью.Controls.Add(_превью);
        группаЛоготипа.Controls.Add(переключатели);
        группаЛоготипа.Controls.Add(контейнерПревью);

        var группаПримечания = new GroupBox
        {
            Text = "Примечание к адресу",
            Dock = DockStyle.Fill,
        };
        EnvelopeUiTheme.ОформитьГруппу(группаПримечания, EnvelopeUiTheme.NoteAccent);

        _примечание.Dock = DockStyle.Fill;
        _примечание.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
        _примечание.Margin = Padding.Empty;
        группаПримечания.Controls.Add(_примечание);

        панель.Controls.Add(группаПримечания);
        панель.Controls.Add(группаЛоготипа);

        return панель;
    }

    /// <summary>
    /// Строит панель «СПРАВОЧНИК ОТПРАВИТЕЛЕЙ».
    /// </summary>
    private Control ПостроитьПанельОтправителей()
    {
        var панель = new GroupBox
        {
            Text = "От кого",
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 0),
        };
        EnvelopeUiTheme.ОформитьГруппу(панель, EnvelopeUiTheme.SenderAccent);

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(1, 2, 1, 1),
            BackColor = EnvelopeUiTheme.PanelBackground,
        };

        var добавить = Кнопка("Добавить", "Добавить отправителя");
        добавить.Click += (_, _) => СоздатьОтправителя();

        var изменить = Кнопка("Изменить", "Изменить отправителя");
        изменить.Click += (_, _) => ИзменитьОтправителя();

        var удалить = Кнопка("Удалить", "Удалить отправителя");
        удалить.Click += (_, _) => УдалитьВыделенные();

        кнопки.Controls.Add(добавить);
        кнопки.Controls.Add(изменить);
        кнопки.Controls.Add(удалить);

        _таблицаОтправителей.Columns.Add("Индекс", "Индекс");
        _таблицаОтправителей.Columns.Add("Наименование", "Наименование");
        _таблицаОтправителей.Columns[0].Width = 70;

        // Наименование заполняет свободную ширину грида: без Fill справа от
        // колонок оставалось серое поле (аудит UX 2026-11-03, A2).
        _таблицаОтправителей.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _таблицаОтправителей.SelectionChanged += (_, _) => ОбновитьВыделенное();

        // Выбор отправителя по первым буквам наименования
        // (ТЗ §8.1, функция 16). Подпись называет действие прямо, а не
        // дублирует назначение раздела (аудит UX 2026-11-03, A7).
        var меткаПоиска = new Label
        {
            Text = "Поиск по названию:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Height = 16,
            Font = EnvelopeUiTheme.ШрифтТекста(8.5f),
            TabIndex = 0,
        };

        _поискОтправителя.Dock = DockStyle.Top;
        _поискОтправителя.Height = 20;
        _поискОтправителя.TabIndex = 1;
        _поискОтправителя.AccessibleName = "Поиск отправителя по названию";
        _поискОтправителя.TextChanged += (_, _) => ВыбратьОтправителяПоПрефиксу();

        // Таблица занимает остаток панели (Dock = Fill): без причаливания она
        // лежала в точке (0,0) поверх кнопок и заголовка, и те были не видны
        // (аудит UX 2026-11-03, A2). Контролы обрабатываются с конца списка,
        // поэтому заполняющая таблица добавляется первой.
        _таблицаОтправителей.Dock = DockStyle.Fill;
        _таблицаОтправителей.TabIndex = 3;
        панель.Controls.Add(_таблицаОтправителей);

        // Подсказка пустого справочника (аудит UX 2026-11-03, G5): без
        // записей таблица объясняет первое действие.
        var подсказкаОтправителей = UI.Components.EmptyStateHint.Создать(
            _таблицаОтправителей,
            "Справочник отправителей пуст. Нажмите «Добавить», чтобы завести первого отправителя.");
        подсказкаОтправителей.Dock = DockStyle.Fill;
        панель.Controls.Add(подсказкаОтправителей);
        панель.Controls.Add(_поискОтправителя);
        панель.Controls.Add(меткаПоиска);
        панель.Controls.Add(кнопки);

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
        var панель = new GroupBox
        {
            Text = "Кому",
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 0),
        };
        EnvelopeUiTheme.ОформитьГруппу(панель, EnvelopeUiTheme.RecipientAccent);

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(1, 2, 1, 1),
            BackColor = EnvelopeUiTheme.PanelBackground,
        };

        // Дополнительные действия — отдельная полоса НИЖЕ первичной: перенос
        // предсказуем (две фиксированные полосы), одна кнопка не уходит
        // «сама по себе» на третью строку (аудит 2026-11-08).
        var дополнительные = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(1, 1, 1, 2),
            BackColor = EnvelopeUiTheme.PanelBackground,
        };

        var добавить = Кнопка("Добавить", "Добавить получателя");
        добавить.Click += (_, _) => ДобавитьПолучателя();

        var изменить = Кнопка("Изменить", "Изменить получателя");
        изменить.Click += (_, _) => ИзменитьПолучателя();

        var удалить = Кнопка("Удалить", "Удалить получателя");
        удалить.Click += (_, _) => УдалитьВыделенные();

        var фильтры = Кнопка(
            "Фильтры…",
            "Быстрый отбор по избранному и категориям адресной книги",
            значок: "filter");
        фильтры.Click += (_, _) => ПоказатьБыстрыеФильтры(фильтры);

        var печать = Кнопка(
            "Печать текущего конверта",
            "Напечатать конверт по редактору и выбранной записи (F5)",
            значок: "print");
        печать.AccessibleName = "Печать текущего конверта";
        печать.AccessibleDescription = "Печатает конверт по текущим адресам редакторов. Горячая клавиша F5.";
        печать.Click += (_, _) => ПечатьТекущийКонверт();

        var превью = Кнопка(
            "Предпросмотр и печать",
            "Печать выделенных получателей с подтверждением через окно предпросмотра (F6)",
            значок: "preview");
        превью.AccessibleName = "Предпросмотр и печать выбранных получателей";
        превью.AccessibleDescription = "Запускает печать выбранных получателей с подтверждением через окно предпросмотра. Горячая клавиша F6.";
        превью.Click += (_, _) => ПечатьВыделенные(сПредпросмотром: true);

        var вExcel = Кнопка("Открыть список в Excel", "Выгрузить текущую таблицу получателей в Excel (XLSX)");
        вExcel.Click += (_, _) => ОткрытьСписокВExcel();

        // Аудит UX 2026-11-03 (E1): кнопка «⚙» выглядела как настройки, но
        // молча сохраняла конфигурацию таблицы. Теперь обе операции названы
        // прямо: сохранение вида (ТЗ §7.6) и диалог оформления (ТЗ §8.11).
        var сохранитьВид = Кнопка("Сохранить вид таблицы", "Запомнить видимость, подписи и ширины столбцов (ТЗ §7.6)");
        сохранитьВид.Click += (_, _) => НастроитьТаблицу();

        var видТаблиц = Кнопка("Вид таблиц…", "Открыть настройки оформления таблиц (ТЗ §8.11, функция 131)");
        видТаблиц.Click += (_, _) => НастроитьОформление();

        кнопки.Controls.Add(добавить);
        кнопки.Controls.Add(изменить);
        кнопки.Controls.Add(удалить);
        кнопки.Controls.Add(фильтры);
        кнопки.Controls.Add(печать);
        кнопки.Controls.Add(превью);

        дополнительные.Controls.Add(вExcel);
        дополнительные.Controls.Add(сохранитьВид);
        дополнительные.Controls.Add(видТаблиц);

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

        // «Группа» заполняет свободную ширину — без серого поля справа
        // (аудит UX 2026-11-03, A2).
        _таблицаПолучателей.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _таблицаПолучателей.CellMouseDown += ОбработатьНажатиеЯчейкиАдреснойКниги;
        _таблицаПолучателей.CellContextMenuStripNeeded += ОбработатьКонтекстноеМенюАдреснойКниги;
        _таблицаПолучателей.SelectionChanged += (_, _) => ОбновитьВыделенное();
        _таблицаПолучателей.CellDoubleClick += (_, _) => ИзменитьПолучателя();
        _таблицаПолучателей.KeyDown += ОбработатьНаборВТаблице;
        _таблицаПолучателей.KeyPress += ОбработатьНаборВТаблицеПоСимволу;

        var контекстноеМеню = ПостроитьКонтекстноеМенюАдреснойКниги();
        _контекстноеМенюАдреснойКниги = контекстноеМеню;
        _пунктИзбранного.Checked = false;
        _пунктИзбранного.Click += (_, _) => ПереключитьФильтрИзбранного();
        _пунктОбъединить.Click += (_, _) => ОбъединитьВРассылку();
        _пунктПереместить.Click += (_, _) => ПереместитьВКатегорию();
        _пунктПометитьОтправителем.Click += (_, _) => ПометитьОтправителем();

        _таблицаПолучателей.ContextMenuStrip = контекстноеМеню;

        // Таблица занимает остаток панели (Dock = Fill) — см. пояснение в
        // панели отправителей (аудит UX 2026-11-03, A2).
        _таблицаПолучателей.Dock = DockStyle.Fill;
        _таблицаПолучателей.TabIndex = 1;
        панель.Controls.Add(_таблицаПолучателей);

        // Подсказка пустой адресной книги (аудит UX 2026-11-03, G5).
        var подсказкаПолучателей = UI.Components.EmptyStateHint.Создать(
            _таблицаПолучателей,
            "В адресной книге нет записей. Нажмите «Добавить», чтобы создать первого получателя.");
        подсказкаПолучателей.Dock = DockStyle.Fill;
        панель.Controls.Add(подсказкаПолучателей);

        // Порядок добавления = порядок закрепления снизу вверх (WinForms
        // укладывает Dock=Top в обратном порядке коллекции): первичная
        // полоса сверху, дополнительные действия под ней.
        панель.Controls.Add(дополнительные);
        панель.Controls.Add(кнопки);

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
        };

        // Граница панелей — после создания дескриптора с ограничениями, как и
        // в рабочей области адресной книги (аудит UX 2026-11-03, A2).
        НастроитьРазделитель(разделитель, желаемое: 220, минимумПервой: 160, минимумВторой: 320);

        _деревоПапок.Dock = DockStyle.Fill;
        _деревоПапок.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
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
            Text = "Папки писем",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = EnvelopeUiTheme.ШрифтЗаголовка(8.5f),
        };

        // Подсказка пустого дерева (аудит UX 2026-11-03, D1): без папок
        // раздел выглядел сломанным, а назначение дерева было неочевидно.
        _подсказкаПапок.Text = "Папок пока нет. Папки появляются вместе с рассылками; письма можно перетаскивать в папки мышью.";
        _подсказкаПапок.Dock = DockStyle.Top;
        _подсказкаПапок.ForeColor = Color.FromArgb(71, 85, 105);
        _подсказкаПапок.Font = EnvelopeUiTheme.ШрифтТекста(8.5f, FontStyle.Italic);
        _подсказкаПапок.Padding = new Padding(4, 2, 4, 2);

        // Начальное состояние без базы — пустое дерево скрыто сразу: пустой
        // TreeView показывает бессмысленную полосу прокрутки (D3).
        _подсказкаПапок.Visible = true;
        _деревоПапок.Visible = false;
        ПоказанаПодсказкаПустыхПапок = true;

        var панельПапок = new Panel { Dock = DockStyle.Fill };
        панельПапок.Controls.Add(_деревоПапок);
        панельПапок.Controls.Add(_подсказкаПапок);
        панельПапок.Controls.Add(заголовок);

        разделитель.Panel1.Controls.Add(панельПапок);
        разделитель.Panel2.Controls.Add(ПостроитьПанельТаблицы(
            _таблицаОтправленных,
            ПолосаДействийОтправленных(),
            "Отправленных писем нет."));

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
            var письмо = await _состав.Данные
                .ПолучитьПисьмоAsync(идентификаторПисьма)
                .ConfigureAwait(true);

            if (письмо is null)
            {
                return;
            }

            await _состав.Данные
                .ПеренестиПисьмоAsync(письмо, идентификаторПапки)
                .ConfigureAwait(true);

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
        var папки = await состав.Данные.ПолучитьПапкиAsync(cancellationToken).ConfigureAwait(true);

        // Счётчики агрегатом по всем папкам (F-E21): выборка первой страницы
        // давала неполные счётчики при письмах глубже 200 записей.
        var счётчики = await состав.Данные
            .ПолучитьСчётчикиПоПапкамAsync(cancellationToken)
            .ConfigureAwait(true);

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

        // Пустое дерево сопровождается подсказкой (аудит UX 2026-11-03, D1),
        // а само дерево скрывается: пустой TreeView показывал бессмысленную
        // горизонтальную полосу прокрутки и выглядел сломанным (D3).
        ОбновитьПустотуДерева();
    }

    /// <summary>
    /// Синхронизирует состояние раздела папок с составом дерева: без папок
    /// показывается подсказка, дерево скрывается вместе со своей полосой
    /// прокрутки; при появлении папок дерево возвращается (D1/D3).
    /// </summary>
    private void ОбновитьПустотуДерева()
    {
        var пусто = _деревоПапок.Nodes.Count == 0;

        _подсказкаПапок.Visible = пусто;
        _деревоПапок.Visible = !пусто;
        ПоказанаПодсказкаПустыхПапок = пусто;
    }

    /// <summary>
    /// Признак показа подсказки пустого дерева папок «Отправленные»:
    /// зеркало состояния раздела для проверок компоновки без показа окна
    /// (эффективный <see cref="Control.Visible"/> скрытой формы всегда ложен).
    /// </summary>
    public bool ПоказанаПодсказкаПустыхПапок { get; private set; }

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
    /// «Отправленные»: полоса действий сверху, таблица на всю оставшуюся
    /// площадь, подсказка вместо пустого грида (аудит UX 2026-11-03:
    /// U2/B1/C1 — экраны без действий; B2/C3 — таблицы в углу пустого поля).
    /// </summary>
    /// <param name="таблица">Таблица раздела.</param>
    /// <param name="действия">Полоса действий; <see langword="null"/> — без полосы.</param>
    /// <param name="подсказкаПустоты">
    /// Текст, показываемый вместо пустой таблицы; <see langword="null"/> — не показывать.
    /// </param>
    private static Control ПостроитьПанельТаблицы(
        DataGridView таблица,
        ToolStrip? действия = null,
        string? подсказкаПустоты = null)
    {
        var панель = new Panel { Dock = DockStyle.Fill };
        таблица.Dock = DockStyle.Fill;
        панель.Controls.Add(таблица);

        if (подсказкаПустоты is not null)
        {
            var подсказка = UI.Components.EmptyStateHint.Создать(таблица, подсказкаПустоты);
            подсказка.Dock = DockStyle.Fill;
            панель.Controls.Add(подсказка);
        }

        if (действия is not null)
        {
            действия.Dock = DockStyle.Top;
            панель.Controls.Add(действия);
        }

        return панель;
    }

    /// <summary>
    /// Строит полосу действий раздела: плоская лента без захвата с текстовыми
    /// кнопками.
    /// </summary>
    /// <param name="пункты">Кнопки и разделители полосы.</param>
    /// <returns>Готовая полоса инструментов.</returns>
    private static ToolStrip ПолосаДействий(params ToolStripItem[] пункты)
    {
        var полоса = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Font = EnvelopeUiTheme.ШрифтТекста(8.5f),
            AutoSize = true,
            Padding = new Padding(2, 1, 0, 1),
            TabIndex = 0,
        };

        полоса.Items.AddRange(пункты);
        EnvelopeUiTheme.ОформитьПолосуДействий(полоса);

        return полоса;
    }

    /// <summary>
    /// Создаёт текстовую кнопку полосы действий с подсказкой.
    /// </summary>
    /// <param name="текст">Подпись кнопки.</param>
    /// <param name="подсказка">Пояснение при наведении.</param>
    /// <param name="действие">Обработчик нажатия.</param>
    /// <returns>Готовая кнопка.</returns>
    private static ToolStripButton ПунктДействия(string текст, string подсказка, EventHandler действие)
    {
        return new ToolStripButton(текст, null, действие)
        {
            ToolTipText = подсказка,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AccessibleName = текст,
        };
    }

    /// <summary>Нейтральный светлый фон рабочих областей (аудит UX 2026-11-03, G3).</summary>
    /// <returns>Цвет фона.</returns>
    private static Color ЦветФона() => EnvelopeUiTheme.WorkspaceBackground;

    /// <summary>
    /// Возвращает таблицам адресной книги визуальную роль, как в прототипе:
    /// отправители читаются по красному заголовку, получатели — по синему.
    /// Остальные таблицы сохраняют нейтральное оформление настроек оператора.
    /// </summary>
    private void ПрименитьАкцентыТаблиц()
    {
        EnvelopeUiTheme.ОформитьЗаголовокТаблицы(
            _таблицаОтправителей,
            EnvelopeUiTheme.SenderHeaderBackground,
            EnvelopeUiTheme.SenderAccent);
        EnvelopeUiTheme.ОформитьЗаголовокТаблицы(
            _таблицаПолучателей,
            EnvelopeUiTheme.RecipientHeaderBackground,
            EnvelopeUiTheme.RecipientAccent);
    }

    /// <summary>
    /// Полоса действий вкладки «Конверты» (аудит UX 2026-11-03, B1: экран
    /// без доступных действий; редактор бланков был достижим только по F8).
    /// </summary>
    /// <returns>Полоса инструментов бланков.</returns>
    private ToolStrip ПолосаДействийБланков() => ПолосаДействий(
        ПунктДействия("Редактор бланков (F8)", "Создать или изменить шаблон конверта (ТЗ §7.5)", (_, _) => ОткрытьРедакторБланков()),
        ПунктДействия("Обновить", "Перезагрузить данные всех таблиц", (_, _) => ОбновитьВсе()));

    /// <summary>
    /// Полоса действий вкладки «Рассылки» (аудит UX 2026-11-03, C1: создать
    /// рассылку с экрана было невозможно — команды жили только в меню).
    /// Правка и печать выбранной рассылки — ТЗ §8.12, функция 156.
    /// </summary>
    /// <returns>Полоса инструментов рассылок.</returns>
    private ToolStrip ПолосаДействийРассылок() => ПолосаДействий(
        ПунктДействия("Новая рассылка…", "Создать рассылку (ТЗ §8.4)", (_, _) => СоздатьРассылку()),
        ПунктДействия("Изменить…", "Изменить выбранную рассылку без создания дубликата (ТЗ §8.12)", (_, _) => ИзменитьРассылку()),
        ПунктДействия("Печать…", "Напечатать письма выбранных рассылок (ТЗ §8.12)", (_, _) => ПечатьРассылок()),
        ПунктДействия("Повторная отправка…", "Повторно отправить существующую рассылку", (_, _) => ПовторнаяОтправка()),
        ПунктДействия("Объединить выделенные", "Собрать выделенные рассылки в одну", (_, _) => ОбъединитьВРассылку()),
        new ToolStripSeparator(),
        ПунктДействия("Электронная рассылка…", "Отправить письма по e-mail (SMTP)", (_, _) => ОткрытьЭлектроннуюРассылку()),
        new ToolStripSeparator(),
        ПунктДействия("Обновить", "Перезагрузить данные всех таблиц", (_, _) => ОбновитьВсе()));

    /// <summary>
    /// Полоса действий вкладки «Отправленные».
    /// </summary>
    /// <returns>Полоса инструментов отправленных писем.</returns>
    private ToolStrip ПолосаДействийОтправленных() => ПолосаДействий(
        ПунктДействия("Печать выделенных…", "Напечатать письма, выбранные в списке (ТЗ §8.12)", (_, _) => ПечатьВыделенныхОтправленных()),
        new ToolStripSeparator(),
        ПунктДействия("Обновить", "Перезагрузить данные всех таблиц", (_, _) => ОбновитьВсе()));

    /// <summary>
    /// Строит строку состояния: «Всего», «Выбрано», «Выведено», «Копий» и
    /// состояние базы (ТЗ §7.1).
    /// </summary>
    private void ПостроитьСтрокуСостояния()
    {
        _строкаСостояния.Dock = DockStyle.Bottom;
        _строкаСостояния.Height = 24;

        // Счётчики идут сплошным потоком с отступами (аудит UX 2026-11-03,
        // F1: прежний ряд на фиксированных позициях наезжал на соседей при
        // длинных значениях); пробный период прижат к правому краю (F2).
        var счётчики = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 5, 0, 0),
        };

        void Настроить(Label метка, string текст)
        {
            метка.Text = текст;
            метка.AutoSize = true;
            метка.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
            метка.Margin = new Padding(0, 0, 16, 0);
        }

        Настроить(_всего, "Всего: 0");
        Настроить(_выбрано, "Выбрано: 0");
        Настроить(_выведено, "Выведено: 0");
        Настроить(_копий, "Копий: 0");
        Настроить(_состояниеБазы, "База: не подключена");
        Настроить(_состояниеПечати, string.Empty);

        счётчики.Controls.AddRange([_всего, _выбрано, _выведено, _копий, _состояниеБазы, _состояниеПечати]);

        _лицензия.Text = string.Empty;
        _лицензия.AutoSize = true;
        _лицензия.Dock = DockStyle.Right;
        _лицензия.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
        _лицензия.Padding = new Padding(0, 5, 8, 0);
        _лицензия.TextAlign = ContentAlignment.MiddleRight;

        _строкаСостояния.Controls.Add(счётчики);
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
            var страница = await _состав.Данные
                .ПолучитьКонтрагентовAsync(new PageRequest(смещение, PageRequest.MaxLimit))
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
        if (_состав is not null && _состав.ОбщийРежим)
        {
            // Копии общей базы делает сервер: у клиента нет файла общей базы
            // и ни одного локального аналога (решение SOURCE-MAP, сценарий 63).
            MessageBox.Show(
                this,
                "Резервное копирование в режиме общей базы выполняет серверная "
                + "служба: копии хранятся в каталоге данных службы на серверном "
                + "компьютере.",
                "Архивирование и восстановление",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

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

        if (_состав.ОбщийРежим)
        {
            // Общие данные переносятся первичным импортом на сервере
            // (решение Р11 SOURCE-MAP): мастер пишет в живую локальную базу,
            // которой в общем режиме нет.
            MessageBox.Show(
                this,
                "Перенос данных из предыдущей версии выполняется при первичной настройке "
                + "серверной части. В подключённом режиме общей базы мастер недоступен.",
                "Переход с предыдущей версии",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var форма = new FormLegacyUpgradeWizard(
            new LegacyUpgradeImporter(_состав.ЛокальнаяСессия!));

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
        // Команда доступна только после создания состава. Не создаём здесь
        // неявный production-менеджер: ранний вызов не должен обходить
        // composition root и обращаться к %AppData%\Picket302.
        if (_состав is null)
        {
            return;
        }

        using var диалог = СоздатьДиалогПодключения();

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
    /// Создаёт независимый диалог подключения с путями текущего состава.
    /// До инициализации состава вызов запрещён: скрытый fallback на
    /// <c>new ConnectionManager()</c> мог обратиться к производственному
    /// профилю из раннего/тестового вызова.
    /// </summary>
    internal FormConnectionSettings СоздатьДиалогПодключения()
    {
        var состав = _состав
            ?? throw new InvalidOperationException(
                "Диалог подключения нельзя создать до инициализации состава приложения.");

        var менеджер = состав.СоздатьМенеджерПодключенияДляДиалога();
        var реестр = состав.СоздатьРеестрСерверовДляДиалога();

        return new FormConnectionSettings(менеджер, реестр);
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

        var журнал = await _состав.Данные
            .ПолучитьПисателяEmailAsync()
            .ConfigureAwait(true);

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
            var контрагент = await _состав.Данные
                .ПолучитьКонтрагентаAsync(идентификатор)
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

        var получатель = await _состав.Данные
            .ПолучитьКонтрагентаAsync(_выбранныеПолучатели[0])
            .ConfigureAwait(true);

        return получатель?.Inn ?? string.Empty;
    }

    /// <summary>
    /// Импортирует контрагентов из выгрузки 1С:Предприятие
    /// (ТЗ §8.5, функция 72: «Импорт контрагентов из выгрузок 1С:Предприятие
    /// 7.7 и 8.x»).
    /// </summary>
    private async void ИмпортИз1С()
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

            var отчёт = await _состав.Данные
                .СлитьИмпортAsync(пакет.Batch, MergePolicy.SkipExisting)
                .ConfigureAwait(true);

            var сообщение =
                $"Прочитано записей: {пакет.RecordCount}.\n" +
                $"Создано: {отчёт.Создано}, обновлено: {отчёт.Обновлено}, " +
                $"пропущено: {отчёт.Пропущено}, конфликтов: {отчёт.Конфликты}.";

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

        // Совместный режим: разбор файла, сопоставление столбцов и проверка
        // записей выполняются на рабочем месте, а слияние — серверной командой
        // import.merge в одной транзакции. Категория адресной книги выбирается
        // и создаётся в общей базе.
        var контроллер = _состав.ОбщийРежим
            ? new ImportWizardController(
                слиятель: new Picket302.App.Shared.GatewayImportMerger(_состав.Данные),
                шлюз: _состав.Данные)
            : new ImportWizardController(
                _состав.ЛокальнаяСессия is null
                    ? null
                    : new Picket302.Infrastructure.Repositories.CounterpartyRepository(_состав.ЛокальнаяСессия),
                _состав.ЛокальнаяСессия is null
                    ? null
                    : new Picket302.Infrastructure.Repositories.GroupRepository(_состав.ЛокальнаяСессия));

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

        // Совместный режим: чтение внешней базы выполняется на рабочем
        // месте, а слияние записей — серверной командой import.merge.
        using var форма = _состав.ОбщийРежим
            ? new FormExternalDbImport(
                слиятель: new Picket302.App.Shared.GatewayImportMerger(_состав.Данные))
            : new FormExternalDbImport(
                new Picket302.Infrastructure.Repositories.CounterpartyRepository(_состав.ЛокальнаяСессия!));

        if (форма.ShowDialog(this) == DialogResult.OK)
        {
            ОбновитьВсе();
        }
    }

    /// <summary>
    /// Создаёт кнопку панели инструментов таблицы. Тип значка задаётся
    /// устойчивым идентификатором, а не текстом кнопки: переименование
    /// подписи не ломает ни значок, ни обработчик (этап 2, п. 6).
    /// </summary>
    /// <param name="текст">Подпись кнопки.</param>
    /// <param name="подсказка">Пояснение при наведении.</param>
    /// <param name="значок">Тип векторного знака или <c>null</c> — по подписи.</param>
    private Button Кнопка(string текст, string подсказка, string? значок = null)
    {
        var кнопка = new Button
        {
            Text = текст,
            AutoSize = true,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            Font = EnvelopeUiTheme.ШрифтТекста(8.5f),
            BackColor = Color.FromArgb(248, 249, 251),
            ForeColor = Color.FromArgb(31, 41, 55),
            Margin = new Padding(1),
            Padding = new Padding(3, 0, 4, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };

        кнопка.FlatAppearance.BorderColor = EnvelopeUiTheme.Border;
        кнопка.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 238, 248);
        кнопка.FlatAppearance.MouseDownBackColor = Color.FromArgb(216, 226, 241);
        кнопка.Image = EnvelopeUiTheme.СоздатьЗначок(значок ?? ВидЗначка(текст));
        кнопка.ImageAlign = ContentAlignment.MiddleLeft;
        кнопка.TextAlign = ContentAlignment.MiddleRight;
        кнопка.TextImageRelation = TextImageRelation.ImageBeforeText;
        кнопка.AccessibleName = текст;
        _подсказки.SetToolTip(кнопка, подсказка);

        return кнопка;
    }

    /// <summary>
    /// Возвращает тип небольшого векторного знака для кнопки панели действий
    /// по умолчанию (когда значок не задан явно).
    /// </summary>
    /// <param name="текст">Текст кнопки.</param>
    private static string ВидЗначка(string текст) => текст switch
    {
        "Добавить" => "add",
        "Изменить" => "edit",
        "Удалить" => "delete",
        "Печать текущего конверта" => "print",
        "Предпросмотр и печать" => "preview",
        "Фильтры…" => "filter",
        "Открыть список в Excel" => "export",
        "Сохранить вид таблицы" or "Вид таблиц…" => "settings",
        _ => "default",
    };

    /// <summary>
    /// Загружает справочник отправителей.
    /// </summary>
    private async Task ЗагрузитьОтправителейAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var отправители = await состав.Данные
            .ПолучитьОтправителейAsync(cancellationToken)
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

        var страница = await состав.Данные
            .ПолучитьКонтрагентовAsync(
                new Picket302.Domain.Common.PageRequest(0, 500),
                фильтр,
                cancellationToken)
            .ConfigureAwait(true);

        _таблицаПолучателей.Rows.Clear();
        _текущиеПолучатели.Clear();

        // Набор строк заменяется: снимок порядка для снятия группировки
        // должен быть переснят по новой загрузке (ревью S07 F-R2-2).
        _группировкаПолучателей.СброситьСнимокПорядка();

        var группы = await состав.Данные.ПолучитьГруппыAsync(cancellationToken).ConfigureAwait(true);

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
        _группировкаПолучателей.Перестроить();
    }

    /// <summary>
    /// Колонка группировки строк адресной книги; <see langword="null"/> —
    /// группировка снята (ТЗ §8.1, функция 10).
    /// </summary>
    public string? ГруппировкаПолучателей => _группировкаПолучателей.КолонкаГруппировки;

    /// <summary>Число строк-заголовков групп адресной книги (для приёмки).</summary>
    public int ЧислоЗаголовковГруппировки => _группировкаПолучателей.КоличествоЗаголовков;

    /// <summary>
    /// Применяет группировку строк адресной книги по колонке
    /// («Группа», «Город», «VIP») либо снимает её (<see langword="null"/>).
    /// </summary>
    /// <param name="колонка">Имя колонки либо <see langword="null"/>.</param>
    public void ПрименитьГруппировкуПолучателей(string? колонка)
    {
        _группировкаПолучателей.Применить(колонка);
    }

    /// <summary>
    /// Загружает дерево категорий.
    /// </summary>
    private async Task ЗагрузитьКатегорииAsync(AppServices состав, CancellationToken cancellationToken)
    {
        _деревоКатегорий.Nodes.Clear();

        var категории = await состав.Данные
            .ПолучитьДеревоКатегорийAsync(cancellationToken)
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
        ОбновитьМенюФильтров(категории);

        // Подписка однократная (F-E9): загрузка дерева выполняется при каждом
        // обновлении, и повторная подписка накапливала обработчики — выбор
        // категории перезагружал список несколько раз.
        if (!_деревоКатегорийПодписано)
        {
            _деревоКатегорийПодписано = true;
            _деревоКатегорий.AfterSelect += ВыбратьКатегорию;
        }
    }

    /// <summary>
    /// Фильтрует адресную книгу по выбранной категории дерева (ТЗ §8.1).
    /// </summary>
    /// <param name="отправитель">Источник события.</param>
    /// <param name="событие">Аргументы выбора узла.</param>
    private void ВыбратьКатегорию(object? отправитель, TreeViewEventArgs событие)
    {
        if (событие.Node?.Tag is not long идентификатор)
        {
            return;
        }

        _поиск.ЗадатьФильтрГрупп(идентификатор);
        ОбновитьСостояниеМенюФильтров();
        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Загружает каталог бланков для вкладки «Конверты».
    /// </summary>
    private async Task ЗагрузитьБланкиAsync(AppServices состав, CancellationToken cancellationToken)
    {
        var шаблоны = await состав.Данные
            .ПолучитьШаблоныAsync(null, cancellationToken)
            .ConfigureAwait(true);

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
        var рассылки = await состав.Данные
            .ПолучитьРассылкиAsync(null, cancellationToken)
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
        var страница = await состав.Данные
            .ПолучитьПисьмаAsync(
                new Picket302.Domain.Common.PageRequest(0, 500),
                null,
                cancellationToken)
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
        // Подписка однократная (F-E10): загрузка списка выполняется при
        // каждом обновлении, и повторная подписка накапливала обработчики.
        if (!_перетаскиваниеПисемПодписано)
        {
            _перетаскиваниеПисемПодписано = true;
            _таблицаОтправленных.MouseDown += ОтправитьПеретаскиваниеПисьма;
        }

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
    /// <summary>
    /// Показывает имя файла базы в строке состояния, а полный путь —
    /// всплывающей подсказкой (аудит UX 2026-11-03, F3).
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    private void ЗадатьСостояниеБазы(AppServices состав)
    {
        _состояниеБазы.Text = $"База: {Path.GetFileName(состав.ПутьБазы)}";
        _подсказкиСтрокиСостояния.SetToolTip(_состояниеБазы, состав.ПутьБазы);
        _состояниеБазы.AccessibleName = $"База данных: {состав.ПутьБазы}";
    }

    private async void ОбновитьСтрокуСостояния(AppServices состав)
    {
        if (!_инициализировано || _выбранныеПолучатели.Count == 0)
        {
            _выведено.Text = "Выведено: 0";
            _копий.Text = "Копий: 0";
            ЗадатьСостояниеБазы(состав);
            return;
        }

        try
        {
            var страница = await состав.Данные
                .ПолучитьПисьмаAsync(new Picket302.Domain.Common.PageRequest(0, 500))
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

        ЗадатьСостояниеБазы(состав);
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
    /// Показывает NAG-окно пробного периода при старте приложения
    /// (ТЗ §8.10, функция 119: «NAG-экран при старте в trial-режиме»).
    /// </summary>
    /// <remarks>
    /// <para>
    /// NAG-стартовый диалог ОТКЛЮЧЁН решением владельца проекта
    /// (2026-11-07): лицензионная фича целиком отложена на самый последний
    /// этап. Текущее поведение: метод сразу возвращает
    /// <see langword="false"/>, окно не показывается.
    /// </para>
    /// <para>Состояние лицензионных элементов:</para>
    /// <list type="bullet">
    /// <item>индикатор лицензии в строке состояния — включён;</item>
    /// <item>лицензионные лимиты (license gate) — включены;</item>
    /// <item><see cref="FormLicenseNag"/> — сохранён для будущего включения.</item>
    /// </list>
    /// <para>
    /// Будущее включение (последний этап) — в <see cref="OnLoad"/> вместо
    /// текущего вызова с возвратом:
    /// <code>
    /// using var форма = new FormLicenseNag(_состав.ВоротаЛицензии.ТекущийСтатус);
    /// форма.ShowDialog(this);
    ///
    /// if (форма.ВыбранноеДействие == FormLicenseNag.КнопкаВводаКлюча)
    /// {
    ///     ОткрытьРегистрацию();
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="состав">Состав приложения.</param>
    /// <returns>
    /// <see langword="true"/>, если NAG-окно было показано; всегда
    /// <see langword="false"/>, пока диалог отключён.
    /// </returns>
    public bool ПоказатьNagЛицензии(AppServices состав)
    {
        ArgumentNullException.ThrowIfNull(состав);

        // DEFERRED_BY_OWNER (2026-11-07): NAG-стартовый диалог отключён,
        // лицензионная фича выполняется на самом последнем этапе.
        return false;
    }

    /// <summary>
    /// Обрабатывает прямой набор с клавиатуры в таблице получателей
    /// (ТЗ §7.7: «Прямой набор с клавиатуры»).
    /// </summary>
    private void ОбработатьНаборВТаблице(object? отправитель, KeyEventArgs аргументы)
    {
        // Символьный ввод обрабатывается в KeyPress (F-E6): виртуальный код
        // клавиши не зависит от раскладки и давал латиницу вместо кириллицы.

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
            аргументы.Handled = true;
            _поиск.УдалитьИзПрямогоНабора();
            _ = ОбновитьПолучателейAsync();
        }
    }

    /// <summary>
    /// Добавляет введённый символ в прямой набор: событие KeyPress даёт
    /// символ текущей раскладки (F-E6).
    /// </summary>
    private void ОбработатьНаборВТаблицеПоСимволу(object? отправитель, KeyPressEventArgs аргументы)
    {
        if (char.IsControl(аргументы.KeyChar))
        {
            return;
        }

        аргументы.Handled = true;
        _поиск.ДобавитьВПрямойНабор(аргументы.KeyChar);
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

        ОбновитьСостояниеМенюФильтров();

        _ = ОбновитьПолучателейAsync();
    }

    /// <summary>
    /// Переключает встроенный фильтр «Избранное» (VIP).
    /// </summary>
    private void ПереключитьФильтрИзбранного()
    {
        _поиск.ЗадатьФильтрИзбранного(!_поиск.ТолькоИзбранное);
        _пунктИзбранного.Checked = _поиск.ТолькоИзбранное;
        ОбновитьСостояниеМенюФильтров();
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

        var результат = await _состав.Данные
            .СохранитьКонтрагентаAsync(форма.Результат)
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var (имя, значение) in форма.ПользовательскиеПоля)
        {
            var запись = await _состав.Данные
                .ЗаписатьПолеКонтрагентаAsync(результат.Value.Id, имя, значение)
                .ConfigureAwait(true);

            if (запись.IsFailure)
            {
                MessageBox.Show(this, запись.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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

        var контрагент = await _состав.Данные
            .ПолучитьКонтрагентаAsync(_выбранныеПолучатели[0])
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

        var результат = await _состав.Данные
            .СохранитьКонтрагентаAsync(форма.Результат)
            .ConfigureAwait(true);

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

        var результат = await _состав.Данные.СоздатьОтправителяAsync(агент).ConfigureAwait(true);

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

        var отправитель = await _состав.Данные
            .ПолучитьКонтрагентаAsync(_выбранныеОтправители[0])
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

        var результат = await _состав.Данные
            .СохранитьКонтрагентаAsync(форма.Результат)
            .ConfigureAwait(true);

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

        var результат = await _состав.Данные
            .ИзменитьСостояниеПоИдентификаторамAsync(_выбранныеПолучатели, false)
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

        try
        {
            foreach (var идентификатор in _выбранныеПолучатели)
            {
                if (await _состав.Данные
                    .ОкончательноУдалитьКонтрагентаAsync(идентификатор)
                    .ConfigureAwait(true))
                {
                    удалено++;
                }
            }
        }
        catch (SharedModeException ошибка)
        {
            MessageBox.Show(
                this,
                ошибка.Message,
                "Адресная книга",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
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

        var страница = await _состав.Данные
            .ПолучитьКонтрагентовAsync(
                new Picket302.Domain.Common.PageRequest(0, 500),
                new Picket302.Domain.Repositories.CounterpartyFilter(IncludeDeleted: true))
            .ConfigureAwait(true);

        var удалённые = страница.Items.Where(контрагент => контрагент.IsDeleted).ToList();

        if (удалённые.Count == 0)
        {
            MessageBox.Show(this, "Удалённых записей нет.", "Сервис", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var итог = await _состав.Данные
            .ИзменитьСостояниеКонтрагентовAsync(удалённые, true)
            .ConfigureAwait(true);

        if (итог.IsFailure)
        {
            MessageBox.Show(this, итог.Error!, "Сервис", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
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
            var контрагент = await _состав.Данные
                .ПолучитьКонтрагентаAsync(идентификатор)
                .ConfigureAwait(true);

            if (контрагент is null)
            {
                continue;
            }

            контрагент.IsSender = true;
            контрагент.UpdatedAt = DateTime.UtcNow;

            var сохранение = await _состав.Данные
                .СохранитьКонтрагентаAsync(контрагент)
                .ConfigureAwait(true);

            if (сохранение.IsFailure)
            {
                MessageBox.Show(this, сохранение.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        await ЗагрузитьОтправителейAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Создаёт независимую копию первой выбранной записи адресной книги.
    /// Адреса и логотип копируются в новые объекты, поэтому последующее
    /// редактирование копии не меняет исходную запись (ТЗ §8.11).
    /// </summary>
    private async void СоздатьКопиюСтроки()
    {
        if (_состав is null || _выбранныеПолучатели.Count == 0)
        {
            return;
        }

        try
        {
            var исходная = await _состав.Данные
                .ПолучитьКонтрагентаAsync(_выбранныеПолучатели[0])
                .ConfigureAwait(true);

            if (исходная is null)
            {
                return;
            }

            var копия = КопироватьКонтрагента(исходная);
            var результат = await _состав.Данные
                .СохранитьКонтрагентаAsync(копия)
                .ConfigureAwait(true);

            if (результат.IsFailure)
            {
                MessageBox.Show(this, результат.Error!, "Адресная книга", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await ЗагрузитьПолучателейAsync(_состав, CancellationToken.None).ConfigureAwait(true);

            foreach (DataGridViewRow строка in _таблицаПолучателей.Rows)
            {
                if (строка.Tag is long идентификатор && идентификатор == копия.Id)
                {
                    _таблицаПолучателей.ClearSelection();
                    строка.Selected = true;
                    _таблицаПолучателей.CurrentCell = строка.Cells["Получатель"];
                    break;
                }
            }
        }
#pragma warning disable CA1031 // Обработчик контекстного меню не должен оставлять необработанное исключение в UI-потоке.
        catch (Exception ошибка)
#pragma warning restore CA1031
        {
            MessageBox.Show(
                this,
                $"Не удалось создать копию строки: {ошибка.Message}",
                "Адресная книга",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>Копирует реквизиты контрагента без идентификатора и аудита.</summary>
    private static Counterparty КопироватьКонтрагента(Counterparty исходная)
    {
        var копия = new Counterparty
        {
            Type = исходная.Type,
            Name = исходная.Name + " (копия)",
            ShortName = исходная.ShortName,
            Inn = исходная.Inn,
            Kpp = исходная.Kpp,
            Ogrn = исходная.Ogrn,
            Okfs = исходная.Okfs,
            Okpo = исходная.Okpo,
            Okved = исходная.Okved,
            Bik = исходная.Bik,
            Ks = исходная.Ks,
            Rs = исходная.Rs,
            Rkc = исходная.Rkc,
            BankName = исходная.BankName,
            PassportSeries = исходная.PassportSeries,
            PassportNumber = исходная.PassportNumber,
            PassportIssuedBy = исходная.PassportIssuedBy,
            PassportIssuedDate = исходная.PassportIssuedDate,
            BirthDate = исходная.BirthDate,
            BirthPlace = исходная.BirthPlace,
            Position = исходная.Position,
            DirectorFio = исходная.DirectorFio,
            Email = исходная.Email,
            Phone = исходная.Phone,
            SpecialNotes = исходная.SpecialNotes,
            Source = исходная.Source,
            Logo = исходная.Logo?.ToArray(),
            GroupId = исходная.GroupId,
            IsVip = исходная.IsVip,
            IsSender = исходная.IsSender,
            IsActive = исходная.IsActive,
            IsDeleted = false,
        };

        копия.SetAddress(AddressMainType.Legal, КопироватьАдрес(исходная.LegalAddress));
        копия.SetAddress(AddressMainType.Actual, КопироватьАдрес(исходная.ActualAddress));
        копия.SetAddress(AddressMainType.Postal, КопироватьАдрес(исходная.PostalAddress));
        копия.AddressMainType = исходная.AddressMainType;
        return копия;
    }

    /// <summary>Создаёт независимую копию структурированного адреса.</summary>
    private static Address? КопироватьАдрес(Address? адрес)
    {
        if (адрес is null)
        {
            return null;
        }

        return Address.Create(адрес.Zip, адрес.Lines.ToArray(), адрес.Raw).Value;
    }

    /// <summary>Копирует текст текущей ячейки в системный буфер обмена.</summary>
    private void КопироватьТекущуюЯчейку()
    {
        var ячейка = _таблицаПолучателей.CurrentCell;
        var текст = ячейка?.FormattedValue?.ToString() ?? string.Empty;

        if (ячейка is null || текст.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(текст);
        }
#pragma warning disable CA1031 // Clipboard может быть временно занят другим процессом.
        catch (Exception ошибка)
#pragma warning restore CA1031
        {
            MessageBox.Show(
                this,
                $"Не удалось скопировать ячейку: {ошибка.Message}",
                "Буфер обмена",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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

        var категории = await _состав.Данные.ПолучитьДеревоКатегорийAsync().ConfigureAwait(true);

        using var выбор = new Form();
        EnvelopeUiTheme.ОформитьОкно(выбор);
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
            var перенос = await _состав.Данные
                .ПеренестиКонтрагентаAsync(идентификатор, категория.Id)
                .ConfigureAwait(true);

            if (перенос.IsFailure)
            {
                MessageBox.Show(this, перенос.Error!, "Категории", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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

        var результат = await _состав.Данные
            .СоздатьРассылкуИзВыборкиAsync(
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
    /// Открывает редактор выбранной рассылки: сохранение правит ту же запись,
    /// не создавая дубликат (ТЗ §8.12, функция 156 — правка рассылки).
    /// </summary>
    private async void ИзменитьРассылку()
    {
        if (_состав is null)
        {
            return;
        }

        var идентификатор = ВыбраннаяРассылка();

        if (идентификатор is null)
        {
            MessageBox.Show(
                this,
                "Выберите рассылку в списке.",
                "Рассылки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var рассылка = await _состав.Данные
            .ПолучитьРассылкуAsync(идентификатор.Value)
            .ConfigureAwait(true);

        if (рассылка is null)
        {
            MessageBox.Show(
                this,
                "Рассылка не найдена — возможно, она была удалена.",
                "Рассылки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var форма = new FormDistributionEditor(_состав, рассылка);

        if (форма.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await ЗагрузитьРассылкиAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Печатает письма выбранных рассылок (ТЗ §8.12, функция 156 — печать
    /// рассылки). Пустые рассылки пропускаются с предупреждением; выбранные
    /// строки не меняются, новых записей печать не создаёт. Сбор писем и
    /// запуск операции выполняет <see cref="Printing.DistributionPrintScenario"/>.
    /// </summary>
    private async void ПечатьРассылок()
    {
        if (_состав is null)
        {
            return;
        }

        var идентификаторы = ИдентификаторыВыбранныхСтрок(_таблицаРассылок);

        if (идентификаторы.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбрано ни одной рассылки. Отметьте строки в списке (Ctrl+щелчок или Shift+щелчок) и повторите печать.",
                "Печать рассылок",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var итог = await Printing.DistributionPrintScenario.ВыполнитьAsync(
            _состав,
            идентификаторы,
            ЗапуститьПечатьРассылокAsync,
            статус: текст => _состояниеПечати.Text = текст)
            .ConfigureAwait(true);

        if (итог.ПредупреждениеБезПринтера is not null)
        {
            MessageBox.Show(
                this,
                итог.ПредупреждениеБезПринтера,
                "Печать рассылок",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (итог.ПропущенныеРассылки.Count > 0)
        {
            MessageBox.Show(
                this,
                "Пропущены рассылки без писем: "
                + string.Join(", ", итог.ПропущенныеРассылки) + ".",
                "Печать рассылок",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (итог.Ошибка is not null && !итог.ОшибкаСообщенаОператору)
        {
            MessageBox.Show(this, итог.Ошибка, "Печать рассылок", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!итог.ПечатьЗапущена)
        {
            return;
        }

        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        ОбновитьСтрокуСостояния(_состав);
    }

    /// <summary>
    /// Запускает печатную операцию рассылок через общий конвейер печати:
    /// диалог, предпросмотр и спулер. Итог конвейера передаётся без
    /// схлопывания: успех, отмена оператора и ошибка различаются, сообщение
    /// ошибки и признак его показа сохраняются. Исключения доступа к данным
    /// переводятся в ошибку с текстом.
    /// </summary>
    /// <param name="mailIds">Идентификаторы писем.</param>
    /// <returns>Итог операции.</returns>
    private async Task<Printing.ПечатьИтог> ЗапуститьПечатьРассылокAsync(IReadOnlyList<long> mailIds)
    {
        try
        {
            return await ВыполнитьПечатьAsync(mailIds, сПредпросмотром: true, пакетнаяПечать: true)
                .ConfigureAwait(true);
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            return new Printing.ПечатьИтог(Printing.ПечатьКод.Ошибка, исключение.Message);
        }
    }

    /// <summary>
    /// Идентификаторы выбранных строк таблицы: без выбора список пуст, повторные
    /// выборы одной записи сводятся к одному идентификатору.
    /// </summary>
    /// <param name="таблица">Таблица с идентификаторами в <see cref="DataGridViewRow.Tag"/>.</param>
    /// <returns>Уникальные идентификаторы выбранных строк.</returns>
    internal static IReadOnlyList<long> ИдентификаторыВыбранныхСтрок(DataGridView таблица)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        return таблица.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(строка => строка.Tag is long)
            .Select(строка => (long)строка.Tag!)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Печатает письма, выбранные в списке «Отправленные» (ТЗ §8.12,
    /// функция 156): печать идёт по готовым отправлениям без создания новых.
    /// </summary>
    private async void ПечатьВыделенныхОтправленных()
    {
        if (_состав is null)
        {
            return;
        }

        var идентификаторы = ИдентификаторыВыбранныхСтрок(_таблицаОтправленных);

        if (идентификаторы.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбрано ни одного письма. Отметьте строки в списке (Ctrl+щелчок или Shift+щелчок) и повторите печать.",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var итогПечати = await ВыполнитьПечатьAsync(идентификаторы, сПредпросмотром: true, пакетнаяПечать: true)
            .ConfigureAwait(true);

        // Обновление таблиц показывает уже созданные записи, но ошибкой не
        // маскируется: при ошибке итог показан, успехом не объявляется.
        ПоказатьОшибкуПечатиЕслиНужно(итогПечати);

        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        ОбновитьСтрокуСостояния(_состав);
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

        var результат = await _состав.Данные
            .КлонироватьРассылкуAsync(идентификатор.Value)
            .ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Рассылки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var текст = $"Создана копия рассылки «{результат.Value.Рассылка.Name}». "
            + $"Писем: {результат.Value.СозданоПисем}.";

        if (результат.Value.Пропущено.Count > 0)
        {
            текст += Environment.NewLine
                + "Пропущены удалённые контрагенты: "
                + string.Join(", ", результат.Value.Пропущено) + ".";
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
    /// Открывает локальный каталог клипартов и назначает выбранный элемент
    /// логотипом получателя (ТЗ §8.10, функция 158). Каталог локальный:
    /// сеть и внешние файлы не используются.
    /// </summary>
    private async void ВыбратьКлипартПолучателя()
    {
        if (_состав is null)
        {
            return;
        }

        long? идентификатор = _таблицаПолучателей.CurrentRow?.Tag is long выбранный
            ? выбранный
            : null;

        if (идентификатор is null)
        {
            MessageBox.Show(
                this,
                "Выберите получателя в таблице «КОМУ:» — клипарт назначается логотипом получателя.",
                "Клипарт",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var диалог = new UI.Forms.FormClipartPicker(_клипарты);

        if (диалог.ShowDialog(this) != DialogResult.OK || диалог.ВыбранныйPng is null)
        {
            return;
        }

        try
        {
            var контрагент = await _состав.Данные
                .ПолучитьКонтрагентаAsync(идентификатор.Value)
                .ConfigureAwait(true);

            if (контрагент is null)
            {
                MessageBox.Show(
                    this,
                    "Получатель не найден — возможно, запись была удалена.",
                    "Клипарт",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            контрагент.Logo = диалог.ВыбранныйPng;

            var сохранение = await _состав.Данные
                .СохранитьКонтрагентаAsync(контрагент)
                .ConfigureAwait(true);

            if (сохранение.IsFailure)
            {
                MessageBox.Show(this, сохранение.Error!, "Клипарт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _сЛоготипом.Checked = true;
            _состояниеПечати.Text = "Клипарт применён: логотип получателя обновлён.";
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось назначить клипарт: {исключение.Message}",
                "Клипарт",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Удаляет логотип у выбранного получателя (ТЗ §8.10, функция 158:
    /// удаление графического элемента).
    /// </summary>
    private async void УбратьЛоготипПолучателя()
    {
        if (_состав is null)
        {
            return;
        }

        long? идентификатор = _таблицаПолучателей.CurrentRow?.Tag is long выбранный
            ? выбранный
            : null;

        if (идентификатор is null)
        {
            MessageBox.Show(
                this,
                "Выберите получателя в таблице «КОМУ:».",
                "Клипарт",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var контрагент = await _состав.Данные
                .ПолучитьКонтрагентаAsync(идентификатор.Value)
                .ConfigureAwait(true);

            if (контрагент?.Logo is null || контрагент.Logo.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "У выбранного получателя логотипа нет.",
                    "Клипарт",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            контрагент.Logo = null;

            var сохранение = await _состав.Данные
                .СохранитьКонтрагентаAsync(контрагент)
                .ConfigureAwait(true);

            if (сохранение.IsFailure)
            {
                MessageBox.Show(this, сохранение.Error!, "Клипарт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _состояниеПечати.Text = "Логотип получателя удалён.";
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось удалить логотип: {исключение.Message}",
                "Клипарт",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
        EnvelopeUiTheme.ОформитьОкно(выбор);
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
        var порогЗначение = await _состав.Параметры
            .GetValueAsync(BatchEditService.ПорогПодтвержденияKey)
            .ConfigureAwait(true);

        var порог = int.TryParse(
            порогЗначение,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var разобранныйПорог) && разобранныйПорог >= 0
            ? разобранныйПорог
            : BatchEditService.ПорогПодтвержденияПоУмолчанию;

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

        var результат = await _состав.Данные
            .ИзменитьПолеПоИдентификаторамAsync(_выбранныеПолучатели, поле!, значение.Text)
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
        await ЗагрузитьКатегорииAsync(_состав, CancellationToken.None).ConfigureAwait(true);
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
        ПрименитьАкцентыТаблиц();

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

        // Legacy-поверхность открывала настоящий редактор столбцов, а не
        // безликое подтверждение сохранения. Черновик формы изменяет таблицу
        // только после ОК; Отмена не трогает рабочее состояние.
        using var форма = new FormTableSettings(_таблицаПолучателей);
        if (форма.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        форма.Применить();

        // После применения сохраняется фактическое состояние столбцов
        // адресной книги: видимость, подписи и ширина (ТЗ §8.11, функция 131;
        // §7.6).
        if (_настройкиВидаТаблиц is not null)
        {
            await _настройкиВидаТаблиц
                .СохранитьAsync(_таблицаПолучателей, ИдентификаторТаблицыПолучателей, CancellationToken.None)
                .ConfigureAwait(true);
        }

        MessageBox.Show(
            this,
            ТекстСохраненияВида(),
            "Настройка таблицы",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Текст подтверждения сохранения вида таблицы адресной книги
    /// (замечание аудита E2): сообщение называет таблицу и сохранённые
    /// параметры, а не безликое «сохранено».
    /// </summary>
    /// <returns>Готовый текст сообщения.</returns>
    public static string ТекстСохраненияВида() =>
        "Вид таблицы «Адресная книга получателей» сохранён: видимость, подписи и ширины столбцов.";

    /// <summary>
    /// Строит контекстное меню строки адресной книги по образцу legacy
    /// KonvertProNet. Все пункты подключены к существующим рабочим потокам:
    /// здесь нет декоративных команд, которые только меняют подпись.
    /// </summary>
    /// <returns>Готовое меню строк адресной книги.</returns>
    private ContextMenuStrip ПостроитьКонтекстноеМенюАдреснойКниги()
    {
        var меню = new ContextMenuStrip
        {
            Font = EnvelopeUiTheme.ШрифтТекста(8.5f),
            AccessibleName = "Контекстное меню адресной книги",
        };

        меню.Items.Add(ПунктКонтекстногоМеню(
            "Поиск",
            "Открыть форму поиска по адресной книге.",
            НачатьПоиск,
            Keys.Control | Keys.F,
            "search"));
        меню.Items.Add(ПунктКонтекстногоМеню(
            "История печати конвертов",
            "Открыть вкладку «Отправленные» с историей созданных отправлений.",
            ОткрытьИсториюПечати,
            Keys.Control | Keys.H,
            "history"));
        меню.Items.Add(ПунктКонтекстногоМеню(
            "Группы конвертов",
            "Открыть вкладку «Рассылки» для работы с группами конвертов.",
            ОткрытьГруппыКонвертов,
            Keys.Control | Keys.G,
            "group"));
        меню.Items.Add(ПунктКонтекстногоМеню(
            "Создать копию строки",
            "Создать новую запись с реквизитами выбранной строки и пометкой «(копия)».",
            СоздатьКопиюСтроки,
            icon: "copy"));

        _пунктПометитьОтправителем.AccessibleName = "Скопировать в таблицу «От кого»";
        _пунктПометитьОтправителем.AccessibleDescription =
            "Пометить выбранные записи отправителями: они появятся в таблице «От кого».";
        _пунктПометитьОтправителем.Image = EnvelopeUiTheme.СоздатьЗначок("sender");
        меню.Items.Add(_пунктПометитьОтправителем);

        меню.Items.Add(ПунктКонтекстногоМеню(
            "Копировать ячейку",
            "Скопировать текст текущей ячейки в буфер обмена.",
            КопироватьТекущуюЯчейку,
            Keys.Control | Keys.C,
            "copy"));

        // Группировка строк сворачиваемыми заголовками (ТЗ §8.1, функция 10):
        // без группировки / по группе / по городу / по VIP.
        var группировка = new ToolStripMenuItem("Группировка строк")
        {
            AccessibleName = "Группировка строк адресной книги",
            AccessibleDescription =
                "Сгруппировать строки адресной книги по выбранной колонке либо снять группировку.",
            Image = EnvelopeUiTheme.СоздатьЗначок("group"),
        };
        ДобавитьПунктыГруппировки(группировка);
        меню.Items.Add(группировка);

        var обмен = new ToolStripMenuItem("Экспорт/импорт таблицы")
        {
            AccessibleName = "Экспорт и импорт таблицы",
            AccessibleDescription = "Обменяться данными адресной книги с файлами и внешними источниками.",
            Image = EnvelopeUiTheme.СоздатьЗначок("exchange"),
        };
        обмен.DropDownItems.Add(ПунктКонтекстногоМеню(
            "Открыть список в Excel",
            "Сохранить текущую таблицу в формате XLSX.",
            ОткрытьСписокВExcel,
            icon: "export"));
        обмен.DropDownItems.Add(ПунктКонтекстногоМеню(
            "Выгрузить данные…",
            "Открыть мастер выгрузки адресной книги.",
            ВыгрузитьДанные,
            icon: "export"));
        обмен.DropDownItems.Add(new ToolStripSeparator());
        обмен.DropDownItems.Add(ПунктКонтекстногоМеню(
            "Мастер импорта адресной книги…",
            "Открыть мастер импорта записей адресной книги.",
            ОткрытьМастерИмпорта,
            icon: "import"));
        обмен.DropDownItems.Add(ПунктКонтекстногоМеню(
            "Импорт из 1С…",
            "Импортировать контрагентов из выгрузки 1С.",
            ИмпортИз1С,
            icon: "import"));
        меню.Items.Add(обмен);

        меню.Items.Add(ПунктКонтекстногоМеню(
            "Настройка таблицы",
            "Настроить видимость, подписи и ширины столбцов адресной книги.",
            НастроитьТаблицу,
            icon: "settings"));

        меню.Items.Add(new ToolStripSeparator());
        _пунктИзбранного.AccessibleName = "Избранное (VIP)";
        _пунктИзбранного.AccessibleDescription = "Показать или скрыть только избранные записи.";
        _пунктОбъединить.AccessibleName = "Объединить в рассылку";
        _пунктОбъединить.AccessibleDescription = "Создать рассылку из выбранных получателей.";
        _пунктПереместить.AccessibleName = "Переместить в категорию";
        _пунктПереместить.AccessibleDescription = "Переместить выбранные записи в категорию.";
        меню.Items.Add(_пунктИзбранного);
        меню.Items.Add(_пунктОбъединить);
        меню.Items.Add(_пунктПереместить);

        return меню;
    }

    /// <summary>
    /// Заполняет подменю «Группировка строк» радио-пунктами: без группировки,
    /// по группе, по городу, по VIP (ТЗ §8.1, функция 10). Отметка текущего
    /// режима синхронизируется при каждом открытии подменю.
    /// </summary>
    private void ДобавитьПунктыГруппировки(ToolStripMenuItem группировка)
    {
        (string Текст, string? Колонка)[] варианты =
        [
            ("Без группировки", null),
            ("По группе", "Группа"),
            ("По городу", "Город"),
            ("По признаку VIP", "VIP"),
        ];

        foreach (var (текст, колонка) in варианты)
        {
            var пункт = new ToolStripMenuItem(текст)
            {
                AccessibleName = $"Группировка: {текст}",
                AccessibleDescription =
                    "Сгруппировать строки адресной книги либо снять группировку.",
            };
            пункт.Click += (_, _) => ПрименитьГруппировкуПолучателей(колонка);
            группировка.DropDownItems.Add(пункт);
        }

        группировка.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripItem элемент in группировка.DropDownItems)
            {
                if (элемент is not ToolStripMenuItem пункт)
                {
                    continue;
                }

                var индекс = группировка.DropDownItems.IndexOf(пункт);
                пункт.Checked = варианты[индекс].Колонка == _группировкаПолучателей.КолонкаГруппировки;
            }
        };
    }

    /// <summary>Создаёт единообразный пункт контекстного меню.</summary>
    private static ToolStripMenuItem ПунктКонтекстногоМеню(
        string текст,
        string описание,
        Action действие,
        Keys горячаяКлавиша = Keys.None,
        string? icon = null)
    {
        var пункт = new ToolStripMenuItem(текст)
        {
            AccessibleName = текст,
            AccessibleDescription = описание,
        };

        if (горячаяКлавиша != Keys.None)
        {
            пункт.ShortcutKeys = горячаяКлавиша;
        }

        if (!string.IsNullOrWhiteSpace(icon))
        {
            пункт.Image = EnvelopeUiTheme.СоздатьЗначок(icon);
        }

        пункт.Click += (_, _) => действие();
        return пункт;
    }

    /// <summary>Открывает вкладку с историей напечатанных отправлений.</summary>
    private void ОткрытьИсториюПечати() => _вкладки.SelectedIndex = 3;

    /// <summary>Открывает вкладку групп/рассылок конвертов.</summary>
    private void ОткрытьГруппыКонвертов() => _вкладки.SelectedIndex = 2;

    /// <summary>
    /// Выбирает строку под правой кнопкой мыши, чтобы последующее действие
    /// контекстного меню было адресовано именно строке под курсором.
    /// </summary>
    private void ОбработатьНажатиеЯчейкиАдреснойКниги(
        object? отправитель,
        DataGridViewCellMouseEventArgs аргументы)
    {
        if (аргументы.Button != MouseButtons.Right || аргументы.RowIndex < 0)
        {
            return;
        }

        if (аргументы.RowIndex >= _таблицаПолучателей.Rows.Count)
        {
            return;
        }

        var строка = _таблицаПолучателей.Rows[аргументы.RowIndex];

        if (!строка.Selected)
        {
            _таблицаПолучателей.ClearSelection();
            строка.Selected = true;
        }

        if (аргументы.ColumnIndex >= 0 && аргументы.ColumnIndex < _таблицаПолучателей.ColumnCount)
        {
            _таблицаПолучателей.CurrentCell = строка.Cells[аргументы.ColumnIndex];
        }
    }

    /// <summary>
    /// Разделяет контекстное меню строк и контекстное меню заголовка столбца.
    /// </summary>
    private void ОбработатьКонтекстноеМенюАдреснойКниги(
        object? отправитель,
        DataGridViewCellContextMenuStripNeededEventArgs аргументы)
    {
        if (аргументы.RowIndex < 0 && аргументы.ColumnIndex >= 0)
        {
            _индексСтолбцаПодКурсором = аргументы.ColumnIndex;
            _контекстноеМенюСтолбцовАдреснойКниги ??= МенюСтолбцовАдреснойКниги();
            аргументы.ContextMenuStrip = _контекстноеМенюСтолбцовАдреснойКниги;
            return;
        }

        аргументы.ContextMenuStrip = _контекстноеМенюАдреснойКниги;
    }

    /// <summary>
    /// Строит контекстное меню заголовков столбцов адресной книги:
    /// скрытие столбца и возврат всех столбцов (ТЗ §8.11, функция 131:
    /// «скрытие колонок»).
    /// </summary>
    /// <returns>Контекстное меню заголовков столбцов.</returns>
    private ContextMenuStrip МенюСтолбцовАдреснойКниги()
    {
        var меню = new ContextMenuStrip { Font = EnvelopeUiTheme.ШрифтТекста(8.5f) };

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

        var контрагент = await _состав.Данные
            .ПолучитьКонтрагентаAsync(_выбранныеПолучатели[0])
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

        // Карточка отправления: категория и вид уведомления выбираются
        // оператором до сохранения (ТЗ §8.1, функции 23 и 32).
        var параметры = ЗапроситьПараметрыОтправления();

        if (параметры is null)
        {
            return;
        }

        ПрименитьПараметрыОтправления(письмо, параметры);

        var результат = await _состав.Данные.СоздатьПисьмоAsync(письмо).ConfigureAwait(true);

        if (результат.IsFailure)
        {
            MessageBox.Show(this, результат.Error!, "Печать", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Номер выдаёт служба: два рабочих места не получают один номер.
        var сНомером = await _состав.Данные
            .ВыдатьНомерПисьмуAsync(результат.Value)
            .ConfigureAwait(true);

        if (сНомером.IsFailure)
        {
            MessageBox.Show(this, сНомером.Error!, "Печать", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        письмо = сНомером.Value;

        var итогПечати = await ВыполнитьПечатьAsync(new[] { письмо.Id }, сПредпросмотром: true)
            .ConfigureAwait(true);

        ПоказатьОшибкуПечатиЕслиНужно(итогПечати);

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
        if (_состав is null)
        {
            return;
        }

        // Аудит UX 2026-11-03 (U3): без выделенных получателей команда
        // молча ничего не делала, и оператор не понимал, почему печать
        // не начинается.
        if (_выбранныеПолучатели.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбран ни один получатель. Отметьте строки в адресной книге (Ctrl+щелчок или Shift+щелчок) и повторите печать.",
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

        // Политика адреса получателя (вариант A, зафиксирована 2026-11-08):
        // адрес не обязателен для СОХРАНЕНИЯ получателя, но получатель без
        // основного адреса пропускается при печати с явным перечислением
        // в сообщении — не молча, не исключением и без зависания; печатное
        // задание для пропущенных не формируется.
        var пропущенныеБезАдреса = new List<string>();

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
            var контрагент = await _состав.Данные
                .ПолучитьКонтрагентаAsync(идентификатор)
                .ConfigureAwait(true);

            if (контрагент is null)
            {
                continue;
            }

            var адрес = контрагент.GetAddress(контрагент.AddressMainType);

            if (адрес is null)
            {
                пропущенныеБезАдреса.Add(контрагент.Name);
                continue;
            }

            var письмо = Picket302.Application.Services.MailFactory.CreateOutcoming(
                контрагент.Id,
                отправитель.Value,
                "C6",
                адрес,
                контрагент.IsVip);

            ПрименитьПараметрыОтправления(письмо, параметры);

            var результат = await _состав.Данные.СоздатьПисьмоAsync(письмо).ConfigureAwait(true);

            if (результат.IsSuccess)
            {
                // Номер выдаёт служба в транзакции записи номера: параллельная
                // работа пяти рабочих мест не создаёт одинаковых номеров.
                var сНомером = await _состав.Данные
                    .ВыдатьНомерПисьмуAsync(результат.Value)
                    .ConfigureAwait(true);

                if (сНомером.IsSuccess)
                {
                    создано++;
                    идентификаторы.Add(сНомером.Value.Id);
                }
                else
                {
                    пропущенныеБезАдреса.Add(
                        $"{контрагент.Name} (номер не выдан: {сНомером.Error})");
                }
            }
        }

        MessageBox.Show(
            this,
            ТекстФормированияОтправлений(создано, пропущенныеБезАдреса),
            "Печать",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        var итогПечати = await ВыполнитьПечатьAsync(идентификаторы, сПредпросмотром, пакетнаяПечать: true)
            .ConfigureAwait(true);

        ПоказатьОшибкуПечатиЕслиНужно(итогПечати);

        await ЗагрузитьОтправленныеAsync(_состав, CancellationToken.None).ConfigureAwait(true);
        ОбновитьСтрокуСостояния(_состав);
    }

    /// <summary>
    /// Текст сообщения о формировании отправлений.
    /// </summary>
    /// <param name="создано">Число сформированных отправлений.</param>
    /// <param name="пропущенныеБезАдреса">
    /// Имена получателей, пропущенных из-за отсутствия основного адреса.
    /// </param>
    /// <returns>
    /// Готовый текст для окна «Печать»: всегда называет число отправлений;
    /// при пропусках перечисляет причины поимённо (политика адреса получателя,
    /// вариант A: адрес не обязателен для сохранения, но получатели без
    /// основного адреса пропускаются при печати явно, а не молча).
    /// </returns>
    public static string ТекстФормированияОтправлений(
        int создано,
        IReadOnlyCollection<string> пропущенныеБезАдреса)
    {
        ArgumentNullException.ThrowIfNull(пропущенныеБезАдреса);

        var текст = $"Отправлений сформировано: {создано}.";

        if (пропущенныеБезАдреса.Count > 0)
        {
            текст += $"\n\nПропущены без основного адреса ({пропущенныеБезАдреса.Count}): "
                + string.Join(", ", пропущенныеБезАдреса)
                + ".\nЗаполните адрес в карточке контрагента и повторите печать.";
        }

        return текст;
    }

    /// <summary>
    /// Выполняет печатную сессию: диалог печати, интерактивный предпросмотр,
    /// затем передача задания в спулер (ТЗ §8.6, функция 79).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="сПредпросмотром">Признак показа интерактивного предпросмотра.</param>
    /// <param name="пакетнаяПечать">Признак пакетной печати (F6 / F7).</param>
    /// <returns>
    /// Итог операции с различением состояний: «Выполнено» — успех;
    /// «Отменено» — оператор отказался (диалог, подсказка подачи,
    /// предпросмотр, отмена блокировки размера); «Ошибка» — отказ конвейера,
    /// отсутствие принтера или сохраняющаяся блокировка размера.
    /// Для ошибок, уже показанных оператору внутри сессии, выставлен
    /// признак <see cref="Printing.ПечатьИтог.СообщениеПоказано"/>.
    /// </returns>
    private async Task<Printing.ПечатьИтог> ВыполнитьПечатьAsync(
        IReadOnlyList<long> mailIds,
        bool сПредпросмотром,
        bool пакетнаяПечать = false)
    {
        var состав = _состав;

        if (состав is null || mailIds.Count == 0)
        {
            return Printing.ПечатьИтог.Отменено;
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

            return new Printing.ПечатьИтог(
                Printing.ПечатьКод.Ошибка,
                PrinterManager.NoDefaultPrinterMessage,
                СообщениеПоказано: true);
        }

        var запрос = await ПоказатьДиалогПечатиAsync(mailIds, сПредпросмотром, пакетнаяПечать)
            .ConfigureAwait(true);

        if (запрос is null)
        {
            return Printing.ПечатьИтог.Отменено;
        }

        if (await состав.ПодсказкаПодачи.ShouldShowAsync().ConfigureAwait(true))
        {
            using var подсказка = new FormFeedOrientationHint();

            if (подсказка.ShowDialog(this) != DialogResult.OK)
            {
                return Printing.ПечатьИтог.Отменено;
            }

            if (подсказка.ПодавитьПредупреждение)
            {
                await состав.ПодсказкаПодачи.SetSuppressedAsync(true).ConfigureAwait(true);
            }
        }

        for (var попытка = 0; попытка < 3; попытка++)
        {
            // ТЗ §8.6, функция 80: предпросмотр показывается до передачи
            // задания в спулер; отказ оператора отменяет печать, сбой
            // построения — ошибка, которая не превращается в отмену.
            if (сПредпросмотром)
            {
                var предпросмотр = await ПоказатьПредпросмотрAsync(запрос).ConfigureAwait(true);

                if (предпросмотр.Код == Printing.ПредпросмотрКод.Ошибка)
                {
                    if (!предпросмотр.СообщениеПоказано)
                    {
                        MessageBox.Show(
                            this,
                            предпросмотр.Сообщение!,
                            "Печать",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    return new Printing.ПечатьИтог(
                        Printing.ПечатьКод.Ошибка,
                        предпросмотр.Сообщение,
                        СообщениеПоказано: true);
                }

                if (предпросмотр.Код == Printing.ПредпросмотрКод.Отменено)
                {
                    return Printing.ПечатьИтог.Отменено;
                }
            }

            var итог = await ЗапуститьПечатьAsync(mailIds, запрос).ConfigureAwait(true);

            // Блокировка размера обрабатывается диалогом и повторными
            // попытками; остальные статусы проходят единый маппинг, где
            // отмена оператора (ABORTED, в том числе с текстом «Печать
            // прервана оператором.») ошибкой не считается.
            if (string.Equals(итог.Status, PrintJobStatus.BlockedSize, StringComparison.Ordinal))
            {
                // D-S17-02: блокировка кодового штампа (отсутствие шрифта
                // ZIPcode) приходит со статусом BlockedSize, но причиной ей
                // НЕ размер бумаги — диалог «Конверт слишком большой» с
                // «Зафиксировать A4» вводил в бесконечный повтор, который
                // шрифт не устраняет никогда. Бумажный диалог не показывается:
                // итог уходит в единый маппинг (сообщение оператору — там,
                // без повторных попыток).
                if (итог.ErrorMessage is not null
                    && итог.ErrorMessage.Contains("ZIPcode", StringComparison.OrdinalIgnoreCase))
                {
                    return Printing.ПечатьИтог.ИзРезультатаКонвейера(итог);
                }

                var решение = await ПоказатьДиалогРазмераAsync(запрос).ConfigureAwait(true);

                if (решение == PaperSizeBlockedDecision.Отмена)
                {
                    return Printing.ПечатьИтог.Отменено;
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
                    return Printing.ПечатьИтог.Отменено;
                }

                запрос = новыйЗапрос;

                continue;
            }

            // Остальные статусы: единый маппинг. Отмена оператора
            // возвращается без окна; ошибка показывается один раз.
            var результат = Printing.ПечатьИтог.ИзРезультатаКонвейера(итог);

            if (результат.Код == Printing.ПечатьКод.Ошибка)
            {
                MessageBox.Show(
                    this,
                    результат.Сообщение!,
                    "Печать",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return результат with { СообщениеПоказано = true };
            }

            return результат;
        }

        // После трёх попыток блокировка размера сохраняется: оператор
        // видит причину, итог не теряется и успехом не объявляется.
        var блокировка = new Printing.ПечатьИтог(
            Printing.ПечатьКод.Ошибка,
            Printing.ПечатьИтог.ТекстБлокировкиРазмера);

        MessageBox.Show(
            this,
            блокировка.Сообщение!,
            "Печать",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);

        return блокировка with { СообщениеПоказано = true };
    }

    /// <summary>
    /// Показывает ошибку печати ровно один раз — только если её ещё не
    /// показал печатный конвейер
    /// (<see cref="Printing.ПечатьИтог.СообщениеПоказано"/>). Отмена и
    /// успех окном не сопровождаются.
    /// </summary>
    /// <param name="итог">Итог печатной операции.</param>
    private void ПоказатьОшибкуПечатиЕслиНужно(Printing.ПечатьИтог итог)
    {
        if (итог.Код == Printing.ПечатьКод.Ошибка && !итог.СообщениеПоказано)
        {
            MessageBox.Show(
                this,
                итог.Сообщение ?? Printing.ПечатьИтог.ОбщийТекстОшибки,
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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

        // ТЗ §8.4, функция 58: галка дампа промежуточных данных восстанавливается
        // из профиля вместе с остальными опциями печати.
        var дампПромежуточных = await состав.ДамперДанныхОтчётов
            .GetEnabledAsync()
            .ConfigureAwait(true);

        СинхронизироватьПунктПромежуточногоДампа(дампПромежуточных);

        var последнийШаблон = await НайтиПоследнийШаблонAsync(состав, шаблоны).ConfigureAwait(true);

        // ТЗ §8.5, функция 35: последние параметры печати одного конверта —
        // число копий и принтер восстанавливаются из профиля.
        var последниеКопии = await состав.ПараметрыПечати.GetLastCopiesAsync().ConfigureAwait(true);
        var последнийПринтер = await состав.ПараметрыПечати.GetLastPrinterAsync().ConfigureAwait(true);

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
            показыватьПредупреждениеРазлиновки: показыватьПредупреждениеРазлиновки,
            профильКалибровки: состав.ПрофильКалибровки,
            копий: последниеКопии,
            последнийПринтер: последнийПринтер);

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

        // ТЗ §8.5, функция 35: запоминание последних параметров печати
        // одного конверта — копии и принтер сохраняются после успешного
        // подтверждения диалога (отмена ничего не перезаписывает).
        await состав.ПараметрыПечати
            .SetLastCopiesAsync(Math.Max(1, запрос.Copies))
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(запрос.PrinterName))
        {
            await состав.ПараметрыПечати
                .SetLastPrinterAsync(запрос.PrinterName)
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
    /// <summary>
    /// Показывает интерактивный предпросмотр. Отказ оператора и сбой
    /// построения различаются: сбой возвращает <see cref="Printing.ПредпросмотрКод.Ошибка"/>
    /// с текстом и признаком показа — ошибка предпросмотра не превращается
    /// в отмену и не теряется.
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <returns>Итог показа предпросмотра.</returns>
    private async Task<Printing.ПредпросмотрИтог> ПоказатьПредпросмотрAsync(PrintRequest запрос)
    {
        var состав = _состав;

        if (состав is null)
        {
            return Printing.ПредпросмотрИтог.Отменено;
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
            // Сбой построения — ошибка, а не отмена: текст показывается
            // здесь ровно один раз.
            var сбой = Printing.ПредпросмотрИтог.ИзИсключения(ex);

            MessageBox.Show(
                this,
                сбой.Сообщение,
                "Печать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return сбой with { СообщениеПоказано = true };
        }

        using (документ)
        using (var окно = new FormPrintPreview(документ))
        {
            return окно.ShowDialog(this) == DialogResult.OK
                ? Printing.ПредпросмотрИтог.Разрешено
                : Printing.ПредпросмотрИтог.Отменено;
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
    /// Синхронизирует состояние галки меню «Сохранять промежуточный XML
    /// данных реестров» с профилем пользователя (ТЗ §8.4, функция 58).
    /// </summary>
    /// <param name="значение">Значение опции.</param>
    private void СинхронизироватьПунктПромежуточногоДампа(bool значение)
    {
        if (_пунктПромежуточногоДампа is null || _пунктПромежуточногоДампа.Checked == значение)
        {
            return;
        }

        _пунктПромежуточногоДампа.Checked = значение;
    }

    /// <summary>
    /// Сохраняет опцию «Сохранять промежуточный XML данных реестров» в базе
    /// (ТЗ §8.4, функция 58).
    /// </summary>
    /// <param name="значение">Значение опции.</param>
    private async Task СохранитьОпциюПромежуточногоДампаAsync(bool значение)
    {
        if (_состав is null)
        {
            return;
        }

        await _состав.ДамперДанныхОтчётов.SetEnabledAsync(значение).ConfigureAwait(true);
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

        // NAG-стартовый диалог отключён решением владельца (2026-11-07,
        // DEFERRED_BY_OWNER): лицензионная фича — на самом последнем этапе.
        // Вызов сохранён: ПоказатьNagЛицензии сейчас всегда возвращает false;
        // путь включения описан в её doc-комментарии.
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

        // Ctrl+Space перехватывается системой как глобальная горячая клавиша
        // (WM_HOTKEY в WndProc, F-E8): в ProcessCmdKey не попадает и не
        // блокируется.

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
    /// Открывает журнал входящей корреспонденции и движения документов
    /// (ТЗ §8.2, функции 29 и 30).
    /// </summary>
    /// <summary>
    /// Открывает диалог подключения к общей базе (совместный режим).
    /// </summary>
    private void ОткрытьПодключениеКОбщейБазе()
    {
        if (_состав is null)
        {
            return;
        }

        using var форма = new FormSharedConnection(
            new SharedClientProfile(Picket302ProfilePaths.ПоУмолчанию().DataDirectory),
            сообщение => _состав.Журнал.LogError(new InvalidOperationException(сообщение)));

        форма.ShowDialog(this);

        if (форма.Результат is not null)
        {
            MessageBox.Show(
                this,
                "Устройство сопряжено. Перезапустите программу: при запуске она "
                + "подключится к общей базе по сохранённым реквизитам.",
                "Общая база",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Открывает окно незавершённых операций и обновляет данные после
    /// успешного повтора.
    /// </summary>
    private void ОткрытьНезавершённыеОперации()
    {
        if (_состав is null)
        {
            return;
        }

        if (_состав.НезавершённыеОперацииStore?.Заблокировано == true)
        {
            MessageBox.Show(
                this,
                _состав.НезавершённыеОперацииStore.ПричинаБлокировки!,
                "Незавершённые операции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        if (!_состав.Данные.ОбщийРежим)
        {
            MessageBox.Show(
                this,
                "Незавершённые операции есть только в режиме общей базы: запись в "
                + "одиночном режиме не теряет ответов.",
                "Незавершённые операции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        if (_состав.Данные.НезавершённыеОперации.Count == 0)
        {
            MessageBox.Show(
                this,
                "Незавершённых операций нет: исход всех операций известен.",
                "Незавершённые операции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        if (FormSharedPendingOperations.Показать(this, _состав.Данные))
        {
            // Повтор мог создать или изменить записи: таблицы перечитываются.
            ОбновитьВсе();
        }
    }

    private void ОткрытьВходящуюКорреспонденцию()
    {
        var состав = _состав;
        if (состав is null)
        {
            return;
        }

        if (состав.ОбщийРежим)
        {
            // Сканы входящих документов хранятся в каталоге профиля рабочего
            // места: журнал в общей базе ссылался бы на файл, которого нет на
            // других компьютерах (решение SOURCE-MAP, сценарий 47).
            MessageBox.Show(
                this,
                "Входящая корреспонденция в режиме общей базы недоступна: сканы "
                + "хранятся на конкретном компьютере и не видны другим рабочим местам.",
                "Входящая корреспонденция",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var форма = new UI.Forms.FormIncomingMail(
            состав.СлужбаВходящих!,
            состав.ТрекерВходящих!);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Открывает диалог трекера Почты России (ТЗ §8.8, функция 110):
    /// регистрация партии и опрос статусов ШПИ.
    /// </summary>
    private async void ОткрытьТрекерПочты()
    {
        var состав = _состав;
        if (состав is null)
        {
            return;
        }

        try
        {
            var трекер = await состав.СоздатьТрекерПочтыAsync().ConfigureAwait(true);

            using var форма = new UI.Forms.FormRussianPostTracker(трекер);
            форма.ShowDialog(this);
        }
        catch (Exception ошибка)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть трекер: {ошибка.Message}",
                "Трекер Почты России");
        }
    }

    /// <summary>
    /// Открывает диалог пользовательских отчётов (ТЗ §8.4, функция 56):
    /// регистрация пакетов «макет .lbk + query.xml», правка запроса с
    /// валидацией и предпросмотр данных поверх текущей базы.
    /// </summary>
    private void ОткрытьПользовательскиеОтчёты()
    {
        var состав = _состав;
        if (состав is null)
        {
            return;
        }

        if (состав.ОбщийРежим)
        {
            // Пользовательские отчёты исполняются поверх открытого соединения
            // SQLite; в общем базе произвольный SQL клиенту не выдаётся
            // (решение SOURCE-MAP, сценарий 49).
            MessageBox.Show(
                this,
                "Пользовательские отчёты в режиме общей базы недоступны: "
                + "отчёты обращаются к базе напрямую, а клиенту общей базы "
                + "произвольные запросы не передаются.",
                "Пользовательские отчёты",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var форма = new UI.Forms.FormUserReports(
            состав.МенеджерПользовательскихОтчётов!,
            состав.ПоставщикДанныхОтчётов!);
        форма.ShowDialog(this);
    }

    /// <summary>
    /// Строит специальный бланк (ТЗ §8.3, функция 49) с подстановкой
    /// реквизитов текущего отправителя и открывает редактор бланков в
    /// режиме предпросмотра/печати. Отмена/закрытие не создают заданий и
    /// не пишут в базу; формат бланка подтверждается сериализацией в тестах.
    /// </summary>
    /// <param name="тип">Вид специального бланка.</param>
    private async void ПоказатьСпециальныйБланк(
        Infrastructure.Templates.SpecialFormType тип)
    {
        var состав = _состав;
        if (состав is null)
        {
            return;
        }

        var идОтправителя = состав.ТекущийОтправитель.CurrentSenderId;
        var отправитель = идОтправителя is null
            ? null
            : await состав.Данные
                .ПолучитьКонтрагентаAsync(идОтправителя.Value)
                .ConfigureAwait(true);

        var бланк = new Infrastructure.Templates.SpecialFormsBuilder()
            .BuildSpecialForm(тип);

        // Подстановка реквизитов текущего отправителя в поля бланка:
        // значения подставляются в поля с совпадающими именами.
        if (отправитель is not null)
        {
            var реквизиты = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Наименование отправителя"] = отправитель.Name,
            };

            var адрес = отправитель.GetAddress(отправитель.AddressMainType);
            if (адрес is not null)
            {
                реквизиты["Адрес отправителя"] = string.Join(
                    ", ",
                    адрес.Lines.Where(строка => !string.IsNullOrWhiteSpace(строка)));
            }

            foreach (var поле in бланк.AllFields())
            {
                if (реквизиты.TryGetValue(поле.Name, out var значение)
                    && !string.IsNullOrWhiteSpace(значение))
                {
                    // CustomBlank-поля не хранят свободного текста значения:
                    // содержимое задаётся скриптом onBeforePrint (присваивание
                    // конкретного значения реквизита отправителя).
                    поле.OnBeforePrint =
                        $"{поле.Name}.text := '@\" + значение.Replace(\"'\", \"''\") + \"';";
                }
            }
        }

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
            начало,
            форма.Режим,
            форма.УчитыватьРегистр,
            форма.УчитыватьФормат);

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
    /// Индекс текущей выделенной записи в списке поиска. Соответствие
    /// устанавливается по тегу-идентификатору, а не позиции строки: при
    /// активной группировке строки-заголовки и блочный перепорядок не
    /// смещают индексное пространство поиска (ревью S07 F1).
    /// </summary>
    private int _текущийИндексПолучателя()
    {
        var строка = _таблицаПолучателей.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault()
            ?? _таблицаПолучателей.CurrentRow;
        return RowGroupingController.СписочныйИндекс(строка, _идентификаторыПолучателей());
    }

    private long[] _идентификаторыПолучателей() =>
        _текущиеПолучатели.Select(запись => запись.Идентификатор).ToArray();

    /// <summary>
    /// Показывает запись таблицы получателей и устанавливает на неё курсор;
    /// при необходимости разворачивает скрытую свёрнутую группу (ревью S07 F1).
    /// </summary>
    private void ПоказатьПолучателя(int индекс)
    {
        if (индекс < 0 || индекс >= _текущиеПолучатели.Count)
        {
            return;
        }

        var строка = RowGroupingController.СтрокаПоИдентификатору(
            _таблицаПолучателей,
            _текущиеПолучатели[индекс].Идентификатор);
        if (строка is null)
        {
            return;
        }

        _группировкаПолучателей.РазвернутьПриНеобходимости(строка);
        _таблицаПолучателей.ClearSelection();
        строка.Selected = true;
        _таблицаПолучателей.CurrentCell = строка.Cells[0];
        _таблицаПолучателей.FirstDisplayedScrollingRowIndex = строка.Index;
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

        var значения = await _состав.Данные
            .ПолучитьЗначенияПоляAsync("post_zip")
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

        // Регистрация на окно (F-E8): сообщение WM_HOTKEY доставляется
        // в WndProc главного окна; прежде сочетание регистрировалось на поток
        // с нулевым дескриптором, и сообщение в окно не попадало.
        _глобальнаяГорячаяКлавиша = GlobalHotKey.CtrlSpace(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(() => СвернутьВОбластьУведомлений());
        }, Handle);

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
            _группировкаПолучателей.Dispose();

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
