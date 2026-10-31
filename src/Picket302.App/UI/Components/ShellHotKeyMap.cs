using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Действия главного окна, вызываемые системными горячими клавишами
/// (ТЗ §7.4: «Системные горячие клавиши»).
/// </summary>
public enum ShellAction
{
    /// <summary>F1 — открытие контекстной справочной системы.</summary>
    ОткрытьСправку = 0,

    /// <summary>F4 — раскрытие выпадающего дерева фильтрации категорий.</summary>
    РаскрытьФильтрКатегорий = 1,

    /// <summary>F5 — немедленная печать текущего выбранного конверта.</summary>
    ПечатьТекущийКонверт = 2,

    /// <summary>F6 — печать всех выделенных записей с предпросмотром.</summary>
    ПечатьВыделенныеСПредпросмотром = 3,

    /// <summary>F7 — пакетная печать выделенных записей без предпросмотра.</summary>
    ПечатьВыделенныеБезПредпросмотра = 4,

    /// <summary>F8 — открытие текущего макета в визуальном дизайнере бланков.</summary>
    ОткрытьРедакторБланков = 5,

    /// <summary>Esc — отмена текущего действия.</summary>
    ОтменитьДействие = 6,

    /// <summary>Ctrl+Space — сворачивание приложения в область уведомлений.</summary>
    СвернутьВОбластьУведомлений = 7,

    /// <summary>Ctrl+A — выделение всех записей активного грида.</summary>
    ВыделитьВсеЗаписи = 8,

    /// <summary>Ctrl+Alt+F8 — безвозвратное пакетное удаление выделенных записей.</summary>
    УдалитьВыделенныеБезвозвратно = 9,
}

/// <summary>
/// Таблица системных горячих клавиш главного окна (ТЗ §7.4).
/// </summary>
/// <remarks>
/// Таблица единая для всех вкладок: горячие клавиши не переопределяются
/// в зависимости от активной вкладки, кроме сочетаний, изменяющих состояние
/// самого окна.
/// </remarks>
public static class ShellHotKeyMap
{
    /// <summary>
    /// Соответствие «горячая клавиша — действие».
    /// </summary>
    public static IReadOnlyDictionary<Keys, ShellAction> Действия { get; } =
        new Dictionary<Keys, ShellAction>
        {
            [Keys.F1] = ShellAction.ОткрытьСправку,
            [Keys.F4] = ShellAction.РаскрытьФильтрКатегорий,
            [Keys.F5] = ShellAction.ПечатьТекущийКонверт,
            [Keys.F6] = ShellAction.ПечатьВыделенныеСПредпросмотром,
            [Keys.F7] = ShellAction.ПечатьВыделенныеБезПредпросмотра,
            [Keys.F8] = ShellAction.ОткрытьРедакторБланков,
            [Keys.Escape] = ShellAction.ОтменитьДействие,
            [Keys.Control | Keys.Space] = ShellAction.СвернутьВОбластьУведомлений,
            [Keys.Control | Keys.A] = ShellAction.ВыделитьВсеЗаписи,
            [Keys.Control | Keys.Alt | Keys.F8] = ShellAction.УдалитьВыделенныеБезвозвратно,
        };

    /// <summary>
    /// Возвращает действие по сочетанию клавиш.
    /// </summary>
    /// <param name="сочетание">Нажатые клавиши.</param>
    /// <returns>Действие либо <see langword="null"/>, если сочетание не задано.</returns>
    public static ShellAction? Найти(Keys сочетание)
        => Действия.TryGetValue(сочетание, out var действие) ? действие : null;

    /// <summary>
    /// Возвращает перечень сочетаний, обрабатываемых окном.
    /// </summary>
    public static IReadOnlyCollection<Keys> Сочетания => Действия.Keys.ToArray();
}
