using System.Runtime.InteropServices;
using System.Windows.Forms;
using Picket302.App;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессия LAY3-01 (stage3.2): панель «Вид логотипа» главного окна —
/// переполнение строки «Клипарт» таблицы переключателей. На 96 DPI нижний
/// ряд FlowLayoutPanel выходил за клиентскую область родительского
/// TableLayoutPanel на ~77 px (137×74 при клиенте 138×127 — evidence
/// LAYOUTCHECK stage3.1), при масштабировании — аналогично.
///
/// Корневая причина: конфликты Dock/AutoSize. TableLayoutPanel
/// «переключатели» (Dock=Fill + AutoSize) не мог вырастить родительский
/// GroupBox: PreferredSize таблицы считался по НЕперенесённым высотам
/// рядов, а при реальной раскладке в узкой колонке FlowLayoutPanel-ряды
/// переносились и становились выше — сумма строк превышала зажатую высоту
/// таблицы. Исправление: строкам-рядам Dock=Fill убран (AutoSizing
/// учитывает перенос при заданной ширине ячейки), таблице назначен
/// Dock=Top — высота группы следует за содержимым на любом DPI.
///
/// Проверяются свойства компоновки иерархии на 96 DPI (в процессе теста)
/// и при симулированном 120/144 DPI (отдельный STA-поток с контекстом
/// PerMonitorV2 и реальным WM_DPICHANGED — тот же конвейер, что у
/// acceptance-сценария run-dpi.ps1); COM, печать и профиль не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class MainShellLayoutOverflowTests
{
    /// <summary>Ширина колонки превью в группе (не участвует в проверках рядов).</summary>
    private const string ИмяГруппыОтображения = "Вид логотипа";

    private static IEnumerable<Control> Обойти(Control контейнер)
    {
        foreach (Control элемент in контейнер.Controls)
        {
            yield return элемент;

            foreach (var вложенный in Обойти(элемент))
            {
                yield return вложенный;
            }
        }
    }

    /// <summary>Таблица переключателей внутри группы «Вид логотипа».</summary>
    private static TableLayoutPanel ТаблицаПереключателей(MainShell окно)
    {
        var группа = Обойти(окно).OfType<GroupBox>().Single(блок => блок.Text == ИмяГруппыОтображения);

        return Обойти(группа).OfType<TableLayoutPanel>().Single();
    }

    /// <summary>
    /// Показывает главное окно заданного размера и выполняет полную
    /// раскладку (паттерн FormCounterpartyLayoutTests: Show + PerformLayout
    /// + DoEvents). Окно заранее получает изолированный профиль и готовый
    /// состав, поэтому Show не запускает асинхронную инициализацию
    /// production-профиля.
    /// </summary>
    private static async Task<(ИзолированныйПрофиль Профиль, MainShell Окно)> ПоказатьОкноAsync(
        int ширина,
        int высота)
    {
        var профиль = new ИзолированныйПрофиль();

        try
        {
            // Show запускает OnLoad. Инициализируем окно заранее с явным
            // временным профилем, чтобы OnLoad не открыл production
            // %AppData%/Picket302/picket302.db и не оставил файловый
            // дескриптор после завершения layout-теста.
            var окно = await профиль.СоздатьОболочкуAsync().ConfigureAwait(true);
            await окно.ИнициализироватьAsync().ConfigureAwait(true);

            окно.StartPosition = FormStartPosition.Manual;
            окно.Show();
            окно.ClientSize = new Size(ширина, высота);
            окно.PerformLayout();
            System.Windows.Forms.Application.DoEvents();

            return (профиль, окно);
        }
        catch
        {
            профиль.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Инвариант LAY3-01: ни один ребёнок таблицы переключателей не выходит
    /// за клиентскую область таблицы (по нижней/правой границе), границы
    /// неотрицательны. Падает ровно на воспроизведении LAY3-01.
    /// </summary>
    private static void ПроверитьОтсутствиеПереполнения(MainShell окно, string контекст)
    {
        var таблица = ТаблицаПереключателей(окно);
        var клиент = таблица.ClientRectangle;

        foreach (Control ребёнок in таблица.Controls)
        {
            Assert.True(
                ребёнок.Bottom <= клиент.Bottom,
                $"{контекст}: ребёнок «{ребёнок.Text}» ({ребёнок.GetType().Name}) " +
                $"выходит за низ таблицы: bounds={ребёнок.Bounds}, клиент={клиент}.");
            Assert.True(
                ребёнок.Right <= клиент.Right,
                $"{контекст}: ребёнок «{ребёнок.Text}» выходит за правую границу таблицы: {ребёнок.Bounds}.");
            Assert.True(
                ребёнок.Top >= клиент.Top && ребёнок.Left >= клиент.Left,
                $"{контекст}: отрицательные координаты ребёнка «{ребёнок.Text}»: {ребёнок.Bounds}.");
        }
    }

    /// <summary>
    /// Все интерактивные элементы панели (переключатели, кнопки клипарта)
    /// видимы, доступны и лежат в клиентской области главного окна.
    /// </summary>
    private static void ПроверитьДоступностьЭлементов(MainShell окно, string контекст)
    {
        var группа = Обойти(окно).OfType<GroupBox>().Single(блок => блок.Text == ИмяГруппыОтображения);

        foreach (var элемент in Обойти(группа).OfType<RadioButton>().Cast<Control>()
                     .Concat(Обойти(группа).OfType<Button>()))
        {
            Assert.True(
                элемент.Visible,
                $"{контекст}: элемент «{элемент.Text}» невидим.");
            Assert.True(
                окно.ClientRectangle.Contains(элемент.Bounds),
                $"{контекст}: элемент «{элемент.Text}» вне клиентской области окна: {элемент.Bounds}.");
            Assert.False(
                string.IsNullOrWhiteSpace(элемент.AccessibleName),
                $"{контекст}: элемент «{элемент.Text}» без AccessibleName.");
        }
    }

    // ------------------------------------------------------------------
    // 96 DPI: основной контракт (процесс тестов).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    public async Task ГлавныйЭкран_ПанельОтображения_БезПереполненияНаОбычном96(int ширина, int высота)
    {
        // Показ обязателен (регрессия LAY3-01): в невызванной форме колонки
        // вырождены (ширина группы 54 px, Dock=Top-таблица получает
        // отрицательную ширину) и инвариант проверки границ не имеет смысла.
        var (профиль, окно) = await ПоказатьОкноAsync(ширина, высота);

        using (профиль)
        using (окно)
        {
            var контекст = $"96 DPI {ширина}×{высота}";

            ПроверитьОтсутствиеПереполнения(окно, контекст);
            ПроверитьДоступностьЭлементов(окно, контекст);
        }
    }

    /// <summary>
    /// Повторный PerformLayout не ломает раскладку повторно (идемпотентность
    /// исправления при последующих пересчётах).
    /// </summary>
    [Fact]
    public async Task ГлавныйЭкран_ПанельОтображения_Стабильна_ПриПовторномPerformLayout()
    {
        var (профиль, окно) = await ПоказатьОкноAsync(1000, 700);

        using (профиль)
        using (окно)
        {
            var таблица = ТаблицаПереключателей(окно);
            var до = Обойти(таблица).Cast<Control>().Select(ребёнок => ребёнок.Bounds).ToList();

            окно.PerformLayout();

            var после = Обойти(таблица).Cast<Control>().Select(ребёнок => ребёнок.Bounds).ToList();

            Assert.Equal(до, после);
            ПроверитьОтсутствиеПереполнения(окно, "повторный PerformLayout");
        }
    }

    // 120/144 DPI проверяются runtime-харнессом (tools/ui-acceptance/
    // run-dpi.ps1): процесс testhost — SystemAware, и симуляция
    // WM_DPICHANGED в нём не отслеживается DeviceDpi (проверено 2026-11-09:
    // ApplicationHighDpiMode задаётся только процессу). Подменять реальный
    // DPI ручным масштабированием запрещено (честность флагов §8.2
    // stage3.1); harness собирается с PerMonitorV2 и доказывает 120/144
    // реальным конвейером (см. docs/.../stage3.2/lay3-01-layoutcheck.md).
}
