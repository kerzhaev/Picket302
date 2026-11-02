using System.Reflection;
using System.Windows.Forms;
using Picket302.App;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессия F-E6: прямой набор адресной книги принимает символы от события
/// KeyPress. Прежде символ вычислялся из виртуального кода клавиши
/// ((char)KeyCode), который не зависит от раскладки, — кириллица давала
/// латинские буквы.
/// </summary>
public sealed class MainShellDirectDialTests
{
    /// <summary>Закрытый обработчик символьного ввода (KeyPress).</summary>
    private static readonly MethodInfo ОбработатьСимвол =
        typeof(MainShell).GetMethod(
            "ОбработатьНаборВТаблицеПоСимволу",
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(KeyPressEventArgs)])
        ?? throw new InvalidOperationException("Обработчик KeyPress не найден.");

    /// <summary>Закрытый обработчик клавиш (KeyDown).</summary>
    private static readonly MethodInfo ОбработатьКлавишу =
        typeof(MainShell).GetMethod(
            "ОбработатьНаборВТаблице",
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(KeyEventArgs)])
        ?? throw new InvalidOperationException("Обработчик KeyDown не найден.");

    [Fact]
    public void ПрямойНабор_СимволКириллицы_ДобавляетсяВПоиск()
    {
        using var окно = new MainShell();

        var аргументы = new KeyPressEventArgs('О');

        ОбработатьСимвол.Invoke(окно, [null, аргументы]);

        Assert.Equal("О", окно.ПрямойНабор);
        Assert.True(аргументы.Handled);
    }

    [Fact]
    public void ПрямойНабор_BackspaceУдаляетСимвол()
    {
        using var окно = new MainShell();

        ОбработатьСимвол.Invoke(окно, [null, new KeyPressEventArgs('О')]);
        ОбработатьСимвол.Invoke(окно, [null, new KeyPressEventArgs('Л')]);

        Assert.Equal("ОЛ", окно.ПрямойНабор);

        var возврат = new KeyEventArgs(Keys.Back);

        ОбработатьКлавишу.Invoke(окно, [null, возврат]);

        Assert.Equal("О", окно.ПрямойНабор);
        Assert.True(возврат.Handled);
    }

    [Fact]
    public void ПрямойНабор_EscСбрасываетПоиск()
    {
        using var окно = new MainShell();

        ОбработатьСимвол.Invoke(окно, [null, new KeyPressEventArgs('О')]);

        Assert.Equal("О", окно.ПрямойНабор);

        var отмена = new KeyEventArgs(Keys.Escape);

        ОбработатьКлавишу.Invoke(окно, [null, отмена]);

        Assert.Equal(string.Empty, окно.ПрямойНабор);
        Assert.True(отмена.Handled);
    }
}
