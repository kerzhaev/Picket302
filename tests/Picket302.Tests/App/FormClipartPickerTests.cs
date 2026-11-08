using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Диалог выбора клипартов (ТЗ §8.10, функция 158): каталог загружается,
/// выбор элемента заполняет результат и включает кнопку выбора; без выбора
/// кнопка недоступна, отсутствующий элемент не выбирается.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormClipartPickerTests
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

    [Fact]
    public void Диалог_ЗагружаетВесьКаталогИПозволяетВыбратьЭлемент()
    {
        var каталог = new ClipartLibrary();
        using var диалог = new FormClipartPicker(каталог);

        var список = Обойти(диалог).OfType<ListView>().Single();

        Assert.Equal(каталог.Items.Count, список.Items.Count);
        Assert.True(список.Items.Count > 0, "Каталог клипартов не должен быть пустым.");

        var выбрать = Обойти(диалог).OfType<Button>()
            .Single(кнопка => кнопка.Text == "Выбрать");

        // Подтверждение доступно только после выделения элемента.
        Assert.False(выбрать.Enabled);
        Assert.Null(диалог.ВыбранныйId);

        Assert.True(диалог.ВыбратьЭлемент(каталог.Items[0].Id));

        Assert.True(выбрать.Enabled);
        Assert.Equal(каталог.Items[0].Id, диалог.ВыбранныйId);
        Assert.NotNull(диалог.ВыбранныйPng);
        Assert.True(диалог.ВыбранныйPng!.Length > 8);
    }

    [Fact]
    public void Диалог_КаждыйЭлементКаталогаВыбирается()
    {
        var каталог = new ClipartLibrary();
        using var диалог = new FormClipartPicker(каталог);

        foreach (var элемент in каталог.Items)
        {
            Assert.True(диалог.ВыбратьЭлемент(элемент.Id));

            Assert.Equal(элемент.Id, диалог.ВыбранныйId);
            Assert.NotNull(диалог.ВыбранныйPng);
        }
    }

    [Fact]
    public void Диалог_ОтсутствующийЭлемент_НеВыбираетсяИСбрасываетРезультат()
    {
        var каталог = new ClipartLibrary();
        using var диалог = new FormClipartPicker(каталог);

        Assert.True(диалог.ВыбратьЭлемент(каталог.Items[0].Id));
        Assert.False(диалог.ВыбратьЭлемент("несуществующий"));

        Assert.Null(диалог.ВыбранныйId);
        Assert.Null(диалог.ВыбранныйPng);
    }

    /// <summary>
    /// Регрессия 2026-11-08: показ диалога НЕ падает с
    /// «ArgumentException: Parameter is not valid» и значки каталога доступны
    /// после создания дескриптора списка.
    /// </summary>
    /// <remarks>
    /// Дефект: <c>ЗаполнитьКаталог</c> добавляла в ImageList исходный
    /// рисунок, привязанный к потоку, и уничтожала его в конце итерации.
    /// ImageList читает изображения отложенно — при создании дескриптора
    /// списка на первый показ формы — и падал по уничтоженному Image
    /// (стек: ListView.OnHandleCreated → ImageList.get_Handle →
    /// CreateBitmap → Image.get_Width). Существующие тесты дефект не ловили,
    /// потому что не показывали форму. Коллекция WinFormsПрофиль сериализует
    /// показ форм между тестами.
    /// </remarks>
    [Fact]
    public void Диалог_ПоказФормы_ЗначкиДоступныПослеСозданияДескриптора()
    {
        var каталог = new ClipartLibrary();
        using var диалог = new FormClipartPicker(каталог);

        var список = Обойти(диалог).OfType<ListView>().Single();

        диалог.Show();
        диалог.PerformLayout();
        System.Windows.Forms.Application.DoEvents();

        try
        {
            // Дескриптор списка создан при показе — значит, ImageList прочитал
            // все добавленные изображения: сбой чтения уничтоженного рисунка
            // проявился бы исключением на этой строке.
            Assert.True(список.IsHandleCreated);
            Assert.True(диалог.IsHandleCreated);

            Assert.Equal(каталог.Items.Count, список.LargeImageList!.Images.Count);
            Assert.Equal(каталог.Items.Count, список.Items.Count);
        }
        finally
        {
            диалог.Close();
        }
    }
}
