using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Контракт доступности и целостности компоновки рабочих диалогов
/// (аудит UX, серия stage3 2026-11-08).
/// </summary>
/// <remarks>
/// <para>
/// Проверяются три свойства, общие для всех диалогов оператора:
/// </para>
/// <list type="number">
/// <item>каждая кнопка имеет непустое <c>AccessibleName</c> — иначе
/// программа чтения с экрана объявляет кнопку безымянной, несмотря на
/// нарисованную подпись (дизайн-контракт §6, правило A1). Дефект
/// подтверждён диагностическим зондом 2026-11-08: у семи кнопок трёх
/// диалогов имя отсутствовало;</item>
/// <item>подпись с переносом помещается в отведённую высоту — обрезанный
/// текст предупреждения недопустим (правило L6);</item>
/// <item>подпись кнопки помещается в её ширину.</item>
/// </list>
/// <para>
/// Диалоги показываются на время проверки: фиксированный диалог без показа
/// не выполняет полную раскладку, и геометрические проверки были бы
/// проверкой «ничего». Коллекция WinFormsПрофиль сериализует показ форм
/// между тестами. COM, реестр, принтер и физическая печать не используются.
/// </para>
/// </remarks>
[Collection("WinFormsПрофиль")]
public sealed class DialogAccessibilityContractTests
{
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

    private static void Показать(Form форма)
    {
        форма.StartPosition = FormStartPosition.Manual;
        форма.Show();
        форма.PerformLayout();
        System.Windows.Forms.Application.DoEvents();
    }

    /// <summary>
    /// Диалоги, в которых проверяется контракт. Список зафиксирован явно:
    /// он совпадает с поверхностями матрицы визуального сравнения
    /// (карточка контрагента, карточка отправления, настройка вида таблиц).
    /// </summary>
    public static IEnumerable<object[]> Диалоги()
    {
        yield return new object[] { "Карточка контрагента", new FormCounterpartyEdit() };
        yield return new object[] { "Карточка отправления", new FormMailEdit() };
        yield return new object[]
        {
            "Настройка вида таблиц",
            new FormGridAppearance(Picket302.Domain.Contracts.GridAppearance.Default),
        };
        yield return new object[]
        {
            "Выбор клипарта",
            new FormClipartPicker(new Picket302.Infrastructure.Templates.ClipartLibrary()),
        };
    }

    /// <summary>
    /// Каждая кнопка диалога имеет доступное имя.
    /// </summary>
    [Theory]
    [MemberData(nameof(Диалоги))]
    public void КнопкиДиалога_ИмеютДоступноеИмя(string имя, Form форма)
    {
        using (форма)
        {
            Показать(форма);

            var кнопки = Обойти(форма).OfType<Button>().ToList();

            Assert.NotEmpty(кнопки);

            foreach (var кнопка in кнопки)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(кнопка.AccessibleName),
                    $"Кнопка «{кнопка.Text}» в диалоге «{имя}» без AccessibleName.");
            }
        }
    }

    /// <summary>
    /// Подписи с переносом не обрезаются: нужная высота помещается в
    /// отведённую.
    /// </summary>
    [Theory]
    [MemberData(nameof(Диалоги))]
    public void ПодписиДиалога_НеОбрезаются(string имя, Form форма)
    {
        using (форма)
        {
            Показать(форма);

            foreach (var метка in Обойти(форма).OfType<Label>())
            {
                // Скрытая метка (пустое состояние и т.п.) раскладывается по
                // Dock при первом показе, поэтому её текущий размер до
                // показа — не признак обрезания.
                if (метка.AutoSize || !метка.Visible || string.IsNullOrWhiteSpace(метка.Text))
                {
                    continue;
                }

                var нужнаяВысота = TextRenderer.MeasureText(
                    метка.Text,
                    метка.Font,
                    new Size(метка.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

                Assert.True(
                    нужнаяВысота <= метка.Height + 1,
                    $"Подпись «{метка.Text}» в диалоге «{имя}» обрезана: нужно {нужнаяВысота} px, есть {метка.Height} px.");
            }
        }
    }

    /// <summary>
    /// Подписи кнопок помещаются в их ширину.
    /// </summary>
    [Theory]
    [MemberData(nameof(Диалоги))]
    public void ПодписиКнопок_ПомещаютсяВШирину(string имя, Form форма)
    {
        using (форма)
        {
            Показать(форма);

            foreach (var кнопка in Обойти(форма).OfType<Button>())
            {
                if (кнопка.Text.Length <= 2)
                {
                    // Глифы-стрелки и прочие символьные подписи не измеряются.
                    continue;
                }

                var нужнаяШирина = TextRenderer.MeasureText(кнопка.Text, кнопка.Font).Width;

                Assert.True(
                    нужнаяШирина <= кнопка.Width - 4,
                    $"Подпись кнопки «{кнопка.Text}» в диалоге «{имя}» обрезана: нужно {нужнаяШирина} px, есть {кнопка.Width} px.");
            }
        }
    }

    /// <summary>
    /// Обычные подписи не набраны капителью (дизайн-контракт §2, правило C4).
    /// Капитель допускается только там, где данные сами заглавные
    /// (ИНН, БИК, КПП, ОГРН, названия столбцов таблиц).
    /// </summary>
    [Theory]
    [MemberData(nameof(Диалоги))]
    public void ПодписиДиалога_НеНабраныКапителью(string имя, Form форма)
    {
        using (форма)
        {
            Показать(форма);

            foreach (var метка in Обойти(форма).OfType<Label>())
            {
                if (string.IsNullOrWhiteSpace(метка.Text))
                {
                    continue;
                }

                var буквы = метка.Text.Where(char.IsLetter).ToArray();

                if (буквы.Length < 3)
                {
                    continue;
                }

                // Служебные технические подписи вида «ИНН:» состоят из
                // заглавных букв с двоеточием — это обозначение поля, а не
                // капитель обычной подписи.
                var техническоеПоле = буквы.All(char.IsUpper)
                    && метка.Text.TrimEnd().TrimEnd(':').Length <= 6
                    && метка.Text.TrimEnd().EndsWith(':');

                Assert.False(
                    буквы.All(char.IsUpper) && !техническоеПоле,
                    $"Подпись «{метка.Text}» в диалоге «{имя}» набрана капителью.");
            }
        }
    }
}
