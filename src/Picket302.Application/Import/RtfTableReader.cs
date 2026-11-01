using System.Globalization;
using System.Text;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Import;

/// <summary>
/// Чтение таблицы из документа Rich Text Format (RTF) — формат выгрузки
/// адресного списка программы (ТЗ §8.5, функция 71: «Импорт и экспорт
/// адресных списков в форматах TXT/CSV/HTML/RTF/XLS/XLSX»).
/// </summary>
/// <remarks>
/// <para>
/// Разбор снимает разметку RTF: группы <c>{…}</c>, управляющие слова
/// (<c>\par</c>, <c>\line</c>, <c>\tab</c>, <c>\cell</c>, <c>\row</c>),
/// экранирование (<c>\\</c>, <c>\{</c>, <c>\}</c>), символы кодовой страницы
/// (<c>'xx</c>) и символы Unicode (<c>\uN</c> с запасным символом).
/// Служебные назначения (<c>\fonttbl</c>, <c>\colortbl</c>, <c>\info</c>,
/// группы с признаком <c>\*</c>) пропускаются: их содержимое не является
/// текстом документа.
/// </para>
/// <para>
/// Таблица восстанавливается по строкам <c>\row</c> и ячейкам <c>\cell</c>.
/// Если в документе нет таблицы, строки формируются из абзацев (<c>\par</c>):
/// при наличии строки заголовков первый непустой абзац становится заголовком
/// столбцов, остальные абзацы — данными; без строки заголовков все абзацы
/// считаются данными, а заголовки остаются пустыми.
/// </para>
/// </remarks>
public sealed class RtfTableReader
{
    /// <summary>
    /// Читает таблицу из документа RTF, заданного строкой.
    /// </summary>
    /// <param name="документ">Содержимое документа RTF.</param>
    /// <param name="естьЗаголовки">Признак наличия строки заголовков.</param>
    /// <param name="начало">Номер первой строки данных (нумерация с единицы).</param>
    /// <returns>Таблица строк документа.</returns>
    public DelimitedTextTable Read(string документ, bool естьЗаголовки = true, int начало = 1)
    {
        ArgumentNullException.ThrowIfNull(документ);

        var разбор = new Разбор(документ);
        разбор.Выполнить();

        return разбор.СобратьТаблицу(естьЗаголовки, начало);
    }

    /// <summary>
    /// Читает таблицу из потока с документом RTF.
    /// </summary>
    /// <param name="поток">Поток с содержимым документа RTF.</param>
    /// <param name="естьЗаголовки">Признак наличия строки заголовков.</param>
    /// <param name="начало">Номер первой строки данных (нумерация с единицы).</param>
    /// <returns>Таблица строк документа.</returns>
    public DelimitedTextTable Read(Stream поток, bool естьЗаголовки = true, int начало = 1)
    {
        ArgumentNullException.ThrowIfNull(поток);

        using var буфер = new MemoryStream();
        поток.CopyTo(буфер);

        return Read(Декодировать(буфер.ToArray()), естьЗаголовки, начало);
    }

