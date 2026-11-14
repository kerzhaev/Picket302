using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Printing;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;

namespace Picket302.App.Shared;

/// <summary>
/// Единая точка доступа оболочки к данным программы.
/// </summary>
/// <remarks>
/// <para>
/// Оболочка обращается к данным только через этот интерфейс. В одиночном
/// режиме его реализация работает поверх локальной базы через существующие
/// прикладные службы; в совместном режиме — через типизированный API
/// серверной службы.
/// </para>
/// <para>
/// Единая точка доступа исключает скрытый переход на местную базу: в
/// совместном режиме в составе приложения нет ни одного репозитория,
/// обращающегося к локальному файлу базы, поэтому недоступный сервер не
/// подменяется молча.
/// </para>
/// <para>
/// Методы возвращают доменные типы: правила предметной области одинаковы в
/// обоих режимах, различается только способ доставки.
/// </para>
/// </remarks>
public interface IDataGateway
{
    /// <summary>Признак того, что данные хранятся в общей базе службы.</summary>
    bool ОбщийРежим { get; }

    /// <summary>
    /// Возвращает страницу контрагентов.
    /// </summary>
    /// <param name="запрос">Запрос страницы.</param>
    /// <param name="фильтр">Фильтр выборки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Страница контрагентов.</returns>
    Task<Page<Counterparty>> ПолучитьКонтрагентовAsync(
        PageRequest запрос,
        CounterpartyFilter? фильтр = null,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает контрагента по идентификатору.
    /// </summary>
    /// <param name="идентификатор">Идентификатор контрагента.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Контрагент либо <see langword="null"/>.</returns>
    Task<Counterparty?> ПолучитьКонтрагентаAsync(long идентификатор, CancellationToken токен = default);

    /// <summary>
    /// Сохраняет контрагента: создаёт новую запись либо изменяет существующую.
    /// </summary>
    /// <param name="контрагент">Карточка контрагента.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат сохранения.</returns>
    Task<Result<Counterparty>> СохранитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default);

    /// <summary>
    /// Помечает контрагента удалённым (мягкое удаление).
    /// </summary>
    /// <param name="контрагент">Карточка контрагента с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<Counterparty>> УдалитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default);

    /// <summary>
    /// Окончательно удаляет контрагента из базы.
    /// </summary>
    /// <param name="идентификатор">Идентификатор контрагента.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> ОкончательноУдалитьКонтрагентаAsync(long идентификатор, CancellationToken токен = default);

