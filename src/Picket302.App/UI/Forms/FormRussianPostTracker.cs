using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Infrastructure.Integration;

namespace Picket302.App.UI.Forms;

/// <summary>Состояние последнего запроса статусов (точка автопроверок).</summary>
public enum TrackerQueryState
{
    /// <summary>Запрос ещё не выполнялся.</summary>
    NotQueried = 0,

    /// <summary>Найдено: статусы получены.</summary>
    Found = 1,

    /// <summary>Нет данных: партия есть, статусы пустые.</summary>
    NoData = 2,

    /// <summary>Ошибка опроса.</summary>
    Error = 3,
}

/// <summary>
/// Диалог трекера отправлений Почты России (ТЗ §8.8, функция 110):
/// регистрация партии (номер, дата, пункт отправки, ШПИ), запрос статусов
/// с состояниями «нет данных / ошибка / найдено». Сетевой обмен — через
/// внедрённый адаптер трекера (форма сама сеть не выполняет).
/// </summary>
public sealed class FormRussianPostTracker : Form
{
    private readonly RussianPostTracker _трекер;

    private readonly TextBox _номерПартии = new();
    private readonly DateTimePicker _датаОтправки = new();
    private readonly TextBox _пунктОтправки = new();
    private readonly TextBox _ши = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _зарегистрировать = new();
    private readonly Button _запросить = new();
    private readonly DataGridView _статусы = new();
    private readonly Label _состояние = new();
    private readonly Button _закрыть = new();

    private TrackBatch? _партия;

    /// <summary>Точка подмены уведомлений в автоматических проверках.</summary>
    internal Action<string, string>? УведомитьДляПроверок { get; set; }

    /// <summary>
    /// Создаёт диалог трекера Почты России.
    /// </summary>
    /// <param name="трекер">Трекер (с внедрённым адаптером для проверок).</param>
    public FormRussianPostTracker(RussianPostTracker трекер)
    {
        ArgumentNullException.ThrowIfNull(трекер);

        _трекер = трекер;

        Text = "Трекер Почты России";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(640, 480);

        ПостроитьИнтерфейс();
        EnvelopeUiTheme.ОформитьОкно(this);
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(_закрыть);
    }

    /// <summary>Состояние последнего запроса (точка автопроверок).</summary>
    internal TrackerQueryState Состояние { get; private set; }
        = TrackerQueryState.NotQueried;

    /// <summary>Число строк статусов в таблице (точка автопроверок).</summary>
    internal int ЧислоСтрокСтатусов => _статусы.Rows.Count;

    /// <summary>Текст состояния (точка автопроверок).</summary>
    internal string ТекстСостояния => _состояние.Text;

    /// <summary>Зарегистрированная партия (точка автопроверок).</summary>
    internal TrackBatch? Партия => _партия;

    /// <summary>
    /// Регистрирует партию из готовых полей — точка автопроверок (без
    /// разборов UI-контролов).
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <param name="shippingDate">Дата отправки.</param>
    /// <param name="shippingPoint">Пункт отправки.</param>
    /// <param name="trackingIds">Список ШПИ (по 14 знаков).</param>
    /// <returns>Зарегистрированная партия.</returns>
    internal TrackBatch ЗарегистрироватьДляПроверокAsync(
        string batchNumber,
        DateTime shippingDate,
        string shippingPoint,
        IReadOnlyList<string> trackingIds)
    {
        var партия = _трекер.RegisterBatch(new RegisterBatchRequest(
            batchNumber,
            shippingDate,
            shippingPoint,
            trackingIds));

        _партия = партия;
        _номерПартии.Text = партия.BatchNumber;
        _пунктОтправки.Text = партия.ShippingPoint;

        ПоказатьСтатус($"Партия «{партия.BatchNumber}» зарегистрирована ({партия.TrackingIds.Count} ШПИ).");

        return партия;
    }

    /// <summary>
    /// Запрашивает статусы зарегистрированной партии — точка автопроверок.
    /// Заполняет таблицу; состояние «нет данных / ошибка / найдено».
    /// </summary>
    internal async Task<TrackQueryResult> ЗапроситьДляПроверокAsync(
        CancellationToken cancellationToken = default)
    {
        if (_партия is null)
        {
            throw new InvalidOperationException("Сначала зарегистрируйте партию отправлений.");
        }

        var результат = await _трекер
            .QueryStatusesWithRetriesAsync(
                _партия.BatchNumber,
                RussianPostTracker.ПопытокПоУмолчанию,
                cancellationToken)
            .ConfigureAwait(true);

        _статусы.Rows.Clear();

        if (результат.IsFailure)
        {
            Состояние = TrackerQueryState.Error;
            ПоказатьСтатус($"Ошибка опроса: {результат.Error}");
            Уведомить?.Invoke($"Ошибка опроса: {результат.Error}", "Трекер Почты России");

            return результат;
        }

        if (результат.Statuses.Count == 0)
        {
            Состояние = TrackerQueryState.NoData;
            ПоказатьСтатус("Нет данных по партии: сервис не вернул статусы ШПИ.");

            return результат;
        }

        Состояние = TrackerQueryState.Found;

        foreach (var статус in результат.Statuses)
        {
            var индекс = _статусы.Rows.Add();
            _статусы.Rows[индекс].Cells[0].Value = статус.TrackingId;
            _статусы.Rows[индекс].Cells[1].Value = статус.Status;
            _статусы.Rows[индекс].Cells[2].Value = статус.Delivered ? "да" : "нет";
            _статусы.Rows[индекс].Cells[3].Value = статус.DeliveredAtUtc?.ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm") ?? "—";
            _статусы.Rows[индекс].Cells[4].Value = статус.Error ?? "—";
        }

        ПоказатьСтатус(
            $"Найдено {результат.Statuses.Count} ШПИ, вручено: {результат.DeliveredCount}.");

        return результат;
    }