    /// <summary>
    /// Декодирует содержимое файла RTF: документ с маркером BOM читается как
    /// UTF-8, иначе байты сохраняются однобайтовым соответствием, чтобы
    /// раскодирование <c>'xx</c> оставалось точным.
    /// </summary>
    /// <param name="байты">Содержимое файла.</param>
    /// <returns>Текст документа.</returns>
    private static string Декодировать(byte[] байты)
    {
        if (байты.Length >= 3 && байты[0] == 0xEF && байты[1] == 0xBB && байты[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(байты, 3, байты.Length - 3);
        }

        return Encoding.Latin1.GetString(байты);
    }

    /// <summary>
    /// Разбор документа RTF: снимает разметку и собирает строки таблицы
    /// либо абзацы документа.
    /// </summary>
    private sealed class Разбор
    {
        /// <summary>
        /// Назначения RTF, содержимое которых не является текстом документа.
        /// </summary>
        private static readonly HashSet<string> ПропускаемыеНазначения = new(StringComparer.Ordinal)
        {
            "fonttbl",
            "colortbl",
            "stylesheet",
            "info",
            "pict",
            "object",
            "filetbl",
            "listtable",
            "listoverridetable",
            "revtbl",
            "rsidtbl",
            "generator",
            "datastore",
            "themedata",
            "colorschememapping",
            "latentstyles",
            "xmlnstbl",
            "upr",
        };

        private readonly string _документ;
        private readonly List<string> _ячейки = [];
        private readonly List<string[]> _строки = [];
        private readonly List<string> _абзацы = [];
        private readonly Stack<Кадр> _кадры = new();

        private int _позиция;
        private int _кодоваяСтраница = 1252;
        private int _пропускСимволов = 1;
        private int _пропуститьСимволов;
        private bool _вЯчейке;

        /// <summary>
        /// Создаёт разбор документа.
        /// </summary>
        /// <param name="документ">Содержимое документа RTF.</param>
        public Разбор(string документ) => _документ = документ;

        /// <summary>
        /// Разбирает документ: снимает разметку и наполняет строки таблицы
        /// либо абзацы.
        /// </summary>
        public void Выполнить()
        {
            var буфер = new StringBuilder();
            var отбрасывать = false;

            while (_позиция < _документ.Length)
            {
                var символ = _документ[_позиция++];

                switch (символ)
                {
                    case '{':
                        _кадры.Push(new Кадр { Буфер = буфер, Отбрасывать = отбрасывать });
                        буфер = new StringBuilder();
                        отбрасывать = ПропускаетсяНазначение();
                        break;

                    case '}':
                        ЗавершитьГруппу(буфер.ToString(), отбрасывать, out буфер, out отбрасывать);
                        break;

                    case '\\':
                        РазобратьУправление(буфер, ref отбрасывать);
                        break;

                    case '\r':
                    case '\n':
                        // Перевод строки в потоке документа не является текстом.
                        break;

                    default:
                        if (_пропуститьСимволов > 0)
                        {
                            // Запасной символ после \uN не является текстом.
                            _пропуститьСимволов--;
                            break;
                        }

                        if (!отбрасывать)
                        {
                            буфер.Append(символ);
                        }

                        break;
                }
            }

            ДобавитьАбзац(буфер.ToString());
        }

        /// <summary>
        /// Собирает таблицу строк документа: строки таблицы RTF либо абзацы,
        /// если таблицы в документе нет.
        /// </summary>
        /// <param name="естьЗаголовки">Признак наличия строки заголовков.</param>
        /// <param name="начало">Номер первой строки данных (нумерация с единицы).</param>
        /// <returns>Таблица строк документа.</returns>
        public DelimitedTextTable СобратьТаблицу(bool естьЗаголовки, int начало)
        {
            var строки = new List<DelimitedTextRow>();
            var номер = 0;

            if (_строки.Count > 0)
            {
                foreach (var ячейки in _строки)
                {
                    номер++;
                    строки.Add(new DelimitedTextRow(номер, ячейки));
                }
            }
            else
            {
                foreach (var абзац in _абзацы)
                {
                    номер++;
                    строки.Add(new DelimitedTextRow(номер, [абзац]));
                }
            }

            var началоДанных = Math.Max(1, начало);
            var заголовки = Array.Empty<string>();
            var данные = строки.Where(строка => строка.RowNumber >= началоДанных).ToList();

            if (естьЗаголовки && строки.Count > 0)
            {
                // Строка заголовков — первая строка документа: «Начать импорт с»
                // задаёт первую строку данных, а не заголовка.
                var строкаЗаголовка = строки[0];

                заголовки = строкаЗаголовка.Cells.ToArray();

                if (данные.Count > 0 && данные[0].RowNumber <= строкаЗаголовка.RowNumber)
                {
                    данные.RemoveAt(0);
                }
            }

            return new DelimitedTextTable(заголовки, данные);
        }

        /// <summary>
        /// Завершает группу RTF: текст группы переносится в родительскую
        /// группу, если сама группа не пропускается.
        /// </summary>
        /// <param name="текстГруппы">Текст текущей группы.</param>
        /// <param name="отбрасыватьГруппу">Признак пропуска текущей группы.</param>
        /// <param name="родительскийБуфер">Буфер родительской группы.</param>
        /// <param name="родительскийОтбрасывать">Признак пропуска родительской группы.</param>
        private void ЗавершитьГруппу(
            string текстГруппы,
            bool отбрасыватьГруппу,
            out StringBuilder родительскийБуфер,
            out bool родительскийОтбрасывать)
        {
            if (_кадры.Count == 0)
            {
                родительскийБуфер = new StringBuilder();
                родительскийОтбрасывать = false;
                return;
            }

            var кадр = _кадры.Pop();

            родительскийБуфер = кадр.Буфер;
            родительскийОтбрасывать = кадр.Отбрасывать;

            // Содержимое пропускаемого назначения («таблица шрифтов», «таблица
            // цветов» и прочие служебные группы) в родительскую группу не
            // переносится.
            if (!отбрасыватьГруппу)
            {
                родительскийБуфер.Append(текстГруппы);
            }
        }

        /// <summary>
        /// Разбирает управляющее слово или управляющий символ RTF.
        /// </summary>
        /// <param name="буфер">Буфер текущей группы.</param>
        /// <param name="отбрасывать">Признак пропуска текущей группы.</param>
        private void РазобратьУправление(StringBuilder буфер, ref bool отбрасывать)
        {
            var код = ПрочитатьКод();

            if (код.ЭтоСимвол)
            {
                РазобратьСимвол(код.Имя, буфер);
                return;
            }

            switch (код.Имя)
            {
                case "par":
                    if (_вЯчейке)
                    {
                        // Перенос строки внутри ячейки таблицы.
                        буфер.Append('\n');
                    }
                    else
                    {
                        ДобавитьАбзац(буфер.ToString());
                        буфер.Clear();
                    }

                    break;

                case "line":
                    буфер.Append('\n');
                    break;

                case "tab":
                    буфер.Append('\t');
                    break;

                case "intbl":
                    _вЯчейке = true;
                    break;

                case "cell":
                    _вЯчейке = true;
                    _ячейки.Add(буфер.ToString());
                    буфер.Clear();
                    break;

                case "row":
                    _ячейки.Add(буфер.ToString());
                    буфер.Clear();
                    _строки.Add(_ячейки.ToArray());
                    _ячейки.Clear();
                    _вЯчейке = false;
                    break;

                case "u":
                    ДобавитьСимволUnicode(буфер, код.Параметр);
                    break;

                case "uc":
                    _пропускСимволов = код.Параметр;
                    break;

                case "ansicpg":
                    _кодоваяСтраница = код.Параметр;
                    break;

                case "*":
                    отбрасывать = true;
                    break;

                default:
                    // Форматирование и параметры таблицы не влияют на содержимое.
                    break;
            }
        }

        /// <summary>
        /// Разбирает управляющий символ RTF: экранирование, символы кодовой
        /// страницы и неразрывные пробелы.
        /// </summary>
        /// <param name="символ">Управляющий символ.</param>
        /// <param name="буфер">Буфер текущей группы.</param>
        private void РазобратьСимвол(string символ, StringBuilder буфер)
        {
            switch (символ)
            {
                case "\\":
                    буфер.Append('\\');
                    break;

                case "{":
                    буфер.Append('{');
                    break;

                case "}":
                    буфер.Append('}');
                    break;

                case "~":
                    буфер.Append('\u00A0');
                    break;

                case "'":
                    буфер.Append(РаскодироватьСимвол());
                    break;

                default:
                    // Символы «-» и «_» текст документа не меняют.
                    break;
            }
        }

        /// <summary>
        /// Добавляет символ Unicode из управляющего слова <c>\uN</c> и
        /// включает пропуск запасного символа.
        /// </summary>
        /// <param name="буфер">Буфер текущей группы.</param>
        /// <param name="код">Код символа Unicode.</param>
        private void ДобавитьСимволUnicode(StringBuilder буфер, int код)
        {
            if (код < 0)
            {
                код += 65536;
            }

            буфер.Append((char)код);

            // Запасной символ после \uN пропускается: его длина задаётся
            // управляющим словом \uc (по умолчанию — один символ).
            _пропуститьСимволов = _пропускСимволов;
        }

        /// <summary>
        /// Раскодирует символ кодовой страницы из пары шестнадцатеричных
        /// цифр после <c>'</c>.
        /// </summary>
        /// <returns>Символ кодовой страницы либо <c>'\0'</c> при ошибке.</returns>
        private char РаскодироватьСимвол()
        {
            if (_позиция + 1 >= _документ.Length)
            {
                return '\0';
            }

            var значение = _документ.Substring(_позиция, 2);
            _позиция += 2;

            if (!int.TryParse(
                    значение,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var код))
            {
                return '\0';
            }

            var байты = new[] { (byte)код };

            return Кодировка().GetString(байты)[0];
        }

        /// <summary>
        /// Возвращает кодировку символов кодовой страницы документа.
        /// </summary>
        /// <returns>Кодировка страницы, объявленной в документе.</returns>
        private Encoding Кодировка()
        {
            if (_кодировка is not null)
            {
                return _кодировка;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                _кодировка = Encoding.GetEncoding(_кодоваяСтраница);
            }
            catch (ArgumentException)
            {
                _кодировка = Encoding.Latin1;
            }

            return _кодировка;
        }

        /// <summary>
        /// Определяет, является ли группа пропускаемым назначением RTF.
        /// </summary>
        /// <returns>Признак пропуска содержимого группы.</returns>
        private bool ПропускаетсяНазначение()
        {
            var запомненная = _позиция;

            // После «{» стоит управляющее слово: обратная косая черта
            // пропускается, чтобы прочитать само слово.
            if (_позиция < _документ.Length && _документ[_позиция] == '\\')
            {
                _позиция++;
            }

            var код = ПрочитатьКод();
            _позиция = запомненная;

            return код.ЭтоСимвол
                ? код.Имя == "*"
                : ПропускаемыеНазначения.Contains(код.Имя);
        }

        /// <summary>
        /// Добавляет абзац документа в список абзацев.
        /// </summary>
        /// <param name="текст">Текст абзаца.</param>
        private void ДобавитьАбзац(string текст)
        {
            if (string.IsNullOrWhiteSpace(текст))
            {
                return;
            }

            _абзацы.Add(текст.Trim());
        }

        /// <summary>
        /// Читает управляющее слово или управляющий символ RTF.
        /// </summary>
        /// <returns>Прочитанный код.</returns>
        private КодУправления ПрочитатьКод()
        {
            if (_позиция >= _документ.Length)
            {
                return new КодУправления(string.Empty, true, 0);
            }

            var символ = _документ[_позиция];

            if (!char.IsLetter(символ))
            {
                _позиция++;
                return new КодУправления(символ.ToString(), true, 0);
            }

            var начало = _позиция;

            while (_позиция < _документ.Length && char.IsLetter(_документ[_позиция]))
            {
                _позиция++;
            }

            var имя = _документ[начало.._позиция];
            var параметр = 0;

            if (_позиция < _документ.Length
                && (_документ[_позиция] == '-' || char.IsAsciiDigit(_документ[_позиция])))
            {
                var знак = 1;

                if (_документ[_позиция] == '-')
                {
                    знак = -1;
                    _позиция++;
                }

                var началоПараметра = _позиция;

                while (_позиция < _документ.Length && char.IsAsciiDigit(_документ[_позиция]))
                {
                    _позиция++;
                }

                if (началоПараметра < _позиция)
                {
                    int.TryParse(
                        _документ[началоПараметра.._позиция],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out параметр);
                }

                параметр *= знак;
            }

            // Пробел после управляющего слова является разделителем и текстом
            // документа не является.
            if (_позиция < _документ.Length && _документ[_позиция] == ' ')
            {
                _позиция++;
            }

            return new КодУправления(имя, false, параметр);
        }

        /// <summary>
        /// Кодировка символов кодовой страницы документа; вычисляется один раз.
        /// </summary>
        private Encoding? _кодировка;
    }

    /// <summary>
    /// Управляющее слово или управляющий символ RTF.
    /// </summary>
    /// <param name="Имя">Имя управляющего слова либо символ.</param>
    /// <param name="ЭтоСимвол">Признак управляющего символа.</param>
    /// <param name="Параметр">Числовой параметр управляющего слова.</param>
    private readonly record struct КодУправления(string Имя, bool ЭтоСимвол, int Параметр);

    /// <summary>
    /// Кадр группы RTF: буфер родительской группы и признак её пропуска.
    /// </summary>
    private sealed class Кадр
    {
        /// <summary>Буфер родительской группы.</summary>
        public StringBuilder Буфер { get; init; } = new();

        /// <summary>Признак пропуска содержимого родительской группы.</summary>
        public bool Отбрасывать { get; init; }
    }
}
