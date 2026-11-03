using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Подсказка пустого списка (аудит UX 2026-11-03, C2/D2): пока в таблице
/// нет строк, вместо пустого грида показывается пояснение с первым
/// действием.
/// </summary>
/// <remarks>
/// <para>
/// Таблица и подсказка взаимно исключаются по видимости, поэтому обе могут
/// занимать <c>Dock = Fill</c> без разделения площади: пустой грид скрывается,
/// и на его месте виден только текст.
/// </para>
/// <para>
/// Слежение за составом строк ведётся событиями таблицы (добавление, удаление
/// и смена источника данных), поэтому подсказка обновляется и при обычной
/// перезагрузке списков, и при перестроении столбцов загрузчиком.
/// </para>
/// </remarks>
public static class EmptyStateHint
{
    /// <summary>
    /// Создаёт подсказку для таблицы и подписывает её на изменение состава
    /// строк.
    /// </summary>
    /// <param name="таблица">Наблюдаемая таблица.</param>
    /// <param name="текст">Текст подсказки.</param>
    /// <returns>Метка-подсказка; добавляется в контейнер после таблицы.</returns>
    public static Label Создать(DataGridView таблица, string текст)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentException.ThrowIfNullOrWhiteSpace(текст);

        var подсказка = new Label
        {
            Text = текст,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(71, 85, 105),
            Font = new Font("Tahoma", 8f, FontStyle.Italic),
            Visible = false,
        };

        void ОбновитьВидимость()
        {
            // У несвязанной таблицы после очистки остаётся строка ввода новой
            // записи (AllowUserToAddRows), поэтому пустота определяется по
            // отсутствию содержательных строк.
            var пусто = !таблица.Rows.Cast<DataGridViewRow>().Any(строка => !строка.IsNewRow);
            подсказка.Visible = пусто;
            таблица.Visible = !пусто;
        }

        таблица.RowsAdded += (_, _) => ОбновитьВидимость();
        таблица.RowsRemoved += (_, _) => ОбновитьВидимость();
        таблица.DataSourceChanged += (_, _) => ОбновитьВидимость();
        таблица.BindingContextChanged += (_, _) => ОбновитьВидимость();

        ОбновитьВидимость();

        return подсказка;
    }
}
