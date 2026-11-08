# Runtime evidence (stage 3, 2026-11-08)

Evidence root: `%TEMP%\p302-evidence-2026-11-08-stage3` (`P302_EVIDENCE_ROOT`).
Every driver: fail-closed, bounded waits, sentinel + production fingerprint
before/after, `Win32_PrintJob` before/after, evidence written once after the
verdict with post-write verification, lock release verified.

## Command matrix (final binaries, marker-verified)

| Driver | PS 5.1.26100 | PS 7.5.5 | Evidence file |
|---|---|---|---|
| `run-emptybook.ps1` | exit 0 PASS | exit 0 PASS | `emptybook-happy-evidence.md` |
| `run-printflow.ps1` (F5+F6 chain) | exit 0 PASS | exit 0 PASS | `printflow-happy-evidence.md` |
| `run-previewflow.ps1` (SEEDFIELDS+PREVIEWSCAN) | exit 0 PASS | exit 0 PASS | `previewflow-happy-evidence.md` |
| `run-noaddress.ps1` (**NEW**, §11.4) | exit 0 PASS | exit 0 PASS | `noaddress-happy-evidence.md` |
| `run-screens.ps1` (**NEW**, matrix shots) | exit 0 PASS ×3 | exit 0 PASS | `screens-happy-evidence.md` |
| `test-evidence-root-guard.ps1` (NEW, static) | exit 0 | exit 0 | console |

Exact command shape (each driver):
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-acceptance\run-<имя>.ps1`
with env `P302_EVIDENCE_ROOT`.

## Scenario 11.4 «Получатель без адреса» (NEW)

Built this round — the mandatory scenario had **no driver** before.
`run-noaddress.ps1` + harness mode `noaddress` (seeds sender with address +
recipient WITHOUT address) + commands `SELECTNOADDR` (selects by attribute,
`ClearSelection` first — exactly one row), `MAILCOUNT` (mails count 0→0→0).

Verified (happy path, both shells):
- F5 → message «У получателя «ООО «Получатель Без Адреса»» не задан основной адрес.» — screenshot `09-no-address-warning.png`; message closes (OK), main window responsive (WM_NULL).
- F6 → message «Отправлений сформировано: 0.\n\nПропущены без основного адреса (1): ООО «Получатель Без Адреса».\nЗаполните адрес в карточке контрагента и повторите печать.» — screenshot `10-no-address-warning-f6.png`; closes; NO preview, NO feed dialog afterwards.
- MAILCOUNT 0→0→0 (no partial record), spooler 0→0, production delta 0/0/0.
- `RUNTIME_NO_ADDRESS_VERIFIED=true`.

Fault injections (all exit 1, `PASS_OF_CONTRACT: true`):
`message-missing`, `mail-created`, `spooler-job`, `final-delta` —
`noaddress-fault-*.md`.

## Preview render proof (previewflow)

```
PREVIEW-SCAN страниц=1 canvasW=1296 canvasH=916 dimsOk=true dark=3296
  outsidePrintable=0 bbox=x=81..796 y=125..857 полосы=[0,586,0,0,0,0,1134,1576]
PREVIEW-FIELD name=[edt_str_to1] kind=text dark=1883
PREVIEW-FIELD name=[edt_str_to2] kind=text dark=827
PREVIEW-FIELD name=[edtIndeks_to] kind=text dark=586
```

Three distinct field renderings; index readable as TEXT (kind=text — the
machine stamp is deliberately NOT claimed; see print-readiness.md).

## Screenshots (all real app, isolated profiles)

`main-1280x720.png`, `main-1000x700.png`, `01-адресная-книга-пустая-1280x720.png`,
`05-counterparty-card.png`, `06-mail-card.png`, `07-grid-appearance.png`,
`08-blank-editor.png`, `09-clipart-picker.png` (10 items with icons),
`10-distribution-editor.png`, `20-карточка-отправления-f5.png`,
`21-диалог-печати-f5.png`, `22-предпросмотр-f6.png`,
`24-messagebox-формирование-*.png`, `25-подача-конвертов-*.png`,
`31-messagebox-формирование-*.png`, `32-предпросмотр-с-полями.png`,
`09-no-address-warning.png`, `10-no-address-warning-f6.png`.

## Cleanup census (final, after all runs)

- Picket302 / UiAcceptance processes: **0**
- `%TEMP%\Picket302Acceptance\*` dirs: **0**; `Picket302Probe`: **0**
- `Picket302.ComAcceptance.lock`: absent; sentinel: absent
- One **own** leftover harness (PID 119900, from a killed probe of this
  session) was identified by cmdline and force-stopped — recorded in
  defects.md ACC3-03; no foreign processes touched.
- Production profile: delta 0/0/0 in every driver; **external** changes to
  `picket302.db`/`connection.json` mid-session recorded as ENV3-02 (not by
  this series).
- `graphify-out/`: untouched (untracked, as at baseline).
