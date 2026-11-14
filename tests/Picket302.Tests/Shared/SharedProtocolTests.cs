using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Матрица совместимости версий протокола совместного режима.
/// </summary>
/// <remarks>
/// Проверяется правило из ТЗ: «версия протокола» — клиент и служба обязаны
/// договориться о версии до обмена данными; несовместимая версия отклоняется,
/// а не приводит к порче данных.
/// </remarks>
public sealed class SharedProtocolTests
{
    [Fact]
    public void Текущая_Версия_Поддерживается()
    {
        Assert.True(SharedProtocol.IsSupported(SharedProtocol.Version));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void Несовместимая_Версия_Отклоняется(int версия)
    {
        Assert.False(SharedProtocol.IsSupported(версия));
    }

    [Fact]
    public void Минимальная_Поддерживаемая_Версия_Входит_В_Диапазон()
    {
        Assert.True(SharedProtocol.IsSupported(SharedProtocol.MinimumSupportedVersion));
        Assert.True(SharedProtocol.MinimumSupportedVersion <= SharedProtocol.Version);
    }

    [Fact]
    public void Порт_Службы_Отличается_От_Порта_PostgreSql()
    {
        // Порт PostgreSQL (5432) не должен пересекаться с портом службы:
        // оба режима могут сосуществовать на одном компьютере.
        Assert.NotEqual(5432, SharedProtocol.DefaultPort);
        Assert.Equal(30242, SharedProtocol.DefaultPort);
    }

    [Fact]
    public void Пределы_Размера_Положительны_И_Упорядочены()
    {
        Assert.True(SharedProtocol.MaxRequestBodyBytes > 0);
        Assert.True(SharedProtocol.MaxTemplatePackageBytes > 0);
        Assert.True(
            SharedProtocol.MaxTemplatePackageExpandedBytes >= SharedProtocol.MaxTemplatePackageBytes);
        Assert.True(SharedProtocol.MaxPageSize > 0);
        Assert.True(SharedProtocol.MaxIdListLength > 0);
    }

    [Fact]
    public void Маршруты_Начинаются_С_Общего_Префикса()
    {
        Assert.StartsWith(SharedProtocol.RoutePrefix, SharedProtocol.HealthRoute);
        Assert.StartsWith(SharedProtocol.RoutePrefix, SharedProtocol.PairRoute);
        Assert.StartsWith(SharedProtocol.RoutePrefix, SharedProtocol.ServerInfoRoute);
        Assert.StartsWith(SharedProtocol.RoutePrefix, SharedProtocol.CommandRoute);
    }
}

/// <summary>
/// Стабильность кодов ошибок протокола.
/// </summary>
public sealed class SharedErrorTests
{
    [Fact]
    public void Коды_Ошибок_Различны()
    {
        var коды = new[]
        {
            SharedErrorCodes.Unauthorized,
            SharedErrorCodes.VersionIncompatible,
            SharedErrorCodes.Conflict,
            SharedErrorCodes.ServiceUnavailable,
            SharedErrorCodes.InsufficientSpace,
            SharedErrorCodes.MigrationFailed,
            SharedErrorCodes.NotFound,
            SharedErrorCodes.ValidationFailed,
            SharedErrorCodes.IdempotencyMismatch,
            SharedErrorCodes.PayloadTooLarge,
            SharedErrorCodes.ArchiveRejected,
            SharedErrorCodes.OperationInProgress,
            SharedErrorCodes.Internal,
        };

        Assert.Equal(коды.Length, коды.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Конфликт_И_Отказ_В_Авторизации_Различаются()
    {
        var конфликт = SharedError.Create(SharedErrorCodes.Conflict, "Запись изменена на другом компьютере.");
        var отказ = SharedError.Create(SharedErrorCodes.Unauthorized, "Устройство отозвано.");

        Assert.True(конфликт.IsConflict);
        Assert.False(конфликт.IsUnauthorized);
        Assert.True(отказ.IsUnauthorized);
        Assert.False(отказ.IsConflict);
    }

    [Fact]
    public void Ошибка_Сохраняет_Идентификатор_Операции()
    {
        var ошибка = SharedError.Create(
            SharedErrorCodes.Internal,
            "Внутренняя ошибка службы.",
            "операция-123");

        Assert.Equal("операция-123", ошибка.OperationId);
    }
}
