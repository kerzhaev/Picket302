using System.Windows.Forms;
using Picket302.Infrastructure.Localization;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог выбора языка интерфейса (ТЗ §8.11, функция 112: «Страница «Язык»
/// настроек; выпадающий список языков»).
/// </summary>
/// <remarks>
/// Переключение языка применяется без перезапуска программы: менеджер
/// локализации сообщает об этом событием, открытые формы перерисовываются.
/// </remarks>
public sealed class FormLanguageSelection : Form
{
    private readonly LanguageManager _менеджер;
    private readonly ComboBox _языки = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 280,
    };

    private readonly Label _пояснение = new();
    private readonly Button _применить = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт диалог выбора языка.
    /// </summary>
    /// <param name="менеджер">Менеджер локализации.</param>
    public FormLanguageSelection(LanguageManager менеджер)
    {
        ArgumentNullException.ThrowIfNull(менеджер);

        _менеджер = менеджер;

        Text = "Язык интерфейса";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(400, 190);

        ПостроитьИнтерфейс();
        ЗаполнитьЯзыки();
    }

    /// <summary>
    /// Код выбранного языка.
    /// </summary>
    public string SelectedCode => _языки.SelectedItem is LanguageInfo язык ? язык.Code : _менеджер.CurrentCode;

    /// <summary>
    /// Применяет выбранный язык. Переключение выполняется без перезапуска.
    /// </summary>
    /// <returns>Код применённого языка.</returns>
    public string ApplyLanguage()
    {
        var код = SelectedCode;

        _менеджер.SwitchLanguage(код);

        return код;
    }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var метка = new Label
        {
            Text = "Язык интерфейса:",
            Location = new Point(14, 26),
            AutoSize = true,
        };

        _языки.Location = new Point(14, 48);

        _пояснение.Location = new Point(14, 82);
        _пояснение.Size = new Size(370, 44);
        _пояснение.Text =
            "Переключение языка применяется без перезапуска программы: подписи открытых окон "
            + "обновляются сразу. Новый язык добавляется копированием шаблона словаря.";

        _применить.Text = "Применить";
        _применить.Size = new Size(100, 26);
        _применить.Location = new Point(180, 146);
        _применить.Click += (_, _) =>
        {
            ApplyLanguage();

            DialogResult = DialogResult.OK;
            Close();
        };

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 26);
        _отмена.Location = new Point(286, 146);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_отмена);
        Controls.Add(_применить);
        Controls.Add(_пояснение);
        Controls.Add(_языки);
        Controls.Add(метка);

        AcceptButton = _применить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Заполняет список языков и выбирает текущий.
    /// </summary>
    private void ЗаполнитьЯзыки()
    {
        foreach (var язык in _менеджер.AvailableLanguages())
        {
            _языки.Items.Add(язык);
        }

        for (var индекс = 0; индекс < _языки.Items.Count; индекс++)
        {
            if (_языки.Items[индекс] is LanguageInfo язык
                && string.Equals(язык.Code, _менеджер.CurrentCode, StringComparison.OrdinalIgnoreCase))
            {
                _языки.SelectedIndex = индекс;
                break;
            }
        }

        if (_языки.SelectedIndex < 0 && _языки.Items.Count > 0)
        {
            _языки.SelectedIndex = 0;
        }
    }
}
