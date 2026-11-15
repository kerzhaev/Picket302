using Picket302.Domain.Entities;

namespace Picket302.App.Shared;

/// <summary>
/// Получение содержимого пакета шаблона (.lbk) на рабочем месте.
/// </summary>
/// <remarks>
/// <para>
/// Строка шаблона в совместном режиме хранит привязку к пакету:
/// идентификатор, версию и SHA-256 содержимого. Путь к файлу на сервере
/// клиенту не передаётся, поэтому содержимое получается по привязке и
/// складывается в кэш рабочего места.
/// </para>
/// <para>
/// Кэш отдаёт только ту версию, отпечаток которой совпадает с объявленным.
/// После смены версии пакета на сервере отпечаток меняется, прежняя копия
/// не используется; повреждённая копия заменяется заново. Несовпадение
/// отпечатка содержимого, полученного от сервера, — отказ с понятным
/// сообщением, а не молчаливая печать по чужому макету.
/// </para>
/// <para>
/// Загрузка содержимого передаётся делегатом: получатель остаётся
/// проверяемым без сетевого транспорта.
/// </para>
/// </remarks>
public sealed class SharedTemplatePackageResolver
{
    private readonly SharedTemplatePackageCache _кэш;

    /// <summary>
    /// Создаёт получатель содержимого пакетов.
    /// </summary>
    /// <param name="кэш">Кэш пакетов рабочего места.</param>
    public SharedTemplatePackageResolver(SharedTemplatePackageCache кэш)
    {
        ArgumentNullException.ThrowIfNull(кэш);

        _кэш = кэш;
    }

    /// <summary>
    /// Возвращает локальный путь к содержимому пакета, с которым связан
    /// шаблон.
    /// </summary>
    /// <param name="шаблон">Шаблон.</param>
    /// <param name="загрузить">
    /// Загрузка содержимого версии пакета: идентификатор, версия, токен
    /// отмены.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Локальный путь к файлу пакета; <see langword="null"/> — шаблон не связан
    /// с файловым пакетом.
    /// </returns>
    /// <exception cref="Picket302.Domain.Common.TemplatePackageUnavailableException">
    /// Содержимое версии пакета получить не удалось либо оно не совпадает с
    /// объявленным отпечатком.
    /// </exception>
    public async Task<string?> РазрешитьAsync(
        PrintTemplate шаблон,
        Func<string, long, CancellationToken, Task<byte[]>> загрузить,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(шаблон);
        ArgumentNullException.ThrowIfNull(загрузить);

        if (шаблон.PackageId is null
            || шаблон.PackageVersion is null
            || string.IsNullOrWhiteSpace(шаблон.PackageHash))
        {
            return null;
        }

        var идентификатор = шаблон.PackageId;
        var версия = шаблон.PackageVersion.Value;

        try
        {
            return await _кэш
                .ПутьКВерсииAsync(
                    идентификатор,
                    версия,
                    шаблон.PackageHash,
                    токен => загрузить(идентификатор, версия, токен),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SharedModeException ошибка)
        {
            throw new Picket302.Domain.Common.TemplatePackageUnavailableException(
                "Содержимое макета шаблона «" + шаблон.Name + "» получить не удалось: "
                + ошибка.Message
                + " Печать и предпросмотр не выполняются: печать по неверной версии макета "
                + "недопустима.");
        }
    }
}
