using System.Drawing;
using System.Windows.Forms;

using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Integration;
using Picket302.Infrastructure.Shell;
using Picket302.Infrastructure.Support;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Фрагмент страницы настроек «Сервис»: интеграция с оболочкой Windows,
/// шаблон веб-поиска по ИНН и параметры утилиты удалённого доступа
/// (ТЗ §8.10, функции 106, 108 и 109; §10.6, функция 135).
/// </summary>
/// <remarks>
/// <para>
/// Пункт контекстного меню Проводника регистрируется в ветке пользователя
/// <c>HKCU\Software\Classes</c> — права администратора не требуются. При отказе
/// доступа страница сообщает, от имени какого пользователя следует запустить
/// программу.
/// </para>
/// <para>
/// Настройки хранятся в таблице параметров программы: шаблон поиска по ИНН —
/// в ключе <c>WebLookupUrlTemplate</c>, параметры утилиты удалённого доступа —
/// в ключах <c>RemoteAssistantUrl</c>, <c>RemoteAssistantFileName</c> и
/// <c>RemoteAssistantFolder</c>.
/// </para>
/// </remarks>
public sealed class FormIntegrationSettingsPage : UserControl
{
    private readonly IParameterRepository? _параметры;
    private readonly ExplorerIntegration _интеграция;

    private readonly CheckBox _пунктМеню = new();
    private readonly Label _состояниеМеню = new();
    private readonly TextBox _шаблонПоиска = new();
    private readonly TextBox _адресУтилиты = new();
    private readonly TextBox _имяФайлаУтилиты = new();
    private readonly TextBox _папкаУтилиты = new();
    private readonly Label _состояние = new();

    /// <summary>
    /// Создаёт фрагмент страницы настроек «Сервис».
    /// </summary>
    /// <param name="параметры">
    /// Хранилище настроек программы; при отсутствии настройки не сохраняются.
    /// </param>
    /// <param name="путьОболочки">
    /// Путь к исполняемому файлу оболочки; по умолчанию — путь текущего
    /// процесса.
    /// </param>
    public FormIntegrationSettingsPage(
        IParameterRepository? параметры = null,
        string? путьОболочки = null)
    {
        _параметры = параметры;

        var путь = путьОболочки;

        if (string.IsNullOrWhiteSpace(путь))
        {
            путь = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Picket302.App.exe");
        }

        _интеграция = new ExplorerIntegration(путь);

        Font = new Font("Segoe UI", 8f);
        Size = new Size(620, 400);

        ПостроитьПроводник();
        ПостроитьПоискПоИнн();
        ПостроитьУдалённыйДоступ();
        ПостроитьСостояние();

        ОбновитьСостояниеРегистрации();
    }

    /// <summary>Интеграция с оболочкой Windows.</summary>
    public ExplorerIntegration ИнтеграцияСОболочкой => _интеграция;

    /// <summary>
    /// Признак того, что пункт контекстного меню зарегистрирован.
    /// </summary>
    public bool ПунктМенюЗарегистрирован => _пунктМеню.Checked;

    /// <summary>Шаблон адреса веб-поиска по ИНН.</summary>
    public string ШаблонПоискаПоИнн
    {
        get => _шаблонПоиска.Text;
        set => _шаблонПоиска.Text = value;
    }

    /// <summary>Адрес загрузки утилиты удалённого доступа.</summary>
    public string АдресУтилиты
    {
        get => _адресУтилиты.Text;
        set => _адресУтилиты.Text = value;
    }

    /// <summary>Имя файла утилиты удалённого доступа.</summary>
    public string ИмяФайлаУтилиты
    {
        get => _имяФайлаУтилиты.Text;
        set => _имяФайлаУтилиты.Text = value;
    }

    /// <summary>Папка размещения утилиты удалённого доступа.</summary>
    public string ПапкаУтилиты
    {
        get => _папкаУтилиты.Text;
        set => _папкаУтилиты.Text = value;
    }

