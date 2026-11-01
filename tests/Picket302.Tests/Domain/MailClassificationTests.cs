using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Категория отправления и вид уведомления (ТЗ §8.1, функции 23 и 32).
/// </summary>
public sealed class MailClassificationTests
{
    [Theory]
    [InlineData(MailCategory.Simple)]
    [InlineData(MailCategory.Registered)]
    [InlineData(MailCategory.DeclaredValue)]
    [InlineData(MailCategory.FirstClass)]
    public void SetCategory_НазначаетКатегориюОтправления(MailCategory категория)
    {
        var письмо = new Mail();

        письмо.SetCategory(категория);

        Assert.Equal(категория, письмо.MailCategory);
    }

    [Fact]
    public void SetCategory_НеизвестнаяКатегория_Отклоняется()
    {
        var письмо = new Mail();

        Assert.Throws<ArgumentOutOfRangeException>(() => письмо.SetCategory((MailCategory)99));
    }

    [Theory]
    [InlineData(NotificationType.Form119, "Form119")]
    [InlineData(NotificationType.National, "National")]
    [InlineData(NotificationType.Form112Ep, "Form112Ep")]
    public void SelectNotificationForm_ВыбираетФормуУведомления(
        NotificationType вид,
        string ожидаемыйКлюч)
    {
        var письмо = new Mail();

        письмо.SelectNotificationForm(вид);

        Assert.Equal(вид, письмо.NotificationType);
        Assert.Equal(ожидаемыйКлюч, письмо.NoticeType);
        Assert.True(письмо.NeedNotification);
    }

    [Fact]
    public void SelectNotificationForm_БезУведомления_СнимаетФлагПечати()
    {
        var письмо = new Mail { NeedNotification = true };

        письмо.SelectNotificationForm(NotificationType.None);

        Assert.Equal(NotificationType.None, письмо.NotificationType);
        Assert.False(письмо.NeedNotification);
    }

    [Fact]
    public void SelectNotificationForm_НеизвестныйВид_Отклоняется()
    {
        var письмо = new Mail();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => письмо.SelectNotificationForm((NotificationType)42));
    }

    [Theory]
    [InlineData("Form119", NotificationType.Form119)]
    [InlineData("ф.119", NotificationType.Form119)]
    [InlineData("119", NotificationType.Form119)]
    [InlineData("National", NotificationType.National)]
    [InlineData("национальная", NotificationType.National)]
    [InlineData("Form112Ep", NotificationType.Form112Ep)]
    [InlineData("112эп", NotificationType.Form112Ep)]
    [InlineData("неизвестно", NotificationType.None)]
    [InlineData("", NotificationType.None)]
    [InlineData(null, NotificationType.None)]
    public void NoticeType_РазбираетсяВВидУведомления(string? значение, NotificationType ожидаемый)
    {
        var письмо = new Mail { NoticeType = значение };

        Assert.Equal(ожидаемый, письмо.NotificationType);
    }
}
