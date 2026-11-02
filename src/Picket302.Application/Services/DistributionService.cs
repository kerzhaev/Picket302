using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Результат повторной отправки рассылки (ТЗ §8.11, функция 142).
/// </summary>
/// <param name="Distribution">Созданная копия рассылки в статусе «Черновик».</param>
/// <param name="SkippedCounterparties">
/// Наименования контрагентов, удалённых к моменту повторной отправки: они
/// пропущены и перечислены в предупреждении.
/// </param>
/// <param name="LettersCreated">Число созданных писем копии.</param>
public sealed record ClonedDistribution(
    Distribution Distribution,
    IReadOnlyList<string> SkippedCounterparties,
    int LettersCreated);

/// <summary>
/// Рассылки: формирование, состав, повторная отправка и список
/// (ТЗ §8.1, функция 21; §8.12, функции 140, 141, 142 и 156).
/// </summary>
public sealed class DistributionService
{
    private readonly IDistributionRepository _рассылки;
    private readonly IMailRepository _письма;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;
    private readonly ILicenseGate? _ворота;

    /// <summary>Создаёт службу рассылок.</summary>
    /// <param name="рассылки">Репозиторий рассылок.</param>
    /// <param name="письма">Репозиторий почтовых отправлений.</param>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    /// <param name="ворота">Ворота лицензии; при отсутствии проверка не выполняется.</param>
    public DistributionService(
        IDistributionRepository рассылки,
        IMailRepository письма,
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы,
        ILicenseGate? ворота = null)
    {
        ArgumentNullException.ThrowIfNull(рассылки);
        ArgumentNullException.ThrowIfNull(письма);
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _рассылки = рассылки;
        _письма = письма;
        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
        _ворота = ворота;
    }

    /// <summary>
    /// Формирует рассылку: создаёт пачку писем по одному из двух видов
    /// (ТЗ §8.1, функция 21).
    /// </summary>
    /// <remarks>
    /// Вид «один ко многим»: один отправитель — множеству контрагентов-получателей.
    /// Вид «многие к одному»: множество отправителей — одному центральному
    /// получателю.
    /// </remarks>
    /// <param name="plan">План рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная рассылка либо текст нарушения.</returns>
    public async Task<Result<Distribution>> GenerateDistributionAsync(
        DistributionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var разрешение = _ворота?.Проверить(LicenseOperation.СозданиеЗаписей);

        if (разрешение is { Разрешено: false })
        {
            return Result<Distribution>.Failure(разрешение.Сообщение);
        }

        if (plan.CounterpartyIds.Count == 0)
        {
            return Result<Distribution>.Failure(
                "Список адресатов пуст — генерация рассылки отменена.");
        }

        if (plan.SenderId <= 0)
        {
            return Result<Distribution>.Failure(
                "Отправитель рассылки не указан — укажите или создайте отправителя.");
        }

        if (plan.CopyCount < Mail.MinimumCopyCount)
        {
            return Result<Distribution>.Failure(
                $"Число экземпляров должно быть не меньше {Mail.MinimumCopyCount}.");
        }

        var рассылка = new Distribution
        {
            Name = string.IsNullOrWhiteSpace(plan.Name) ? "Рассылка" : plan.Name.Trim(),
            SenderId = plan.SenderId,
            Kind = plan.Kind,
            Note = plan.Note,
            State = DistributionState.Draft,
            CreatedAt = DateTime.UtcNow,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _рассылки.AddAsync(рассылка, cancellationToken).ConfigureAwait(false);

            var создано = 0;

            foreach (var контрагентId in plan.CounterpartyIds)
            {
                var письмо = await СоздатьПисьмоAsync(
                    рассылка.Id,
                    plan,
                    контрагентId,
                    cancellationToken).ConfigureAwait(false);

                if (письмо is null)
                {
                    continue;
                }

                await _письма.AddAsync(письмо, cancellationToken).ConfigureAwait(false);
                создано++;
            }

            if (создано == 0)
            {
                await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result<Distribution>.Failure(
                    "Ни у одного адресата не найден основной адрес — генерация рассылки отменена.");
            }

            var конвертов = await _рассылки
                .RecalculateEnvelopesRequiredAsync(рассылка.Id, cancellationToken)
                .ConfigureAwait(false);

            рассылка.EnvelopesRequired = конвертов;

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Distribution>.Success(рассылка);
    }

    /// <summary>
    /// Создаёт рассылку из выделения в адресной книге — контекстное действие
    /// «Объединить в рассылку» (ТЗ §8.11, функция 141).
    /// </summary>
    /// <remarks>
    /// Рассылка создаётся в статусе «Черновик» сразу с готовым составом
    /// получателей и заполненным счётчиком конвертов: редактор открывается с
    /// уже добавленными получателями.
    /// </remarks>
    /// <param name="counterpartyIds">Идентификаторы выбранных контрагентов.</param>
    /// <param name="senderId">Текущий отправитель (ТЗ §8.1, функция 16).</param>
    /// <param name="name">Наименование рассылки.</param>
    /// <param name="envelopeType">Формат конверта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная рассылка либо текст нарушения.</returns>
    public async Task<Result<Distribution>> CreateFromSelectionAsync(
        IReadOnlyList<long> counterpartyIds,
        long? senderId = null,
        string? name = null,
        string? envelopeType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counterpartyIds);

        if (counterpartyIds.Count == 0)
        {
            return Result<Distribution>.Failure(
                "Не выбрано ни одной записи — пункт меню «Объединить в рассылку» неактивен.");
        }

        if (senderId is null or <= 0)
        {
            return Result<Distribution>.Failure(
                "Не выбран текущий отправитель — укажите отправителя в верхней части окна.");
        }

        var план = new DistributionPlan
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Рассылка" : name.Trim(),
            Kind = DistributionKind.OneToMany,
            SenderId = senderId.Value,
            CounterpartyIds = counterpartyIds.Distinct().ToList(),
            EnvelopeType = envelopeType,
        };

