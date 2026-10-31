using Picket302.Domain.Serialization;

namespace Picket302.Infrastructure.Templates;

/// <summary>Виды стандартных прикладных бланков (ТЗ §8.3, функция 49).</summary>
public enum SpecialFormType
{
    /// <summary>Доверенность на получение почты.</summary>
    PowerOfAttorney = 0,

    /// <summary>Заявление о включении в реестр кредиторов.</summary>
    CreditorApplication = 1,
}

/// <summary>
/// Стандартные прикладные бланки: «Доверенность на получение почты» и
/// «Заявление о включении в реестр кредиторов» (ТЗ §8.3, функция 49:
/// <c>Infrastructure.Templates.SpecialFormsBuilder.BuildSpecialForm</c>).
/// </summary>
public sealed class SpecialFormsBuilder
{
    /// <summary>Габарит листа А4 книжной ориентации.</summary>
    private const decimal ШиринаA4 = 210m;

    /// <summary>Высота листа А4.</summary>
    private const decimal ВысотаA4 = 297m;

    /// <summary>
    /// Собирает бланк служебной формы с реквизитными полями
    /// (ТЗ §8.3, функция 49).
    /// </summary>
    /// <param name="type">Вид формы.</param>
    /// <param name="fields">Имена полей формы.</param>
    /// <returns>Модель бланка на листе А4.</returns>
    public CustomBlank BuildSpecialForm(SpecialFormType type, IReadOnlyList<string>? fields = null)
    {
        var бланк = type switch
        {
            SpecialFormType.PowerOfAttorney => Заготовка("Доверенность на получение почты", fields ?? Доверенность()),
            SpecialFormType.CreditorApplication => Заготовка("Заявление о включении в реестр кредиторов", fields ?? ЗаявлениеКредитора()),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный вид служебного бланка."),
        };

        return бланк;
    }

    /// <summary>Бланк «Доверенность на получение почты» (библиотека, ф.37).</summary>
    public CustomBlank BuildPowerOfAttorney() =>
        BuildSpecialForm(SpecialFormType.PowerOfAttorney);

    /// <summary>Бланк «Заявление о включении в реестр кредиторов» (библиотека, ф.37).</summary>
    public CustomBlank BuildCreditorApplication() =>
        BuildSpecialForm(SpecialFormType.CreditorApplication);

    /// <summary>Шаблон реестра почтовых отправлений (библиотека, ф.37).</summary>
    public CustomBlank BuildRegistry103() =>
        Заготовка("Форма реестра Ф.103", new[]
        {
            "Наименование отправителя",
            "Адрес отправителя",
            "Исходящий номер",
            "Дата отправки",
            "Получатель",
            "Адрес получателя",
            "ШПИ",
            "Вес",
            "Плата за пересылку",
            "Сумма объявленной ценности",
            "Итого",
            "Подпись оператора",
            "Подпись приемщика",
        });

    /// <summary>Простой реестр передачи (библиотека, ф.37).</summary>
    public CustomBlank BuildSimpleRegistry() =>
        Заготовка("Реестр почтовых отправлений", new[]
        {
            "№",
            "Получатель",
            "Адрес",
            "ШПИ",
            "Подпись в получении",
        });

    /// <summary>Универсальный пользовательский шаблон реестра (библиотека, ф.37).</summary>
    public CustomBlank BuildRegistryTemplate() =>
        Заготовка("Шаблон реестра", new[]
        {
            "Заголовок",
            "Колонка 1",
            "Колонка 2",
            "Колонка 3",
            "Колонка 4",
            "Итого",
        });

    /// <summary>Шаблон описи вложений (библиотека, ф.37).</summary>
    public CustomBlank BuildInventoryTemplate() =>
        Заготовка("Шаблон описи", new[]
        {
            "Наименование описи",
            "Кому",
            "Куда",
            "Наименование предмета",
            "Количество",
            "Стоимость",
            "Сумма",
            "Итого",
        });

    /// <summary>Журнал регистрации входящей корреспонденции (библиотека, ф.37).</summary>
    public CustomBlank BuildIncomingJournal() =>
        Заготовка("Журнал входящих", new[]
        {
            "Входящий номер",
            "Дата регистрации",
            "Отправитель",
            "ИНН",
            "Содержание",
            "Отметка об исполнении",
            "Исполнитель",
        });

    /// <summary>Поля доверенности.</summary>
    private static string[] Доверенность() => new[]
    {
        "Организация",
        "ИНН",
        "ОГРН",
        "Доверитель",
        "Должность доверителя",
        "Паспорт доверителя: серия",
        "Паспорт доверителя: номер",
        "Паспорт доверителя: кем выдан",
        "Паспорт доверителя: когда выдан",
        "Доверенное лицо",
        "Паспорт доверенного лица: серия",
        "Паспорт доверенного лица: номер",
        "Паспорт доверенного лица: кем выдан",
        "Паспорт доверенного лица: когда выдан",
        "Дата выдачи доверенности",
        "Срок действия",
        "Подпись руководителя",
        "Печать",
    };

    /// <summary>Поля заявления кредитора.</summary>
    private static string[] ЗаявлениеКредитора() => new[]
    {
        "Организация",
        "ИНН",
        "ОГРН",
        "Адрес",
        "Руководитель",
        "Расчетный счет",
        "Название банка",
        "БИК",
        "К/с",
        "Сумма требования",
        "Основание требования",
        "Дата заявления",
        "Подпись",
    };

    private static CustomBlank Заготовка(string наименование, IReadOnlyList<string> поля)
    {
        var бланк = new CustomBlank
        {
            Name = наименование,
            Country = "RUS",
            StandardLayout = false,
            Geometry = new BlankGeometry
            {
                WidthMm = ШиринаA4,
                HeightMm = ВысотаA4,
                Orientation = BlankOrientation.Portrait,
                BleedMm = 0.0m,
                CropMarks = false,
            },
        };

        var страница = new BlankPage { Number = 1 };

        const decimal отступ = 20m;
        var y = отступ;

        foreach (var поле in поля)
        {
            страница.Fields.Add(new BlankFieldSpec
            {
                Name = поле,
                Kind = BlankFieldKinds.Text,
                Bounds = new BlankBounds
                {
                    XMm = отступ,
                    YMm = y,
                    WidthMm = ШиринаA4 - 2 * отступ,
                    HeightMm = BlankFieldsBuilder.ВысотаСтроки,
                },
                Printable = true,
            });

            y += BlankFieldsBuilder.ВысотаСтроки + BlankFieldsBuilder.Зазор;
        }

        бланк.Pages.Add(страница);

        return бланк;
    }
}
