# Stage 5 runtime capture manifest

Дата: 2026-11-10
Сценарий: `run-emptybook.ps1`
Режим: скрытый WinForms acceptance harness, окно не переводилось на передний план
Результат драйвера: `exit 0`

## Кадр

| Файл | SHA-256 | Размер |
|---|---|---:|
| `emptybook-printwindow-1280x720.png` | `54FF395B1D24D22DCF8E6BA094F9D6B5D3D306EF1E19E38FB7D961703DDBBB91` | 38 827 байт |

## Метод и доказательства

- `AccWin.Capture` вызван после получения `READY` и `COUNTS`.
- В исходном capture-коде первым вызывается `PrintWindow` с
  `PW_RENDERFULLCONTENT`; `BitBlt` разрешён только как ограниченный fallback.
- Положение окна задавалось через `SetWindowPos` с `SWP_NOACTIVATE | SWP_NOZORDER
  | SWP_SHOWWINDOW`; фокус пользователя не менялся.
- `COUNTS всего=0 отправителей=0 получателей=0` совпал полностью.
- `graceful=True`, `killed=False`, `TreeOk=True`, `HarnessNotExited=False`.
- Временный профиль удалён; `production delta: new=0 changed=0 removed=0`.
- В stdout нет `FAIL` и символов замены `U+FFFD`.

Полный сырой журнал: `emptybook-happy-evidence-2026-11-10.md`.

## Результат визуальной проверки

Свежий кадр содержит одну строку главного меню (`Файл`, `Правка`, `Вид`,
`Данные`, `Сервис`, `Печать`, `Рассылки`, `Справка`). Наложенная вторая строка,
которая присутствует в старом `docs/ux-audit-2026-11-10/evidence/01-...png`,
в свежем `PrintWindow`-кадре отсутствует. Старый файл не переписывался: он
остаётся историческим evidence и должен рассматриваться как устаревший кадр до
отдельного решения о его архивировании.