        return await GenerateDistributionAsync(план, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Создаёт копию сохранённой или отправленной рассылки для повторной отправки
    /// (ТЗ §8.11, функция 142).
    /// </summary>
    /// <remarks>
    /// Копия получает новый идентификатор и статус «Черновик»; исходная рассылка
    /// не изменяется. Адреса берутся из текущих карточек контрагентов: удалённые
    /// к этому моменту контрагенты пропускаются и перечисляются в предупреждении.
    /// </remarks>
    /// <param name="distributionId">Идентификатор исходной рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Копия рассылки либо текст нарушения.</returns>
    public async Task<Result<ClonedDistribution>> CloneForResendAsync(
        long distributionId,
        CancellationToken cancellationToken = default)
    {
        var разрешение = _ворота?.Проверить(LicenseOperation.СозданиеЗаписей);

        if (разрешение is { Разрешено: false })
        {
            return Result<ClonedDistribution>.Failure(разрешение.Сообщение);
        }

        var исходная = await _рассылки.GetByIdAsync(distributionId, cancellationToken)
            .ConfigureAwait(false);

        if (исходная is null)
        {
            return Result<ClonedDistribution>.Failure(
                $"Рассылка с идентификатором {distributionId} не найдена.");
        }

        var письма = await _письма.GetByDistributionAsync(distributionId, cancellationToken)
            .ConfigureAwait(false);

        if (письма.Count == 0)
        {
            return Result<ClonedDistribution>.Failure(
                "Печать пустой рассылки блокируется: в рассылке нет ни одного письма.");
        }

        var копия = new Distribution
        {
            Name = $"{исходная.Name} (повтор)",
            SenderId = исходная.SenderId,
            Kind = исходная.Kind,
            Note = исходная.Note,
            State = DistributionState.Draft,
            CreatedAt = DateTime.UtcNow,
        };

        var пропущены = new List<string>();
        var создано = 0;

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _рассылки.AddAsync(копия, cancellationToken).ConfigureAwait(false);

            foreach (var письмо in письма)
            {
                // Роли получателя и отправителя переносятся без ветвления по
                // виду рассылки (F-D2): прежде в «многие к одному» роли
                // инвертировались, и адрес снимался со стороны отправителя.
                var получатель = письмо.CounterpartyId;
                var отправитель = письмо.SenderId;

                var контрагент = await _контрагенты.GetByIdAsync(получатель, cancellationToken)
                    .ConfigureAwait(false);

                if (контрагент is null || контрагент.IsDeleted)
                {
                    пропущены.Add(контрагент?.Name ?? $"#{получатель}");
                    continue;
                }

                var адрес = контрагент.GetAddress(контрагент.AddressMainType);

                if (адрес is null)
                {
                    пропущены.Add(контрагент.Name);
                    continue;
                }

                var новое = MailFactory.CreateOutcoming(
                    получатель,
                    отправитель,
                    письмо.EnvelopeType ?? "C6",
                    адрес,
                    контрагент.IsVip,
                    письмо.CopyCount);

                новое.DistributionId = копия.Id;
                новое.MailCategory = письмо.MailCategory;
                новое.Note = письмо.Note;
                новое.NoticeType = письмо.NoticeType;
                новое.NeedNotification = письмо.NeedNotification;
                новое.NeedReturnAddress = письмо.NeedReturnAddress;

                await _письма.AddAsync(новое, cancellationToken).ConfigureAwait(false);
                создано++;
            }

            if (создано == 0)
            {
                await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);

                return Result<ClonedDistribution>.Failure(
                    "Все получатели исходной рассылки удалены — повторная отправка невозможна.");
            }

            var конвертов = await _рассылки
                .RecalculateEnvelopesRequiredAsync(копия.Id, cancellationToken)
                .ConfigureAwait(false);

            копия.EnvelopesRequired = конвертов;

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<ClonedDistribution>.Success(
            new ClonedDistribution(копия, пропущены, создано));
    }

