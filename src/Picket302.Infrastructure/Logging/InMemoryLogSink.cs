using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace Picket302.Infrastructure.Logging;

/// <summary>
/// Приёмник журнала, хранящий записи в памяти (ТЗ §10.3; §8.11, функция 111:
/// «Сбой файловой системы логов — деградация в память с выводом в консоль
/// отладки, приложение не падает»).
/// </summary>
/// <remarks>
/// <para>
/// Приёмник используется, когда файловая система журналов недоступна: записи
/// не теряются безвозвратно, а остаются в памяти до вывода в консоль отладки.
/// Объём ограничен, чтобы деградация не приводила к нехватке памяти.
/// </para>
/// </remarks>
public sealed class InMemoryLogSink : ILogEventSink
{
    /// <summary>Предельное число хранимых записей.</summary>
    public const int DefaultCapacity = 2000;

    private readonly ConcurrentQueue<string> _записи = new();
    private readonly int _вместимость;

    /// <summary>
    /// Создаёт приёмник с предельным объёмом по умолчанию.
    /// </summary>
    public InMemoryLogSink()
        : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Создаёт приёмник с заданным предельным объёмом.
    /// </summary>
    /// <param name="вместимость">Предельное число хранимых записей.</param>
    public InMemoryLogSink(int вместимость)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(вместимость);

        _вместимость = вместимость;
    }

    /// <summary>Число хранимых записей.</summary>
    public int Count => _записи.Count;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var запись = logEvent.RenderMessage(CultureInfo.InvariantCulture);

        if (logEvent.Exception is not null)
        {
            // Исключение с трассировкой сохраняется в памяти так же, как в
            // файловом канале (шаблон вывода {Message}{NewLine}{Exception}).
            запись += Environment.NewLine + logEvent.Exception;
        }

        _записи.Enqueue(запись);

        while (_записи.Count > _вместимость && _записи.TryDequeue(out _))
        {
            // Освобождение места: из очереди удаляется самая старая запись.
        }
    }

    /// <summary>
    /// Возвращает хранимые записи, начиная с самой старой.
    /// </summary>
    /// <param name="предел">Предельное число возвращаемых записей.</param>
    /// <returns>Перечень записей журнала.</returns>
    public IReadOnlyList<string> Read(int предел = DefaultCapacity)
    {
        if (предел <= 0)
        {
            return Array.Empty<string>();
        }

        return _записи.ToArray()
            .TakeLast(предел)
            .ToArray();
    }

    /// <summary>
    /// Очищает хранимые записи.
    /// </summary>
    public void Clear() => _записи.Clear();
}
