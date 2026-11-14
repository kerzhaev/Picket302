import io


def patch(path, edits):
    s = io.open(path, encoding="utf-8").read()
    for old, new in edits:
        if old not in s:
            raise SystemExit("NOT FOUND in %s:\n%s" % (path, old[:240]))
        s = s.replace(old, new, 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(s)
    print("patched", path)


p = "src/Picket302.App/MainShell.cs"

patch(p, [
    # Startup: shared credentials are honored; an unreachable server never
    # silently falls back to the local base.
    (
        """        else if (_состав is null)
        {
            _состав = пути is null
                ? await AppServices
                    .СоздатьAsync(путьБазы, true, cancellationToken)
                    .ConfigureAwait(true)
                : await AppServices
                    .СоздатьAsync(пути, true, cancellationToken)
                    .ConfigureAwait(true);
            _составСозданоВнутри = true;
        }""",
        """        else if (_состав is null)
        {
            if (пути is null)
            {
                // Производственный запуск: сначала проверяется профиль
                // подключения к общей базе. Сопряжённое устройство обязано
                // подключиться к службе; недоступный сервер не подменяется
                // местной базой — оператор решает явно.
                var профильКлиента = new SharedClientProfile(
                    Picket302ProfilePaths.ПоУмолчанию().DataDirectory);

                var реквизиты = профильКлиента.Прочитать();
                var секрет = профильКлиента.ПрочитатьСекрет();

                if (реквизиты.Готовы && секрет is not null)
                {
                    try
                    {
                        _состав = await AppServices
                            .СоздатьОбщийAsync(
                                Picket302ProfilePaths.ПоУмолчанию(),
                                реквизиты,
                                секрет,
                                cancellationToken)
                            .ConfigureAwait(true);

                        _составСозданоВнутри = true;

                        await ИнициализироватьОбщийСоставAsync(_состав, cancellationToken)
                            .ConfigureAwait(true);

                        _инициализировано = true;
                        ОбновитьСтрокуСостояния(_состав);
                        ОбновитьИндикаторЛицензии(_состав);

                        return;
                    }
                    catch (SharedModeException ошибка)
                    {
                        if (!ПодтвердитьЛокальныйЗапуск(ошибка.Message))
                        {
                            throw;
                        }

                        // Явное решение оператора продолжить локально: это не
                        // автоматический переход, а осознанный выбор после
                        // сообщения о недоступности общей базы.
                    }
                }
            }

            _состав = пути is null
                ? await AppServices
                    .СоздатьAsync(путьБазы, true, cancellationToken)
                    .ConfigureAwait(true)
                : await AppServices
                    .СоздатьAsync(пути, true, cancellationToken)
                    .ConfigureAwait(true);
            _составСозданоВнутри = true;
        }""",
    ),
    # Demo seeding must not run in shared mode through the local path.
    (
        """        var активный = _состав!;

        // Подключение к базе состоялось: после первого подключения имя базы
        // данных изменению не подлежит (ТЗ §8.11, функция 127).
        активный.МенеджерПодключения.MarkConnectionEstablished();""",
        """        var активный = _состав!;

        // Подключение к базе состоялось: после первого подключения имя базы
        // данных изменению не подлежит (ТЗ §8.11, функция 127).
        активный.МенеджерПодключения.MarkConnectionEstablished();""",
    ),
])

# Shared composition initialization: the same loads as local, plus the usage
# snapshot refresh.
s = io.open(p, encoding="utf-8").read()
anchor = """    /// <summary>
    /// Строит главное меню окна (ТЗ §7.1: «Файл Правка Вид Сервис Печать
    Рассылки Справка»).
    /// </summary>"""
addition = """    /// <summary>
    /// Обновляет снимок объёма общей базы после подключения: лимиты пробного
    /// периода проверяются по общим данным.
    /// </summary>
    private async Task ИнициализироватьОбщийСоставAsync(
        AppServices состав,
        CancellationToken cancellationToken)
    {
        if (состав.ПрофильКлиента is null)
        {
            return;
        }

        // Демо-наполнение в общем режиме не запускается: общая база наполняется
        // только решением оператора (команда наполнения на сервере).
        _оформление = await состав.НастройкиОтображения.LoadAppearanceAsync(cancellationToken)
            .ConfigureAwait(true);

        GridAppearanceManager.Применить(_таблицаОтправителей, _оформление);
        GridAppearanceManager.Применить(_таблицаПолучателей, _оформление);
        GridAppearanceManager.Применить(_таблицаБланков, _оформление);
        GridAppearanceManager.Применить(_таблицаРассылок, _оформление);
        GridAppearanceManager.Применить(_таблицаОтправленных, _оформление);
        ПрименитьАкцентыТаблиц();

        await ЗагрузитьОтправителейAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПолучателейAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьКатегорииAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьБланкиAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьРассылкиAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьОтправленныеAsync(состав, cancellationToken).ConfigureAwait(true);
        await ЗагрузитьПапкиAsync(состав, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Спрашивает оператора, продолжать ли локально, когда общая база
    /// недоступна. Автоматического перехода нет: решение принимает человек.
    /// </summary>
    /// <param name="сообщение">Сообщение о недоступности.</param>
    /// <returns><see langword="true"/>, если оператор выбрал локальный запуск.</returns>
    private bool ПодтвердитьЛокальныйЗапуск(string сообщение)
    {
        var ответ = MessageBox.Show(
            this,
            сообщение + Environment.NewLine + Environment.NewLine
            + "Продолжить работу с локальной базой на этом компьютере? "
            + "Общие данные при этом не видны и не изменяются.",
            "Общая база недоступна",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return ответ == DialogResult.Yes;
    }

""" + anchor

if anchor in s:
    s = s.replace(anchor, addition, 1)
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("initialization helpers added")
else:
    raise SystemExit("menu anchor not found")

print("done")