    /// <summary>
    /// Возвращает список рассылок с фильтром «Созданы с»
    /// (ТЗ §8.12, функция 156).
    /// </summary>
    /// <param name="filter">Условия отбора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список представлений рассылок.</returns>
    public async Task<IReadOnlyList<DistributionView>> ListDistributionsAsync(
        DistributionFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var рассылки = await _рассылки.GetListAsync(filter, cancellationToken).ConfigureAwait(false);
        var представления = new List<DistributionView>(рассылки.Count);

        foreach (var рассылка in рассылки)
        {
            var письма = await _письма.GetByDistributionAsync(рассылка.Id, cancellationToken)
                .ConfigureAwait(false);

            представления.Add(new DistributionView(
                рассылка.Id,
                рассылка.Name,
                рассылка.Kind,
                рассылка.State,
                рассылка.SenderId,
                рассылка.Note,
                рассылка.EnvelopesRequired,
                рассылка.CreatedAt,
                рассылка.DispatchedAt,
                письма.Count));
        }

        return представления;
    }

    /// <summary>
    /// Пересчитывает счётчик «Всего необходимо конвертов» рассылки как сумму
    /// <c>copy_count</c> входящих писем (ТЗ §8.12, функция 155).
    /// </summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число конвертов.</returns>
    public Task<int> RecalculateEnvelopesRequiredAsync(
        long distributionId,
        CancellationToken cancellationToken = default)
        => _рассылки.RecalculateEnvelopesRequiredAsync(distributionId, cancellationToken);

    /// <summary>
    /// Сохраняет рассылку: переводит её из черновика в сохранённую
    /// (ТЗ §8.12, функция 156: состояния «Черновик / Сохранена / Отправлена»).
    /// </summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая рассылка либо текст нарушения.</returns>
    public async Task<Result<Distribution>> SaveAsync(
        long distributionId,
        CancellationToken cancellationToken = default)
    {
        var рассылка = await _рассылки.GetByIdAsync(distributionId, cancellationToken)
            .ConfigureAwait(false);

        if (рассылка is null)
        {
            return Result<Distribution>.Failure(
                $"Рассылка с идентификатором {distributionId} не найдена.");
        }

        if (рассылка.State != DistributionState.Draft)
        {
            return Result<Distribution>.Failure(
                $"Рассылка в состоянии «{рассылка.State}» не может быть сохранена повторно.");
        }

        рассылка.State = DistributionState.Saved;

        await _рассылки.UpdateAsync(рассылка, cancellationToken).ConfigureAwait(false);

        return Result<Distribution>.Success(рассылка);
    }

    /// <summary>
    /// Отмечает рассылку как отправленную (ТЗ §8.12, функция 156).
    /// </summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="moment">Момент отправки (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отправленная рассылка либо текст нарушения.</returns>
    public async Task<Result<Distribution>> MarkDispatchedAsync(
        long distributionId,
        DateTime? moment = null,
        CancellationToken cancellationToken = default)
    {
        var рассылка = await _рассылки.GetByIdAsync(distributionId, cancellationToken)
            .ConfigureAwait(false);

        if (рассылка is null)
        {
            return Result<Distribution>.Failure(
                $"Рассылка с идентификатором {distributionId} не найдена.");
        }

        var письма = await _письма.GetByDistributionAsync(distributionId, cancellationToken)
            .ConfigureAwait(false);

        if (письма.Count == 0)
        {
            return Result<Distribution>.Failure(
                "Печать пустой рассылки блокируется: в рассылке нет ни одного письма.");
        }

        рассылка.State = DistributionState.Dispatched;
        рассылка.DispatchedAt = moment ?? DateTime.UtcNow;

        await _рассылки.UpdateAsync(рассылка, cancellationToken).ConfigureAwait(false);

        return Result<Distribution>.Success(рассылка);
    }

    /// <summary>
    /// Создаёт письмо рассылки по плану для указанного контрагента.
    /// </summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="plan">План рассылки.</param>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Письмо либо <see langword="null"/>, если у получателя нет основного адреса.</returns>
    /// <remarks>
    /// Роли зависят от вида рассылки (F-D1): в «один ко многим» перебираемый
    /// контрагент — получатель, в «многие к одному» получателем является
    /// центральный контрагент (<c>plan.SenderId</c>), а перебираемый
    /// контрагент используется только в роли отправителя. Адрес снимается
    /// со стороны получателя.
    /// </remarks>
    private async Task<Mail?> СоздатьПисьмоAsync(
        long distributionId,
        DistributionPlan plan,
        long counterpartyId,
        CancellationToken cancellationToken)
    {
        var получательId = plan.Kind == DistributionKind.OneToMany ? counterpartyId : plan.SenderId;
        var отправительId = plan.Kind == DistributionKind.OneToMany ? plan.SenderId : counterpartyId;

        var получатель = await _контрагенты.GetByIdAsync(получательId, cancellationToken)
            .ConfigureAwait(false);

        if (получатель is null || получатель.IsDeleted)
        {
            return null;
        }

        var адрес = получатель.GetAddress(получатель.AddressMainType);

        if (адрес is null)
        {
            return null;
        }

        var письмо = MailFactory.CreateOutcoming(
            получательId,
            отправительId,
            plan.EnvelopeType ?? "C6",
            адрес,
            получатель.IsVip,
            plan.CopyCount);

        письмо.DistributionId = distributionId;

        return письмо;
    }
}
