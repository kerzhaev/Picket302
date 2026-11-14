namespace Picket302.Shared.Protocol;

/// <summary>
/// Контекст выполнения команды на стороне службы.
/// </summary>
/// <param name="OperationId">
/// Идентификатор операции: клиент задаёт его один раз на команду, повтор
/// запроса с тем же идентификатором возвращает прежний результат.
/// </param>
/// <param name="DeviceId">Идентификатор сопряжённого устройства-клиента.</param>
/// <param name="DeviceName">Имя устройства, показанное администратору.</param>
/// <param name="StartedAt">
/// Момент начала операции (UTC). Служба сверяет его с записью в журнале
/// идемпотентности: результат фиксируется только если запись всё ещё
/// принадлежит этой попытке, а не была перехвачена другой.
/// </param>
public sealed record SharedCommandContext(
    string OperationId,
    string DeviceId,
    string DeviceName,
    DateTime StartedAt);

/// <summary>
/// Команда совместного режима: именованная типизированная операция.
/// </summary>
/// <typeparam name="TRequest">Тип запроса команды.</typeparam>
/// <typeparam name="TResponse">Тип ответа команды.</typeparam>
public abstract class SharedCommand<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Имя команды в URL. Имя уникально в пределах службы и не меняется после
    /// выпуска: переименование ломает работающие клиенты.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Признак того, что команду допустимо повторять автоматически при
    /// обрыве связи. Идемпотентные команды повторяются безопасно, остальные
    /// — только по явному решению оператора.
    /// </summary>
    public virtual bool IsSafeToRetry => false;

    /// <summary>
    /// Выполняет команду.
    /// </summary>
    /// <param name="context">Контекст выполнения.</param>
    /// <param name="request">Запрос команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    public abstract Task<TResponse> ExecuteAsync(
        SharedCommandContext context,
        TRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Описание зарегистрированной команды: имя, типы запроса и ответа и способ
/// вызова без приведения типов.
/// </summary>
public sealed class SharedCommandDescriptor
{
    private readonly Func<SharedCommandContext, object, CancellationToken, Task<object>> _вызов;

    internal SharedCommandDescriptor(
        string name,
        Type requestType,
        Type responseType,
        bool isSafeToRetry,
        Func<SharedCommandContext, object, CancellationToken, Task<object>> вызов)
    {
        Name = name;
        RequestType = requestType;
        ResponseType = responseType;
        IsSafeToRetry = isSafeToRetry;
        _вызов = вызов;
    }

    /// <summary>Имя команды в URL.</summary>
    public string Name { get; }

    /// <summary>Тип запроса команды.</summary>
    public Type RequestType { get; }

    /// <summary>Тип ответа команды.</summary>
    public Type ResponseType { get; }

    /// <summary>Признак допустимости автоматического повтора.</summary>
    public bool IsSafeToRetry { get; }

    /// <summary>
    /// Выполняет команду без приведения типов.
    /// </summary>
    /// <param name="context">Контекст выполнения.</param>
    /// <param name="request">Запрос команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    public Task<object> InvokeAsync(
        SharedCommandContext context,
        object request,
        CancellationToken cancellationToken)
        => _вызов(context, request, cancellationToken);
}

/// <summary>
/// Реестр команд совместного режима.
/// </summary>
/// <remarks>
/// Реестр — единственное место, где имя команды связано с её типом и
/// обработчиком. Регистрация проверяет уникальность имён: опечатка в имени
/// обнаруживается при старте службы, а не при первом обращении клиента.
/// </remarks>
public sealed class SharedCommandRegistry
{
    private readonly Dictionary<string, SharedCommandDescriptor> _команды = new(StringComparer.Ordinal);

    /// <summary>
    /// Зарегистрированные команды.
    /// </summary>
    public IReadOnlyCollection<SharedCommandDescriptor> Команды => _команды.Values;

    /// <summary>
    /// Регистрирует команду.
    /// </summary>
    /// <typeparam name="TRequest">Тип запроса.</typeparam>
    /// <typeparam name="TResponse">Тип ответа.</typeparam>
    /// <param name="команда">Команда.</param>
    /// <exception cref="ArgumentException">
    /// Команда с таким именем уже зарегистрирована.
    /// </exception>
    public void Register<TRequest, TResponse>(SharedCommand<TRequest, TResponse> команда)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(команда);

        if (string.IsNullOrWhiteSpace(команда.Name))
        {
            throw new ArgumentException("Имя команды не задано.", nameof(команда));
        }

        if (_команды.ContainsKey(команда.Name))
        {
            throw new ArgumentException(
                $"Команда «{команда.Name}» уже зарегистрирована.",
                nameof(команда));
        }

        _команды[команда.Name] = new SharedCommandDescriptor(
            команда.Name,
            typeof(TRequest),
            typeof(TResponse),
            команда.IsSafeToRetry,
            async (контекст, запрос, токен) =>
            {
                var результат = await команда
                    .ExecuteAsync(контекст, (TRequest)запрос, токен)
                    .ConfigureAwait(false);

                return (object)результат!;
            });
    }

    /// <summary>
    /// Ищет команду по имени.
    /// </summary>
    /// <param name="name">Имя команды.</param>
    /// <param name="descriptor">Найденное описание команды.</param>
    /// <returns><see langword="true"/>, если команда зарегистрирована.</returns>
    public bool TryGet(string name, out SharedCommandDescriptor descriptor)
        => _команды.TryGetValue(name, out descriptor!);
}
