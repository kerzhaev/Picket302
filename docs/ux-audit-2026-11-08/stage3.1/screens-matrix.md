# Screens-2: оставшиеся runtime-поверхности (stage3.1, 2026-11-08)

Драйвер `run-screens2.ps1` + harness-режим `screens2` (профиль БЕЗ шаблонов;
шаблон сеется командой `SEEDTEMPLATE` по требованию). Каждый этап — отдельное
проверяемое условие в журнале `screens2-happy-evidence.md`; 8 fault-инъекций
(`P302_SCREENS2_FAULT=…`) все дали exit 1 (`screens2-fault-*.md`).

## Результаты этапов (PS 5.1 + PS 7, exit 0)

| Этап | Поверхность | Доказательство | Скриншот |
|---|---|---|---|
| S1 | «Отправленные» пустая | SETTAB 3 → SENTINFO строк=0 подсказка=True; таблица не сломана | `40-sent-empty.png` |
| S2 | Карточка отправления (длинные значения) + ОТМЕНА | карточка открыта на получателе с длинными полями, Отмена закрывает, MAILCOUNT 0 | `41-mailcard-longvalues.png` |
| S3 | Карточка → ОК → печать → Отмена; «Отправленные» с данными | MAILCOUNT 0→1; вкладка SENTINFO строк=1 | `42-print-dialog.png`, `43-sent-with-data.png` |
| S4 | **Нет шаблона** | CB_GETCOUNT комбобокса «Шаблон конверта:» = **0** (по метке, COMBOBOX правее её конца); диалог скриншочен; Отмена корректна; MAILCOUNT=2; предпросмотр не открыт | `45-no-template-print-dialog.png` |
| S5 | **Блокировка размера бумаги** | F7 (прямая печать) → подача «Продолжить» → OversizeWarning «Конверт слишком большой…» → ОК → «Проверка размера бумаги» → Отмена; UI отвечает; спулер 0→0 | `46a-oversize-warning.png`, `46-paper-size-blocked.png` |
| S6 | Клипарт (смоук UX3-01) | SHOW → CLIPSELECT items=10 значки=10 выбрано=1 активна=True нажато=True закрыт=True → повторный SHOW → HIDE; WM_NULL ok | (09-clipart-picker.png stage3) |
| S7 | Сброс вида таблиц | GRIDRESET до=[focus=255 selected=65280 hint=8421376 zebra=True font=10 row=22] → после=[4227327/6205183/3723516 zebra=False font=8 row=17] — точное совпадение с конрактом GridAppearance | `47-grid-appearance-reset.png` |
| S8 | Редактор бланка | SHOW → скриншот → HIDE; без GDI+-исключений | `48-blank-editor.png` |

## Статусы флагов

```
RUNTIME_SENT_EMPTY_VERIFIED=true      (S1, runtime)
RUNTIME_SENT_DATA_VERIFIED=true       (S3, runtime)
RUNTIME_NO_TEMPLATE_VERIFIED=true     (S4, runtime: пустой список + корректная отмена;
                                       сообщение-конвейер «Шаблон не выбран.» покрыто
                                       кодом PrintSessionController + тестами)
RUNTIME_PAPER_WARNING_VERIFIED=true   (S5, runtime: полный диалоговый путь до BLOCKED_SIZE
                                       без задания в спулер)
RUNTIME_NO_PRINTER_VERIFIED=CONTRACT_ONLY  (run-noprinter.ps1: путь MainShell реальный,
                                       но отсутствие принтера — подменяемым провайдером,
                                       не удалением системного принтера стенда)
```

## Fault-инъекции (все exit 1, PASS_OF_CONTRACT: true)

`sent-empty`, `mailcard`, `sent-data`, `no-template`, `paper-dialog`,
`clipart`, `gridreset`, `final-delta` — каждая инъекция честно валит прогон.

## Найдено попутно (см. defects.md)

- **LAY3-01**: FlowLayoutPanel переполняет строку родительского TableLayoutPanel
  (evidence LAYOUTCHECK на 96/120/144 dpi: ребёнок 137×74 при клиенте 138×127) —
  DPI-независимо, виден в главном окне (цепочка
  SplitContainer<SplitterPanel<TLP<Panel<GroupBox<TLP<FlowLayoutPanel).
