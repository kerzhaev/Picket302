using Picket302.Infrastructure.Integration;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка поиска адреса контрагента по ИНН/УНН в интернете
/// (ТЗ §8.10, функция 108).
/// </summary>
/// <remarks>
/// Браузер не открывается: адрес подменяется делегатом, поэтому проверка
/// не зависит от наличия обозревателя в системе.
/// </remarks>
public sealed class WebLookupLauncherTests
{
    /// <summary>
    /// Плейсхолдер <c>{INN}</c> заменяется значением ИНН.
    /// </summary>
    [Fact]
    public void ПлейсхолдерИнн_ЗаменяетсяЗначением()
    {
        string? открытыйАдрес = null;

        var запускатель = new WebLookupLauncher(
            имя => имя == WebLookupLauncher.КлючШаблона ? "https://example.com/search?q={INN}" : null,
            адрес =>
            {
                открытыйАдрес = адрес;

                return true;
            },
            () => null);

        Assert.True(запускатель.TryOpenWebLookup("7707083893"));
        Assert.Equal("https://example.com/search?q=7707083893", открытыйАдрес);
        Assert.Null(запускатель.ПоследняяОшибка);
    }

    /// <summary>
    /// Плейсхолдер распознаётся без учёта регистра: <c>{inn}</c> также
    /// подставляется.
    /// </summary>
    [Fact]
    public void ПлейсхолдерВНижнемРегистре_ТакжеПодставляется()
    {
        string? открытыйАдрес = null;

        var запускатель = new WebLookupLauncher(
            имя => "https://example.com/search?q={inn}",
            адрес =>
            {
                открытыйАдрес = адрес;

                return true;
            },
            () => null);

        Assert.True(запускатель.TryOpenWebLookup("7707083893"));
        Assert.Equal("https://example.com/search?q=7707083893", открытыйАдрес);
    }

    /// <summary>
    /// Без настроенного шаблона поиск не выполняется, а пользователю
    /// предлагается настроить шаблон на странице «Сервис».
    /// </summary>
    [Fact]
    public void БезШаблона_ВозвращаетсяСообщениеОНастройке()
    {
        var запускатель = new WebLookupLauncher(
            _ => null,
            _ => true,
            () => null);

        Assert.False(запускатель.TryOpenWebLookup("7707083893"));
        Assert.NotNull(запускатель.ПоследняяОшибка);
        Assert.Contains("Сервис", запускатель.ПоследняяОшибка!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Адрес со схемой, отличной от http и https, не открывается: запуск
    /// произвольного протокола исключён.
    /// </summary>
    [Fact]
    public void НедопустимаяСхема_ОтказываетВОткрытии()
    {
        var запускатель = new WebLookupLauncher(
            _ => "file:///C:/Windows/System32/cmd.exe?{INN}",
            _ => true,
            () => null);

        Assert.False(запускатель.TryOpenWebLookup("7707083893"));
        Assert.NotNull(запускатель.ПоследняяОшибка);
        Assert.Contains("http", запускатель.ПоследняяОшибка!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Белорусская локализация использует прямой запрос к ЕГР.
    /// </summary>
    [Fact]
    public void БелорусскаяЛокализация_ИспользуетШаблонЕгр()
    {
        Assert.Equal(
            WebLookupLauncher.ШаблонЕгрБеларусь,
            WebLookupLauncher.ШаблонПоУмолчаниюДляЛокализации("be-BY"));

        Assert.Null(WebLookupLauncher.ШаблонПоУмолчаниюДляЛокализации("ru-RU"));

        string? открытыйАдрес = null;

        var запускатель = new WebLookupLauncher(
            _ => null,
            адрес =>
            {
                открытыйАдрес = адрес;

                return true;
            },
            () => WebLookupLauncher.ШаблонПоУмолчаниюДляЛокализации("be-BY"));

        Assert.True(запускатель.TryOpenWebLookup("7707083893"));
        Assert.NotNull(открытыйАдрес);
        Assert.Contains("egrc.nalog.by", открытыйАдрес!, StringComparison.Ordinal);
        Assert.Contains("7707083893", открытыйАдрес!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Пустой ИНН: поиск не выполняется, пользователю сообщается о причине.
    /// </summary>
    [Fact]
    public void ПустойИнн_НеОткрываетПоиск()
    {
        var запускатель = new WebLookupLauncher(
            _ => "https://example.com/search?q={INN}",
            _ => true,
            () => null);

        Assert.False(запускатель.TryOpenWebLookup("  "));
        Assert.NotNull(запускатель.ПоследняяОшибка);
    }
}
