using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Printing.Engine;

/// <summary>
/// Режим печати на разлинованные (типографские) конверты (ТЗ §8.6, функция 89:
/// <c>Printing.Engine.PreprintedMode.StripBackground</c>).
/// </summary>
/// <remarks>
/// <para>В режиме «Размеченный типографский конверт» из печати исключаются фоновые
/// изображения, рамки и сетка разлиновки: выводятся только адресные данные и
/// индекс. Фоновым слоем бланка считается растровое поле (вид <c>picture</c>).</para>
/// <para>Предупреждение о соответствии ГОСТ Р 51506-99 не блокирует печать и
/// сопровождается чекбоксом «Больше не показывать это сообщение». Флаг
/// подавления хранится в профиле пользователя так же, как флаг подсказки о
/// подаче конвертов узкой стороной (см. <c>Printing.Warnings.FeedOrientationHint</c>).</para>
/// </remarks>
public sealed class PreprintedMode
{
    /// <summary>Текст предупреждения о соответствии ГОСТ и риске выхода за разлиновку.</summary>
    public const string Warning =
        "Внимание: печать на разлинованный типографский конверт. Убедитесь, что конверт "
        + "соответствует ГОСТ Р 51506-99 и что партия разлиновки совпадает с макетом: "
        + "при несовпадении данные могут попасть вне полей разлиновки.";

    /// <summary>Ключ флага подавления предупреждения в профиле пользователя.</summary>
    public const string SuppressionKey = "SuppressPreprintedModeWarning";

    /// <summary>Текст чекбокса подавления предупреждения.</summary>
    public const string SuppressCaption = "Больше не показывать это сообщение";

    private readonly IParameterRepository? _параметры;

    private bool _подавлено;

    /// <summary>
    /// Создаёт режим печати на разлинованный конверт.
    /// </summary>
    /// <param name="parameters">
    /// Хранилище параметров профиля пользователя для флага подавления
    /// предупреждения. Необязательный параметр: без него предупреждение
    /// всегда выводится, а состояние флага хранится только в памяти.
    /// </param>
    public PreprintedMode(IParameterRepository? parameters = null)
    {
        _параметры = parameters;
    }

    /// <summary>
    /// Исключает из печати фоновые слои бланка.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <returns>Контекст без фоновых полей и с предупреждением о разлиновке.</returns>
    public PrintJobContext StripBackground(PrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var поля = context.Fields
            .Where(поле => !string.Equals(поле.Kind, BlankFieldKind.Picture, StringComparison.Ordinal))
            .ToList();

        var предупреждения = new List<string>(context.Warnings);

        if (!_подавлено)
        {
            предупреждения.Add(Warning);
        }

        return context with
        {
            Fields = поля,
            Preprinted = true,
            Warnings = предупреждения,
        };
    }

    /// <summary>
    /// Исключает из печати фоновые слои бланка, предварительно уточняя в
    /// профиле пользователя состояние флага подавления предупреждения.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Контекст без фоновых полей и, если предупреждение не подавлено, с ним.</returns>
    public async Task<PrintJobContext> StripBackgroundAsync(
        PrintJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await ShouldShowAsync(cancellationToken).ConfigureAwait(false);

        return StripBackground(context);
    }

    /// <summary>
    /// Определяет, следует ли показывать предупреждение о разлиновке.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если предупреждение нужно показать.</returns>
    public async Task<bool> ShouldShowAsync(CancellationToken cancellationToken = default)
    {
        if (_параметры is null)
        {
            return !_подавлено;
        }

        var значение = await _параметры
            .GetValueAsync(SuppressionKey, cancellationToken)
            .ConfigureAwait(false);

        _подавлено = string.Equals(значение, "1", StringComparison.Ordinal);

        return !_подавлено;
    }

    /// <summary>
    /// Сохраняет флаг подавления предупреждения в профиле пользователя.
    /// </summary>
    /// <param name="suppressed">Признак подавления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SetSuppressedAsync(bool suppressed, CancellationToken cancellationToken = default)
    {
        _подавлено = suppressed;

        if (_параметры is null)
        {
            return;
        }

        var параметр = await _параметры
            .GetByNameAsync(SuppressionKey, cancellationToken)
            .ConfigureAwait(false);

        if (параметр is null)
        {
            await _параметры
                .AddAsync(
                    new Parameter { Name = SuppressionKey, Value = suppressed ? "1" : "0" },
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        параметр.Value = suppressed ? "1" : "0";
        await _параметры.UpdateAsync(параметр, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Текст предупреждения о соответствии ГОСТ Р 51506-99.</summary>
    public string WarningText => Warning;
}