    /// <summary>
    /// Регистрирует пункт контекстного меню Проводника.
    /// </summary>
    /// <returns><see langword="true"/>, если пункт зарегистрирован.</returns>
    public bool ЗарегистрироватьПунктМеню()
    {
        try
        {
            _интеграция.RegisterShellMenu();
            _пунктМеню.Checked = true;
            ОбновитьСостояниеРегистрации();

            return true;
        }
        catch (ShellIntegrationException ошибка)
        {
            _пунктМеню.Checked = false;
            _состояниеМеню.Text = ошибка.Message;

            MessageBox.Show(
                this,
                ошибка.Message,
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }
    }

    /// <summary>
    /// Удаляет пункт контекстного меню Проводника.
    /// </summary>
    /// <returns><see langword="true"/>, если пункт удалён.</returns>
    public bool УдалитьПунктМеню()
    {
        try
        {
            _интеграция.UnregisterShellMenu();
            _пунктМеню.Checked = false;
            ОбновитьСостояниеРегистрации();

            return true;
        }
        catch (ShellIntegrationException ошибка)
        {
            ОбновитьСостояниеРегистрации();

            MessageBox.Show(
                this,
                ошибка.Message,
                "Настройки интеграции",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }
    }

    /// <summary>
    /// Сохраняет настройки страницы: шаблон веб-поиска по ИНН и параметры
    /// утилиты удалённого доступа.
    /// </summary>
    /// <returns><see langword="true"/>, если настройки сохранены.</returns>
    public async Task<bool> СохранитьНастройкиAsync()
    {
        if (_параметры is null)
        {
            _состояние.Text = "Хранилище настроек недоступно — настройки не сохранены.";

            return false;
        }

        try
        {
            await СохранитьПараметрAsync(WebLookupLauncher.КлючШаблона, _шаблонПоиска.Text).ConfigureAwait(true);
            await СохранитьПараметрAsync(RemoteAssistantLauncher.КлючUrl, _адресУтилиты.Text).ConfigureAwait(true);
            await СохранитьПараметрAsync(RemoteAssistantLauncher.КлючИмениФайла, _имяФайлаУтилиты.Text).ConfigureAwait(true);
            await СохранитьПараметрAsync(RemoteAssistantLauncher.КлючПапки, _папкаУтилиты.Text).ConfigureAwait(true);
        }
        catch (Exception ошибка)
        {
            _состояние.Text = $"Не удалось сохранить настройки: {ошибка.Message}";

            return false;
        }

        _состояние.Text = "Настройки сохранены.";

        return true;
    }

    /// <summary>
    /// Загружает настройки страницы из хранилища параметров.
    /// </summary>
    public async Task ЗагрузитьНастройкиAsync()
    {
        if (_параметры is null)
        {
            return;
        }

        _шаблонПоиска.Text = await ПрочитатьПараметрAsync(WebLookupLauncher.КлючШаблона).ConfigureAwait(true) ?? string.Empty;
        _адресУтилиты.Text = await ПрочитатьПараметрAsync(RemoteAssistantLauncher.КлючUrl).ConfigureAwait(true) ?? string.Empty;
        _имяФайлаУтилиты.Text = await ПрочитатьПараметрAsync(RemoteAssistantLauncher.КлючИмениФайла).ConfigureAwait(true) ?? string.Empty;
        _папкаУтилиты.Text = await ПрочитатьПараметрAsync(RemoteAssistantLauncher.КлючПапки).ConfigureAwait(true) ?? string.Empty;
    }

    /// <summary>
    /// Строит раздел интеграции с Проводником Windows.
    /// </summary>
    private void ПостроитьПроводник()
    {
        var заголовок = new Label
        {
            Text = "Проводник Windows",
            AutoSize = true,
            Location = new Point(10, 8),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        _пунктМеню.Text = "Проводник Windows: пункт в контекстном меню";
        _пунктМеню.AutoSize = true;
        _пунктМеню.Location = new Point(24, 32);
        _пунктМеню.CheckedChanged += (_, _) => ПереключитьПунктМеню();

        _состояниеМеню.AutoSize = false;
        _состояниеМеню.Location = new Point(24, 56);
        _состояниеМеню.Size = new Size(560, 32);

        Controls.Add(заголовок);
        Controls.Add(_пунктМеню);
        Controls.Add(_состояниеМеню);
    }

    /// <summary>
    /// Строит раздел шаблона веб-поиска по ИНН.
    /// </summary>
    private void ПостроитьПоискПоИнн()
    {
        var заголовок = new Label
        {
            Text = "Поиск по ИНН/УНН в интернете",
            AutoSize = true,
            Location = new Point(10, 96),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        var пояснение = new Label
        {
            Text = "Шаблон адреса поиска; плейсхолдер {INN} заменяется на ИНН контрагента:",
            AutoSize = true,
            Location = new Point(24, 118),
        };

        _шаблонПоиска.Location = new Point(24, 138);
        _шаблонПоиска.Width = 540;

        Controls.Add(заголовок);
        Controls.Add(пояснение);
        Controls.Add(_шаблонПоиска);
    }

    /// <summary>
    /// Строит раздел параметров утилиты удалённого доступа.
    /// </summary>
    private void ПостроитьУдалённыйДоступ()
    {
        var заголовок = new Label
        {
            Text = "Утилита удалённого доступа (параметры задаёт администратор)",
            AutoSize = true,
            Location = new Point(10, 172),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        var метки = new (string Заголовок, TextBox Поле)[]
        {
            ("Адрес загрузки (https):", _адресУтилиты),
            ("Имя файла утилиты:", _имяФайлаУтилиты),
            ("Папка размещения:", _папкаУтилиты),
        };

        var строка = 0;

        foreach (var (текст, поле) in метки)
        {
            var метка = new Label
            {
                Text = текст,
                AutoSize = true,
                Location = new Point(24, 198 + (строка * 26)),
            };

            поле.Location = new Point(190, 195 + (строка * 26));
            поле.Width = 374;

            Controls.Add(метка);
            Controls.Add(поле);

            строка++;
        }

        Controls.Add(заголовок);
    }

    /// <summary>
    /// Строит нижние кнопки и строку состояния.
    /// </summary>
    private void ПостроитьСостояние()
    {
        var сохранить = new Button
        {
            Text = "Сохранить настройки",
            Size = new Size(160, 24),
            Location = new Point(10, 300),
        };

        сохранить.Click += async (_, _) => await СохранитьНастройкиAsync().ConfigureAwait(true);

        _состояние.AutoSize = false;
        _состояние.Location = new Point(10, 332);
        _состояние.Size = new Size(560, 20);

        Controls.Add(сохранить);
        Controls.Add(_состояние);
    }

    /// <summary>
    /// Регистрирует или удаляет пункт контекстного меню в зависимости от
    /// состояния флажка.
    /// </summary>
    private void ПереключитьПунктМеню()
    {
        if (_пунктМеню.Checked)
        {
            ЗарегистрироватьПунктМеню();
        }
        else
        {
            УдалитьПунктМеню();
        }
    }

    /// <summary>
    /// Обновляет надпись о состоянии регистрации пункта контекстного меню.
    /// </summary>
    private void ОбновитьСостояниеРегистрации()
    {
        var зарегистрирован = _интеграция.IsRegistered();

        _пунктМеню.Checked = зарегистрирован;

        _состояниеМеню.Text = зарегистрирован
            ? $"Пункт контекстного меню зарегистрирован: {_интеграция.ПутьОболочки} --import \"%1\"."
            : "Пункт контекстного меню не зарегистрирован. Регистрация выполняется в HKCU — права администратора не нужны.";
    }

    /// <summary>
    /// Читает значение параметра программы.
    /// </summary>
    /// <param name="имя">Имя параметра.</param>
    /// <returns>Значение параметра либо <see langword="null"/>.</returns>
    private async Task<string?> ПрочитатьПараметрAsync(string имя)
        => await _параметры!.GetValueAsync(имя).ConfigureAwait(true);

    /// <summary>
    /// Сохраняет значение параметра программы.
    /// </summary>
    /// <param name="имя">Имя параметра.</param>
    /// <param name="значение">Значение параметра.</param>
    private async Task СохранитьПараметрAsync(string имя, string значение)
    {
        var существующий = await _параметры!.GetByNameAsync(имя).ConfigureAwait(true);

        if (существующий is null)
        {
            await _параметры.AddAsync(
                new Domain.Entities.Parameter
                {
                    Name = имя,
                    Value = значение,
                    UpdatedAt = DateTime.UtcNow,
                }).ConfigureAwait(true);

            return;
        }

        существующий.Value = значение;
        существующий.UpdatedAt = DateTime.UtcNow;

        await _параметры.UpdateAsync(существующий).ConfigureAwait(true);
    }
}
