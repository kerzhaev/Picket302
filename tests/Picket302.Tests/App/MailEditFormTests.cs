using Picket302.App.UI.Forms;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Карточка отправления: выпадающие списки категории отправления и вида
/// уведомления, флаг «нужно уведомление» (ТЗ §8.1, функции 23 и 32).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class MailEditFormTests
{
    [Fact]
    public void КарточкаОтправления_СпискиЗаполнены_ВсемиВидами()
    {
        using var форма = new FormMailEdit();

        Assert.Equal(4, форма.ПереченьКатегорий.Items.Count);
        Assert.Equal(4, форма.ПереченьУведомлений.Items.Count);
        Assert.Contains("Заказное письмо", форма.ПереченьКатегорий.Items.OfType<string>());
        Assert.Contains("Бандероль с объявленной ценностью", форма.ПереченьКатегорий.Items.OfType<string>());
        Assert.Contains("1-й класс", форма.ПереченьКатегорий.Items.OfType<string>());
        Assert.Contains("ф.119 (РФ)", форма.ПереченьУведомлений.Items.OfType<string>());
        Assert.Contains("Национальная форма", форма.ПереченьУведомлений.Items.OfType<string>());
        Assert.Contains("ф.112эп (совмещённый перевод)", форма.ПереченьУведомлений.Items.OfType<string>());
    }

    [Fact]
    public void КарточкаОтправления_ЗначенияПоУмолчанию_ПростоеПисьмоБезУведомления()
    {
        using var форма = new FormMailEdit();

        Assert.Equal(MailCategory.Simple, форма.Категория);
        Assert.Equal(NotificationType.None, форма.ВидУведомления);
        Assert.False(форма.НужноУведомление);
        Assert.Equal(Mail.MinimumCopyCount, форма.ЧислоКопий);
        Assert.Equal("C6", форма.ФорматКонверта);
    }

    [Fact]
    public void КарточкаОтправления_ВыборПрименяется_КОтправлению()
    {
        using var форма = new FormMailEdit();

        форма.ПереченьКатегорий.SelectedIndex = 1;
        форма.ПереченьУведомлений.SelectedIndex = 1;
        форма.ФлажокУведомления.Checked = true;
        форма.ПолеКопий.Value = 3;

        Assert.Equal(MailCategory.Registered, форма.Категория);
        Assert.Equal(NotificationType.Form119, форма.ВидУведомления);
        Assert.True(форма.НужноУведомление);
        Assert.Equal(3, форма.ЧислоКопий);

        var письмо = new Mail { CopyCount = 1 };

        форма.Применить(письмо);

        Assert.Equal(MailCategory.Registered, письмо.MailCategory);
        Assert.Equal(NotificationType.Form119, письмо.NotificationType);
        Assert.Equal("Form119", письмо.NoticeType);
        Assert.True(письмо.NeedNotification);
        Assert.Equal(3, письмо.CopyCount);
    }

    [Fact]
    public void КарточкаОтправления_БезУведомления_ПолеОчищается()
    {
        using var форма = new FormMailEdit();

        var письмо = new Mail { CopyCount = 1, NoticeType = "Form119", NeedNotification = true };

        форма.Применить(письмо);

        Assert.Equal(NotificationType.None, письмо.NotificationType);
        Assert.Null(письмо.NoticeType);
        Assert.False(письмо.NeedNotification);
    }

    [Fact]
    public void КарточкаОтправления_НачальныеЗначения_Восстанавливаются()
    {
        var значения = new MailCardValues(
            MailCategory.FirstClass,
            NotificationType.Form112Ep,
            НужноУведомление: true,
            ФорматКонверта: "DL",
            ЧислоКопий: 2);

        using var форма = new FormMailEdit(значения);

        Assert.Equal(MailCategory.FirstClass, форма.Категория);
        Assert.Equal(NotificationType.Form112Ep, форма.ВидУведомления);
        Assert.True(форма.НужноУведомление);
        Assert.Equal("DL", форма.ФорматКонверта);
        Assert.Equal(2, форма.ЧислоКопий);
    }
}