    /// <summary>Строит элементы диалога.</summary>
    private void ПостроитьИнтерфейс()
    {
        var параметры = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        параметры.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        параметры.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ДобавитьСтроку(параметры, 0, "Номер партии:", _номерПартии, "Номер партии отправлений");
        ДобавитьСтроку(параметры, 1, "Дата отправки:", _датаОтправки, "Дата отправки партии");
        ДобавитьСтроку(параметры, 2, "Пункт отправки:", _пунктОтправки, "Пункт отправки партии");

        _ши.AccessibleName = "Идентификаторы ШПИ";
        _ши.AccessibleDescription = "По одному 14-значному ШПИ в строке.";
        _ши.Height = 60;
        ДобавитьСтроку(параметры, 3, "ШПИ (по строке):", _ши, "Идентификаторы ШПИ");

        var строкаКнопок = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
        };

        _зарегистрировать.Text = "Зарегистрировать партию";
        _зарегистрировать.AutoSize = true;
        _зарегистрировать.AccessibleName = "Зарегистрировать партию отправлений";
        _зарегистрировать.Click += async (_, _) => await ЗарегистрироватьAsync().ConfigureAwait(true);

        _запросить.Text = "Запросить статусы";
        _запросить.AutoSize = true;
        _запросить.Enabled = false;
        _запросить.AccessibleName = "Запросить статусы отправлений";
        _запросить.Click += async (_, _) => await ЗапроситьAsync().ConfigureAwait(true);

        строкаКнопок.Controls.Add(_зарегистрировать);
        строкаКнопок.Controls.Add(_запросить);
        параметры.Controls.Add(строкаКнопок, 1, 4);

        _статусы.Dock = DockStyle.Fill;
        _статусы.ReadOnly = true;
        _статусы.AllowUserToAddRows = false;
        _статусы.AllowUserToDeleteRows = false;
        _статусы.RowHeadersVisible = false;
        _статусы.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _статусы.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _статусы.AccessibleName = "Статусы отправлений";
        _статусы.Columns.Add("ши", "ШПИ");
        _статусы.Columns.Add("статус", "Статус");
        _статусы.Columns.Add("вручено", "Вручено");
        _статусы.Columns.Add("дата", "Дата вручения");
        _статусы.Columns.Add("ошибка", "Ошибка");
        _статусы.Columns[0].Width = 150;
        _статусы.Columns[1].Width = 190;
        _статусы.Columns[2].Width = 80;
        _статусы.Columns[3].Width = 130;
        _статусы.Columns[4].FillWeight = 100;
        _статусы.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _состояние.Dock = DockStyle.Bottom;
        _состояние.Height = 30;
        _состояние.TextAlign = ContentAlignment.MiddleLeft;
        _состояние.ForeColor = EnvelopeUiTheme.MutedText;
        _состояние.AccessibleName = "Состояние запроса статусов";

        var низ = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 8, 8, 0) };

        _закрыть.Text = "Закрыть";
        _закрыть.AutoSize = true;
        _закрыть.DialogResult = DialogResult.Cancel;
        _закрыть.AccessibleName = "Закрыть трекер Почты России";

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };

        кнопки.Controls.Add(_закрыть);
        низ.Controls.Add(кнопки);

        Controls.Add(_статусы);
        Controls.Add(параметры);
        Controls.Add(_состояние);
        Controls.Add(низ);

        CancelButton = _закрыть;
    }

    /// <summary>Добавляет строку «метка — контрол».</summary>
    private static void ДобавитьСтроку(
        TableLayoutPanel сетка,
        int строка,
        string подпись,
        Control контрол,
        string доступноеИмя)
    {
        контрол.AccessibleName = доступноеИмя;
        сетка.Controls.Add(new Label
        {
            Text = подпись,
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0),
        }, 0, строка);
        сетка.Controls.Add(контрол, 1, строка);
    }

    /// <summary>Регистрация партии из контролов диалога.</summary>
    private async Task ЗарегистрироватьAsync()
    {
        var номер = _номерПартии.Text.Trim();

        if (номер.Length == 0)
        {
            Уведомить?.Invoke("Укажите номер партии.", "Трекер Почты России");
            _номерПартии.Focus();

            return;
        }

        var шпи = _ши.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(строка => строка.Trim())
            .Where(строка => строка.Length > 0)
            .ToList();

        try
        {
            ЗарегистрироватьДляПроверокAsync(
                номер,
                _датаОтправки.Value.Date,
                _пунктОтправки.Text.Trim(),
                шпи);
            _запросить.Enabled = true;
        }
        catch (Exception ошибка) when (ошибка is ArgumentException or InvalidOperationException)
        {
            Уведомить?.Invoke(ошибка.Message, "Трекер Почты России");
        }
    }

    /// <summary>Запрос статусов из контролов диалога.</summary>
    private async Task ЗапроситьAsync()
    {
        try
        {
            await ЗапроситьДляПроверокAsync().ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
        {
            Уведомить?.Invoke(ошибка.Message, "Трекер Почты России");
        }
    }

    /// <summary>Обновляет строку состояния.</summary>
    private void ПоказатьСтатус(string текст)
    {
        _состояние.Text = текст.Length > 200 ? текст[..200] + "…" : текст;
    }

    /// <summary>Уведомление (подменяется в проверках).</summary>
    private Action<string, string>? Уведомить => УведомитьДляПроверок;
}