    /// <summary>
    /// Помечает контрагентов удалёнными либо возвращает их в работу.
    /// </summary>
    /// <param name="контрагенты">Записи с прочитанными версиями.</param>
    /// <param name="восстановить">Восстановить записи вместо удаления.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число затронутых записей.</returns>
    Task<Result<int>> ИзменитьСостояниеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        bool восстановить,
        CancellationToken токен = default);

    /// <summary>
    /// Пакетно изменяет одно поле контрагентов.
    /// </summary>
    /// <param name="контрагенты">Записи с прочитанными версиями.</param>
    /// <param name="свойство">Имя изменяемого поля.</param>
    /// <param name="значение">Новое значение.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число затронутых записей.</returns>
    Task<Result<int>> ИзменитьПолеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        string свойство,
        string? значение,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает справочник отправителей.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список отправителей.</returns>
    Task<IReadOnlyList<Counterparty>> ПолучитьОтправителейAsync(CancellationToken токен = default);

    /// <summary>
    /// Возвращает контрагентов выбранной группы.
    /// </summary>
    /// <param name="идентификаторГруппы">Идентификатор группы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список контрагентов.</returns>
    Task<IReadOnlyList<Counterparty>> ПолучитьКонтрагентовГруппыAsync(
        long идентификаторГруппы,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает различные значения поля контрагентов (для фильтров).
    /// </summary>
    /// <param name="поле">Имя поля.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список значений.</returns>
    Task<IReadOnlyList<string>> ПолучитьЗначенияПоляAsync(string поле, CancellationToken токен = default);

    /// <summary>
    /// Ищет контрагента по ключу слияния (ИНН, наименование, индекс).
    /// </summary>
    /// <param name="инн">ИНН.</param>
    /// <param name="наименование">Наименование.</param>
    /// <param name="индекс">Почтовый индекс.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Контрагент либо <see langword="null"/>.</returns>
    Task<Counterparty?> НайтиПоКлючуСлиянияAsync(
        string? инн,
        string наименование,
        string? индекс,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает пользовательские поля контрагента.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Идентификатор контрагента.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список полей.</returns>
    Task<IReadOnlyList<CounterpartyField>> ПолучитьПоляКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает значения пользовательских полей контрагента.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Идентификатор контрагента.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Словарь «имя поля — значение».</returns>
    Task<IReadOnlyDictionary<string, string>> ПолучитьЗначенияПолейКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default);

    /// <summary>
    /// Записывает значение пользовательского поля контрагента.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Идентификатор контрагента.</param>
    /// <param name="поле">Имя поля.</param>
    /// <param name="значение">Значение поля.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат записи.</returns>
    Task<Result<CounterpartyField>> ЗаписатьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        string значение,
        CancellationToken токен = default);

    /// <summary>
    /// Удаляет значение пользовательского поля контрагента.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Идентификатор контрагента.</param>
    /// <param name="поле">Имя поля.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<bool>> УдалитьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        CancellationToken токен = default);

    /// <summary>
    /// Создаёт отправителя.
    /// </summary>
    /// <param name="агент">Данные отправителя.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат создания.</returns>
    Task<Result<Counterparty>> СоздатьОтправителяAsync(AgentDto агент, CancellationToken токен = default);

    /// <summary>
    /// Возвращает список групп.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список групп.</returns>
    Task<IReadOnlyList<Group>> ПолучитьГруппыAsync(CancellationToken токен = default);

    /// <summary>
    /// Возвращает дерево категорий адресной книги.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список категорий верхнего уровня с потомками.</returns>
    Task<IReadOnlyList<Category>> ПолучитьДеревоКатегорийAsync(CancellationToken токен = default);

    /// <summary>
    /// Создаёт категорию дерева адресной книги.
    /// </summary>
    /// <param name="наименование">Наименование категории.</param>
    /// <param name="родитель">Родительская категория.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат создания.</returns>
    Task<Result<Category>> СоздатьКатегориюAsync(
        string наименование,
        long? родитель = null,
        CancellationToken токен = default);

    /// <summary>
    /// Переименовывает категорию.
    /// </summary>
    /// <param name="категория">Категория с прочитанной версией.</param>
    /// <param name="наименование">Новое наименование.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат переименования.</returns>
    Task<Result<Category>> ПереименоватьКатегориюAsync(
        Category категория,
        string наименование,
        CancellationToken токен = default);

    /// <summary>
    /// Удаляет категорию вместе с её содержимым.
    /// </summary>
    /// <param name="категория">Категория с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<int>> УдалитьКатегориюAsync(Category категория, CancellationToken токен = default);

    /// <summary>
    /// Переносит контрагента в другую категорию.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Контрагент.</param>
    /// <param name="идентификаторКатегории">Категория назначения.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат переноса.</returns>
    Task<Result<Counterparty>> ПеренестиКонтрагентаAsync(
        long идентификаторКонтрагента,
        long идентификаторКатегории,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает список папок отправлений.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список папок.</returns>
    Task<IReadOnlyList<Folder>> ПолучитьПапкиAsync(CancellationToken токен = default);

    /// <summary>
    /// Создаёт папку отправлений.
    /// </summary>
    /// <param name="наименование">Наименование папки.</param>
    /// <param name="родитель">Родительская папка.</param>
    /// <param name="примечание">Примечание.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат создания.</returns>
    Task<Result<Folder>> СоздатьПапкуAsync(
        string наименование,
        long? родитель = null,
        string? примечание = null,
        CancellationToken токен = default);

    /// <summary>
    /// Переименовывает папку.
    /// </summary>
    /// <param name="папка">Папка с прочитанной версией.</param>
    /// <param name="наименование">Новое наименование.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат переименования.</returns>
    Task<Result<Folder>> ПереименоватьПапкуAsync(
        Folder папка,
        string наименование,
        CancellationToken токен = default);

    /// <summary>
    /// Переносит папку.
    /// </summary>
    /// <param name="папка">Папка с прочитанной версией.</param>
    /// <param name="родитель">Новая родительская папка.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат переноса.</returns>
    Task<Result<Folder>> ПеренестиПапкуAsync(
        Folder папка,
        long? родитель,
        CancellationToken токен = default);

    /// <summary>
    /// Удаляет папку.
    /// </summary>
    /// <param name="папка">Папка с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<bool>> УдалитьПапкуAsync(Folder папка, CancellationToken токен = default);

    /// <summary>
    /// Возвращает число отправлений в папке.
    /// </summary>
    /// <param name="идентификаторПапки">Идентификатор папки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число отправлений.</returns>
    Task<int> ПолучитьЧислоПисемВПапкеAsync(long идентификаторПапки, CancellationToken токен = default);

    /// <summary>
    /// Возвращает страницу отправлений.
    /// </summary>
    /// <param name="запрос">Запрос страницы.</param>
    /// <param name="фильтр">Фильтр выборки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Страница отправлений.</returns>
    Task<Page<Mail>> ПолучитьПисьмаAsync(
        PageRequest запрос,
        MailFilter? фильтр = null,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает отправление по идентификатору.
    /// </summary>
    /// <param name="идентификатор">Идентификатор отправления.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Отправление либо <see langword="null"/>.</returns>
    Task<Mail?> ПолучитьПисьмоAsync(long идентификатор, CancellationToken токен = default);

    /// <summary>
    /// Создаёт отправление.
    /// </summary>
    /// <param name="письмо">Отправление.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат создания.</returns>
    Task<Result<Mail>> СоздатьПисьмоAsync(Mail письмо, CancellationToken токен = default);

    /// <summary>
    /// Меняет статус отправления.
    /// </summary>
    /// <param name="письмо">Отправление с прочитанной версией.</param>
    /// <param name="статус">Новый статус.</param>
    /// <param name="момент">Момент перехода.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат смены статуса.</returns>
    Task<Result<Mail>> СменитьСтатусПисьмаAsync(
        Mail письмо,
        MailStatus статус,
        DateTime? момент = null,
        CancellationToken токен = default);

    /// <summary>
    /// Переносит отправление в папку.
    /// </summary>
    /// <param name="письмо">Отправление с прочитанной версией.</param>
    /// <param name="идентификаторПапки">Папка назначения.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат переноса.</returns>
    Task<Result<Mail>> ПеренестиПисьмоAsync(
        Mail письмо,
        long идентификаторПапки,
        CancellationToken токен = default);

    /// <summary>
    /// Обновляет адрес получателя в непечатанных письмах.
    /// </summary>
    /// <param name="идентификаторКонтрагента">Контрагент-получатель.</param>
    /// <param name="адрес">Новый адрес.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число обновлённых писем.</returns>
    Task<int> ОбновитьАдресВЧерновикахAsync(
        long идентификаторКонтрагента,
        Address адрес,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает счётчики отправлений по папкам.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Счётчики: идентификатор папки — число писем.</returns>
    Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(CancellationToken токен = default);

    /// <summary>
    /// Возвращает письма рассылки.
    /// </summary>
    /// <param name="идентификаторРассылки">Идентификатор рассылки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список писем рассылки.</returns>
    Task<IReadOnlyList<Mail>> ПолучитьПисьмаРассылкиAsync(
        long идентификаторРассылки,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает писателя журнала email-рассылок: в одиночном режиме журнал
    /// ведётся в локальной базе, в совместном — в общей базе службы.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Писатель журнала.</returns>
    Task<IEmailLogWriter> ПолучитьПисателяEmailAsync(CancellationToken токен = default);

    /// <summary>
    /// Помечает записи удалёнными либо возвращает их в работу по
    /// идентификаторам: версии записей читаются при выполнении.
    /// </summary>
    /// <param name="идентификаторы">Идентификаторы записей.</param>
    /// <param name="восстановить">Восстановить записи вместо удаления.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число затронутых записей.</returns>
    Task<Result<int>> ИзменитьСостояниеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        bool восстановить,
        CancellationToken токен = default);

    /// <summary>
    /// Пакетно изменяет одно поле контрагентов по идентификаторам: версии
    /// записей читаются при выполнении, конфликт прекращает всю операцию.
    /// </summary>
    /// <param name="идентификаторы">Идентификаторы записей.</param>
    /// <param name="свойство">Имя изменяемого поля.</param>
    /// <param name="значение">Новое значение.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число затронутых записей.</returns>
    Task<Result<int>> ИзменитьПолеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        string свойство,
        string? значение,
        CancellationToken токен = default);

    /// <summary>
    /// Выдаёт отправлению очередной регистрационный номер.
    /// </summary>
    /// <param name="письмо">Отправление с прочитанной версией.</param>
    /// <param name="шаблон">Шаблон номера.</param>
    /// <param name="момент">Момент выдачи.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Отправление с присвоенным номером.</returns>
    Task<Result<Mail>> ВыдатьНомерПисьмуAsync(
        Mail письмо,
        string? шаблон = null,
        DateTime? момент = null,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает список рассылок.
    /// </summary>
    /// <param name="фильтр">Фильтр выборки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список рассылок.</returns>
    Task<IReadOnlyList<DistributionView>> ПолучитьРассылкиAsync(
        DistributionFilter? фильтр = null,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает рассылку по идентификатору.
    /// </summary>
    /// <param name="идентификатор">Идентификатор рассылки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Рассылка либо <see langword="null"/>.</returns>
    Task<Distribution?> ПолучитьРассылкуAsync(long идентификатор, CancellationToken токен = default);

    /// <summary>
    /// Формирует рассылку: рассылка и её письма создаются одной операцией.
    /// </summary>
    /// <param name="план">План рассылки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат формирования.</returns>
    Task<Result<Distribution>> СформироватьРассылкуAsync(
        DistributionPlan план,
        CancellationToken токен = default);

    /// <summary>
    /// Создаёт рассылку по выбранным контрагентам.
    /// </summary>
    /// <param name="идентификаторы">Получатели.</param>
    /// <param name="отправитель">Отправитель.</param>
    /// <param name="наименование">Наименование рассылки.</param>
    /// <param name="формат">Формат конверта.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат создания.</returns>
    Task<Result<Distribution>> СоздатьРассылкуИзВыборкиAsync(
        IReadOnlyList<long> идентификаторы,
        long? отправитель = null,
        string? наименование = null,
        string? формат = null,
        CancellationToken токен = default);

    /// <summary>
    /// Клонирует рассылку для повторной отправки.
    /// </summary>
    /// <param name="идентификатор">Исходная рассылка.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат клонирования.</returns>
    Task<Result<РассылкаКлон>> КлонироватьРассылкуAsync(long идентификатор, CancellationToken токен = default);

    /// <summary>
    /// Изменяет сохранённую рассылку.
    /// </summary>
    /// <param name="запрос">Данные правки.</param>
    /// <param name="ожидаемаяВерсия">
    /// Версия рассылки, прочитанная вызывающим: при устаревшей версии служба
    /// отклоняет правку без частичной записи.
    /// </param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат правки.</returns>
    Task<Result<РассылкаПравка>> ИзменитьРассылкуAsync(
        DistributionUpdateRequest запрос,
        long ожидаемаяВерсия,
        CancellationToken токен = default);

    /// <summary>
    /// Переводит рассылку из черновика в сохранённую.
    /// </summary>
    /// <param name="рассылка">Рассылка с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат сохранения.</returns>
    Task<Result<Distribution>> СохранитьРассылкуAsync(Distribution рассылка, CancellationToken токен = default);

    /// <summary>
    /// Отмечает рассылку как отправленную.
    /// </summary>
    /// <param name="рассылка">Рассылка с прочитанной версией.</param>
    /// <param name="момент">Момент отправки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат отметки.</returns>
    Task<Result<Distribution>> ОтметитьРассылкуОтправленнойAsync(
        Distribution рассылка,
        DateTime? момент = null,
        CancellationToken токен = default);

    /// <summary>
    /// Пересчитывает требуемое число конвертов рассылки.
    /// </summary>
    /// <param name="рассылка">Рассылка с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число конвертов.</returns>
    Task<int> ПересчитатьКонвертыРассылкиAsync(Distribution рассылка, CancellationToken токен = default);

    /// <summary>
    /// Возвращает шаблоны печати.
    /// </summary>
    /// <param name="вид">Вид шаблона; пусто — все виды.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список шаблонов.</returns>
    Task<IReadOnlyList<PrintTemplate>> ПолучитьШаблоныAsync(
        string? вид = null,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает поля бланка.
    /// </summary>
    /// <param name="идентификаторШаблона">Идентификатор шаблона.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список полей.</returns>
    Task<IReadOnlyList<BlankField>> ПолучитьПоляШаблонаAsync(
        long идентификаторШаблона,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает метаданные управляемых пакетов шаблонов.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список пакетов.</returns>
    Task<IReadOnlyList<TemplatePackageDto>> ПолучитьПакетыШаблоновAsync(CancellationToken токен = default);

    /// <summary>
    /// Сохраняет шаблон печати.
    /// </summary>
    /// <param name="шаблон">Шаблон.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат сохранения.</returns>
    Task<Result<PrintTemplate>> СохранитьШаблонAsync(
        PrintTemplate шаблон,
        CancellationToken токен = default);

    /// <summary>
    /// Удаляет шаблон печати.
    /// </summary>
    /// <param name="шаблон">Шаблон с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<bool>> УдалитьШаблонAsync(PrintTemplate шаблон, CancellationToken токен = default);

    /// <summary>
    /// Сохраняет поле бланка.
    /// </summary>
    /// <param name="поле">Поле бланка.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат сохранения.</returns>
    Task<Result<BlankField>> СохранитьПолеШаблонаAsync(
        BlankField поле,
        CancellationToken токен = default);

    /// <summary>
    /// Удаляет поле бланка.
    /// </summary>
    /// <param name="поле">Поле с прочитанной версией.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    Task<Result<bool>> УдалитьПолеШаблонаAsync(BlankField поле, CancellationToken токен = default);

    /// <summary>
    /// Возвращает данные отправлений для печати.
    /// </summary>
    /// <param name="идентификаторы">Идентификаторы отправлений.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Данные для печати.</returns>
    Task<IReadOnlyList<PrintableItem>> ПолучитьДанныеПечатиAsync(
        IReadOnlyList<long> идентификаторы,
        CancellationToken токен = default);

    /// <summary>
    /// Фиксирует печатное событие в общем журнале.
    /// </summary>
    /// <param name="идентификаторы">Отправления.</param>
    /// <param name="описаниеШаблона">Описание использованного шаблона.</param>
    /// <param name="исход">Исход печати.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Результат фиксации.</returns>
    Task<Result<PrintHistory>> ЗаписатьПечатноеСобытиеAsync(
        IReadOnlyList<long> идентификаторы,
        string описаниеШаблона,
        PrintEventOutcome исход,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает страницу записей журнала печати.
    /// </summary>
    /// <param name="запрос">Запрос страницы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Страница журнала.</returns>
    Task<Page<PrintHistory>> ПолучитьИсториюПечатиAsync(
        PageRequest запрос,
        CancellationToken токен = default);

    /// <summary>
    /// Возвращает счётчики объёма общей базы (для лимитов пробного периода).
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Снимок объёма.</returns>
    Task<СнимокИспользования> ПолучитьСнимокОбъёмаAsync(CancellationToken токен = default);

    /// <summary>
    /// Возвращает счётчики выборочной очистки базы.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Счётчики по областям.</returns>
    Task<IReadOnlyDictionary<ClearScope, int>> ПолучитьСчётчикиОчисткиAsync(CancellationToken токен = default);

    /// <summary>
    /// Выборочно очищает общую базу.
    /// </summary>
    /// <param name="область">Область очистки.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    Task<Result<int>> ОчиститьБазуAsync(ClearScope область, CancellationToken токен = default);

    /// <summary>
    /// Наполняет общую базу демонстрационными данными.
    /// </summary>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число созданных записей.</returns>
    Task<int> НаполнитьДемоДаннымиAsync(CancellationToken токен = default);

    /// <summary>
    /// Сливает импортируемые записи с общими данными.
    /// </summary>
    /// <param name="пакет">Пакет импорта.</param>
    /// <param name="политика">Правило слияния.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число созданных, обновлённых и пропущенных записей.</returns>
    Task<ImportMergeReport> СлитьИмпортAsync(
        ImportBatch пакет,
        MergePolicy политика,
        CancellationToken токен = default);
}

/// <summary>
/// Результат клонирования рассылки.
/// </summary>
/// <param name="Рассылка">Новая рассылка.</param>
/// <param name="СозданоПисем">Число созданных писем.</param>
/// <param name="Пропущено">Контрагенты, пропущенные при клонировании.</param>
public sealed record РассылкаКлон(
    Distribution Рассылка,
    int СозданоПисем,
    IReadOnlyList<string> Пропущено);

/// <summary>
/// Результат правки рассылки.
/// </summary>
/// <param name="Рассылка">Рассылка после правки.</param>
/// <param name="ДобавленоПисем">Число добавленных писем.</param>
/// <param name="УдаленоПисем">Число удалённых писем.</param>
/// <param name="Пропущено">Контрагенты, пропущенные при правке.</param>
public sealed record РассылкаПравка(
    Distribution Рассылка,
    int ДобавленоПисем,
    int УдаленоПисем,
    IReadOnlyList<string> Пропущено);

/// <summary>
/// Отчёт о слиянии импорта.
/// </summary>
/// <param name="Создано">Число созданных записей.</param>
/// <param name="Обновлено">Число обновлённых записей.</param>
/// <param name="Пропущено">Число пропущенных записей.</param>
/// <param name="Конфликты">Число записей с конфликтами.</param>
public sealed record ImportMergeReport(
    int Создано,
    int Обновлено,
    int Пропущено,
    int Конфликты);
